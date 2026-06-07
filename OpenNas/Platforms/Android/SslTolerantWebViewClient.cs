using Android.Net.Http;
using Android.Webkit;
using AWebView = Android.Webkit.WebView;

namespace OpenNas.Platforms.Android;

/// <summary>允许内网 NAS 自签 HTTPS（HttpClient 已忽略校验，WebView 默认会拦截）。</summary>
internal class SslTolerantWebViewClient : WebViewClient
{
    private readonly Action<string?>? _onPageFinished;

    public SslTolerantWebViewClient(Action<string?>? onPageFinished = null) => _onPageFinished = onPageFinished;

    public override void OnReceivedSslError(AWebView? view, SslErrorHandler? handler, SslError? error) =>
        handler?.Proceed();

    public override void OnPageFinished(AWebView? view, string? url)
    {
        base.OnPageFinished(view, url);
        _onPageFinished?.Invoke(url);
    }
}
