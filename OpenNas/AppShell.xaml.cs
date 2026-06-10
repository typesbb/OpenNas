using OpenNas.Views;

namespace OpenNas;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        var tabBar = new TabBar();

        tabBar.Items.Add(CreateTab("相册", "tab_albums", "albums", AppServices.GetRequired<AlbumsPage>()));
        tabBar.Items.Add(CreateTab("文件", "tab_files", "files", AppServices.GetRequired<FilesPage>()));
        tabBar.Items.Add(CreateTab("任务", "tab_tasks", "tasks", AppServices.GetRequired<TasksPage>()));
        tabBar.Items.Add(CreateTab("我的", "tab_profile", "profile", AppServices.GetRequired<ProfilePage>()));

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
}
