using NSynology;
using NSynology.Diagnostics;
using OpenNas.Services;

namespace OpenNas;

public partial class LoginPage : ContentPage
{
    private readonly ConnectionService _connection;

    public LoginPage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _connection.InitializeAsync();
            if (_connection.ActiveProfile != null)
                ServerLabel.Text = $"当前：{_connection.ActiveProfile.DisplayName} · {_connection.ActiveProfile.BaseUrl}";
        }
        catch (Exception ex)
        {
            AppLog.Error("加载连接配置失败", ex);
        }
    }

    private async void OnEntryCompleted(object sender, EventArgs e) => await LoginAsync();

    private async void OnLoginButtonClicked(object sender, EventArgs e) => await LoginAsync();

    private async Task LoginAsync()
    {
        var username = usernameEntry.Text ?? "";
        var password = passwordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("提示", "请输入用户名和密码。", "确定");
            return;
        }

        try
        {
            if (_connection.ActiveProfile != null)
            {
                SynologyManager.Init(_connection.ActiveProfile.BaseUrl);
                SynologyManager.Client.RestorePersistedPhotosDeviceId();
#if DEBUG
                if (SynologyHttpTrace.IsEnabled)
                    SynologyManager.Client.ConfigureHttpTrace(true, SynologyDebugLog.Write);
#endif
            }

            if (await SynologyManager.Client.Auth.LoginOfficialAppStyleAsync(username, password))
            {
                await _connection.OnLoginSuccessAsync();

                if (Application.Current?.Windows.Count > 0)
                    Application.Current.Windows[0].Page = new AppShell();
            }
            else
            {
                await DisplayAlert("登录失败", "无法连接 NAS，请检查地址、账号密码，或在 DSM 中确认已安装 Synology Photos。", "确定");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("登录失败", ex);
            await DisplayAlert("登录", ex.Message, "确定");
        }
    }

    private async void OnConnectionSettingsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new Views.ConnectionSettingsPage(_connection));
}
