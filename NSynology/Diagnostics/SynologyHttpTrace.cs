using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace NSynology.Diagnostics;

/// <summary>
/// NSynology HTTP 诊断日志（供 SDK 宿主在调试时启用）。默认关闭，不影响正式发布。
/// </summary>
public static class SynologyHttpTrace
{
    private static readonly object Gate = new();

    /// <summary>是否记录每次 HTTP 请求/响应。</summary>
    public static bool IsEnabled { get; set; }

    /// <summary>单段响应/请求体最大字符数（超出截断）。</summary>
    public static int MaxBodyChars { get; set; } = 180;

    /// <summary>是否脱敏 passwd、SynoToken、sid 等。</summary>
    public static bool RedactSecrets { get; set; } = true;

    /// <summary>日志输出；未设置时使用 <see cref="Debug.WriteLine"/>。</summary>
    public static Action<string>? Logger { get; set; }

    /// <summary>启用跟踪。建议在创建 <see cref="SynologyClient"/> 之前调用，或随后对客户端调用 <c>RecreateHttpClient()</c>。</summary>
    public static void Enable(Action<string>? logger = null)
    {
        IsEnabled = true;
        Logger = logger;
    }

    public static void Disable()
    {
        IsEnabled = false;
    }

    internal static void Write(string message)
    {
        if (!IsEnabled)
            return;

        var line = message;
        if (RedactSecrets)
            line = Redact(line);

        var log = Logger ?? (static s => Debug.WriteLine(s));
        lock (Gate)
        {
            log(line);
        }
    }

    internal static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = Regex.Replace(
            text,
            @"(?i)(passwd|password|passwd=)([^&\s""]+)",
            m => $"{m.Groups[1].Value}=***",
            RegexOptions.CultureInvariant);

        text = Regex.Replace(
            text,
            @"(?i)(SynoToken=)([^&\s""]+)",
            m => $"{m.Groups[1].Value}***",
            RegexOptions.CultureInvariant);

        text = Regex.Replace(
            text,
            @"(?i)(_sid=)([^&\s""]+)",
            m => $"{m.Groups[1].Value}***",
            RegexOptions.CultureInvariant);

        text = Regex.Replace(
            text,
            @"(?i)(""sid""\s*:\s*"")([^""]+)("")",
            m => $"{m.Groups[1].Value}***{m.Groups[3].Value}",
            RegexOptions.CultureInvariant);

        text = Regex.Replace(
            text,
            @"(?i)(""synotoken""\s*:\s*"")([^""]+)("")",
            m => $"{m.Groups[1].Value}***{m.Groups[3].Value}",
            RegexOptions.CultureInvariant);

        return text;
    }

    internal static string Truncate(string? text) => Truncate(text, MaxBodyChars);

    internal static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return "(empty)";

        if (text.Length <= maxChars)
            return text;

        return text[..maxChars] + "…";
    }

    internal static string FormatHeaders(HttpHeaders headers)
    {
        var sb = new StringBuilder();
        foreach (var h in headers)
        {
            foreach (var v in h.Value)
                sb.AppendLine($"  {h.Key}: {v}");
        }

        return sb.Length == 0 ? "  (none)" : sb.ToString().TrimEnd();
    }
}
