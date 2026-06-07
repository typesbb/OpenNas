using OpenNas.Models;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class ConnectionSettingsPage : ContentPage
{
    private readonly ConnectionService _connection;
    private List<NasProfile> _profiles = new();
    private NasProfile? _selected;

    public ConnectionSettingsPage(ConnectionService connection)
    {
        InitializeComponent();
        _connection = connection;
        KindPicker.ItemsSource = new[] { "内网", "外网" };
        WifiOnlySwitch.IsToggled = _connection.GetWifiOnly();
        ConfirmDeleteSwitch.IsToggled = _connection.GetConfirmBeforeDelete();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _profiles = await _connection.LoadProfilesAsync();
        ProfilesView.ItemsSource = _profiles;
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = e.CurrentSelection.FirstOrDefault() as NasProfile;
        if (_selected == null) return;
        NameEntry.Text = _selected.DisplayName;
        UrlEntry.Text = _selected.BaseUrl;
        KindPicker.SelectedIndex = _selected.NetworkKind == NetworkKind.Lan ? 0 : 1;
    }

    private async void OnSaveProfileClicked(object sender, EventArgs e)
    {
        var url = UrlEntry.Text?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            await DisplayAlert("错误", "请输入 NAS 地址", "确定");
            return;
        }

        var profile = _selected ?? new NasProfile();
        profile.DisplayName = NameEntry.Text?.Trim() ?? "NAS";
        profile.BaseUrl = NasUrlHelper.NormalizeBaseUrl(url);
        profile.NetworkKind = KindPicker.SelectedIndex == 1 ? NetworkKind.Wan : NetworkKind.Lan;

        var list = await _connection.LoadProfilesAsync();
        var existing = list.FirstOrDefault(p => p.Id == profile.Id);
        if (existing == null) list.Add(profile);
        else
        {
            var idx = list.FindIndex(p => p.Id == profile.Id);
            list[idx] = profile;
        }

        await _connection.SaveProfilesAsync(list);
        _profiles = list;
        ProfilesView.ItemsSource = null;
        ProfilesView.ItemsSource = _profiles;
        await DisplayAlert("已保存", "配置已更新", "确定");
    }

    private async void OnSetActiveClicked(object sender, EventArgs e)
    {
        if (_selected == null)
        {
            await DisplayAlert("提示", "请先选择一条配置", "确定");
            return;
        }
        await _connection.SetActiveProfileAsync(_selected);
        await DisplayAlert("已切换", "当前连接已更新，若未登录请重新登录。", "确定");
    }

    private void OnWifiOnlyToggled(object sender, ToggledEventArgs e) =>
        _connection.SetWifiOnly(e.Value);

    private void OnConfirmDeleteToggled(object sender, ToggledEventArgs e) =>
        _connection.SetConfirmBeforeDelete(e.Value);

    private async void OnAckDeleteRiskClicked(object sender, EventArgs e)
    {
        var ok = await DisplayAlert(
            "删除风险提示",
            "备份成功后删除本地文件不可恢复。请确认 NAS 上已成功备份后再开启各规则的「备份后删除」。",
            "我已了解", "取消");
        if (ok)
        {
            _connection.SetAcknowledgedDeleteRisk(true);
            await DisplayAlert("已确认", "可为规则开启「备份后删除」。", "确定");
        }
    }

    private async void OnReloginClicked(object sender, EventArgs e)
    {
        await _connection.LogoutAsync();
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new NavigationPage(AppServices.GetRequired<LoginPage>());
    }
}
