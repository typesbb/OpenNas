using OpenNas.Core.Data;
using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas.Views;

public partial class StartupPage : ContentPage
{
    private readonly BackupDatabase _db;
    private readonly ConnectionService _connection;
    private readonly IAuthNavigation _authNavigation;
    private bool _bootstrapped;

    public StartupPage(BackupDatabase db, ConnectionService connection, IAuthNavigation authNavigation)
    {
        InitializeComponent();
        _db = db;
        _connection = connection;
        _authNavigation = authNavigation;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_bootstrapped)
            return;

        _bootstrapped = true;
        await BootstrapAsync();
    }

    private async Task BootstrapAsync()
    {
        try
        {
            await _db.EnsureInitializedAsync();
            await _connection.InitializeAsync();

            if (Application.Current?.Windows.Count > 0)
            {
                if (_connection.IsLoggedIn)
                    await _authNavigation.GoToMainShellAsync();
                else
                    await _authNavigation.GoToLoginAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("启动初始化失败", ex);
            if (Application.Current?.Windows.Count > 0)
                await _authNavigation.GoToLoginAsync();
        }
    }
}
