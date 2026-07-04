using OpenNas.Core.Data;
using OpenNas.Helpers;
using OpenNas.Core.Models;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class BackupRulesView : ContentView
{
    private readonly BackupDatabase _db;
    private List<BackupRule> _rules = [];

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

    private async void OnRuleSelected(object? sender, SelectionChangedEventArgs e)
    {
        var sel = e.CurrentSelection;
        if (sel is not { Count: > 0 } || sel[0] is not BackupRule rule) return;
        RulesView.SelectedItem = null;

        var action = await (Application.Current?.Windows[0]?.Page)!.DisplayActionSheetAsync(
            rule.LocalAlbumName, "取消", "删除规则", "切换启用", "切换备份完成后删除");
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
        else if (action == "切换备份完成后删除")
        {
            if (!rule.DeleteAfterBackup)
            {
                var ok = await (Application.Current?.Windows[0]?.Page)!.DisplayAlertAsync(
                    "备份完成后删除",
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
        try
        {
            var rule = await BackupRuleCreator.CreateFromUserInputAsync(
                (Application.Current?.Windows[0]?.Page)!, _db);
            if (rule == null)
                return;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("备份规则操作失败", ex);
            var page = Application.Current?.Windows[0]?.Page;
            await UiFeedback.ShowApiErrorAsync(page, "错误", ex);
        }
    }
}
