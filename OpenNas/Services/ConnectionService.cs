using System.Text.Json;
using NSynology;
using NSynology.Diagnostics;
using OpenNas.Helpers;
using OpenNas.Models;

namespace OpenNas.Services;

public class ConnectionService
{
    private const string ProfilesKey = "nas_profiles";
    private const string ActiveProfileKey = "active_profile_id";
    private const string WifiOnlyKey = "backup_wifi_only";
    private const string ConfirmDeleteKey = "backup_confirm_delete";
    private const string DeleteRiskAckKey = "backup_delete_risk_ack";

    public NasProfile? ActiveProfile { get; private set; }

    public bool IsLoggedIn =>
        SynologyManager.Client != null && !string.IsNullOrEmpty(SynologyManager.Client.Sid);

    public event EventHandler? ConnectionChanged;

    public void NotifyConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);

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
            profiles.Add(new NasProfile
            {
                DisplayName = "内网",
                BaseUrl = "https://192.168.0.2:5001",
                NetworkKind = NetworkKind.Lan
            });
            profiles.Add(new NasProfile
            {
                DisplayName = "外网",
                BaseUrl = "https://your-domain.com:5001",
                NetworkKind = NetworkKind.Wan
            });
            await SaveProfilesAsync(profiles);
        }

        var activeId = Preferences.Get(ActiveProfileKey, profiles[0].Id);
        ActiveProfile = profiles.FirstOrDefault(p => p.Id == activeId) ?? profiles[0];
        await ApplyActiveProfileAsync(restoreSid: true);
    }

    public async Task<List<NasProfile>> LoadProfilesAsync()
    {
        var json = Preferences.Get(ProfilesKey, "");
        if (string.IsNullOrWhiteSpace(json)) return new List<NasProfile>();
        return JsonSerializer.Deserialize<List<NasProfile>>(json) ?? new List<NasProfile>();
    }

    public async Task SaveProfilesAsync(List<NasProfile> profiles)
    {
        Preferences.Set(ProfilesKey, JsonSerializer.Serialize(profiles));
        await Task.CompletedTask;
    }

    public async Task SetActiveProfileAsync(NasProfile profile)
    {
        ActiveProfile = profile;
        Preferences.Set(ActiveProfileKey, profile.Id);
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
        SecureStorage.Remove(SynoTokenKey(ActiveProfile.Id));
        SynologyManager.Init(ActiveProfile.BaseUrl);
        SynologyManager.Client.Sid = null;
        SynologyManager.Client.SynoToken = null;
        await Task.CompletedTask;
    }

    public async Task OnLoginSuccessAsync()
    {
        await PersistSessionAsync();
    }

    /// <summary>将 sid / SynoToken 写入 SecureStorage。</summary>
    public async Task PersistSessionAsync()
    {
        if (ActiveProfile == null) return;
        await SecureStorage.SetAsync(SidKey(ActiveProfile.Id), SynologyManager.Client.Sid ?? "");
        if (!string.IsNullOrEmpty(SynologyManager.Client.SynoToken))
            await SecureStorage.SetAsync(SynoTokenKey(ActiveProfile.Id), SynologyManager.Client.SynoToken);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LogoutAsync()
    {
        if (ActiveProfile != null)
        {
            SecureStorage.Remove(SidKey(ActiveProfile.Id));
            SecureStorage.Remove(SynoTokenKey(ActiveProfile.Id));
        }

        if (!string.IsNullOrEmpty(SynologyManager.Client.Sid))
            await SynologyManager.Client.Auth.LogoutAsync();

        SynologyManager.Client.Sid = null;
        SynologyManager.Client.SynoToken = null;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清除已过期的 sid，保留 NAS 连接配置与登录页用户名。</summary>
    public async Task InvalidateStoredSessionAsync(string reason)
    {
        AppLog.Warn(reason);
        if (ActiveProfile != null)
        {
            SecureStorage.Remove(SidKey(ActiveProfile.Id));
            SecureStorage.Remove(SynoTokenKey(ActiveProfile.Id));
        }

        if (SynologyManager.Client != null)
        {
            SynologyManager.Client.Sid = null;
            SynologyManager.Client.SynoToken = null;
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public bool GetWifiOnly() => Preferences.Get(WifiOnlyKey, true);
    public void SetWifiOnly(bool value) => Preferences.Set(WifiOnlyKey, value);

    public bool GetConfirmBeforeDelete() => Preferences.Get(ConfirmDeleteKey, true);
    public void SetConfirmBeforeDelete(bool value) => Preferences.Set(ConfirmDeleteKey, value);

    public bool HasAcknowledgedDeleteRisk() => Preferences.Get(DeleteRiskAckKey, false);
    public void SetAcknowledgedDeleteRisk(bool value) => Preferences.Set(DeleteRiskAckKey, value);

    public static string SidKey(string profileId) => $"sid_{profileId}";
    public static string SynoTokenKey(string profileId) => $"synotoken_{profileId}";

    public string GetConnectionLabel()
    {
        if (ActiveProfile == null) return "未配置";
        var connected = !string.IsNullOrEmpty(SynologyManager.Client?.Sid);
        return $"{NasProfileDisplay.FormatTitle(ActiveProfile)} · {(connected ? "已连接" : "未登录")}";
    }
}
