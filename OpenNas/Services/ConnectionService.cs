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
    private const string WifiOnlyKey = "backup_wifi_only";
    private const string DownloadWifiOnlyKey = "download_wifi_only";
    private const string ConfirmDeleteKey = "backup_confirm_delete";
    private const string DeleteRiskAckKey = "backup_delete_risk_ack";
    private const string AutoSwitchEnabledKey = "auto_switch_enabled";

    private CancellationTokenSource? _autoSwitchCts;
    private readonly SemaphoreSlim _autoSwitchLock = new(1, 1);
    private DateTime _manualSwitchCooldownUntil = DateTime.MinValue;
    private DateTime _lastConnectivityEventUtc = DateTime.MinValue;
    private DateTime _lastEnsureUtc = DateTime.MinValue;
    private EndpointEnsureResult _lastEnsureResult = EndpointEnsureResult.Skipped;
    private string? _lastEnsureNetworkFingerprint;
    private bool _endpointDecisionFresh;
    private bool _networkMonitoringStarted;
    private List<NasProfile> _cachedProfiles = new();
    private string? _serverKey;
    private static int _sessionFailureHandling;
    private readonly IAuthNavigation _authNavigation;

    public NasProfile? ActiveProfile { get; private set; }

    public bool IsLoggedIn =>
        SynologyManager.Client != null && !string.IsNullOrEmpty(SynologyManager.Client.Sid);

    public event EventHandler? ConnectionChanged;

    /// <summary>BaseUrl 实际变更后触发（自动/手动切换），页面可据此静默刷新。</summary>
    public event EventHandler? AddressSwitched;

    /// <summary>API 层检测到 106/107 并完成登出跳转后触发。</summary>
    public event EventHandler? SessionExpired;

    public ConnectionService(IAuthNavigation authNavigation)
    {
        _authNavigation = authNavigation;
        SynologyManager.SessionExpiredHandler = OnApiSessionExpiredAsync;
    }

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
    //  Endpoint ensure（主动刷新 / 网络变化共用同一套探测与切换）
    // ============================================================

    private const int ConnectivityDebounceSeconds = 3;
    private const int ConnectivitySettleMs = 400;
    private const int ProbeTimeoutMs = 800;

    /// <summary>
    /// 确保当前使用最优 NAS 地址。网络未变且已有可靠结论时直接复用，不重复探测。
    /// 网络变化、上次不可达、或结论失效时才并行探测（内网优先）。
    /// </summary>
    public Task<EndpointEnsureResult> EnsureBestEndpointAsync(
        CancellationToken cancellationToken = default) =>
        EnsureBestEndpointCoreAsync(
            respectManualCooldown: false,
            showToastOnSwitch: true,
            notifyAddressSwitched: false,
            forceProbe: false,
            cancellationToken);

    /// <summary>
    /// App 从后台回到前台：强制重新探测（WiFi→WiFi 指纹可能相同，不能只靠缓存）。
    /// 若发生切换会通知页面静默刷新。
    /// </summary>
    public Task<EndpointEnsureResult> EnsureBestEndpointOnResumeAsync(
        CancellationToken cancellationToken = default)
    {
        InvalidateEndpointDecision();
        return EnsureBestEndpointCoreAsync(
            respectManualCooldown: false,
            showToastOnSwitch: true,
            notifyAddressSwitched: true,
            forceProbe: true,
            cancellationToken);
    }

    private Task TryAutoSwitchAsync(CancellationToken cancellationToken = default) =>
        EnsureBestEndpointCoreAsync(
            respectManualCooldown: true,
            showToastOnSwitch: true,
            notifyAddressSwitched: true,
            forceProbe: true,
            cancellationToken);

    private void StartNetworkMonitoring()
    {
        if (_networkMonitoringStarted) return;
        _networkMonitoringStarted = true;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        // 网络形态一变，下次刷新必须重新探测
        InvalidateEndpointDecision();

        if (Connectivity.Current.NetworkAccess == NetworkAccess.None) return;

        var now = DateTime.UtcNow;
        if ((now - _lastConnectivityEventUtc).TotalSeconds < ConnectivityDebounceSeconds)
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
                await Task.Delay(ConnectivitySettleMs, token);
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

    private void InvalidateEndpointDecision()
    {
        _endpointDecisionFresh = false;
        _lastEnsureNetworkFingerprint = null;
    }

    /// <summary>NetworkAccess + 连接类型（WiFi/蜂窝等），用于判断「网络有没有变」。</summary>
    private static string GetNetworkFingerprint()
    {
        var access = Connectivity.Current.NetworkAccess;
        var profiles = string.Join(
            ',',
            Connectivity.Current.ConnectionProfiles.OrderBy(static p => p));
        return $"{access}|{profiles}";
    }

    private async Task<EndpointEnsureResult> EnsureBestEndpointCoreAsync(
        bool respectManualCooldown,
        bool showToastOnSwitch,
        bool notifyAddressSwitched,
        bool forceProbe,
        CancellationToken cancellationToken)
    {
        if (!AutoSwitchEnabled)
            return EndpointEnsureResult.Skipped;

        if (respectManualCooldown && DateTime.UtcNow < _manualSwitchCooldownUntil)
            return EndpointEnsureResult.Skipped;

        var profiles = _cachedProfiles.Count > 0
            ? _cachedProfiles.ToList()
            : await LoadProfilesAsync();
        if (profiles.Count <= 1)
            return EndpointEnsureResult.Skipped;

        var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan);
        var wanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Wan);
        if (lanProfile == null)
            return EndpointEnsureResult.Skipped;

        await _autoSwitchLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fingerprint = GetNetworkFingerprint();

            // 网络没变、且上次已确认最优地址 → 跳过探测（普通刷新走这里）
            if (!forceProbe
                && _endpointDecisionFresh
                && fingerprint == _lastEnsureNetworkFingerprint
                && _lastEnsureResult is EndpointEnsureResult.Ready or EndpointEnsureResult.Switched)
            {
                AppLog.Debug($"EnsureEndpoint: reuse ({_lastEnsureResult}), network unchanged");
                return _lastEnsureResult == EndpointEnsureResult.Switched
                    ? EndpointEnsureResult.Ready
                    : _lastEnsureResult;
            }

            // 内外网并行探测；都通则优先内网
            var lanTask = IsUrlReachableAsync(lanProfile.BaseUrl, cancellationToken);
            var wanTask = wanProfile != null
                ? IsUrlReachableAsync(wanProfile.BaseUrl, cancellationToken)
                : Task.FromResult(false);
            await Task.WhenAll(lanTask, wanTask);

            var isLanReachable = await lanTask;
            var isWanReachable = await wanTask;
            cancellationToken.ThrowIfCancellationRequested();

            NasProfile? targetProfile = null;
            if (isLanReachable)
                targetProfile = lanProfile;
            else if (isWanReachable && wanProfile != null)
                targetProfile = wanProfile;

            var currentUrl = SynologyManager.Client?.BaseUrl?.TrimEnd('/');
            var targetUrl = targetProfile?.BaseUrl?.TrimEnd('/');

            _lastEnsureUtc = DateTime.UtcNow;
            _lastEnsureNetworkFingerprint = fingerprint;

            if (targetProfile == null)
            {
                AppLog.Warn(
                    $"EnsureEndpoint: LAN={isLanReachable}, WAN={isWanReachable}, all unreachable");
                // 不可达不缓存为 fresh：下次刷新还会再探，便于恢复
                _endpointDecisionFresh = false;
                _lastEnsureResult = EndpointEnsureResult.Unreachable;
                return _lastEnsureResult;
            }

            if (currentUrl != null
                && string.Equals(currentUrl, targetUrl, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Debug(
                    $"EnsureEndpoint: already best (LAN={isLanReachable}, WAN={isWanReachable})");
                _endpointDecisionFresh = true;
                _lastEnsureResult = EndpointEnsureResult.Ready;
                return _lastEnsureResult;
            }

            var toKind = NasProfileDisplay.KindLabel(targetProfile.NetworkKind);
            AppLog.Debug(
                $"EnsureEndpoint: {ActiveProfile?.BaseUrl ?? "-"} -> {targetProfile.BaseUrl}, LAN={isLanReachable}, WAN={isWanReachable}");

            await SwitchToProfileAsync(
                targetProfile,
                toastKind: showToastOnSwitch ? toKind : null,
                notifyAddressSwitched: notifyAddressSwitched);
            _endpointDecisionFresh = true;
            _lastEnsureResult = EndpointEnsureResult.Switched;
            return _lastEnsureResult;
        }
        finally
        {
            _autoSwitchLock.Release();
        }
    }

    /// <summary>轻量 HEAD 探测（短超时，便于快速判定通断）。</summary>
    private static async Task<bool> IsUrlReachableAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs)
            };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(ProbeTimeoutMs);
            using var request = new HttpRequestMessage(HttpMethod.Head, baseUrl);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Probe failed: {baseUrl}", ex);
            return false;
        }
    }

    private async Task SwitchToProfileAsync(
        NasProfile targetProfile,
        string? toastKind = null,
        bool notifyAddressSwitched = true)
    {
        SynologyManager.BeginAddressSwitch();
        try
        {
            ActiveProfile = targetProfile;
            await SaveActiveProfileIdAsync(targetProfile.Id);
            await ApplyActiveProfileAsync(restoreSid: true);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            if (notifyAddressSwitched)
                AddressSwitched?.Invoke(this, EventArgs.Empty);

            if (!string.IsNullOrEmpty(toastKind))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                    await UiFeedback.ToastAsync($"已切换到{toastKind}"));
            }
        }
        finally
        {
            // 宽限期内抑制旧 Client 已销毁 / 挂起请求失败的误报弹窗
            SynologyManager.EndAddressSwitch(TimeSpan.FromSeconds(3));
        }
    }

    // ============================================================
    //  Apply active profile
    // ============================================================

    public async Task SetActiveProfileAsync(NasProfile profile)
    {
        _manualSwitchCooldownUntil = DateTime.UtcNow.AddSeconds(30);
        InvalidateEndpointDecision();

        var currentUrl = SynologyManager.Client?.BaseUrl?.TrimEnd('/');
        var targetUrl = profile.BaseUrl?.TrimEnd('/');
        var urlChanged = currentUrl == null
            || !string.Equals(currentUrl, targetUrl, StringComparison.OrdinalIgnoreCase);

        if (urlChanged)
        {
            await SwitchToProfileAsync(profile);
            return;
        }

        ActiveProfile = profile;
        await SaveActiveProfileIdAsync(profile.Id);
        await ApplyActiveProfileAsync(restoreSid: true);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearActiveProfileAsync()
    {
        ActiveProfile = null;
        try
        {
            SecureStorage.Remove(ActiveProfileSecureKey);
        }
        catch
        {
            // ignore
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
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
                using (var validateCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var validity = await SynologyManager.Client.Auth.TryValidateAppSessionAsync(validateCts.Token);
                    if (validity == false)
                    {
                        AppLog.Warn("AutoSwitch: session validation returned false, keeping stored SID");
                        return;
                    }

                    if (validity == true)
                        await PersistSessionAsync();
                }
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

    public async Task InvalidateStoredSessionAsync(string reason, bool tryServerLogout = false)
    {
        AppLog.Warn(reason);
        LogRepository.Instance.AppendOperation("NAS 会话已过期");

        if (tryServerLogout
            && SynologyManager.Client != null
            && !string.IsNullOrEmpty(SynologyManager.Client.Sid))
        {
            try
            {
                await SynologyManager.Client.Auth.LogoutAsync();
            }
            catch (Exception logoutEx)
            {
                AppLog.Debug("会话失效时服务端登出失败（可忽略）", logoutEx);
            }
        }

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

    private async Task OnApiSessionExpiredAsync(SynologyApiException ex)
    {
        if (Interlocked.CompareExchange(ref _sessionFailureHandling, 1, 0) != 0)
            return;

        try
        {
            await InvalidateStoredSessionAsync(ex.Message, tryServerLogout: true);
            await _authNavigation.GoToLoginAsync();
            SessionExpired?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref _sessionFailureHandling, 0);
        }
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
        InvalidateEndpointDecision();
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

    private const string ProfilesSecureKey = "opennas_nas_profiles";
    private const string ActiveProfileSecureKey = "opennas_active_profile_id";

    private static string LegacyProfilesFilePath =>
        Path.Combine(FileSystem.AppDataDirectory, "nas_profiles.json");

    private static string LegacyActiveProfileIdFilePath =>
        Path.Combine(FileSystem.AppDataDirectory, "active_profile.txt");

    private static readonly SemaphoreSlim _profileFileLock = new(1, 1);

    private async Task<List<NasProfile>> LoadProfilesFromFileAsync()
    {
        await _profileFileLock.WaitAsync();
        try
        {
            var json = await SecureStorage.GetAsync(ProfilesSecureKey);
            if (string.IsNullOrWhiteSpace(json) && File.Exists(LegacyProfilesFilePath))
            {
                json = await File.ReadAllTextAsync(LegacyProfilesFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    await SecureStorage.SetAsync(ProfilesSecureKey, json);
                    TryDeleteLegacyFile(LegacyProfilesFilePath);
                }
            }

            if (string.IsNullOrWhiteSpace(json))
                return [];

            return JsonSerializer.Deserialize<List<NasProfile>>(json) ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Error("读取 NAS 配置文件失败", ex);
            return [];
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
            await SecureStorage.SetAsync(ProfilesSecureKey, json);
            TryDeleteLegacyFile(LegacyProfilesFilePath);
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

    private static void TryDeleteLegacyFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private async Task<string?> LoadActiveProfileIdAsync()
    {
        try
        {
            var id = await SecureStorage.GetAsync(ActiveProfileSecureKey);
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();

            if (!File.Exists(LegacyActiveProfileIdFilePath))
                return null;

            var text = await File.ReadAllTextAsync(LegacyActiveProfileIdFilePath);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            id = text.Trim();
            await SecureStorage.SetAsync(ActiveProfileSecureKey, id);
            TryDeleteLegacyFile(LegacyActiveProfileIdFilePath);
            return id;
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
            await SecureStorage.SetAsync(ActiveProfileSecureKey, profileId);
            TryDeleteLegacyFile(LegacyActiveProfileIdFilePath);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存活跃配置文件 ID 失败", ex);
        }
    }

    // ============================================================
    //  Settings
    // ============================================================

    public bool GetWifiOnly() { try { return Preferences.Get(WifiOnlyKey, true); } catch { return true; } }
    public void SetWifiOnly(bool value) { try { Preferences.Set(WifiOnlyKey, value); } catch { } }

    public bool GetDownloadWifiOnly() { try { return Preferences.Get(DownloadWifiOnlyKey, true); } catch { return true; } }
    public void SetDownloadWifiOnly(bool value) { try { Preferences.Set(DownloadWifiOnlyKey, value); } catch { } }

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
