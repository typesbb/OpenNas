using Android.Graphics;
using Android.Util;

namespace OpenNas.Platforms.Android;

internal static class AlbumGridBitmapCache
{
    private const int MaxEntries = 72;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap> Cache = new();
    private static readonly LinkedList<string> Order = new();

    public static Bitmap? Get(string key)
    {
        lock (Gate)
            return Cache.TryGetValue(key, out var bmp) && !bmp.IsRecycled ? bmp : null;
    }

    public static void Put(string key, Bitmap bitmap)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var old) && !ReferenceEquals(old, bitmap))
                old.Recycle();

            Cache[key] = bitmap;
            Order.Remove(key);
            Order.AddLast(key);

            while (Order.Count > MaxEntries)
            {
                var evict = Order.First!.Value;
                Order.RemoveFirst();
                if (Cache.Remove(evict, out var removed) && !removed.IsRecycled)
                    removed.Recycle();
            }
        }
    }

    public static Bitmap? Decode(string key, byte[] bytes)
    {
        var cached = Get(key);
        if (cached != null)
            return cached;

        try
        {
            var bmp = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (bmp == null)
                return null;

            Put(key, bmp);
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn("OpenNas", $"网格缩略图解码失败: {ex.Message}");
            return null;
        }
    }
}
