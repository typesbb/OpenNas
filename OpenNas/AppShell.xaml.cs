using OpenNas.Helpers;
using OpenNas.Views;

namespace OpenNas;

public partial class AppShell : Shell
{
    public AppShell(
        AlbumsPage albumsPage,
        FilesPage filesPage,
        TasksPage tasksPage,
        ProfilePage profilePage)
    {
        InitializeComponent();

        var tabBar = new TabBar();

        tabBar.Items.Add(CreateTab("相册", "tab_albums", "albums", albumsPage));
        tabBar.Items.Add(CreateTab("文件", "tab_files", "files", filesPage));
        tabBar.Items.Add(CreateTab("任务", "tab_tasks", "tasks", tasksPage));
        tabBar.Items.Add(CreateTab("我的", "tab_profile", "profile", profilePage));

        Items.Add(tabBar);
    }

    private static ShellContent CreateTab(string title, string icon, string route, ContentPage page) =>
        new()
        {
            Title = title,
            Icon = icon,
            Route = route,
            Content = page
        };

    /// <summary>底部 Tab 再点当前页：走各页 RefreshAsync（相册等会先 Ensure 地址再加载）。</summary>
    public static Task TryRefreshCurrentPageAsync()
    {
        if (Current?.CurrentPage is IRefreshable refreshable)
            return refreshable.RefreshAsync();
        return Task.CompletedTask;
    }
}
