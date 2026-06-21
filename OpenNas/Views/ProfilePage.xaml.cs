using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas.Views;

public partial class ProfilePage : ContentPage
{
    private const string LastUsernameKey = "last_username";
    private const string ThemeKey = "app_theme";
    private static readonly string[] ThemeLabels = ["跟随系统", "浅色", "深色"];
    private readonly ConnectionService _connection;

    private int CurrentThemeIndex => Preferences.Default.Get(ThemeKey, 0);

    private static AppTheme IndexToTheme(int i) => i switch
    {
        1 => AppTheme.Light,
        2 => AppTheme.Dark,
        _ => AppTheme.Unspecified
    };

    public ProfilePage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
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
            ProfileNameLabel.Text = "未配置 NAS";
            ProfileUrlLabel.Text = "";
            return;
        }

        ProfileNameLabel.Text = NasProfileDisplay.FormatTitle(profile);
        ProfileUrlLabel.Text = profile.BaseUrl;
    }

    private void RefreshCacheSize()
    {
        var bytes = NasMediaCache.GetTotalSizeBytes();
        CacheSizeLabel.Text = NasMediaCache.FormatBytes(bytes);
    }

    private async void OnConnectionClicked(object? sender, EventArgs e) =>
        await ShellNavigation.PushAsync(new ConnectionSettingsPage(_connection));

    private async void OnBackupSettingsClicked(object? sender, EventArgs e) =>
        await ShellNavigation.PushAsync(new BackupSettingsPage(_connection));

    private async void OnLogClicked(object? sender, EventArgs e) =>
        await ShellNavigation.PushAsync(AppServices.GetRequired<LogPage>());

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
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new NavigationPage(AppServices.GetRequired<LoginPage>());
    }

    private void OnThemeRowTapped(object? sender, TappedEventArgs e)
    {
        var next = (CurrentThemeIndex + 1) % 3;
        Preferences.Default.Set(ThemeKey, next);
        Application.Current!.UserAppTheme = IndexToTheme(next);
        ThemeLabel.Text = ThemeLabels[next];
    }
}