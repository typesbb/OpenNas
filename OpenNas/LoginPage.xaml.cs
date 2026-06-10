using NSynology;
using NSynology.Diagnostics;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas;

public partial class LoginPage : ContentPage
{
    private const string LastUsernameKey = "last_username";
    private readonly ConnectionService _connection;
    private bool _isLoggingIn;
    private bool _passwordVisible;

    public LoginPage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var savedUsername = Preferences.Default.Get(LastUsernameKey, "");
        if (!string.IsNullOrEmpty(savedUsername))
            usernameEntry.Text = savedUsername;

        try
        {
            await _connection.InitializeAsync();
            if (_connection.ActiveProfile != null)
                ServerLabel.Text = $"当前：{_connection.ActiveProfile.DisplayName} · {_connection.ActiveProfile.BaseUrl}";
            else
                ServerLabel.Text = "尚未配置 NAS，请先设置连接地址";
        }
        catch (Exception ex)
        {
            AppLog.Error("加载连接配置失败", ex);
            ServerLabel.Text = "加载连接配置失败";
        }
    }

    private void OnUsernameCompleted(object sender, EventArgs e) => passwordEntry.Focus();

    private async void OnEntryCompleted(object sender, EventArgs e) => await LoginAsync();

    private async void OnLoginButtonClicked(object sender, EventArgs e) => await LoginAsync();

    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        passwordEntry.IsPassword = !_passwordVisible;
        TogglePasswordButton.Text = _passwordVisible ? "隐藏" : "显示";
    }

    private async Task LoginAsync()
    {
        if (_isLoggingIn) return;

        var username = usernameEntry.Text ?? "";
        var password = passwordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await UiFeedback.AlertAsync(this, "提示", "请输入用户名和密码。");
            return;
        }

        SetLoggingIn(true);
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
                Preferences.Default.Set(LastUsernameKey, username.Trim());
                await _connection.OnLoginSuccessAsync();

                if (Application.Current?.Windows.Count > 0)
                    Application.Current.Windows[0].Page = new AppShell();
            }
            else
            {
                await UiFeedback.AlertAsync(this, "登录失败",
                    "无法连接 NAS，请检查地址、账号密码，或在 DSM 中确认已安装 Synology Photos。");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("登录失败", ex);
            await UiFeedback.AlertAsync(this, "登录", ex.Message);
        }
        finally
        {
            SetLoggingIn(false);
        }
    }

    private void SetLoggingIn(bool loggingIn)
    {
        _isLoggingIn = loggingIn;
        LoginIndicator.IsVisible = loggingIn;
        LoginIndicator.IsRunning = loggingIn;
        LoginButton.IsEnabled = !loggingIn;
        SettingsButton.IsEnabled = !loggingIn;
        usernameEntry.IsEnabled = !loggingIn;
        passwordEntry.IsEnabled = !loggingIn;
        LoginButton.Text = loggingIn ? "正在连接…" : "登录";
    }

    private async void OnConnectionSettingsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new Views.ConnectionSettingsPage(_connection));
}
