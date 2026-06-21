using OpenNas.Core.Data;
using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas.Views;

public partial class StartupPage : ContentPage
{
    public StartupPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await BootstrapAsync();
    }

    private static async Task BootstrapAsync()
    {
        try
        {
            Routing.RegisterRoute(nameof(ConnectionSettingsPage), typeof(ConnectionSettingsPage));

            await AppServices.GetRequired<BackupDatabase>().EnsureInitializedAsync();

            var connection = AppServices.GetRequired<ConnectionService>();
            await connection.InitializeAsync();

            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = connection.IsLoggedIn
                    ? new AppShell()
                    : new NavigationPage(AppServices.GetRequired<LoginPage>());
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("启动初始化失败", ex);
            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new NavigationPage(AppServices.GetRequired<LoginPage>());
        }
    }
}
