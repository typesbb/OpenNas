using OpenNas.Core.Models;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class ProfilePage : ContentPage
{
    private const string LastUsernameKey = "last_username";
    private const string ThemeKey = "app_theme";
    private static readonly string[] ThemeLabels = ["跟随系统", "浅色", "深色"];
    private readonly ConnectionService _connection;
    private readonly IServiceProvider _services;
    private readonly IAuthNavigation _authNavigation;

    private int CurrentThemeIndex => Preferences.Default.Get(ThemeKey, 0);

    private static AppTheme IndexToTheme(int i) => i switch
    {
        1 => AppTheme.Light,
        2 => AppTheme.Dark,
        _ => AppTheme.Unspecified
    };

    public ProfilePage(ConnectionService connection, IServiceProvider services, IAuthNavigation authNavigation)
    {
        InitializeComponent();
        _connection = connection;
        _services = services;
        _authNavigation = authNavigation;
        _connection.ConnectionChanged += (_, _) => RefreshProfile();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshProfile();
        RefreshCacheSize();
        ThemeLabel.Text = ThemeLabels[CurrentThemeIndex];
    }

    private void RefreshProfile()
    {
        var username = Preferences.Default.Get(LastUsernameKey, "");
        UsernameLabel.Text = string.IsNullOrWhiteSpace(username) ? "未登录" : username;

        var profile = _connection.ActiveProfile;
        if (profile == null)
        {
            ConnectionDetailLabel.Text = "未配置 NAS";
            SwitchButton.IsVisible = false;
            return;
        }

        var kind = NasProfileDisplay.KindLabel(profile.NetworkKind);
        ConnectionDetailLabel.Text = $"{kind}  {profile.BaseUrl}";
        SwitchButton.IsVisible = true;
    }

    private void RefreshCacheSize()
    {
        var bytes = NasMediaCache.GetTotalSizeBytes();
        CacheSizeLabel.Text = NasMediaCache.FormatBytes(bytes);
    }

    private async void OnConnectionClicked(object? sender, EventArgs e) =>
        await ShellNavigation.PushAsync(_services.GetRequiredService<ConnectionSettingsPage>());

    private async void OnSwitchClicked(object? sender, EventArgs e)
    {
        var profiles = await _connection.LoadProfilesAsync();
        var names = profiles.Select(NasProfileDisplay.FormatTitle).ToArray();
        var pick = await DisplayActionSheetAsync("切换连接", "取消", null, names);
        if (pick == null || pick == "取消") return;
        var idx = Array.IndexOf(names, pick);
        if (idx >= 0)
        {
            await _connection.SetActiveProfileAsync(profiles[idx]);
            await UiFeedback.ToastAsync("已切换连接");
        }
    }

    private async void OnBackupSettingsClicked(object? sender, EventArgs e) =>
        await ShellNavigation.PushAsync(_services.GetRequiredService<BackupSettingsPage>());

    private async void OnLogClicked(object? sender, EventArgs e) =>
        await ShellNavigation.PushAsync(_services.GetRequiredService<LogPage>());

    private async void OnClearCacheClicked(object? sender, EventArgs e)
    {
        var bytes = NasMediaCache.GetTotalSizeBytes();
        if (bytes <= 0)
        {
            await UiFeedback.AlertAsync(this, "清理缓存", "当前没有可清理的缓存。");
            return;
        }

        var size = NasMediaCache.FormatBytes(bytes);
        var count = NasMediaCache.GetFileCount();
        var detail = count > 0 ? $"将删除约 {size}（{count} 个文件）" : $"将删除约 {size}";
        var ok = await UiFeedback.ConfirmAsync(
            this,
            "清理缓存",
            $"{detail}，下次浏览会重新从 NAS 拉取。",
            "清理",
            "取消");
        if (!ok)
            return;

        await NasMediaCache.ClearAllAsync();
        LogRepository.Instance.AppendOperation("清理缓存");
        RefreshCacheSize();
        ThemeLabel.Text = ThemeLabels[CurrentThemeIndex];
        await UiFeedback.AlertAsync(this, "清理缓存", "缓存已清理。");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var ok = await UiFeedback.ConfirmAsync(this, "退出", "确定退出登录？", "退出", "取消");
        if (!ok)
            return;

        await _connection.LogoutAsync();
        await _authNavigation.GoToLoginAsync();
    }

    private void OnThemeRowTapped(object? sender, TappedEventArgs e)
    {
        var next = (CurrentThemeIndex + 1) % 3;
        Preferences.Default.Set(ThemeKey, next);
        Application.Current!.UserAppTheme = IndexToTheme(next);
        ThemeLabel.Text = ThemeLabels[next];
    }
}
