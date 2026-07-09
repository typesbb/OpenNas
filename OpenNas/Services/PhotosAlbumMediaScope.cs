namespace OpenNas.Services;

/// <summary>
/// 当前浏览的相册上下文（与我共享相册需 passphrase / album_id 才能拉取媒体）。
/// </summary>
public static class PhotosAlbumMediaScope
{
    public static int? CurrentAlbumId { get; private set; }
    public static string? CurrentPassphrase { get; private set; }
    public static bool AllowDownload { get; private set; } = true;

    public static void Set(int albumId, string? passphrase, bool allowDownload = true)
    {
        CurrentAlbumId = albumId;
        CurrentPassphrase = string.IsNullOrWhiteSpace(passphrase) ? null : passphrase;
        AllowDownload = allowDownload;
    }

    public static void Clear()
    {
        CurrentAlbumId = null;
        CurrentPassphrase = null;
        AllowDownload = true;
    }

    public static IDisposable Use(int albumId, string? passphrase, bool allowDownload = true)
    {
        var previousId = CurrentAlbumId;
        var previousPassphrase = CurrentPassphrase;
        var previousAllowDownload = AllowDownload;
        Set(albumId, passphrase, allowDownload);
        return new ScopeRestore(previousId, previousPassphrase, previousAllowDownload);
    }

    private sealed class ScopeRestore(int? albumId, string? passphrase, bool allowDownload) : IDisposable
    {
        public void Dispose()
        {
            CurrentAlbumId = albumId;
            CurrentPassphrase = passphrase;
            AllowDownload = allowDownload;
        }
    }
}
