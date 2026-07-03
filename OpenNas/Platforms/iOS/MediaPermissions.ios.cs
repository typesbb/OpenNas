#if IOS || MACCATALYST
using Microsoft.Maui.ApplicationModel;

namespace OpenNas.Services;

public static class MediaPermissions
{
    public static async Task<bool> EnsurePhotosAddAsync()
    {
        var status = await Permissions.CheckStatusAsync<PhotosAddPermission>();
        if (status == PermissionStatus.Granted)
            return true;

        if (status == PermissionStatus.Disabled)
            return false;

        status = await Permissions.RequestAsync<PhotosAddPermission>();
        return status == PermissionStatus.Granted;
    }
}

public sealed class PhotosAddPermission : Permissions.BasePermission
{
    public override Task<PermissionStatus> CheckStatusAsync() =>
        Task.FromResult(MapStatus(global::Photos.PHPhotoLibrary.AuthorizationStatus));

    public override Task<PermissionStatus> RequestAsync()
    {
        var tcs = new TaskCompletionSource<PermissionStatus>();
        global::Photos.PHPhotoLibrary.RequestAuthorization(global::Photos.PHAccessLevel.AddOnly, status =>
            tcs.TrySetResult(MapStatus(status)));
        return tcs.Task;
    }

    protected override void Invoke(bool isRequiredForAccess) { }

    private static PermissionStatus MapStatus(global::Photos.PHAuthorizationStatus status) =>
        status switch
        {
            global::Photos.PHAuthorizationStatus.Authorized => PermissionStatus.Granted,
            global::Photos.PHAuthorizationStatus.Limited => PermissionStatus.Granted,
            global::Photos.PHAuthorizationStatus.Denied => PermissionStatus.Denied,
            global::Photos.PHAuthorizationStatus.Restricted => PermissionStatus.Disabled,
            _ => PermissionStatus.Unknown
        };
}
#endif
