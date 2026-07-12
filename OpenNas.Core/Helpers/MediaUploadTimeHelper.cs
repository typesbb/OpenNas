using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNas.Core.Helpers;

public static partial class MediaUploadTimeHelper
{
    /// <summary>将 MediaStore / 文件系统时间归一为 Unix 秒（兼容毫秒输入）。</summary>
    public static long NormalizeToUnixSeconds(long raw)
    {
        if (raw <= 0)
            return 0;
        return raw < 10_000_000_000L ? raw : raw / 1000;
    }

    /// <summary>优先拍摄时间，回退修改时间。</summary>
    public static long ResolveUploadMtimeSeconds(long dateTaken, long dateModified)
    {
        var taken = NormalizeToUnixSeconds(dateTaken);
        if (taken > 0)
            return taken;

        return NormalizeToUnixSeconds(dateModified);
    }

    public static long ResolveUploadMtimeSeconds(long dateTaken, long dateModified, long fallbackUnixSeconds)
    {
        var resolved = ResolveUploadMtimeSeconds(dateTaken, dateModified);
        return resolved > 0 ? resolved : fallbackUnixSeconds;
    }

    public static long TryParseFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return 0;

        var weChat = MmExportTimestamp().Match(fileName);
        if (weChat.Success && long.TryParse(weChat.Groups[1].Value, out var weChatMs))
            return NormalizeToUnixSeconds(weChatMs);

        var camera = CameraFileTimestamp().Match(fileName);
        if (camera.Success
            && DateTime.TryParseExact(
                camera.Groups[1].Value,
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var cameraDt))
        {
            return new DateTimeOffset(cameraDt).ToUnixTimeSeconds();
        }

        return 0;
    }

    [GeneratedRegex(@"mmexport(\d{13})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MmExportTimestamp();

    [GeneratedRegex(@"(?:IMG|VID|PXL|MVIMG)_(\d{8}_\d{6})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CameraFileTimestamp();
}
