using NSynology;
using OpenNas.Data;
using OpenNas.Services;

namespace OpenNas;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(AppServices.GetRequired<LoginPage>()));
    }

    protected override async void OnStart()
    {
        base.OnStart();
        Routing.RegisterRoute(nameof(Views.ConnectionSettingsPage), typeof(Views.ConnectionSettingsPage));

        try
        {
            await AppServices.GetRequired<BackupDatabase>().EnsureInitializedAsync();

            var connection = AppServices.GetRequired<ConnectionService>();
            await connection.InitializeAsync();
            if (!connection.IsLoggedIn)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Windows.Count > 0)
                    Windows[0].Page = new AppShell();
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("自动登录跳过", ex);
        }
    }
}
