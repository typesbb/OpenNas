using OpenNas.Helpers;
using OpenNas.Models;
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
        await LoadCurrentConfigAsync();
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
        // Build LAN address
        var lanHost = LanHost.Text?.Trim();
        if (string.IsNullOrWhiteSpace(lanHost))
        {
            await UiFeedback.AlertAsync(this, "错误", "请输入内网 (LAN) 的 IP 地址或域名");
            return;
        }

        // Build WAN address (optional)
        var wanHost = WanHost.Text?.Trim();

        var profiles = await _connection.LoadProfilesAsync();

        // Save/update LAN profile
        var lanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Lan)
                         ?? new NasProfile { NetworkKind = NetworkKind.Lan };
        lanProfile.DisplayName = "内网";
        lanProfile.BaseUrl = BuildUrl(LanProtocol, lanHost, LanPort);
        lanProfile.NetworkKind = NetworkKind.Lan;

        if (!profiles.Any(p => p.Id == lanProfile.Id))
            profiles.Add(lanProfile);
        else
        {
            var idx = profiles.FindIndex(p => p.Id == lanProfile.Id);
            profiles[idx] = lanProfile;
        }

        // Save/update WAN profile (only if host is entered)
        if (!string.IsNullOrWhiteSpace(wanHost))
        {
            var wanProfile = profiles.FirstOrDefault(p => p.NetworkKind == NetworkKind.Wan)
                             ?? new NasProfile { NetworkKind = NetworkKind.Wan, DisplayName = "外网" };
            wanProfile.DisplayName = "外网";
            wanProfile.BaseUrl = BuildUrl(WanProtocol, wanHost, WanPort);
            wanProfile.NetworkKind = NetworkKind.Wan;

            if (!profiles.Any(p => p.Id == wanProfile.Id))
                profiles.Add(wanProfile);
            else
            {
                var idx = profiles.FindIndex(p => p.Id == wanProfile.Id);
                profiles[idx] = wanProfile;
            }
        }

        await _connection.SaveProfilesAsync(profiles);

        // Keep the same active profile if it still exists, otherwise set to LAN
        var active = _connection.ActiveProfile;
        if (active == null || !profiles.Any(p => p.Id == active.Id))
            active = lanProfile;

        await _connection.SetActiveProfileAsync(active);
        LogRepository.Instance.AppendOperation("修改连接设置");

        StatusLabel.Text = $"当前：{active.BaseUrl}";
        await UiFeedback.ToastAsync("连接配置已保存");
        await Navigation.PopAsync();
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
