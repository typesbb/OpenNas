namespace OpenNas.Helpers;

internal static class NasThumbnailBytes
{
    /// <summary>上传占位图或 NAS 损坏缩略图通常极小（官方 App 正常 sm 缩略图为数 KB 以上）。</summary>
    public const int MinValidThumbnailBytes = 512;

    public static bool IsLikelyPlaceholder(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return true;
        if (data.Length <= MinValidThumbnailBytes)
            return true;

        // 159 字节 MinimalJpeg 占位图（与 AppThumbnailGenerator 一致）
        return data.Length == 159
               && data[0] == 0xFF
               && data[1] == 0xD8
               && data[2] == 0xFF;
    }
}
