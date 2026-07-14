using Android.Content;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace OpenNas.Platforms.Android;

public class OpenNasShellRenderer : ShellRenderer
{
    public OpenNasShellRenderer(Context context) : base(context)
    {
    }

    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem) =>
        new OpenNasShellItemRenderer(this);
}

public class OpenNasShellItemRenderer : ShellItemRenderer
{
    public OpenNasShellItemRenderer(IShellContext shellContext) : base(shellContext)
    {
    }

    protected override void OnTabReselected(ShellSection shellSection)
    {
        base.OnTabReselected(shellSection);
        MainThread.BeginInvokeOnMainThread(static () => _ = AppShell.TryRefreshCurrentPageAsync());
    }
}
