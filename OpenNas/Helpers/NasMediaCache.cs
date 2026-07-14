using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NSynology.Foto;

namespace OpenNas.Helpers;

/// <summary>
/// NAS 媒体本地磁盘缓存。根目录为 <see cref="FileSystem.CacheDirectory"/>，
/// Android 上对应系统「应用缓存」，手机管家清理缓存时会一并删除。
/// 缩略图与原片分目录、分配额、分淘汰，避免大视频撑破总上限后误伤缩略图。
/// </summary>
public static class NasMediaCache
{
    /// <summary>缩略图配额（独立）。</summary>
    private const long MaxThumbnailCacheBytes = 150L * 1024 * 1024;

    /// <summary>原片/视频配额（独立）。</summary>
    private const long MaxOriginalCacheBytes = 500L * 1024 * 1024;

    private static readonly object StatsLock = new();
    private static readonly ConcurrentDictionary<string, byte> ProtectedPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private static int _cachedFileCount;
    private static long _cachedTotalBytes;
    private static long _cachedThumbBytes;
    private static long _cachedOriginalBytes;
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
        InvalidateStats();
        _ = EvictThumbnailsIfNeededAsync();
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
        NotifyOriginalStored();
        return path;
    }

    /// <summary>进度下载等自行写盘完成后调用，刷新统计并按需淘汰原片。</summary>
    public static void NotifyOriginalStored()
    {
        InvalidateStats();
        _ = EvictOriginalsIfNeededAsync();
    }

    /// <summary>标记路径正被播放/下载使用，原片淘汰时跳过。</summary>
    public static void ProtectPath(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            ProtectedPaths[path] = 0;
    }

    public static void UnprotectPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        ProtectedPaths.TryRemove(path, out _);
        // 大视频停止使用后立刻检查原片配额，避免长期占满无法清理。
        _ = EvictOriginalsIfNeededAsync();
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
        long total;
        long thumbs;
        long originals;
        lock (StatsLock)
        {
            if (!_statsValid)
                RefreshStats();
            total = _cachedTotalBytes;
            thumbs = _cachedThumbBytes;
            originals = _cachedOriginalBytes;
        }

        if (thumbs >= MaxThumbnailCacheBytes * 9 / 10)
            _ = EvictThumbnailsIfNeededAsync();
        if (originals >= MaxOriginalCacheBytes * 9 / 10)
            _ = EvictOriginalsIfNeededAsync();

        return total;
    }

    public static Task ClearAllAsync()
    {
        NasThumbnailLoader.ClearMemoryCache();
        NasOriginalLoader.ClearMemoryCache();
        ProtectedPaths.Clear();
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
            _statsValid = false;
    }

    private static void RefreshStats()
    {
        if (!Directory.Exists(RootDir))
        {
            _cachedFileCount = 0;
            _cachedTotalBytes = 0;
            _cachedThumbBytes = 0;
            _cachedOriginalBytes = 0;
            _statsValid = true;
            return;
        }

        var thumbs = SumDirectory(ThumbnailDir);
        var originals = SumDirectory(OriginalDir);
        _cachedThumbBytes = thumbs.Bytes;
        _cachedOriginalBytes = originals.Bytes;
        _cachedTotalBytes = thumbs.Bytes + originals.Bytes;
        _cachedFileCount = thumbs.Count + originals.Count;
        _statsValid = true;
    }

    private static (long Bytes, int Count) SumDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return (0, 0);

        long total = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
                count++;
            }
            catch { }
        }

        return (total, count);
    }

    private static Task EvictThumbnailsIfNeededAsync() =>
        EvictDirectoryIfNeededAsync(ThumbnailDir, MaxThumbnailCacheBytes, respectProtected: false);

    private static Task EvictOriginalsIfNeededAsync() =>
        EvictDirectoryIfNeededAsync(OriginalDir, MaxOriginalCacheBytes, respectProtected: true);

    private static Task EvictDirectoryIfNeededAsync(string dir, long maxBytes, bool respectProtected)
    {
        List<(string Path, long Size)> evictionOrder;
        long currentSize;
        lock (StatsLock)
        {
            RefreshStats();
            currentSize = string.Equals(dir, ThumbnailDir, StringComparison.OrdinalIgnoreCase)
                ? _cachedThumbBytes
                : _cachedOriginalBytes;

            if (currentSize < maxBytes * 9 / 10)
                return Task.CompletedTask;

            if (!Directory.Exists(dir))
                return Task.CompletedTask;

            evictionOrder = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(f =>
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        return (Path: f, Size: fi.Length, LastWrite: fi.LastWriteTime);
                    }
                    catch
                    {
                        return (Path: f, Size: 0L, LastWrite: DateTime.MinValue);
                    }
                })
                .OrderBy(f => f.LastWrite)
                .Select(f => (f.Path, f.Size))
                .ToList();
        }

        var targetSize = maxBytes * 85 / 100;
        foreach (var file in evictionOrder)
        {
            if (currentSize <= targetSize)
                break;

            // 仅原片：跳过下载/播放中的文件。未受保护的大视频可以删，否则配额会永久卡死。
            if (respectProtected && ProtectedPaths.ContainsKey(file.Path))
                continue;

            try
            {
                File.Delete(file.Path);
                currentSize -= file.Size;
            }
            catch { }
        }

        lock (StatsLock)
            RefreshStats();

        return Task.CompletedTask;
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
