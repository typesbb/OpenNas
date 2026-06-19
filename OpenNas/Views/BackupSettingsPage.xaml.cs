using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class BackupSettingsPage : ContentPage
{
    private readonly ConnectionService _connection;

    public BackupSettingsPage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
        WifiOnlySwitch.IsToggled = _connection.GetWifiOnly();
        ConfirmDeleteSwitch.IsToggled = _connection.GetConfirmBeforeDelete();
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private void OnWifiOnlyToggled(object? sender, ToggledEventArgs e) =>
        _connection.SetWifiOnly(e.Value);

    private void OnConfirmDeleteToggled(object? sender, ToggledEventArgs e) =>
        _connection.SetConfirmBeforeDelete(e.Value);

    private async void OnAckDeleteRiskClicked(object? sender, EventArgs e)
    {
        var ok = await UiFeedback.ConfirmAsync(this,
            "删除风险提示",
            "备份成功后删除本地文件不可恢复。请确认 NAS 上已成功备份后再开启各规则的「备份完成后删除」。",
            "我已了解", "取消");
        if (!ok)
            return;

        _connection.SetAcknowledgedDeleteRisk(true);
        await UiFeedback.ToastAsync("已确认，可为规则开启「备份完成后删除」");
    }
}
