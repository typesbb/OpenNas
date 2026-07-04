using NSynology;
using NSynology.Diagnostics;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class LoginPage : ContentPage
{
    private const string LastUsernameKey = "last_username";
    private readonly ConnectionService _connection;
    private readonly IAuthNavigation _authNavigation;
    private bool _isLoggingIn;
    private bool _passwordVisible;
    private bool _initialized;

    public LoginPage(ConnectionService connection, IAuthNavigation authNavigation)
    {
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
        _connection = connection;
        _authNavigation = authNavigation;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_initialized)
        {
            _initialized = true;

            var savedUsername = Preferences.Default.Get(LastUsernameKey, "");
            if (!string.IsNullOrEmpty(savedUsername))
                usernameEntry.Text = savedUsername;

            try
            {
                await _connection.InitializeAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("加载连接配置失败", ex);
                ShowErrorBanner("加载连接配置失败",
                    "无法读取保存的连接信息，请重新配置。\n" + ex.GetType().Name);
                return;
            }
        }

        // Always refresh UI on every appearance (e.g. returning from connection settings)
        RefreshConnectionStatus();
    }

    private void RefreshConnectionStatus()
    {
        if (_connection.IsLoggedIn)
        {
            _ = _authNavigation.GoToMainShellAsync();
            return;
        }

        if (_connection.ActiveProfile != null
            && !string.IsNullOrWhiteSpace(_connection.ActiveProfile.BaseUrl))
        {
            ConnectionErrorBanner.IsVisible = false;
            ServerLabel.Text = $"当前：{_connection.ActiveProfile.BaseUrl}";
        }
        else
        {
            ShowErrorBanner("尚未配置 NAS 连接",
                "请先设置 NAS 的 IP 地址或域名，然后重试登录。");
        }
    }

    private void ShowErrorBanner(string title, string detail)
    {
        ConnectionErrorBanner.IsVisible = true;
        ConnectionErrorTitle.Text = title;
        ConnectionErrorDetail.Text = detail;
        ServerLabel.Text = "";
    }

    private void OnUsernameCompleted(object sender, EventArgs e) => passwordEntry.Focus();

    private async void OnEntryCompleted(object sender, EventArgs e) => await LoginAsync();

    private async void OnLoginButtonClicked(object sender, EventArgs e) => await LoginAsync();

    private async void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        if (_isLoggingIn) return;

        _passwordVisible = !_passwordVisible;
        passwordEntry.IsPassword = !_passwordVisible;
        TogglePasswordButton.Source = _passwordVisible ? "eye_off.svg" : "eye_open.svg";
        SemanticProperties.SetHint(TogglePasswordButton, _passwordVisible ? "隐藏密码" : "显示密码");
    }

    private async Task LoginAsync()
    {
        if (_isLoggingIn) return;

        // 检查连接是否已配置
        if (_connection.ActiveProfile == null || string.IsNullOrWhiteSpace(_connection.ActiveProfile.BaseUrl))
        {
            await UiFeedback.AlertAsync(this, "连接未配置",
                "请先点击「连接设置」配置 NAS 的 IP 地址或域名。");
            ConnectionErrorBanner.IsVisible = true;
            ConnectionErrorTitle.Text = "尚未配置 NAS 连接";
            ConnectionErrorDetail.Text = "请先设置连接，再尝试登录。";
            return;
        }

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
            SynologyManager.Init(_connection.ActiveProfile.BaseUrl);
            SynologyManager.Client.RestorePersistedPhotosDeviceId();
#if DEBUG
            if (SynologyHttpTrace.IsEnabled)
                SynologyManager.Client.ConfigureHttpTrace(true, SynologyDebugLog.Write);
#endif

            if (await SynologyManager.Client.Auth.LoginAppStyleAsync(username, password))
            {
                Preferences.Default.Set(LastUsernameKey, username.Trim());
                await _connection.OnLoginSuccessAsync();
                await _authNavigation.GoToMainShellAsync();
            }
            else
            {
                await UiFeedback.AlertAsync(this, "登录失败",
                    "无法连接 NAS，请检查：\n" +
                    "• 地址/端口是否正确\n" +
                    "• NAS 是否已开机并联网\n" +
                    "• 用户名和密码是否正确\n" +
                    "• DSM 中已安装 Synology Photos");
            }
        }
        catch (NullReferenceException ex)
        {
            AppLog.Error("登录失败：ActiveProfile 为空或 SynologyManager 未初始化", ex);
            await UiFeedback.AlertAsync(this, "连接异常",
                "NAS 连接信息异常，请点击「连接设置」重新配置地址。");
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("登录网络错误", ex);
            await UiFeedback.AlertAsync(this, "网络错误",
                $"无法连接到 NAS，请检查地址和网络连接。\n\n{ex.Message}");
        }
        catch (TaskCanceledException)
        {
            await UiFeedback.AlertAsync(this, "连接超时",
                "连接 NAS 超时，请检查：\n" +
                "• NAS 是否已开机\n" +
                "• IP 地址和端口是否正确\n" +
                "• 手机与 NAS 是否在同一网络");
        }
        catch (Exception ex)
        {
            AppLog.Error("登录失败", ex);
            await UiFeedback.AlertAsync(this, "登录失败",
                $"无法连接 NAS，请检查地址和网络连接。\n\n{ex.Message}");
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
        TogglePasswordButton.IsEnabled = !loggingIn;
        LoginButton.Text = loggingIn ? "正在连接…" : "登录";
    }

    private async void OnConnectionSettingsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new Views.ConnectionSettingsPage(_connection));

    private async void OnGoToSettingsClicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new Views.ConnectionSettingsPage(_connection));
}
