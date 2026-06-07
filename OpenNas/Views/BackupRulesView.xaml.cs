using NSynology;
using NSynology.Foto;
using OpenNas.Data;
using OpenNas.Models;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class BackupRulesView : ContentView
{
    private readonly BackupDatabase _db;
    private List<BackupRule> _rules = new();

    public BackupRulesView(BackupDatabase db)
    {
        InitializeComponent();
        _db = db;
        Loaded += async (_, _) => await ReloadAsync();
        RulesView.SelectionMode = SelectionMode.Single;
        RulesView.SelectionChanged += OnRuleSelected;
    }

    private async Task ReloadAsync()
    {
        _rules = await _db.GetRulesAsync();
        RulesView.ItemsSource = null;
        RulesView.ItemsSource = _rules;
    }

    private async void OnRuleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not BackupRule rule) return;
        RulesView.SelectedItem = null;

        var action = await Application.Current!.MainPage!.DisplayActionSheet(
            rule.LocalAlbumName, "取消", "删除规则", "切换启用", "切换备份后删除");
        if (action == "删除规则")
        {
            await _db.DeleteRuleAsync(rule.Id);
            await ReloadAsync();
        }
        else if (action == "切换启用")
        {
            rule.Enabled = !rule.Enabled;
            await _db.SaveRuleAsync(rule);
            await ReloadAsync();
        }
        else if (action == "切换备份后删除")
        {
            if (!rule.DeleteAfterBackup)
            {
                var ok = await Application.Current.MainPage.DisplayAlert(
                    "备份后删除",
                    "开启后，文件成功上传到 NAS 后将尝试删除手机本地副本。是否开启？",
                    "开启", "取消");
                if (!ok) return;
            }
            rule.DeleteAfterBackup = !rule.DeleteAfterBackup;
            await _db.SaveRuleAsync(rule);
            await ReloadAsync();
        }
    }

    private async void OnAddRuleClicked(object sender, EventArgs e)
    {
#if !ANDROID
        await Application.Current!.MainPage!.DisplayAlert("提示", "备份规则仅支持 Android。", "确定");
        return;
#else
        try
        {
            if (!await MediaPermissions.EnsureReadMediaAsync())
            {
                await Application.Current!.MainPage!.DisplayAlert(
                    "需要权限", "请允许访问照片和视频后再添加备份规则。", "确定");
                return;
            }

            var media = new Media.LocalMediaService();
            var localAlbums = await media.GetLocalAlbumsAsync();
            if (localAlbums.Count == 0)
            {
                await Application.Current!.MainPage!.DisplayAlert("提示", "未找到本机相册", "确定");
                return;
            }

            var localNames = localAlbums.Select(a => a.Name).ToArray();
            var pickLocal = await Application.Current!.MainPage!.DisplayActionSheet("选择本机相册", "取消", null, localNames);
            if (pickLocal == null || pickLocal == "取消") return;
            var local = localAlbums.First(a => a.Name == pickLocal);

            var nasAlbums = (await SynologyManager.Client.Foto.GetAlbumsAsync(0, 100)).ToList();
            var nasNames = nasAlbums.Select(a => a.Name).ToArray();
            var pickNas = await Application.Current!.MainPage!.DisplayActionSheet("选择 NAS 相册", "取消", "新建相册...", nasNames);
            if (pickNas == null || pickNas == "取消") return;

            Album remote;
            if (pickNas == "新建相册...")
            {
                var newName = await Application.Current!.MainPage!.DisplayPromptAsync("新建相册", "相册名称");
                if (string.IsNullOrWhiteSpace(newName)) return;
                remote = await SynologyManager.Client.Foto.CreateNormalAlbumAsync(newName);
            }
            else
                remote = nasAlbums.First(a => a.Name == pickNas);

            var delete = await Application.Current!.MainPage!.DisplayAlert(
                "备份后删除", "是否在备份成功后删除手机上的原文件？", "是", "否");

            var rule = new BackupRule
            {
                LocalAlbumId = local.Id,
                LocalAlbumName = local.Name,
                RemoteAlbumId = remote.Id,
                RemoteAlbumName = remote.Name,
                Enabled = true,
                DeleteAfterBackup = delete
            };
            await _db.SaveRuleAsync(rule);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("备份规则操作失败", ex);
            await Application.Current!.MainPage!.DisplayAlert("错误", ex.Message, "确定");
        }
#endif
    }
}
