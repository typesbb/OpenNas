using NSynology.Foto;

namespace OpenNas.Helpers;

public static class PhotoDetailFormatter
{
    public static string FormatTitle(Photo photo) =>
        string.IsNullOrWhiteSpace(photo.Filename) ? "未命名" : photo.Filename;

    public static string FormatSubtitle(Photo photo)
    {
        var parts = new List<string>(4);

        var time = FormatTakenTime(photo);
        if (!string.IsNullOrEmpty(time))
            parts.Add(time);

        if (photo.FileSize > 0)
            parts.Add(NasMediaCache.FormatBytes(photo.FileSize));

        var resolution = FormatResolution(photo);
        if (!string.IsNullOrEmpty(resolution))
            parts.Add(resolution);

        if (photo.IsVideo)
        {
            var duration = FormatVideoDuration(photo);
            if (!string.IsNullOrEmpty(duration))
                parts.Add(duration);
        }

        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    public static string FormatTakenTime(Photo photo)
    {
        var unix = photo.Time > 0 ? photo.Time : PhotoDateHelper.NormalizeUnixTime(photo.IndexedTime);
        if (unix <= 0)
            return "";

        var dt = DateTimeOffset.FromUnixTimeSeconds(PhotoDateHelper.NormalizeUnixTime(unix)).LocalDateTime;
        return dt.ToString("yyyy-MM-dd HH:mm");
    }

    public static string FormatResolution(Photo photo)
    {
        if (photo.IsVideo)
        {
            var meta = photo.Additional?.VideoMeta;
            if (meta is { ResolutionX: > 0, ResolutionY: > 0 })
                return $"{meta.ResolutionX}×{meta.ResolutionY}";
        }

        var res = photo.Additional?.Resolution;
        if (res is { Width: > 0, Height: > 0 })
        {
            var orientation = photo.Additional?.Orientation ?? 1;
            var swapped = orientation is 5 or 6 or 7 or 8;
            return swapped ? $"{res.Height}×{res.Width}" : $"{res.Width}×{res.Height}";
        }

        return "";
    }

    public static string FormatVideoDuration(Photo photo)
    {
        var raw = photo.Additional?.VideoMeta?.Duration ?? 0;
        if (raw <= 0)
            return "";

        // Synology Photos 通常以毫秒返回；极短值再按秒兜底。
        var ts = TimeSpan.FromMilliseconds(raw);
        if (ts.TotalSeconds < 1 && raw is >= 1 and < 86_400)
            ts = TimeSpan.FromSeconds(raw);

        if (ts.TotalSeconds < 1)
            return "";

        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
