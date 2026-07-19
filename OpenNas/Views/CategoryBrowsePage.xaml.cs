using Microsoft.Maui.Controls.Shapes;
using NSynology;
using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.ViewModels;

namespace OpenNas.Views;

public partial class CategoryBrowsePage : ContentPage, IRefreshable
{
    private const int PageSize = 30;

    private readonly PhotosBrowseCategory _category;
    private readonly ConnectionService _connection;
    private readonly PhotosLibraryContext? _libraryContext;
    private readonly int? _filterId;
    private readonly List<Photo> _photos = [];
    private readonly List<BrowseAlbumItem> _browseItems = [];
    private bool _loading;
    private bool _hasMore = true;
    private int _offset;
    private bool _photoMode;

    public CategoryBrowsePage(
        PhotosBrowseCategory category,
        ConnectionService connection,
        PhotosLibraryContext? libraryContext = null,
        int? filterId = null,
        string? title = null)
    {
        _category = category;
        _connection = connection;
        _libraryContext = libraryContext;
        _filterId = filterId;
        _photoMode = IsFilteredPhotoMode(category, filterId);

        InitializeComponent();
        TitleLabel.Text = ResolveTitle(category, title, filterId);
        ConfigureSpaceBar();
        ConfigureCollectionView();
        RefreshHost.Refreshing += OnPullRefreshing;
        _libraryContext?.ExploreLibraryChanged += OnExploreLibraryChanged;
        Loaded += async (_, _) => await LoadAsync(reset: true);
    }

    private void OnExploreLibraryChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            ConfigureSpaceBar();
            await LoadAsync(reset: true);
        });

    private void ConfigureSpaceBar()
    {
        if (_libraryContext == null ||
            !_libraryContext.IsExploreCategorySpaceSwitcherVisible(_category))
        {
            SpaceBar.IsVisible = false;
            return;
        }

        SpaceBar.Bind(
            _libraryContext,
            PhotosViewMode.Explore,
            () => _libraryContext.IsExploreCategorySpaceSwitcherVisible(_category));
    }

    private void ConfigureCollectionView()
    {
        if (_photoMode || _category is PhotosBrowseCategory.RecentlyAdded or PhotosBrowseCategory.Video)
        {
            _photoMode = true;
            ItemsView.ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical);
            ItemsView.ItemTemplate = new DataTemplate(() =>
            {
                var grid = new Grid
                {
                    Margin = new Thickness(2),
                    HeightRequest = 112,
                    BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                        ? (Color)Application.Current!.Resources["SurfaceMutedDark"]
                        : (Color)Application.Current!.Resources["SurfaceMuted"]
                };
                var image = new Controls.AlbumGridPhotoView { Aspect = Aspect.AspectFill };
                grid.Children.Add(image);
                var tap = new TapGestureRecognizer();
                tap.Tapped += OnPhotoTapped;
                grid.GestureRecognizers.Add(tap);
                return grid;
            });
        }
        else
        {
            ItemsView.ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical);
            ItemsView.ItemTemplate = new DataTemplate(() =>
            {
                var stack = new VerticalStackLayout { Padding = new Thickness(6, 10), Spacing = 6 };
                var border = new Border
                {
                    StrokeThickness = 0,
                    HeightRequest = 112,
                    BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                        ? (Color)Application.Current!.Resources["SurfaceMutedDark"]
                        : (Color)Application.Current!.Resources["SurfaceMuted"]
                };
                border.StrokeShape = new RoundRectangle { CornerRadius = 14 };
                var image = new Image { Aspect = Aspect.AspectFill };
                image.HandlerChanged += (_, _) =>
                {
                    if (image.BindingContext is BrowseAlbumItem item)
                        NasThumbnailLoader.TryLoadBrowseItemThumbnail(image, item);
                };
                border.Content = image;
                stack.Children.Add(border);
                var nameLabel = new Label
                {
                    FontSize = 13,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                };
                nameLabel.SetBinding(Label.TextProperty, "Name");
                stack.Children.Add(nameLabel);
                var tap = new TapGestureRecognizer();
                tap.Tapped += OnBrowseItemTapped;
                stack.GestureRecognizers.Add(tap);
                return stack;
            });
        }
    }

    private static bool IsFilteredPhotoMode(PhotosBrowseCategory category, int? filterId) =>
        category is PhotosBrowseCategory.RecentlyAdded or PhotosBrowseCategory.Video
            || (filterId.HasValue && category is PhotosBrowseCategory.Person
                or PhotosBrowseCategory.Concept
                or PhotosBrowseCategory.Geocoding
                or PhotosBrowseCategory.GeneralTag);

    internal static string ResolveTitle(PhotosBrowseCategory category, string? title, int? filterId = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        if (category == PhotosBrowseCategory.Person && filterId.HasValue)
            return "人物";

        return GetDefaultTitle(category);
    }

    private static string GetDefaultTitle(PhotosBrowseCategory category) => category switch
    {
        PhotosBrowseCategory.RecentlyAdded => "最近添加",
        PhotosBrowseCategory.Person => "人物",
        PhotosBrowseCategory.Concept => "主题",
        PhotosBrowseCategory.Geocoding => "地点",
        PhotosBrowseCategory.GeneralTag => "标签",
        PhotosBrowseCategory.Video => "视频",
        _ => "浏览"
    };

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync(reset: true, showBusyIndicator: false);
        RefreshHost.IsRefreshing = false;
    }

    public Task RefreshAsync() => LoadAsync(reset: true);

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (_hasMore && !_loading)
            await LoadAsync(reset: false);
    }

    private async Task LoadAsync(bool reset, bool showBusyIndicator = true)
    {
        if (_loading)
            return;

        _loading = true;
        if (showBusyIndicator)
        {
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
        }

        if (reset)
        {
            _offset = 0;
            _hasMore = true;
            _photos.Clear();
            _browseItems.Clear();
        }

        try
        {
            var client = SynologyManager.Client;
            if (client == null || string.IsNullOrEmpty(client.Sid))
                return;

            _libraryContext?.ApplyExploreCookies();
            var library = _libraryContext?.ExploreLibrary ?? PhotosLibrary.PersonalSpace;
            using var _ = PhotosMediaLibraryScope.Use(library);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            if (_photoMode)
            {
                var batch = await LoadPhotosAsync(client, _offset, PageSize, cts.Token);
                _photos.AddRange(batch);
                _offset += batch.Count;
                _hasMore = batch.Count >= PageSize;
                ItemsView.ItemsSource = _photos.ToList();
            }
            else
            {
                var batch = await LoadBrowseItemsAsync(client, _offset, PageSize, cts.Token);
                _browseItems.AddRange(batch);
                _offset += batch.Count;
                _hasMore = batch.Count >= PageSize;
                ItemsView.ItemsSource = _browseItems.ToList();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("加载分类内容失败", ex);
            await DisplayAlertAsync(TitleLabel.Text, $"加载失败：{ex.Message}", "确定");
        }
        finally
        {
            if (showBusyIndicator)
            {
                BusyIndicator.IsRunning = false;
                BusyIndicator.IsVisible = false;
            }
            _loading = false;
        }
    }

    private Task<IReadOnlyList<Photo>> LoadPhotosAsync(
        SynologyClient client,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var library = _libraryContext?.ExploreLibrary ?? PhotosLibrary.PersonalSpace;
        if (_category == PhotosBrowseCategory.RecentlyAdded)
            return PhotosBrowseGateway.ListRecentlyAddedAsync(client, offset, limit, cancellationToken);
        if (_category == PhotosBrowseCategory.Video)
            return PhotosBrowseGateway.ListVideosAsync(client, library, offset, limit, cancellationToken);
        if (_filterId.HasValue)
            return PhotosBrowseGateway.ListFilteredPhotosAsync(
                client, library, _category, _filterId.Value, offset, limit, cancellationToken);

        return Task.FromResult<IReadOnlyList<Photo>>([]);
    }

    private Task<IReadOnlyList<BrowseAlbumItem>> LoadBrowseItemsAsync(
        SynologyClient client,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var library = _libraryContext?.ExploreLibrary ?? PhotosLibrary.PersonalSpace;
        return PhotosBrowseGateway.ListBrowseItemsAsync(
            client, library, _category, offset, limit, cancellationToken);
    }

    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not Photo photo)
            return;

        var index = _photos.FindIndex(p => p.Id == photo.Id);
        if (index < 0)
            index = 0;

        PhotosAlbumMediaScope.Clear();
        var thumbBytes = GridThumbnailCapture.TryCapture(sender, photo);
        NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var thumbPath);
        await ShellNavigation.PushModalAsync(new PhotoViewerPage(
            _photos, index, _connection, _libraryContext?.ExploreLibrary ?? PhotosLibrary.PersonalSpace,
            seedThumbnailPath: thumbPath,
            seedThumbnailBytes: thumbBytes));
    }

    private async void OnBrowseItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not BrowseAlbumItem item)
            return;

        if (_category is PhotosBrowseCategory.Person or PhotosBrowseCategory.Concept
            or PhotosBrowseCategory.Geocoding or PhotosBrowseCategory.GeneralTag)
        {
            await ShellNavigation.PushAsync(new CategoryBrowsePage(
                _category, _connection, _libraryContext, item.Id,
                ResolveTitle(_category, item.Name, item.Id)));
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();
}
