using NSynology;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ConnectionService _connection;

    public ProfilePage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
        ConnectionBanner.Bind(_connection);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConnectionBanner.Refresh();
        RefreshNasInfo();
    }

    private void RefreshNasInfo()
    {
        var profile = _connection.ActiveProfile;
        if (profile == null)
        {
            ProfileNameLabel.Text = "未配置 NAS";
            ProfileUrlLabel.Text = "";
            LoginStateLabel.Text = "";
            return;
        }

        ProfileNameLabel.Text = profile.DisplayName;
        ProfileUrlLabel.Text = profile.BaseUrl;
        var loggedIn = !string.IsNullOrEmpty(SynologyManager.Client?.Sid);
        LoginStateLabel.Text = loggedIn ? "已登录" : "未登录，请重新登录";
    }

    private async void OnConnectionClicked(object sender, EventArgs e) =>
        await ShellNavigation.PushAsync(new ConnectionSettingsPage(_connection));

    private async void OnReloginClicked(object sender, EventArgs e)
    {
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new NavigationPage(AppServices.GetRequired<LoginPage>());
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        var ok = await DisplayAlert("退出", "确定退出登录？", "退出", "取消");
        if (!ok) return;
        await _connection.LogoutAsync();
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new NavigationPage(AppServices.GetRequired<LoginPage>());
    }
}
