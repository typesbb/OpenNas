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
    private const string ProfilesKey = "nas_profiles";
    private const string ActiveProfileKey = "active_profile_id";
    private const string WifiOnlyKey = "backup_wifi_only";
    private const string ConfirmDeleteKey = "backup_confirm_delete";
    private const string DeleteRiskAckKey = "backup_delete_risk_ack";
    private const string AutoSwitchEnabledKey = "auto_switch_enabled";

    private CancellationTokenSource? _autoSwitchCts;
    private readonly SemaphoreSlim _autoSwitchLock = new(1, 1);
    private DateTime _manualSwitchCooldownUntil = DateTime.MinValue;
    private bool _networkMonitoringStarted;

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

        StartNetworkMonitoring();
        _ = TryAutoSwitchAsync();
    }

    private void StartNetworkMonitoring()
    {
        if (_networkMonitoringStarted) return;
        _networkMonitoringStarted = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None) return;

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

    private async Task TryAutoSwitchAsync(CancellationToken cancellationToken = default)
    {
        if (!AutoSwitchEnabled) return;
        if (DateTime.UtcNow < _manualSwitchCooldownUntil) return;

        await _autoSwitchLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (AppServices.GetRequired<BackupEngine>().Progress.IsRunning) return;

            var profiles = await LoadProfilesAsync();
            var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan);
            var wanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Wan);

            if (lanProfile == null) return;

            var isLanReachable = await IsLanReachableAsync(lanProfile.BaseUrl);
            cancellationToken.ThrowIfCancellationRequested();

            NasProfile? targetProfile = null;
            if (isLanReachable)
            {
                if (ActiveProfile?.Id != lanProfile.Id)
                    targetProfile = lanProfile;
            }
            else if (wanProfile != null)
            {
                if (ActiveProfile?.Id != wanProfile.Id)
                    targetProfile = wanProfile;
            }

            if (targetProfile == null) return;

            ActiveProfile = targetProfile;
            await SaveActiveProfileIdAsync(targetProfile.Id);
            await ApplyActiveProfileAsync(restoreSid: true);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);

            var kindLabel = NasProfileDisplay.KindLabel(targetProfile.NetworkKind);
            MainThread.BeginInvokeOnMainThread(async () =>
                await UiFeedback.ToastAsync($"已切换到{kindLabel}"));
        }
        finally
        {
            _autoSwitchLock.Release();
        }
    }

    private static async Task<bool> IsLanReachableAsync(string baseUrl)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
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

    public async Task<List<NasProfile>> LoadProfilesAsync()
    {
        return await LoadProfilesFromFileAsync();
    }

    public Task SaveProfilesAsync(List<NasProfile> profiles)
    {
        return SaveProfilesToFileAsync(profiles);
    }
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

        string? sid = null;
        string? synoToken = null;
        if (restoreSid)
        {
            sid = await SecureStorage.GetAsync(SidKey(ActiveProfile.Id));
            synoToken = await SecureStorage.GetAsync(SynoTokenKey(ActiveProfile.Id));

        }

        if (!string.IsNullOrEmpty(sid))
        {
            try
            {
                SynologyManager.Init(baseUrl, sid, synoToken);
                SynologyManager.Client.RestoreOfficialAppSessionCookies(sid);

                var storedDid = await SecureStorage.GetAsync(DidKey(ActiveProfile.Id));
                if (!string.IsNullOrEmpty(storedDid))
                    SynologyManager.Client.ApplyOfficialAppDeviceId(storedDid);
#if DEBUG
                if (SynologyHttpTrace.IsEnabled)
                    SynologyManager.Client.ConfigureHttpTrace(true, SynologyDebugLog.Write);
#endif
                var validity = await SynologyManager.Client.Auth.TryValidateOfficialAppSessionAsync();
                if (validity == false)
                {
                    await InvalidateStoredSessionAsync("NAS 会话已过期，请重新登录。");
                    return;
                }

                if (validity == true)
                    await PersistSessionAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("恢复 NAS 会话失败（保留本地会话，稍后重试）", ex);
                SynologyManager.Init(baseUrl, sid, synoToken);
                SynologyManager.Client.RestoreOfficialAppSessionCookies(sid);
            }
            finally
            {
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        SynologyManager.Init(baseUrl);
    }

    public async Task ClearStoredCredentialsAsync()
    {
        if (ActiveProfile == null)
            return;

        SecureStorage.Remove(SidKey(ActiveProfile.Id));
        SecureStorage.Remove(DidKey(ActiveProfile.Id));
        SecureStorage.Remove(SynoTokenKey(ActiveProfile.Id));
        SynologyManager.Init(ActiveProfile.BaseUrl);
        SynologyManager.Client.Sid = null;
        SynologyManager.Client.SynoToken = null;
        await Task.CompletedTask;
    }

    public async Task OnLoginSuccessAsync()
    {
        await PersistSessionAsync();
        LogRepository.Instance.AppendOperation("登录成功");
    }

    /// <summary>将 sid / SynoToken 写入 SecureStorage。</summary>
    public async Task PersistSessionAsync()
    {
        if (ActiveProfile == null) return;
        await SecureStorage.SetAsync(SidKey(ActiveProfile.Id), SynologyManager.Client.Sid ?? "");
        var did = SynologyManager.Client.PhotosDeviceId
                  ?? SynologyManager.Client.GetDidCookieValue();
        if (!string.IsNullOrEmpty(did))
        {
            await SecureStorage.SetAsync(DidKey(ActiveProfile.Id), did);
        }
        if (!string.IsNullOrEmpty(SynologyManager.Client.SynoToken))
            await SecureStorage.SetAsync(SynoTokenKey(ActiveProfile.Id), SynologyManager.Client.SynoToken);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LogoutAsync()
    {
        if (ActiveProfile != null)
        {
            SecureStorage.Remove(SidKey(ActiveProfile.Id));
            SecureStorage.Remove(DidKey(ActiveProfile.Id));
            SecureStorage.Remove(SynoTokenKey(ActiveProfile.Id));
        }

        if (!string.IsNullOrEmpty(SynologyManager.Client.Sid))
            await SynologyManager.Client.Auth.LogoutAsync();

        SynologyManager.Client.Sid = null;
        SynologyManager.Client.PhotosDeviceId = null;
        SynologyManager.Client.SynoToken = null;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        LogRepository.Instance.AppendOperation("退出登录");
    }

    /// <summary>清除已过期的 sid，保留 NAS 连接配置与登录页用户名。</summary>
    public async Task InvalidateStoredSessionAsync(string reason)
    {
        AppLog.Warn(reason);
        LogRepository.Instance.AppendOperation("NAS 会话已过期");
        if (ActiveProfile != null)
        {
            SecureStorage.Remove(SidKey(ActiveProfile.Id));
            SecureStorage.Remove(DidKey(ActiveProfile.Id));
            SecureStorage.Remove(SynoTokenKey(ActiveProfile.Id));
        }

        if (SynologyManager.Client != null)
        {
            SynologyManager.Client.Sid = null;
            SynologyManager.Client.PhotosDeviceId = null;
            SynologyManager.Client.SynoToken = null;
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public bool GetWifiOnly() { try { return Preferences.Get(WifiOnlyKey, true); } catch { return true; } }
    public void SetWifiOnly(bool value) { try { Preferences.Set(WifiOnlyKey, value); } catch { } }

    public bool GetConfirmBeforeDelete() { try { return Preferences.Get(ConfirmDeleteKey, true); } catch { return true; } }
    public void SetConfirmBeforeDelete(bool value) { try { Preferences.Set(ConfirmDeleteKey, value); } catch { } }

    public bool HasAcknowledgedDeleteRisk() { try { return Preferences.Get(DeleteRiskAckKey, false); } catch { return false; } }
    public void SetAcknowledgedDeleteRisk(bool value) { try { Preferences.Set(DeleteRiskAckKey, value); } catch { } }

    public static string SidKey(string profileId) => $"sid_{profileId}";
    public static string DidKey(string profileId) => $"did_{profileId}";
    public static string SynoTokenKey(string profileId) => $"synotoken_{profileId}";

    public string GetConnectionLabel()
    {
        if (ActiveProfile == null) return "未配置";
        var connected = !string.IsNullOrEmpty(SynologyManager.Client?.Sid);
        return $"{NasProfileDisplay.FormatTitle(ActiveProfile)} · {(connected ? "已连接" : "未登录")}";
    }
}
