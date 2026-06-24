using OpenNas.Helpers;
using OpenNas.Core.Models;
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

        // 状态色基于连接状态而非网络类型
        var connected = !string.IsNullOrEmpty(NSynology.SynologyManager.Client?.Sid);
        if (connected)
        {
            StatusLabel.TextColor = GetThemeColor("TextPrimary", Colors.Black);
            StatusStripe.Color = GetThemeColor("Success", Colors.Green);
        }
        else
        {
            StatusLabel.TextColor = GetThemeColor("TextPrimary", Colors.Black);
            StatusStripe.Color = GetThemeColor("Warning", Colors.Orange);
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
        var names = profiles.Select(NasProfileDisplay.FormatTitle).ToArray();
        var pick = await (Application.Current?.Windows[0]?.Page)!.DisplayActionSheetAsync("切换连接", "取消", null, names);
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
