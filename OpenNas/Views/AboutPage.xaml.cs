using OpenNas.Helpers;

namespace OpenNas.Views;

public partial class AboutPage : ContentPage
{
    private const string GitHubUrl = "https://github.com/typesbb/OpenNas";
    private const string IssuesUrl = "https://github.com/typesbb/OpenNas/issues";
    private const string FeedbackEmail = "typesbb@qq.com";
    private const string QqGroupNumber = "79757800";

    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"版本 {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnGitHubTapped(object? sender, TappedEventArgs e) =>
        await OpenUriAsync(GitHubUrl);

    private async void OnIssuesTapped(object? sender, TappedEventArgs e) =>
        await OpenUriAsync(IssuesUrl);

    private async void OnEmailTapped(object? sender, TappedEventArgs e) =>
        await OpenUriAsync($"mailto:{FeedbackEmail}");

    private async void OnQqGroupTapped(object? sender, TappedEventArgs e)
    {
        await Clipboard.Default.SetTextAsync(QqGroupNumber);
        await UiFeedback.ToastAsync("已复制 QQ 群号");
    }

    private static async Task OpenUriAsync(string url)
    {
        try
        {
            await Launcher.Default.OpenAsync(url);
        }
        catch
        {
            await UiFeedback.ToastAsync("无法打开链接");
        }
    }
}
