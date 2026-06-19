#if ANDROID
using OpenNas.Media;
#endif
using NSynology;
using NSynology.Foto;
using OpenNas.Data;
using OpenNas.Models;
using OpenNas.Services;

namespace OpenNas.Helpers;

public static class BackupRuleCreator
{
    public static async Task<BackupRule?> CreateFromUserInputAsync(Page page, BackupDatabase db)
    {
#if !ANDROID
        await page.DisplayAlertAsync("提示", "备份规则仅支持 Android。", "确定");
        return null;
#else
        if (!await MediaPermissions.EnsureReadMediaAsync())
        {
            await page.DisplayAlertAsync("需要权限", "请允许访问照片和视频后再添加备份规则。", "确定");
            return null;
        }

        var media = new LocalMediaService();
        var localAlbums = await media.GetLocalAlbumsAsync();
        if (localAlbums.Count == 0)
        {
            await page.DisplayAlertAsync("提示", "未找到本机相册", "确定");
            return null;
        }

        var localNames = localAlbums.Select(a => a.Name).ToArray();
        var pickLocal = await page.DisplayActionSheetAsync("选择本机相册", "取消", null, localNames);
        if (pickLocal is null or "取消") return null;
        var local = localAlbums.First(a => a.Name == pickLocal);

        var nasAlbums = (await SynologyManager.Client.Foto.GetAlbumsAsync(0, 100)).ToList();
        var nasNames = nasAlbums.Select(a => a.Name).ToArray();
        var pickNas = await page.DisplayActionSheetAsync("选择 NAS 相册", "取消", "新建相册...", nasNames);
        if (pickNas is null or "取消") return null;

        Album remote;
        if (pickNas == "新建相册...")
        {
            var newName = await page.DisplayPromptAsync("新建相册", "相册名称");
            if (string.IsNullOrWhiteSpace(newName)) return null;
            remote = await SynologyManager.Client.Foto.CreateNormalAlbumAsync(newName);
        }
        else
            remote = nasAlbums.First(a => a.Name == pickNas);

        var delete = await page.DisplayAlertAsync(
            "备份完成后删除", "是否在全部文件备份完成后，批量删除手机上的原文件？", "是", "否");

        if (delete)
        {
            var confirmed = await page.DisplayAlertAsync(
                "⚠️ 风险确认",
                "开启后将删除手机本地文件，不可恢复。\n\n请确认你已理解此风险。",
                "确认开启", "取消");
            if (!confirmed)
                delete = false;
        }

        if (delete)
        {
            var conn = AppServices.GetRequired<ConnectionService>();
            conn.SetAcknowledgedDeleteRisk(true);
        }

        var rule = new BackupRule
        {
            LocalAlbumId = local.Id,
            LocalAlbumName = local.Name,
            RemoteAlbumId = remote.Id,
            RemoteAlbumName = remote.Name,
            Enabled = true,
            DeleteAfterBackup = delete
        };
        await db.SaveRuleAsync(rule);
        return rule;
#endif
    }
}
