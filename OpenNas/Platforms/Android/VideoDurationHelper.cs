using Android.Media;
using NSynology.Foto;

namespace OpenNas.Platforms.Android;

internal static class VideoDurationHelper
{
    public static TimeSpan? TryGetFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var retriever = new MediaMetadataRetriever();
        try
        {
            retriever.SetDataSource(path);
            var raw = retriever.ExtractMetadata(MetadataKey.Duration);
            if (!long.TryParse(raw, out var ms) || ms <= 0)
                return null;

            return TimeSpan.FromMilliseconds(ms);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                retriever.Release();
            }
            catch
            {
                // ignore
            }
        }
    }

    public static TimeSpan? TryGetFromPhoto(Photo? photo)
    {
        var raw = photo?.Additional?.VideoMeta?.Duration ?? 0;
        if (raw <= 0)
            return null;

        // Synology Photos 通常以毫秒返回；极短值再按秒兜底。
        var duration = TimeSpan.FromMilliseconds(raw);
        if (duration.TotalSeconds < 1 && raw is >= 1 and < 86_400)
            return TimeSpan.FromSeconds(raw);

        return duration;
    }
}
