using NSynology.Foto;

namespace OpenNas.Helpers;

internal static class AlbumShareHelper
{
    public static string? ResolvePassphrase(Album album)
    {
        if (!string.IsNullOrWhiteSpace(album.Passphrase))
            return album.Passphrase;

        return string.IsNullOrWhiteSpace(album.Additional?.SharingInfo?.Passphrase)
            ? null
            : album.Additional.SharingInfo.Passphrase;
    }

    public static bool RequiresSharePassphrase(Album album) =>
        !string.IsNullOrEmpty(ResolvePassphrase(album));

    /// <summary>他人分享给我的相册（非自己创建）。自有相册即便有分享链接也不算。</summary>
    public static bool IsSharedWithMe(Album album)
    {
        var perm = album.Additional?.AccessPermission;
        if (perm != null)
            return !perm.Own;

        return false;
    }

    public static bool CanDownload(Album album) =>
        !IsSharedWithMe(album) || (album.Additional?.AccessPermission?.Download ?? false);

    public static bool CanManage(Album album) =>
        !IsSharedWithMe(album) || (album.Additional?.AccessPermission?.Manage ?? false);

    public static bool CanUpload(Album album) =>
        !IsSharedWithMe(album) || (album.Additional?.AccessPermission?.Upload ?? false);
}
