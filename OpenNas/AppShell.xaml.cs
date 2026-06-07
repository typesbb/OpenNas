using OpenNas.Views;



namespace OpenNas;



public partial class AppShell : Shell

{

    public AppShell()

    {

        InitializeComponent();



        var tabBar = new TabBar();

        tabBar.Items.Add(new ShellContent

        {

            Title = "相册",

            Route = "albums",

            Content = AppServices.GetRequired<AlbumsPage>()

        });

        tabBar.Items.Add(new ShellContent

        {

            Title = "文件",

            Route = "files",

            Content = AppServices.GetRequired<FilesPage>()

        });

        tabBar.Items.Add(new ShellContent

        {

            Title = "任务",

            Route = "tasks",

            Content = AppServices.GetRequired<TasksPage>()

        });

        tabBar.Items.Add(new ShellContent

        {

            Title = "我的",

            Route = "profile",

            Content = AppServices.GetRequired<ProfilePage>()

        });



        Items.Add(tabBar);

    }

}


