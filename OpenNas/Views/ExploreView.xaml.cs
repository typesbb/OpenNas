using Microsoft.Maui.Controls.Shapes;

using NSynology;

using NSynology.Foto;

using OpenNas.Controls;

using OpenNas.Helpers;

using OpenNas.Services;

using OpenNas.ViewModels;



namespace OpenNas.Views;



public partial class ExploreView : ContentView

{

    private ConnectionService? _connection;

    private PhotosLibraryContext? _libraryContext;

    private bool _loading;

    private IReadOnlyList<PhotoCategory> _categories = [];



    public void Bind(ConnectionService connection, PhotosLibraryContext libraryContext)

    {

        _connection = connection;

        _libraryContext = libraryContext;

        _libraryContext.ExploreLibraryChanged += OnExploreLibraryChanged;

    }



    public ExploreView()

    {

        InitializeComponent();

        RefreshHost.Refreshing += OnPullRefreshing;

    }



    private void OnExploreLibraryChanged(object? sender, EventArgs e) =>

        MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());



    private async void OnPullRefreshing(object? sender, EventArgs e)

    {

        await RefreshAsync();

        RefreshHost.IsRefreshing = false;

    }



    public async Task RefreshAsync()

    {

        if (_loading)

            return;



        _loading = true;

        BusyIndicator.IsVisible = true;

        BusyIndicator.IsRunning = true;



        try

        {

            var client = SynologyManager.Client;

            if (client == null || string.IsNullOrEmpty(client.Sid))

            {

                SectionsHost.Children.Clear();

                return;

            }



            _libraryContext?.ApplyExploreCookies();
            if (_libraryContext?.ExploreLibrary == PhotosLibrary.SharedSpace)
                _libraryContext.SetExploreLibrary(PhotosLibrary.PersonalSpace);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            _categories = await client.FotoBrowse.GetCategoriesAsync(cts.Token);

            BuildSections();

        }

        catch (Exception ex)

        {

            AppLog.Error("加载探索页失败", ex);

        }

        finally

        {

            BusyIndicator.IsRunning = false;

            BusyIndicator.IsVisible = false;

            _loading = false;

        }

    }



    private void BuildSections()

    {

        SectionsHost.Children.Clear();

        foreach (var section in CreateSections().Where(s => s.IsVisible))

            SectionsHost.Children.Add(BuildSectionView(section));

    }



    private IEnumerable<ExploreSectionViewModel> CreateSections()

    {

        var ids = _categories.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        yield return new ExploreSectionViewModel

        {

            Category = PhotosBrowseCategory.RecentlyAdded,

            Title = "最近添加",

            IsVisible = ids.Contains("recently_added")

        };

        yield return new ExploreSectionViewModel

        {

            Category = PhotosBrowseCategory.Person,

            Title = "人物",

            IsVisible = ids.Contains("person")

        };

        yield return new ExploreSectionViewModel

        {

            Category = PhotosBrowseCategory.Concept,

            Title = "主题",

            IsVisible = ids.Contains("concept")

        };

        yield return new ExploreSectionViewModel

        {

            Category = PhotosBrowseCategory.Geocoding,

            Title = "地点",

            IsVisible = ids.Contains("geocoding")

        };

        yield return new ExploreSectionViewModel

        {

            Category = PhotosBrowseCategory.GeneralTag,

            Title = "标签",

            IsVisible = ids.Contains("general_tag")

        };

        yield return new ExploreSectionViewModel

        {

            Category = PhotosBrowseCategory.Video,

            Title = "视频",

            IsVisible = ids.Contains("video")

        };

    }



    private View BuildSectionView(ExploreSectionViewModel section)
    {
        var container = new VerticalStackLayout { Spacing = 10 };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };

        header.Add(new Label
        {
            Text = section.Title,
            FontFamily = "OpenSansSemibold",
            FontSize = 17,
            VerticalOptions = LayoutOptions.Center
        });

        var moreLabel = new Label
        {
            Text = "查看全部 ›",
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center
        };
        moreLabel.SetAppThemeColor(Label.TextColorProperty,
            (Color)Application.Current!.Resources["BrandPrimary"],
            (Color)Application.Current.Resources["BrandPrimaryLight"]);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenCategoryAsync(section.Category);
        moreLabel.GestureRecognizers.Add(tap);

        header.Add(moreLabel);
        Grid.SetColumn(moreLabel, 1);

        container.Children.Add(header);
        container.Children.Add(BuildPreviewRow(section.Category));
        return container;
    }



    private View BuildPreviewRow(PhotosBrowseCategory category)

    {

        var scroll = new ScrollView

        {

            Orientation = ScrollOrientation.Horizontal,

            HorizontalScrollBarVisibility = ScrollBarVisibility.Never

        };

        var row = new HorizontalStackLayout { Spacing = 8 };

        scroll.Content = row;

        _ = LoadPreviewAsync(category, row);

        return scroll;

    }



    private async Task LoadPreviewAsync(PhotosBrowseCategory category, HorizontalStackLayout row)

    {

        var client = SynologyManager.Client;

        if (client == null)

            return;



        try

        {

            _libraryContext?.ApplyExploreCookies();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            switch (category)
            {
                case PhotosBrowseCategory.RecentlyAdded:
                {
                    var photos = await PhotosBrowseGateway.ListRecentlyAddedAsync(client, 0, 4, cts.Token);
                    foreach (var photo in photos)
                        row.Children.Add(CreatePhotoPreview(photo, category));
                    break;
                }
                case PhotosBrowseCategory.Video:
                {
                    var videos = await PhotosBrowseGateway.ListVideosAsync(
                        client, PhotosLibrary.PersonalSpace, 0, 4, cts.Token);
                    foreach (var photo in videos)
                        row.Children.Add(CreatePhotoPreview(photo, category));
                    break;
                }
                case PhotosBrowseCategory.Person:
                {
                    var persons = await PhotosBrowseGateway.ListBrowseItemsAsync(
                        client, PhotosLibrary.PersonalSpace, PhotosBrowseCategory.Person, 0, 4, cts.Token);
                    foreach (var person in persons.Where(p => p.Show))
                        row.Children.Add(CreateBrowseItemPreview(person, category, person.Id));
                    break;
                }
                default:
                {
                    var items = await PhotosBrowseGateway.ListBrowseItemsAsync(
                        client, PhotosLibrary.PersonalSpace, category, 0, 4, cts.Token);
                    foreach (var item in items)
                        row.Children.Add(CreateBrowseItemPreview(item, category, item.Id));
                    break;
                }
            }

        }

        catch (Exception ex)

        {

            AppLog.Error($"加载探索预览失败: {category}", ex);

        }

    }



    private View CreatePhotoPreview(Photo photo, PhotosBrowseCategory category)

    {

        var border = CreatePreviewBorder(isCircle: false);

        var image = new Image { Aspect = Aspect.AspectFill };

        border.Content = image;

        NasThumbnailLoader.TryLoadPhotoThumbnail(image, photo, forGrid: true);



        var tap = new TapGestureRecognizer();

        tap.Tapped += async (_, _) => await OpenCategoryAsync(category);

        border.GestureRecognizers.Add(tap);

        return border;

    }



    private View CreateBrowseItemPreview(BrowseAlbumItem item, PhotosBrowseCategory category, int itemId)

    {

        var isPerson = category == PhotosBrowseCategory.Person;

        var border = CreatePreviewBorder(isCircle: isPerson);

        var image = new Image { Aspect = Aspect.AspectFill };

        border.Content = image;

        NasThumbnailLoader.TryLoadBrowseItemThumbnail(image, item);



        var tap = new TapGestureRecognizer();

        tap.Tapped += async (_, _) =>

        {

            if (isPerson)

                await ShellNavigation.PushAsync(new CategoryBrowsePage(
                    category, _connection!, _libraryContext, itemId,
                    CategoryBrowsePage.ResolveTitle(category, item.Name, itemId)));

            else

                await OpenCategoryAsync(category);

        };

        border.GestureRecognizers.Add(tap);

        return border;

    }



    private static Border CreatePreviewBorder(bool isCircle)

    {

        var size = isCircle ? 72.0 : 88.0;

        var border = new Border

        {

            WidthRequest = size,

            HeightRequest = size,

            StrokeThickness = 0,

            BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark

                ? (Color)Application.Current.Resources["SurfaceMutedDark"]

                : (Color)Application.Current.Resources["SurfaceMuted"]

        };

        border.StrokeShape = isCircle

            ? new RoundRectangle { CornerRadius = size / 2 }

            : new RoundRectangle { CornerRadius = 12 };

        return border;

    }



    private Task OpenCategoryAsync(PhotosBrowseCategory category)

    {

        if (_connection == null)

            return Task.CompletedTask;



        return ShellNavigation.PushAsync(new CategoryBrowsePage(category, _connection, _libraryContext));

    }

}


