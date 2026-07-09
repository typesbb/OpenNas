using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.ViewModels;

namespace OpenNas.Views;

public partial class AlbumsPage : ContentPage
{
    private readonly ConnectionService _connection;
    private readonly PhotosLibraryContext _libraryContext;
    private readonly AlbumsPageViewModel _viewModel;

    public AlbumsPage(
        ConnectionService connection,
        PhotosLibraryContext libraryContext,
        AlbumsPageViewModel viewModel)
    {
        _connection = connection;
        _libraryContext = libraryContext;
        _viewModel = viewModel;
        InitializeComponent();

        AlbumsContent.BindConnection(connection, libraryContext);
        ExploreContent.Bind(connection, libraryContext);
        TimelineContent.Bind(connection, libraryContext);
        SpaceBar.Bind(libraryContext);

        _libraryContext.ViewModeChanged += OnViewModeChanged;
        _libraryContext.AlbumFilterChanged += OnSpaceContextChanged;
        _libraryContext.TimelineLibraryChanged += OnSpaceContextChanged;
        _libraryContext.ExploreLibraryChanged += OnSpaceContextChanged;

        Loaded += OnPageLoaded;
        UpdateViewTitle();
        UpdateViewModeUi();
        UpdateToolbar();
        UpdateSpaceBarLayout();
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        await _libraryContext.RefreshTeamSpaceSettingsAsync();
        UpdateViewTitle();
        ApplyMediaScopeForCurrentView();
        UpdateSpaceBarLayout();
        await EnsureCurrentViewLoadedAsync();
    }

    private void OnViewModeChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateViewTitle();
            UpdateViewModeUi();
            UpdateToolbar();
            UpdateSpaceBarLayout();
            _ = EnsureCurrentViewLoadedAsync();
        });
    }

    private void OnSpaceContextChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateSpaceBarLayout();
            ApplyMediaScopeForCurrentView();

            if (_libraryContext.CurrentViewMode == PhotosViewMode.Timeline)
            {
                TimelineContent.InvalidateCache();
                _ = TimelineContent.RefreshAsync(force: true);
            }
        });
    }

    private async void OnViewTitleClicked(object? sender, TappedEventArgs e)
    {
        var mode = _libraryContext.CurrentViewMode;
        var items = new List<DropdownMenuItem>
        {
            new("explore", "探索", mode == PhotosViewMode.Explore),
            new("albums", "相册", mode == PhotosViewMode.Albums),
            new("timeline", "时间线", mode == PhotosViewMode.Timeline)
        };

        var selected = await ShowDropdownBelowAsync(ViewTitleButton, items);
        if (selected == "explore")
            _libraryContext.SetViewMode(PhotosViewMode.Explore);
        else if (selected == "albums")
            _libraryContext.SetViewMode(PhotosViewMode.Albums);
        else if (selected == "timeline")
            _libraryContext.SetViewMode(PhotosViewMode.Timeline);
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        if (_libraryContext.CurrentViewMode != PhotosViewMode.Albums)
            return;

        var selected = await ShowDropdownBelowAsync(SortButton, AlbumsContent.GetSortMenuItems());
        if (!string.IsNullOrEmpty(selected))
            await AlbumsContent.HandleSortAsync(selected);
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_viewModel.CanCreateAlbum)
            return;

        var selected = await ShowDropdownBelowAsync(AddButton, [new DropdownMenuItem("create", "新建相册")]);
        if (selected == "create")
            await AlbumsContent.CreateAlbumAsync();
    }

    private Task<string?> ShowDropdownBelowAsync(VisualElement anchor, IReadOnlyList<DropdownMenuItem> items)
    {
        var origin = VisualPositionHelper.GetOriginInWindow(anchor);
        return Dropdown.ShowAtWindowAsync(items, origin.X, origin.Y + anchor.Height + 4);
    }

    private void UpdateViewTitle()
    {
        ViewTitleLabel.Text = _viewModel.ViewModeTitle;
        _viewModel.RefreshTitles();
    }

    private void UpdateToolbar()
    {
        SortButton.IsVisible = _viewModel.ShowSortButton;
        AddButton.IsVisible = _viewModel.CanCreateAlbum;
    }

    private void UpdateSpaceBarLayout() => SpaceBar.RefreshState();

    private void UpdateViewModeUi()
    {
        ExploreContent.IsVisible = _libraryContext.CurrentViewMode == PhotosViewMode.Explore;
        AlbumsContent.IsVisible = _libraryContext.CurrentViewMode == PhotosViewMode.Albums;
        TimelineContent.IsVisible = _libraryContext.CurrentViewMode == PhotosViewMode.Timeline;
        ApplyMediaScopeForCurrentView();
    }

    private void ApplyMediaScopeForCurrentView()
    {
        PhotosMediaLibraryScope.Current = _libraryContext.GetActiveLibrary();
    }

    private Task EnsureCurrentViewLoadedAsync(bool force = false) => _libraryContext.CurrentViewMode switch
    {
        PhotosViewMode.Albums => AlbumsContent.RefreshAsync(force),
        PhotosViewMode.Timeline => TimelineContent.RefreshAsync(force),
        _ => ExploreContent.RefreshAsync(force)
    };

    public Task RefreshCurrentViewAsync(bool force = false) => EnsureCurrentViewLoadedAsync(force);
}
