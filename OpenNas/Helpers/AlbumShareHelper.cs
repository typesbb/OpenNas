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

    public static bool CanDownload(Album album) =>
        !RequiresSharePassphrase(album) || (album.Additional?.AccessPermission?.Download ?? false);

    public static bool CanManage(Album album) =>
        !RequiresSharePassphrase(album) || (album.Additional?.AccessPermission?.Manage ?? false);

    public static bool CanUpload(Album album) =>
        !RequiresSharePassphrase(album) || (album.Additional?.AccessPermission?.Upload ?? false);
}
