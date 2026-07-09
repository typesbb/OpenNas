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

        _libraryContext.ViewModeChanged += OnViewModeChanged;
        _libraryContext.AlbumFilterChanged += OnAlbumFilterChanged;
        _libraryContext.TimelineLibraryChanged += OnTimelineLibraryChanged;

        Loaded += OnPageLoaded;
        UpdateViewTitle();
        UpdateViewModeUi();
        UpdateToolbar();
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        await _libraryContext.RefreshTeamSpaceSettingsAsync();
        UpdateViewTitle();
        ApplyMediaScopeForCurrentView();
        await RefreshCurrentViewAsync();
    }

    private void OnViewModeChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            UpdateViewTitle();
            UpdateViewModeUi();
            UpdateToolbar();
            await RefreshCurrentViewAsync();
        });
    }

    private void OnAlbumFilterChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdateToolbar);
    }

    private void OnTimelineLibraryChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateToolbar();
            ApplyMediaScopeForCurrentView();
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

        var selected = await ShowDropdownBelowAsync((VisualElement)sender!, items);
        if (selected == "explore")
            _libraryContext.SetViewMode(PhotosViewMode.Explore);
        else if (selected == "albums")
            _libraryContext.SetViewMode(PhotosViewMode.Albums);
        else if (selected == "timeline")
            _libraryContext.SetViewMode(PhotosViewMode.Timeline);
    }

    private async void OnAlbumFilterClicked(object? sender, TappedEventArgs e)
    {
        if (_libraryContext.CurrentViewMode != PhotosViewMode.Albums)
            return;

        var selected = await ShowDropdownBelowAsync(AlbumFilterButton, AlbumsContent.GetFilterMenuItems());
        if (!string.IsNullOrEmpty(selected))
            await AlbumsContent.HandleFilterAsync(selected);
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        if (_libraryContext.CurrentViewMode != PhotosViewMode.Albums)
            return;

        var selected = await ShowDropdownBelowAsync(SortButton, AlbumsContent.GetSortMenuItems());
        if (!string.IsNullOrEmpty(selected))
            await AlbumsContent.HandleSortAsync(selected);
    }

    private async void OnTimelineSpaceClicked(object? sender, TappedEventArgs e)
    {
        if (_libraryContext.CurrentViewMode != PhotosViewMode.Timeline ||
            !_libraryContext.SharedSpaceEnabled)
            return;

        var items = new List<DropdownMenuItem>
        {
            new("personal", "个人空间", _libraryContext.TimelineLibrary == PhotosLibrary.PersonalSpace),
            new("shared", "共享空间", _libraryContext.TimelineLibrary == PhotosLibrary.SharedSpace)
        };

        var selected = await ShowDropdownBelowAsync(TimelineSpaceButton, items);
        if (selected == "personal")
            _libraryContext.SetTimelineLibrary(PhotosLibrary.PersonalSpace);
        else if (selected == "shared")
            _libraryContext.SetTimelineLibrary(PhotosLibrary.SharedSpace);
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
        AlbumFilterButton.IsVisible = _viewModel.ShowAlbumFilterButton;
        SortButton.IsVisible = _viewModel.ShowSortButton;
        TimelineSpaceButton.IsVisible = _viewModel.ShowTimelineSpaceButton;
        AddButton.IsVisible = _viewModel.CanCreateAlbum;
        AlbumFilterLabel.Text = _viewModel.AlbumFilterTitle;
        TimelineSpaceLabel.Text = _viewModel.TimelineLibraryTitle;
    }

    private void UpdateViewModeUi()
    {
        ExploreContent.IsVisible = _libraryContext.CurrentViewMode == PhotosViewMode.Explore;
        AlbumsContent.IsVisible = _libraryContext.CurrentViewMode == PhotosViewMode.Albums;
        TimelineContent.IsVisible = _libraryContext.CurrentViewMode == PhotosViewMode.Timeline;
        ApplyMediaScopeForCurrentView();
    }

    private void ApplyMediaScopeForCurrentView()
    {
        PhotosMediaLibraryScope.Current = _libraryContext.CurrentViewMode switch
        {
            PhotosViewMode.Timeline => _libraryContext.TimelineLibrary,
            PhotosViewMode.Explore => _libraryContext.ExploreLibrary,
            _ => PhotosLibrary.PersonalSpace
        };
    }

    private Task RefreshCurrentViewAsync() => _libraryContext.CurrentViewMode switch
    {
        PhotosViewMode.Albums => AlbumsContent.RefreshAsync(),
        PhotosViewMode.Timeline => TimelineContent.RefreshAsync(),
        _ => ExploreContent.RefreshAsync()
    };
}
