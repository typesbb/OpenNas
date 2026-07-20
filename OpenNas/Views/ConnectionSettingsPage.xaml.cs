using OpenNas.Helpers;
using OpenNas.Core.Models;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class ConnectionSettingsPage : ContentPage
{
    private const string QuickConnectSuffix = ".cn5.quickconnect.cn";

    private readonly ConnectionService _connection;
    private bool _suppressWanHostTextChanged;

    public ConnectionSettingsPage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
        LanProtocol.SelectedIndex = 0;   // default https
        WanProtocol.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadCurrentConfigAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("加载连接配置页面失败", ex);
            StatusLabel.Text = "加载配置失败，请重新输入连接信息。";
        }
    }

    private async Task LoadCurrentConfigAsync()
    {
        var profiles = await _connection.LoadProfilesAsync();

        var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan);
        PopulateAddress(lanProfile, LanProtocol, LanHost, LanPort);

        var wanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Wan);
        PopulateWanAddress(wanProfile);

        AutoSwitchToggle.IsToggled = _connection.AutoSwitchEnabled;

        var active = _connection.ActiveProfile;
        if (active != null && !string.IsNullOrWhiteSpace(active.BaseUrl))
            StatusLabel.Text = $"当前：{active.BaseUrl}";
        else
            StatusLabel.Text = "尚未配置连接";
    }

    private static void PopulateAddress(NasProfile? profile, Picker protocol, Entry host, Entry port)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.BaseUrl))
            return;

        if (Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var uri))
        {
            protocol.SelectedIndex = uri.Scheme == Uri.UriSchemeHttps ? 0 : 1;
            host.Text = uri.Host;
            port.Text = uri.Port.ToString();
        }
        else
        {
            host.Text = profile.BaseUrl;
        }
    }

    private void PopulateWanAddress(NasProfile? profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            UpdateWanHostSuffixVisibility();
            return;
        }

        _suppressWanHostTextChanged = true;
        try
        {
            if (Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var uri))
            {
                WanProtocol.SelectedIndex = uri.Scheme == Uri.UriSchemeHttps ? 0 : 1;
                WanPort.Text = uri.Port.ToString();
                WanHost.Text = StripQuickConnectSuffixForDisplay(uri.Host);
            }
            else
            {
                WanHost.Text = StripQuickConnectSuffixForDisplay(profile.BaseUrl);
            }
        }
        finally
        {
            _suppressWanHostTextChanged = false;
        }

        UpdateWanHostSuffixVisibility();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var lanHost = LanHost.Text?.Trim();
            var wanHost = ResolveWanHostForSave();

            var profiles = await _connection.LoadProfilesAsync();

            UpsertOrRemoveProfile(profiles, NetworkKind.Lan, "内网", lanHost, LanProtocol, LanPort, defaultPort: "5001");
            UpsertOrRemoveProfile(profiles, NetworkKind.Wan, "外网", wanHost, WanProtocol, WanPort, defaultPort: "443");

            await _connection.SaveProfilesAsync(profiles);

            var active = _connection.ActiveProfile;
            if (active == null || !profiles.Any(p => p.Id == active.Id))
            {
                active = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan)
                         ?? profiles.FirstOrDefault();
            }

            if (active != null)
            {
                await _connection.SetActiveProfileAsync(active);
                StatusLabel.Text = $"当前：{active.BaseUrl}";
            }
            else
            {
                await _connection.ClearActiveProfileAsync();
                StatusLabel.Text = "尚未配置连接";
            }

            LogRepository.Instance.AppendOperation("修改连接设置");
            await UiFeedback.ToastAsync("连接配置已保存");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("保存连接配置失败", ex);
            StatusLabel.Text = "保存配置失败，请重试。";
        }
    }

    private void OnWanHostTextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateWanHostSuffixVisibility();

    private void UpdateWanHostSuffixVisibility()
    {
        if (_suppressWanHostTextChanged)
            return;

        WanHostSuffix.IsVisible = ShouldShowQuickConnectSuffix(WanHost.Text);
    }

    /// <summary>保存时：若正显示补全段，则拼上后缀。</summary>
    private string? ResolveWanHostForSave()
    {
        var raw = WanHost.Text?.Trim();
        if (string.IsNullOrEmpty(raw))
            return raw;

        if (ShouldShowQuickConnectSuffix(raw))
            return raw.TrimEnd('.') + QuickConnectSuffix;

        return NormalizeHostInput(raw);
    }

    private static string StripQuickConnectSuffixForDisplay(string host)
    {
        host = NormalizeHostInput(host);
        if (host.EndsWith(QuickConnectSuffix, StringComparison.OrdinalIgnoreCase))
            return host[..^QuickConnectSuffix.Length];
        return host;
    }

    private static bool ShouldShowQuickConnectSuffix(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        host = NormalizeHostInput(host);
        if (System.Net.IPAddress.TryParse(host, out _))
            return false;
        if (host.Contains("quickconnect.", StringComparison.OrdinalIgnoreCase))
            return false;
        // 其它完整域名（含点）不补全；纯 ID 才显示后缀段
        return !host.Contains('.');
    }

    private static string NormalizeHostInput(string host)
    {
        host = host.Trim().TrimEnd('.');
        if (host.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(host, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;
        return host;
    }

    private static void UpsertOrRemoveProfile(
        List<NasProfile> profiles,
        NetworkKind kind,
        string displayName,
        string? host,
        Picker protocol,
        Entry port,
        string defaultPort)
    {
        var existing = profiles.FirstOrDefault(p => p.NetworkKind == kind);
        if (string.IsNullOrWhiteSpace(host))
        {
            if (existing != null)
                profiles.Remove(existing);
            return;
        }

        var profile = existing ?? new NasProfile { NetworkKind = kind };
        profile.DisplayName = displayName;
        profile.BaseUrl = BuildUrl(protocol, host, port, defaultPort);
        profile.NetworkKind = kind;

        if (existing == null)
            profiles.Add(profile);
        else
        {
            var idx = profiles.FindIndex(p => p.Id == profile.Id);
            profiles[idx] = profile;
        }
    }

    private static string BuildUrl(Picker protocol, string host, Entry port, string defaultPort)
    {
        var scheme = protocol.SelectedIndex == 0 ? "https" : "http";
        var portText = port.Text?.Trim();
        if (string.IsNullOrEmpty(portText))
            portText = defaultPort;
        return $"{scheme}://{host}:{portText}";
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private void OnAutoSwitchToggled(object? sender, ToggledEventArgs e)
    {
        _connection.AutoSwitchEnabled = e.Value;
    }
}
