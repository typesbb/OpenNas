using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas;

public partial class App : Application
{
    private const string ThemeKey = "app_theme";

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
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new StartupPage());
}