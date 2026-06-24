using System.Text.Json;
using NSynology;
using NSynology.Diagnostics;
using OpenNas.Helpers;
using OpenNas.Core.Models;
using System.IO;
using OpenNas.Core.Services;

namespace OpenNas.Services;

public class ConnectionService
{
    private const string ActiveProfileKey = "active_profile_id";
    private const string WifiOnlyKey = "backup_wifi_only";
    private const string ConfirmDeleteKey = "backup_confirm_delete";
    private const string DeleteRiskAckKey = "backup_delete_risk_ack";
    private const string AutoSwitchEnabledKey = "auto_switch_enabled";

    private CancellationTokenSource? _autoSwitchCts;
    private readonly SemaphoreSlim _autoSwitchLock = new(1, 1);
    private DateTime _manualSwitchCooldownUntil = DateTime.MinValue;
    private DateTime _lastConnectivityEventUtc = DateTime.MinValue;
    private bool _networkMonitoringStarted;
    private List<NasProfile> _cachedProfiles = new();
    private string? _serverKey;

    public NasProfile? ActiveProfile { get; private set; }

    public bool IsLoggedIn =>
        SynologyManager.Client != null && !string.IsNullOrEmpty(SynologyManager.Client.Sid);

    public event EventHandler? ConnectionChanged;

    public void NotifyConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);

    public bool AutoSwitchEnabled
    {
        get { try { return Preferences.Get(AutoSwitchEnabledKey, true); } catch { return true; } }
        set
        {
            try { Preferences.Set(AutoSwitchEnabledKey, value); } catch { }
            if (value)
                _ = TryAutoSwitchAsync();
        }
    }

    // ============================================================
    //  服务器维度会话 key（LAN / WAN 共用同一套 SID / DID / SynoToken）
    // ============================================================

    /// <summary>从 LAN Profile 的 hostname 推导稳定的服务器标识。</summary>
    private async Task<string> GetServerKeyAsync()
    {
        if (_serverKey != null)
            return _serverKey;

        var profiles = _cachedProfiles.Count > 0 ? _cachedProfiles : await LoadProfilesAsync();
        var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan);
        var hostProfile = lanProfile ?? profiles.FirstOrDefault();
        if (hostProfile != null && Uri.TryCreate(hostProfile.BaseUrl, UriKind.Absolute, out var uri))
            _serverKey = uri.Host.Replace('.', '_').Replace(':', '_');
        else
            _serverKey = "unknown";

        return _serverKey;
    }

    private void InvalidateServerKey() => _serverKey = null;

    private async Task<string> SidKeyAsync() => $"sid_{await GetServerKeyAsync()}";
    private async Task<string> DidKeyAsync() => $"did_{await GetServerKeyAsync()}";
    private async Task<string> SynoTokenKeyAsync() => $"synotoken_{await GetServerKeyAsync()}";

    // ============================================================
    //  Initialize
    // ============================================================

    public async Task InitializeAsync()
    {
        var profiles = await LoadProfilesAsync();
        var profilesChanged = false;
        foreach (var profile in profiles)
        {
            var normalized = NasUrlHelper.NormalizeBaseUrl(profile.BaseUrl);
            if (normalized == profile.BaseUrl)
                continue;
            profile.BaseUrl = normalized;
            profilesChanged = true;
        }

        if (profilesChanged)
            await SaveProfilesAsync(profiles);

        if (profiles.Count == 0)
        {
            ActiveProfile = null;
            return;
        }

        var activeId = (await LoadActiveProfileIdAsync()) ?? profiles[0].Id;
        ActiveProfile = profiles.FirstOrDefault(p => p.Id == activeId) ?? profiles[0];
        await ApplyActiveProfileAsync(restoreSid: true);

        // 0 或 1 个 Profile：不需要自动切换
        if (profiles.Count <= 1) return;

        StartNetworkMonitoring();
        _ = TryAutoSwitchAsync();
    }

    // ============================================================
    //  Network monitoring（30s 去抖）
    // ============================================================

    private void StartNetworkMonitoring()
    {
        if (_networkMonitoringStarted) return;
        _networkMonitoringStarted = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None) return;

        var now = DateTime.UtcNow;
        if ((now - _lastConnectivityEventUtc).TotalSeconds < 30)
            return;
        _lastConnectivityEventUtc = now;

        _autoSwitchCts?.Cancel();
        _autoSwitchCts?.Dispose();
        _autoSwitchCts = new CancellationTokenSource();
        var token = _autoSwitchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, token);
                await TryAutoSwitchAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warn("自动切换连接失败", ex);
            }
        }, token);
    }

    // ============================================================
    //  Auto-switch
    // ============================================================

    private async Task TryAutoSwitchAsync(CancellationToken cancellationToken = default)
    {
        if (!AutoSwitchEnabled) return;
        if (DateTime.UtcNow < _manualSwitchCooldownUntil) return;

        var profiles = _cachedProfiles.Count > 0
            ? _cachedProfiles.ToList()
            : await LoadProfilesAsync();
        if (profiles.Count <= 1) return;

        var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan);
        var wanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Wan);
        if (lanProfile == null) return;

        await _autoSwitchLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 内网优先
            var isLanReachable = await IsUrlReachableAsync(lanProfile.BaseUrl);
            cancellationToken.ThrowIfCancellationRequested();

            NasProfile? targetProfile = null;
            if (isLanReachable)
            {
                targetProfile = lanProfile;
            }
            else if (wanProfile != null)
            {
                var isWanReachable = await IsUrlReachableAsync(wanProfile.BaseUrl);
                if (isWanReachable)
                    targetProfile = wanProfile;
            }

            // 同 BaseUrl 跳过重建
            var currentUrl = SynologyManager.Client?.BaseUrl?.TrimEnd('/');
            var targetUrl = targetProfile?.BaseUrl?.TrimEnd('/');
            if (targetProfile != null && currentUrl != null
                && string.Equals(currentUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Debug($"AutoSwitch: same URL, skip (LAN reachable={isLanReachable})");
                return;
            }

            if (targetProfile == null)
            {
                AppLog.Warn(
                    $"AutoSwitch: LAN reachable={isLanReachable}, WAN={wanProfile != null}, all unreachable");
                return;
            }

            var toKind = NasProfileDisplay.KindLabel(targetProfile.NetworkKind);
            AppLog.Debug(
                $"AutoSwitch: {ActiveProfile?.BaseUrl ?? "-"} -> {targetProfile.BaseUrl}, LAN reachable={isLanReachable}");

            ActiveProfile = targetProfile;
            await SaveActiveProfileIdAsync(targetProfile.Id);
            await ApplyActiveProfileAsync(restoreSid: true);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);

            MainThread.BeginInvokeOnMainThread(async () =>
                await UiFeedback.ToastAsync($"已切换到{toKind}"));
        }
        finally
        {
            _autoSwitchLock.Release();
        }
    }

    /// <summary>轻量 HEAD 探测（1.5s 超时）。</summary>
    private static async Task<bool> IsUrlReachableAsync(string baseUrl)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1.5) };
            using var request = new HttpRequestMessage(HttpMethod.Head, baseUrl);
            await client.SendAsync(request);
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    // ============================================================
    //  Apply active profile
    // ============================================================

    public async Task SetActiveProfileAsync(NasProfile profile)
    {
        _manualSwitchCooldownUntil = DateTime.UtcNow.AddSeconds(30);
        ActiveProfile = profile;
        await SaveActiveProfileIdAsync(profile.Id);
        await ApplyActiveProfileAsync(restoreSid: true);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ApplyActiveProfileAsync(bool restoreSid)
    {
        if (ActiveProfile == null || string.IsNullOrWhiteSpace(ActiveProfile.BaseUrl)) return;

        var baseUrl = NasUrlHelper.NormalizeBaseUrl(ActiveProfile.BaseUrl);
        if (!string.Equals(baseUrl, ActiveProfile.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            ActiveProfile.BaseUrl = baseUrl;
            var all = await LoadProfilesAsync();
            var idx = all.FindIndex(p => p.Id == ActiveProfile.Id);
            if (idx >= 0)
            {
                all[idx] = ActiveProfile;
                await SaveProfilesAsync(all);
            }
        }

        // 当前 Client 已使用同一 BaseUrl → 无需重建
        if (SynologyManager.IsInitializedFor(baseUrl))
            return;

        string? sid = null;
        string? synoToken = null;
        string? did = null;
        if (restoreSid)
        {
            sid = await SecureStorage.GetAsync(await SidKeyAsync());
            synoToken = await SecureStorage.GetAsync(await SynoTokenKeyAsync());
            did = await SecureStorage.GetAsync(await DidKeyAsync());
        }

        if (!string.IsNullOrEmpty(sid))
        {
            try
            {
                SynologyManager.Init(baseUrl, sid, synoToken);
                SynologyManager.Client.RestoreAppSessionCookies(sid);

                if (!string.IsNullOrEmpty(did))
                    SynologyManager.Client.ApplyAppDeviceId(did);
#if DEBUG
                if (SynologyHttpTrace.IsEnabled)
                    SynologyManager.Client.ConfigureHttpTrace(true, SynologyDebugLog.Write);
#endif
                var validity = await SynologyManager.Client.Auth.TryValidateAppSessionAsync();
                if (validity == false)
                {
                    AppLog.Warn("AutoSwitch: session validation returned false, keeping stored SID");
                    return;
                }

                if (validity == true)
                    await PersistSessionAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("恢复 NAS 会话失败（保留本地会话，稍后重试）", ex);
                SynologyManager.Init(baseUrl, sid, synoToken);
                SynologyManager.Client.RestoreAppSessionCookies(sid);
                if (!string.IsNullOrEmpty(did))
                    SynologyManager.Client.ApplyAppDeviceId(did);
            }
            finally
            {
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        SynologyManager.Init(baseUrl);
    }

    // ============================================================
    //  Credential management
    // ============================================================

    public async Task ClearStoredCredentialsAsync()
    {
        var sidKey = await SidKeyAsync();
        var didKey = await DidKeyAsync();
        var tokenKey = await SynoTokenKeyAsync();
        SecureStorage.Remove(sidKey);
        SecureStorage.Remove(didKey);
        SecureStorage.Remove(tokenKey);

        if (ActiveProfile != null)
            SynologyManager.Init(ActiveProfile.BaseUrl);
        else
            SynologyManager.Client.Sid = null;

        SynologyManager.Client.SynoToken = null;
        await Task.CompletedTask;
    }

    public async Task OnLoginSuccessAsync()
    {
        await PersistSessionAsync();
        LogRepository.Instance.AppendOperation("登录成功");
    }

    public async Task PersistSessionAsync()
    {
        var sidKey = await SidKeyAsync();
        var didKey = await DidKeyAsync();
        var tokenKey = await SynoTokenKeyAsync();

        await SecureStorage.SetAsync(sidKey, SynologyManager.Client.Sid ?? "");
        var did = SynologyManager.Client.PhotosDeviceId
                  ?? SynologyManager.Client.GetDidCookieValue();
        if (!string.IsNullOrEmpty(did))
            await SecureStorage.SetAsync(didKey, did);
        if (!string.IsNullOrEmpty(SynologyManager.Client.SynoToken))
            await SecureStorage.SetAsync(tokenKey, SynologyManager.Client.SynoToken);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LogoutAsync()
    {
        var sidKey = await SidKeyAsync();
        var didKey = await DidKeyAsync();
        var tokenKey = await SynoTokenKeyAsync();
        SecureStorage.Remove(sidKey);
        SecureStorage.Remove(didKey);
        SecureStorage.Remove(tokenKey);

        if (!string.IsNullOrEmpty(SynologyManager.Client.Sid))
            await SynologyManager.Client.Auth.LogoutAsync();

        SynologyManager.Client.Sid = null;
        SynologyManager.Client.PhotosDeviceId = null;
        SynologyManager.Client.SynoToken = null;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        LogRepository.Instance.AppendOperation("退出登录");
    }

    public async Task InvalidateStoredSessionAsync(string reason)
    {
        AppLog.Warn(reason);
        LogRepository.Instance.AppendOperation("NAS 会话已过期");

        var sidKey = await SidKeyAsync();
        var didKey = await DidKeyAsync();
        var tokenKey = await SynoTokenKeyAsync();
        SecureStorage.Remove(sidKey);
        SecureStorage.Remove(didKey);
        SecureStorage.Remove(tokenKey);

        if (SynologyManager.Client != null)
        {
            SynologyManager.Client.Sid = null;
            SynologyManager.Client.PhotosDeviceId = null;
            SynologyManager.Client.SynoToken = null;
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    // ============================================================
    //  Profile management（含内存缓存）
    // ============================================================

    public async Task<List<NasProfile>> LoadProfilesAsync()
    {
        var profiles = await LoadProfilesFromFileAsync();
        _cachedProfiles = profiles;
        return profiles;
    }

    public async Task SaveProfilesAsync(List<NasProfile> profiles)
    {
        _cachedProfiles = profiles;
        InvalidateServerKey();
        await SaveProfilesToFileAsync(profiles);
    }

    /// <summary>删除 Profile 并清理对应会话凭证。</summary>
    public async Task DeleteProfileAsync(string profileId)
    {
        var profiles = await LoadProfilesAsync();
        var toRemove = profiles.FirstOrDefault(p => p.Id == profileId);
        if (toRemove == null) return;

        profiles.Remove(toRemove);
        await SaveProfilesAsync(profiles);

        if (profiles.Count == 0)
        {
            var sidKey = await SidKeyAsync();
            var didKey = await DidKeyAsync();
            var tokenKey = await SynoTokenKeyAsync();
            SecureStorage.Remove(sidKey);
            SecureStorage.Remove(didKey);
            SecureStorage.Remove(tokenKey);
        }

        if (ActiveProfile?.Id == profileId)
        {
            ActiveProfile = profiles.FirstOrDefault();
            if (ActiveProfile != null)
            {
                await SaveActiveProfileIdAsync(ActiveProfile.Id);
                await ApplyActiveProfileAsync(restoreSid: true);
            }
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ============================================================
    //  File I/O
    // ============================================================

    private static string ActiveProfileIdFilePath =>
        Path.Combine(FileSystem.AppDataDirectory, "active_profile.txt");

    private async Task<string?> LoadActiveProfileIdAsync()
    {
        try
        {
            if (!File.Exists(ActiveProfileIdFilePath))
                return null;
            var text = await File.ReadAllTextAsync(ActiveProfileIdFilePath);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex)
        {
            AppLog.Error("读取活跃配置文件 ID 失败", ex);
            return null;
        }
    }

    private async Task SaveActiveProfileIdAsync(string profileId)
    {
        try
        {
            await File.WriteAllTextAsync(ActiveProfileIdFilePath, profileId);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存活跃配置文件 ID 失败", ex);
        }
    }

    private static string ProfilesFilePath =>
        Path.Combine(FileSystem.AppDataDirectory, "nas_profiles.json");

    private static readonly SemaphoreSlim _profileFileLock = new(1, 1);

    private async Task<List<NasProfile>> LoadProfilesFromFileAsync()
    {
        await _profileFileLock.WaitAsync();
        try
        {
            if (!File.Exists(ProfilesFilePath))
                return new List<NasProfile>();
            var json = await File.ReadAllTextAsync(ProfilesFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<NasProfile>();
            return JsonSerializer.Deserialize<List<NasProfile>>(json) ?? new List<NasProfile>();
        }
        catch (Exception ex)
        {
            AppLog.Error("读取 NAS 配置文件失败", ex);
            return new List<NasProfile>();
        }
        finally
        {
            _profileFileLock.Release();
        }
    }

    private async Task SaveProfilesToFileAsync(List<NasProfile> profiles)
    {
        await _profileFileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(profiles);
            await File.WriteAllTextAsync(ProfilesFilePath, json);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存 NAS 配置文件失败", ex);
        }
        finally
        {
            _profileFileLock.Release();
        }
    }

    // ============================================================
    //  Settings
    // ============================================================

    public bool GetWifiOnly() { try { return Preferences.Get(WifiOnlyKey, true); } catch { return true; } }
    public void SetWifiOnly(bool value) { try { Preferences.Set(WifiOnlyKey, value); } catch { } }

    public bool GetConfirmBeforeDelete() { try { return Preferences.Get(ConfirmDeleteKey, true); } catch { return true; } }
    public void SetConfirmBeforeDelete(bool value) { try { Preferences.Set(ConfirmDeleteKey, value); } catch { } }

    public bool HasAcknowledgedDeleteRisk() { try { return Preferences.Get(DeleteRiskAckKey, false); } catch { return false; } }
    public void SetAcknowledgedDeleteRisk(bool value) { try { Preferences.Set(DeleteRiskAckKey, value); } catch { } }

    public string GetConnectionLabel()
    {
        if (ActiveProfile == null) return "未配置";
        var connected = !string.IsNullOrEmpty(SynologyManager.Client?.Sid);
        return $"{NasProfileDisplay.FormatTitle(ActiveProfile)} · {(connected ? "已连接" : "未登录")}";
    }
}
