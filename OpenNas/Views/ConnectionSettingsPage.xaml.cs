using OpenNas.Helpers;
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
            await UiFeedback.AlertAsync(this, "错误", "请输入 NAS 地址");
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
        await UiFeedback.ToastAsync("配置已保存");
    }

    private async void OnSetActiveClicked(object sender, EventArgs e)
    {
        if (_selected == null)
        {
            await UiFeedback.AlertAsync(this, "提示", "请先选择一条配置");
            return;
        }
        await _connection.SetActiveProfileAsync(_selected);
        await UiFeedback.ToastAsync("已切换当前连接");
    }
}
