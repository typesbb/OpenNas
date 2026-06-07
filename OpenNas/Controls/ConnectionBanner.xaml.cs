using OpenNas.Models;
using OpenNas.Services;

namespace OpenNas.Controls;

public partial class ConnectionBanner : ContentView
{
    private ConnectionService? _connection;

    public ConnectionBanner()
    {
        InitializeComponent();
    }

    public void Bind(ConnectionService connection)
    {
        _connection = connection;
        connection.ConnectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        if (_connection?.ActiveProfile == null) return;
        StatusLabel.Text = _connection.GetConnectionLabel();
        UrlLabel.Text = _connection.ActiveProfile.BaseUrl;
        if (_connection.ActiveProfile.NetworkKind == NetworkKind.Wan)
        {
            StatusLabel.TextColor = Colors.DarkOrange;
        }
        else
        {
            StatusLabel.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Colors.White : Colors.Black;
        }
    }

    private async void OnSwitchClicked(object sender, EventArgs e)
    {
        if (_connection == null) return;
        var profiles = await _connection.LoadProfilesAsync();
        var names = profiles.Select(p => $"{(p.NetworkKind == NetworkKind.Lan ? "内网" : "外网")} - {p.DisplayName}").ToArray();
        var pick = await Application.Current!.MainPage!.DisplayActionSheet("切换连接", "取消", null, names);
        if (pick == null || pick == "取消") return;
        var idx = Array.IndexOf(names, pick);
        if (idx >= 0)
            await _connection.SetActiveProfileAsync(profiles[idx]);
        Refresh();
    }
}
