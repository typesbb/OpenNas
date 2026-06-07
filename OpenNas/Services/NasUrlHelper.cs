namespace OpenNas.Services;

internal static class NasUrlHelper
{
    /// <summary>
    /// DSM 管理端口 5001 默认走 HTTPS；误填 http 会导致 Android「Cleartext HTTP not permitted」崩溃。
    /// </summary>
    public static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        url = url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        if (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 5001)
        {
            var https = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps }.Uri.ToString();
            return https.TrimEnd('/');
        }

        return url;
    }
}
