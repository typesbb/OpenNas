using Android;
using Microsoft.Maui.ApplicationModel;

namespace OpenNas.Services;

public static class MediaPermissions
{
    public static async Task<bool> EnsureReadMediaAsync()
    {
        if (!await EnsureGrantedAsync<ReadMediaImagesPermission>())
            return false;

        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            !await EnsureGrantedAsync<ReadMediaVideoPermission>())
            return false;

        return true;
    }

    public static async Task<bool> EnsureWriteMediaAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            return true;

        return await EnsureGrantedAsync<WriteExternalStoragePermission>();
    }

    public static async Task<bool> EnsureNotificationsAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;

        return await EnsureGrantedAsync<PostNotificationsPermission>();
    }

    private static async Task<bool> EnsureGrantedAsync<TPermission>()
        where TPermission : Permissions.BasePermission, new()
    {
        var status = await Permissions.CheckStatusAsync<TPermission>();
        if (status == PermissionStatus.Granted)
            return true;

        if (status == PermissionStatus.Disabled)
            return false;

        status = await Permissions.RequestAsync<TPermission>();
        return status == PermissionStatus.Granted;
    }
}

public sealed class ReadMediaImagesPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? [(Manifest.Permission.ReadMediaImages, true)]
            : [(Manifest.Permission.ReadExternalStorage, true)];
}

public sealed class ReadMediaVideoPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(Manifest.Permission.ReadMediaVideo, true)];
}

public sealed class WriteExternalStoragePermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(Manifest.Permission.WriteExternalStorage, true)];
}

public sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(Manifest.Permission.PostNotifications, true)];
}
