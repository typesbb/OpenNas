using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace NSynology.Diagnostics;

/// <summary>捕获 HttpClient 往返并写入 <see cref="SynologyHttpTrace"/>。</summary>
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

        string? requestBody = null;
        if (request.Content != null)
        {
            await request.Content.LoadIntoBufferAsync();
            if (request.Content is MultipartFormDataContent multipart)
                requestBody = await FormatMultipartSummaryAsync(multipart, cancellationToken);
            else
                requestBody = SynologyHttpTrace.Truncate(
                    await request.Content.ReadAsStringAsync(cancellationToken));
        }

        var uri = request.RequestUri?.ToString() ?? "(null)";
        SynologyHttpTrace.Write(
            $"""
            [NSynology #{id}] >>> {request.Method} {uri}
            Request-Headers:
            {SynologyHttpTrace.FormatHeaders(request.Headers)}
            {(request.Content != null ? $"Content-Headers:\n{SynologyHttpTrace.FormatHeaders(request.Content.Headers)}\n" : "")}Request-Body:
            {requestBody ?? "(none)"}
            """);

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            SynologyHttpTrace.Write($"[NSynology #{id}] <<< EXCEPTION after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        sw.Stop();
        string responseBody;
        if (response.Content != null)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isBinary = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);

            if (isBinary)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                responseBody = $"(binary {bytes.Length} bytes, content-type={mediaType})";
                var binary = new ByteArrayContent(bytes);
                if (response.Content.Headers.ContentType != null)
                    binary.Headers.ContentType = response.Content.Headers.ContentType;
                response.Content = binary;
            }
            else
            {
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var charset = response.Content.Headers.ContentType?.CharSet ?? "utf-8";
                Encoding encoding;
                try
                {
                    encoding = Encoding.GetEncoding(charset);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }

                var textMediaType = string.IsNullOrEmpty(mediaType) ? "text/plain" : mediaType;
                response.Content = new StringContent(responseBody, encoding, textMediaType);
            }

            foreach (var h in response.Content.Headers)
            {
                if (!h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    response.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
        }
        else
        {
            responseBody = "(no content)";
        }

        SynologyHttpTrace.Write(
            $"""
            [NSynology #{id}] <<< {(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds}ms)
            Response-Headers:
            {SynologyHttpTrace.FormatHeaders(response.Headers)}
            {(response.Content != null ? $"Content-Headers:\n{SynologyHttpTrace.FormatHeaders(response.Content.Headers)}\n" : "")}Response-Body:
            {SynologyHttpTrace.Truncate(responseBody)}
            """);

        return response;
    }

    private static async Task<string> FormatMultipartSummaryAsync(
        MultipartFormDataContent multipart,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(multipart/form-data)");
        foreach (var part in multipart)
        {
            var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "(part)";
            if (part.Headers.ContentDisposition?.FileName != null)
            {
                var fileName = part.Headers.ContentDisposition.FileName.Trim('"');
                var len = part.Headers.ContentLength;
                var mime = part.Headers.ContentType?.MediaType ?? "application/octet-stream";
                sb.AppendLine($"  [{name}] file={fileName}, {len ?? -1} bytes, {mime}");
                continue;
            }

            var text = await part.ReadAsStringAsync(cancellationToken);
            sb.AppendLine($"  [{name}] = {SynologyHttpTrace.Truncate(text)}");
        }

        return sb.ToString().TrimEnd();
    }
}
