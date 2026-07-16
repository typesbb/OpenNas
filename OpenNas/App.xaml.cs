using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas;

public partial class App : Application
{
    private const string ThemeKey = "app_theme";

    public static IServiceProvider Services { get; private set; } = null!;

    internal static void ConfigureServices(IServiceProvider services) => Services = services;

    public App()
    {
        InitializeComponent();

        var themeIndex = Preferences.Default.Get(ThemeKey, 0);
        UserAppTheme = themeIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        // 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogRepository.Instance.AppendError("未处理异常", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogRepository.Instance.AppendError("未观察到的任务异常", e.Exception);
            e.SetObserved();
        };

#if ANDROID
        // Android: 捕获 async void 异常（AppDomain.UnhandledException 抓不到）
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            LogRepository.Instance.AppendError("Android 未处理异常（含 async void）", e.Exception);
            e.Handled = true;
        };
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(Services.GetRequiredService<StartupPage>());
        window.Resumed += OnWindowResumed;
        return window;
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        // 后台期间可能漏掉 ConnectivityChanged（尤其 WiFi→另一 WiFi），回前台强制确认地址
        _ = EnsureEndpointOnResumeAsync();
    }

    private static async Task EnsureEndpointOnResumeAsync()
    {
        try
        {
            var connection = Services.GetService<ConnectionService>();
            if (connection == null)
                return;

            await connection.EnsureBestEndpointOnResumeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("回前台自动确认连接失败", ex);
        }
    }
}