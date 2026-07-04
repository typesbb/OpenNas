using OpenNas.Views;

namespace OpenNas.Services;

public sealed class AuthNavigation(IServiceProvider services) : IAuthNavigation
{
    public Task GoToLoginAsync() =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Application.Current?.Windows.Count > 0)
            {
                var loginPage = services.GetRequiredService<LoginPage>();
                Application.Current.Windows[0].Page = new NavigationPage(loginPage);
            }
        });

    public Task GoToMainShellAsync() =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Application.Current?.Windows.Count > 0)
            {
                var shell = services.GetRequiredService<AppShell>();
                Application.Current.Windows[0].Page = shell;
            }
        });
}
