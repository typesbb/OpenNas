using System.Security.Cryptography;
using System.Text;
using NSynology.Foto;

namespace OpenNas.Helpers;

/// <summary>
/// NAS 媒体本地磁盘缓存。根目录为 <see cref="FileSystem.CacheDirectory"/>，
/// Android 上对应系统「应用缓存」，手机管家清理缓存时会一并删除。
/// </summary>
public static class NasMediaCache
{
    private const long MaxCacheSizeBytes = 500L * 1024 * 1024;
    private static readonly object StatsLock = new();
    private static long _approxTotalSize;
    private static int _cachedFileCount;
    private static long _cachedTotalBytes;
    private static bool _statsValid;

    private static readonly string RootDir = Path.Combine(FileSystem.CacheDirectory, "nas-media");
    private static readonly string ThumbnailDir = Path.Combine(RootDir, "thumbnails");
    private static readonly string OriginalDir = Path.Combine(RootDir, "originals");

    public static string ThumbnailsDirectory => ThumbnailDir;

    public static string GetThumbnailFilePath(int unitId, string cacheKey)
    {
        EnsureDirectories();
        var token = HashToken($"{unitId}:{cacheKey}");
        return Path.Combine(ThumbnailDir, $"{unitId}_{token}.jpg");
    }

    public static bool TryGetThumbnailFile(int unitId, string cacheKey, out string path)
    {
        path = GetThumbnailFilePath(unitId, cacheKey);
        if (!File.Exists(path))
            return false;

        try
        {
            return new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void TryInvalidateThumbnail(int unitId, string cacheKey)
    {
        var path = GetThumbnailFilePath(unitId, cacheKey);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }

        InvalidateStats();
    }

    public static async Task WriteThumbnailAsync(int unitId, string cacheKey, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0)
            return;

        EnsureDirectories();
        var path = GetThumbnailFilePath(unitId, cacheKey);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
        Interlocked.Add(ref _approxTotalSize, bytes.Length);
        InvalidateStats();
        _ = EvictIfNeededAsync();
    }

    public static string GetOriginalFilePath(Photo photo)
    {
        EnsureDirectories();
        var ext = GetSafeExtension(photo.Filename);
        var token = HashToken($"{photo.Id}:{photo.Filename}");
        return Path.Combine(OriginalDir, $"{photo.Id}_{token}{ext}");
    }

    public static bool TryGetOriginalFile(Photo photo, out string path)
    {
        path = GetOriginalFilePath(photo);
        if (!File.Exists(path))
            return false;

        try
        {
            return new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> WriteOriginalFromStreamAsync(
        Photo photo,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectories();
        var path = GetOriginalFilePath(photo);
        var temp = path + ".tmp";

        await using (var file = File.Create(temp))
        {
            await stream.CopyToAsync(file, cancellationToken);
        }

        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
        var fileSize = new FileInfo(path).Length;
        Interlocked.Add(ref _approxTotalSize, fileSize);
        InvalidateStats();
        _ = EvictIfNeededAsync();
        return path;
    }

    public static int GetFileCount()
    {
        lock (StatsLock)
        {
            if (!_statsValid)
                RefreshStats();
            return _cachedFileCount;
        }
    }

    public static long GetTotalSizeBytes()
    {
        lock (StatsLock)
        {
            if (!_statsValid)
                RefreshStats();
            return _cachedTotalBytes;
        }
    }

    public static Task ClearAllAsync()
    {
        NasThumbnailLoader.ClearMemoryCache();
        NasOriginalLoader.ClearMemoryCache();
        InvalidateStats();

        if (!Directory.Exists(RootDir))
            return Task.CompletedTask;

        try
        {
            Directory.Delete(RootDir, recursive: true);
        }
        catch
        {
            // best effort
        }

        EnsureDirectories();
        return Task.CompletedTask;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }

    private static void InvalidateStats()
    {
        lock (StatsLock)
        {
            _statsValid = false;
            _approxTotalSize = 0;
            _cachedFileCount = 0;
            _cachedTotalBytes = 0;
        }
    }

    private static void RefreshStats()
    {
        if (!Directory.Exists(RootDir))
        {
            _cachedFileCount = 0;
            _cachedTotalBytes = 0;
            _approxTotalSize = 0;
            _statsValid = true;
            return;
        }

        long total = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(RootDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
                count++;
            }
            catch { }
        }

        _cachedFileCount = count;
        _cachedTotalBytes = total;
        _approxTotalSize = total;
        _statsValid = true;
    }

    private static async Task EvictIfNeededAsync()
    {
        if (Interlocked.Read(ref _approxTotalSize) < MaxCacheSizeBytes * 9 / 10)
            return;

        // scan and evict oldest files until under cap
        List<(string Path, long Size, DateTime LastWrite)> files;
        lock (StatsLock)
        {
            RefreshStats(); // exact count before eviction
            if (_cachedTotalBytes < MaxCacheSizeBytes)
                return;

            files = Directory.EnumerateFiles(RootDir, "*", SearchOption.AllDirectories)
                .Select(f =>
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        return (Path: f, Size: fi.Length, LastWrite: fi.LastWriteTime);
                    }
                    catch { return (Path: f, Size: 0L, LastWrite: DateTime.MinValue); }
                })
                .OrderBy(f => f.LastWrite)
                .ToList();
        }

        var targetSize = MaxCacheSizeBytes * 85 / 100; // evict to 85% of cap
        var currentSize = files.Sum(f => f.Size);
        foreach (var file in files)
        {
            if (currentSize <= targetSize)
                break;

            try
            {
                File.Delete(file.Path);
                Interlocked.Add(ref _approxTotalSize, -file.Size);
                currentSize -= file.Size;
            }
            catch { }
        }

        lock (StatsLock)
            RefreshStats();
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(ThumbnailDir);
        Directory.CreateDirectory(OriginalDir);
    }

    private static string GetSafeExtension(string? filename)
    {
        var ext = Path.GetExtension(filename ?? "");
        if (string.IsNullOrEmpty(ext) || ext.Length > 8)
            return ".jpg";
        return ext.ToLowerInvariant();
    }

    private static string HashToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
