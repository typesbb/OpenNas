using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NSynology.Diagnostics;

/// <summary>捕获 HttpClient 往返并写入 <see cref="SynologyHttpTrace"/>（单行摘要）。</summary>
internal sealed class SynologyHttpTraceHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private static int _sequence;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!SynologyHttpTrace.IsEnabled)
            return await base.SendAsync(request, cancellationToken);

        var id = Interlocked.Increment(ref _sequence);
        var sw = Stopwatch.StartNew();
        var requestSummary = await BuildRequestSummaryAsync(request, cancellationToken);

        SynologyHttpTrace.Write($"#{id} >>> {request.Method} {requestSummary}");

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            SynologyHttpTrace.Write($"#{id} <<< ERR {sw.ElapsedMilliseconds}ms {ex.GetType().Name}");
            throw;
        }

        sw.Stop();
        var responseBody = await ReadResponseTextAsync(response, cancellationToken);
        var responseSummary = SummarizeResponseBody(responseBody, (int)response.StatusCode);
        SynologyHttpTrace.Write($"#{id} <<< {(int)response.StatusCode} {sw.ElapsedMilliseconds}ms {responseSummary}");

        return response;
    }

    private static async Task<string> BuildRequestSummaryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? "(null)";
        var path = request.RequestUri?.AbsolutePath ?? uri;

        if (request.Content is null)
            return ShortenUri(uri);

        var mediaType = request.Content.Headers.ContentType?.MediaType ?? "";
        var length = request.Content.Headers.ContentLength;

        if (mediaType.Contains("multipart", StringComparison.OrdinalIgnoreCase))
            return $"{path} multipart {FormatBytes(length ?? 0)}";

        if (length > 64 * 1024
            || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return $"{path} {mediaType} {FormatBytes(length ?? 0)}";
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        RestoreStringContent(request, body, mediaType);

        var api = MatchFormField(body, "api") ?? MatchQuery(uri, "api");
        var method = MatchFormField(body, "method") ?? MatchQuery(uri, "method");
        if (!string.IsNullOrEmpty(api))
        {
            var hint = string.IsNullOrEmpty(method) ? api : $"{api}.{method}";
            return $"{path} {hint}";
        }

        return $"{path} {SynologyHttpTrace.Truncate(body)}";
    }

    private static async Task<string> ReadResponseTextAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
            return "";

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            ReplaceContent(response, bytes, mediaType);
            return $"(binary {bytes.Length} bytes)";
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        ReplaceContent(response, text, mediaType);
        return text;
    }

    private static void ReplaceContent(HttpResponseMessage response, byte[] bytes, string mediaType)
    {
        var binary = new ByteArrayContent(bytes);
        if (!string.IsNullOrEmpty(mediaType))
            binary.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        response.Content = binary;
    }

    private static void ReplaceContent(HttpResponseMessage response, string text, string mediaType)
    {
        var charset = (response.Content?.Headers.ContentType?.CharSet ?? "utf-8").Trim('"');
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(charset); }
        catch { encoding = Encoding.UTF8; }

        var type = string.IsNullOrEmpty(mediaType) ? "text/plain" : mediaType;
        response.Content = new StringContent(text, encoding, type);
    }

    private static void RestoreStringContent(HttpRequestMessage request, string body, string mediaType)
    {
        var type = string.IsNullOrEmpty(mediaType) ? "application/x-www-form-urlencoded" : mediaType;
        request.Content = new StringContent(body, Encoding.UTF8, type);
    }

    private static string SummarizeResponseBody(string body, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
            return statusCode >= 400 ? "empty" : "ok";

        if (body.StartsWith("(binary ", StringComparison.Ordinal))
            return body;

        var oneLine = body.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (oneLine.Contains("\"success\"", StringComparison.Ordinal))
        {
            var success = Regex.Match(oneLine, @"""success""\s*:\s*(true|false)");
            if (success.Success)
            {
                if (success.Groups[1].Value == "false")
                {
                    var code = Regex.Match(oneLine, @"""code""\s*:\s*(-?\d+)");
                    return code.Success ? $"fail code={code.Groups[1].Value}" : "fail";
                }

                var id = Regex.Match(oneLine, @"""id""\s*:\s*(\d+)");
                var action = Regex.Match(oneLine, @"""action""\s*:\s*""([^""]+)""");
                if (id.Success || action.Success)
                {
                    var parts = new List<string>();
                    if (action.Success)
                        parts.Add(action.Groups[1].Value);
                    if (id.Success)
                        parts.Add($"id={id.Groups[1].Value}");
                    return string.Join(' ', parts);
                }

                return "ok";
            }
        }

        if (oneLine.Contains("\"error\"", StringComparison.Ordinal))
        {
            var code = Regex.Match(oneLine, @"""code""\s*:\s*(-?\d+)");
            return code.Success
                ? $"error code={code.Groups[1].Value}"
                : "error";
        }

        if (statusCode >= 400)
            return SynologyHttpTrace.Truncate(oneLine, 240);

        return oneLine.Length > 128 ? $"({oneLine.Length} chars)" : SynologyHttpTrace.Truncate(oneLine);
    }

    private static string? MatchFormField(string body, string name)
    {
        var match = Regex.Match(
            body,
            $@"(?:^|&){Regex.Escape(name)}=([^&]+)",
            RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    private static string? MatchQuery(string uri, string name)
    {
        var match = Regex.Match(uri, $@"(?:[?&]){Regex.Escape(name)}=([^&]+)", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    private static string ShortenUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return u.AbsolutePath + (string.IsNullOrEmpty(u.Query) ? "" : " …");
        return uri;
    }

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes}B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1}KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1}MB"
        };
}
