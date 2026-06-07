using AWebView = Android.Webkit.WebView;
using AViewGroup = global::Android.Views.ViewGroup;
using FrameLayout = Android.Widget.FrameLayout;
using GravityFlags = global::Android.Views.GravityFlags;
using LayerType = global::Android.Views.LayerType;

namespace OpenNas.Platforms.Android;

/// <summary>将原生 WebView 挂到 Activity 右下角（极小、近透明），避免遮挡 UI 且不被 OEM 节流 JS。</summary>
internal sealed class OffScreenWebViewHost : IDisposable
{
    private const int WidthPx = 320;
    private const int HeightPx = 480;

    private readonly AWebView _webView;
    private readonly AViewGroup? _parent;

    public AWebView WebView => _webView;

    private OffScreenWebViewHost(AWebView webView, AViewGroup? parent)
    {
        _webView = webView;
        _parent = parent;
    }

    public static OffScreenWebViewHost? TryAttach()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? MainActivity.Instance;
        var parent = activity?.Window?.DecorView as AViewGroup;
        if (activity == null || parent == null)
            return null;

        var webView = new AWebView(activity);
        var lp = new FrameLayout.LayoutParams(WidthPx, HeightPx)
        {
            Gravity = GravityFlags.Bottom | GravityFlags.End
        };
        webView.LayoutParameters = lp;
        webView.Alpha = 0.02f;
        webView.SetLayerType(LayerType.Hardware, null);
        parent.AddView(webView);
        webView.OnResume();
        webView.ResumeTimers();
        return new OffScreenWebViewHost(webView, parent);
    }

    public void KeepAlive()
    {
        try
        {
            _webView.OnResume();
            _webView.ResumeTimers();
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        try
        {
            _webView.StopLoading();
            _parent?.RemoveView(_webView);
            _webView.Destroy();
        }
        catch
        {
            // ignore
        }
    }
}
