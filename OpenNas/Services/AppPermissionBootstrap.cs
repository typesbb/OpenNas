using OpenNas.Helpers;

namespace OpenNas.Services;

public static class AppPermissionBootstrap
{
    public static async Task<bool> EnsureDownloadPermissionsAsync(Page? page)
    {
#if ANDROID
        if (await MediaPermissions.EnsureWriteMediaAsync().ConfigureAwait(false))
            return true;
#elif IOS || MACCATALYST
        if (await MediaPermissions.EnsurePhotosAddAsync().ConfigureAwait(false))
            return true;
#else
        return true;
#endif

#if ANDROID || IOS || MACCATALYST
        if (page == null)
            return false;

        var openSettings = await page.DisplayAlertAsync(
            "需要相册权限",
            "下载照片和视频需要保存到本机相册。请在系统设置中允许 OpenNas 访问相册。",
            "去设置",
            "取消");
        if (openSettings)
            AppInfo.ShowSettingsUI();

        return false;
#else
        return true;
#endif
    }

    public static async Task<bool> EnsureDownloadAllowedAsync(Page page, ConnectionService connection)
    {
        if (NasPhotoDownloadService.IsWifiBlocked(connection))
        {
            await UiFeedback.AlertAsync(page, "下载受限", "已开启「仅 Wi-Fi 时下载」，请连接 Wi-Fi 后再试。");
            return false;
        }

        return await EnsureDownloadPermissionsAsync(page);
    }
}
