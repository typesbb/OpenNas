using Android.Webkit;
using NSynology;
using AWebView = Android.Webkit.WebView;

namespace OpenNas.Platforms.Android;

internal static class AndroidCookieBridge
{
    internal static void SyncToWebView(string baseUrl, SynologyClient client, AWebView? webView = null)
    {
        var cm = CookieManager.Instance!;
        cm.SetAcceptCookie(true);
        if (webView != null)
            cm.SetAcceptThirdPartyCookies(webView, true);

        var secure = baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        foreach (var part in client.BuildCookieHeader().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
            {
                var suffix = secure ? "; path=/; secure" : "; path=/";
                cm.SetCookie(baseUrl, $"{part}{suffix}");
            }
        }

        cm.Flush();
    }

    internal static string? ReadNasCookies(params string[] baseUrls)
    {
        CookieManager.Instance?.Flush();
        var paths = new[]
        {
            "",
            "/photo/",
            "/webman/",
            "/webman/3rdparty/SynologyPhotos/",
            "/socket.io/"
        };

        foreach (var baseUrl in baseUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            foreach (var path in paths)
            {
                var url = string.IsNullOrEmpty(path) ? baseUrl.TrimEnd('/') : $"{baseUrl.TrimEnd('/')}{path}";
                var raw = CookieManager.Instance?.GetCookie(url);
                if (!string.IsNullOrWhiteSpace(raw) && raw.Contains("_SSID=", StringComparison.Ordinal))
                    return NormalizeCookieHeader(raw);
            }
        }

        foreach (var baseUrl in baseUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            var fallback = CookieManager.Instance?.GetCookie(baseUrl.TrimEnd('/'));
            if (!string.IsNullOrWhiteSpace(fallback) && fallback.Contains("_SSID=", StringComparison.Ordinal))
                return NormalizeCookieHeader(fallback);
        }

        return null;
    }

    internal static bool ContainsSsid(string? cookieHeader) =>
        !string.IsNullOrEmpty(cookieHeader) && cookieHeader.Contains("_SSID=", StringComparison.Ordinal);

    private static string NormalizeCookieHeader(string raw) =>
        raw.Replace('\n', ';').Trim().TrimEnd(';');
}
