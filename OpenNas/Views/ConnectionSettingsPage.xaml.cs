using OpenNas.Helpers;
using OpenNas.Core.Models;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class ConnectionSettingsPage : ContentPage
{
    private readonly ConnectionService _connection;

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

        // Populate LAN
        var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan);
        PopulateAddress(lanProfile, LanProtocol, LanHost, LanPort);

        // Populate WAN
        var wanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Wan);
        PopulateAddress(wanProfile, WanProtocol, WanHost, WanPort);

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

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var lanHost = LanHost.Text?.Trim();
            var wanHost = WanHost.Text?.Trim();

            var profiles = await _connection.LoadProfilesAsync();

            UpsertOrRemoveProfile(profiles, NetworkKind.Lan, "内网", lanHost, LanProtocol, LanPort);
            UpsertOrRemoveProfile(profiles, NetworkKind.Wan, "外网", wanHost, WanProtocol, WanPort);

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

    private static void UpsertOrRemoveProfile(
        List<NasProfile> profiles,
        NetworkKind kind,
        string displayName,
        string? host,
        Picker protocol,
        Entry port)
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
        profile.BaseUrl = BuildUrl(protocol, host, port);
        profile.NetworkKind = kind;

        if (existing == null)
            profiles.Add(profile);
        else
        {
            var idx = profiles.FindIndex(p => p.Id == profile.Id);
            profiles[idx] = profile;
        }
    }

    private static string BuildUrl(Picker protocol, string host, Entry port)
    {
        var scheme = protocol.SelectedIndex == 0 ? "https" : "http";
        var portText = port.Text?.Trim();
        if (string.IsNullOrEmpty(portText))
            portText = "5001";
        return $"{scheme}://{host}:{portText}";
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private void OnAutoSwitchToggled(object? sender, ToggledEventArgs e)
    {
        _connection.AutoSwitchEnabled = e.Value;
    }
}
