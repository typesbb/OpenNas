using OpenNas.Helpers;
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
        if (_connection?.ActiveProfile == null)
        {
            StatusLabel.Text = "未配置连接";
            UrlLabel.Text = "";
            StatusStripe.Color = GetThemeColor("TextMuted", Colors.Gray);
            return;
        }

        StatusLabel.Text = _connection.GetConnectionLabel();
        UrlLabel.Text = _connection.ActiveProfile.BaseUrl;

        if (_connection.ActiveProfile.NetworkKind == NetworkKind.Wan)
        {
            StatusLabel.TextColor = GetThemeColor("Warning", Colors.Orange);
            StatusStripe.Color = GetThemeColor("Warning", Colors.Orange);
        }
        else
        {
            StatusLabel.TextColor = GetThemeColor("TextPrimary", Colors.Black);
            StatusStripe.Color = GetThemeColor("Success", Colors.Green);
        }
    }

    private static Color GetThemeColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    private async void OnSwitchClicked(object sender, EventArgs e)
    {
        if (_connection == null) return;
        var profiles = await _connection.LoadProfilesAsync();
        var names = profiles.Select(p => $"{(p.NetworkKind == NetworkKind.Lan ? "内网" : "外网")} - {p.DisplayName}").ToArray();
        var pick = await Application.Current!.MainPage!.DisplayActionSheet("切换连接", "取消", null, names);
        if (pick == null || pick == "取消") return;
        var idx = Array.IndexOf(names, pick);
        if (idx >= 0)
        {
            await _connection.SetActiveProfileAsync(profiles[idx]);
            await UiFeedback.ToastAsync("已切换连接");
        }
        Refresh();
    }
}
