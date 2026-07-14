using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using UIKit;

namespace OpenNas.Platforms.iOS;

public class OpenNasShellRenderer : ShellRenderer
{
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem item) =>
        new OpenNasShellItemRenderer(this)
        {
            ShellItem = item
        };
}

public class OpenNasShellItemRenderer : ShellItemRenderer
{
    public OpenNasShellItemRenderer(IShellContext context) : base(context)
    {
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        var previous = ShouldSelectViewController;
        ShouldSelectViewController = (tabController, viewController) =>
        {
            if (ReferenceEquals(viewController, SelectedViewController))
            {
                MainThread.BeginInvokeOnMainThread(static () => _ = AppShell.TryRefreshCurrentPageAsync());
                return false;
            }

            return previous?.Invoke(tabController, viewController) ?? true;
        };
    }
}
