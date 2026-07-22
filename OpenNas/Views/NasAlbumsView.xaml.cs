using System.Collections.ObjectModel;
using NSynology;
using NSynology.Foto;
using OpenNas.Behaviors;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class NasAlbumsView : ContentView
{
    private const string AlbumSortKey = "album_sort_key";

    private readonly ObservableCollection<Album> _albums = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private ConnectionService? _connection;
    private PhotosLibraryContext? _libraryContext;
    private AlbumListOrder _albumOrder = new();
    private AlbumListFilter? _lastDisplayFilter;
    private AlbumListFilter? _loadedForFilter;
    private string _activeSortKey;
    private bool _suppressAlbumTap;

    public void BindConnection(ConnectionService connection, PhotosLibraryContext libraryContext)
    {
        _connection = connection;
        _libraryContext = libraryContext;
        _libraryContext.AlbumFilterChanged += OnContextChanged;
    }

    public async Task RefreshAsync(bool force = false)
    {
        if (_connection != null)
            await _connection.EnsureBestEndpointAsync();

        if (force)
            _lastDisplayFilter = null;
        await LoadAlbumsAsync(force);
    }

    public NasAlbumsView()
    {
        _activeSortKey = Preferences.Default.Get(AlbumSortKey, "start_desc");
        InitializeComponent();
        AlbumsView.ItemsSource = _albums;
        AlbumsRefreshView.Refreshing += OnPullRefreshing;
        LongPressBehavior.LongPressed += OnAlbumLongPressed;
    }

    public Task CreateAlbumAsync() => CreateAlbumInternalAsync();

    public IReadOnlyList<DropdownMenuItem> GetSortMenuItems()
    {
        var filter = PhotosLibraryContext.NormalizeAlbumFilter(
            _libraryContext?.CurrentAlbumFilter ?? AlbumListFilter.My);
        var items = new List<DropdownMenuItem>();

        if (_libraryContext?.CurrentViewMode == PhotosViewMode.Albums &&
            filter == AlbumListFilter.SharedWithMe)
        {
            items.Add(new("share_desc", "共享时间（新到旧）", _activeSortKey == "share_desc"));
            items.Add(new("share_asc", "共享时间（旧到新）", _activeSortKey == "share_asc"));
            items.Add(new("start_desc", "开始时间（新到旧）", _activeSortKey == "start_desc"));
            items.Add(new("start_asc", "开始时间（旧到新）", _activeSortKey == "start_asc"));
        }
        else
        {
            items.Add(new("start_desc", "开始时间（新到旧）", _activeSortKey == "start_desc"));
            items.Add(new("start_asc", "开始时间（旧到新）", _activeSortKey == "start_asc"));
        }

        items.Add(new("count_desc", "照片数量（多→少）", _activeSortKey == "count_desc"));
        items.Add(new("count_asc", "照片数量（少→多）", _activeSortKey == "count_asc"));
        items.Add(new("name_asc", "相册名称（A→Z）", _activeSortKey == "name_asc"));
        items.Add(new("name_desc", "相册名称（Z→A）", _activeSortKey == "name_desc"));
        items.Add(new("create_desc", "创建时间（新→旧）", _activeSortKey == "create_desc"));
        items.Add(new("update_desc", "更新时间（新→旧）", _activeSortKey == "update_desc"));

        return items;
    }

    public Task HandleSortAsync(string key) => SetSortModeAsync(key);

    public IReadOnlyList<DropdownMenuItem> GetOptionsMenuItems() => GetSortMenuItems();

    public async Task HandleOptionAsync(string key)
    {
        switch (key)
        {
            case "start_desc":
            case "start_asc":
            case "share_desc":
            case "share_asc":
            case "count_desc":
            case "count_asc":
            case "name_asc":
            case "name_desc":
            case "create_desc":
            case "update_desc":
                await SetSortModeAsync(key);
                break;
        }
    }

    public async Task SetSortModeAsync(string key)
    {
        _activeSortKey = key;
        Preferences.Default.Set(AlbumSortKey, key);

        if (IsClientSort(key))
        {
            if (_albums.Count == 0)
                await LoadAlbumsAsync();
            else
                ApplyClientSort();
            return;
        }

        var filter = PhotosLibraryContext.NormalizeAlbumFilter(
            _libraryContext?.CurrentAlbumFilter ?? AlbumListFilter.My);
        if (filter == AlbumListFilter.SharedWithMe)
        {
            _albumOrder.SharedWithMeSortBy = key is "share_asc" or "share_desc" ? "share_modify_time" : "start_time";
            _albumOrder.SharedWithMeSortDirection = key is "share_asc" or "start_asc" ? "asc" : "desc";
        }
        else
        {
            _albumOrder.AlbumListSortBy = "start_time";
            _albumOrder.AlbumListSortDirection = key == "start_asc" ? "asc" : "desc";
        }

        var client = SynologyManager.Client;
        if (client != null && !string.IsNullOrEmpty(client.Sid))
        {
            try
            {
                await client.Foto.SetAlbumListOrderAsync(_albumOrder);
            }
            catch (Exception ex)
            {
                AppLog.Error("保存相册排序失败", ex);
            }
        }

        await LoadAlbumsAsync(force: true);
    }

    private static bool IsClientSort(string key) =>
        key is "count_desc" or "count_asc" or "name_asc" or "name_desc" or "create_desc" or "update_desc";

    private void OnContextChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = RefreshAsync(force: true);
        });
    }

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        if (_connection != null)
            await _connection.EnsureBestEndpointAsync();

        _lastDisplayFilter = null;
        await LoadAlbumsAsync(force: true, showBusyIndicator: false);
        AlbumsRefreshView.IsRefreshing = false;
    }

    private async Task LoadAlbumsAsync(bool force = false, bool showBusyIndicator = true)
    {
        var filter = PhotosLibraryContext.NormalizeAlbumFilter(
            _libraryContext?.CurrentAlbumFilter ?? AlbumListFilter.My);
        if (!force && _albums.Count > 0 && _loadedForFilter == filter)
            return;

        await _loadGate.WaitAsync();
        var previousAlbums = _albums.ToList();
        try
        {
            if (showBusyIndicator)
            {
                BusyIndicator.IsVisible = true;
                BusyIndicator.IsRunning = true;
            }
            AlbumsView.IsEnabled = false;

            var client = SynologyManager.Client;
            if (client == null || string.IsNullOrEmpty(client.Sid))
            {
                await MainThread.InvokeOnMainThreadAsync(() => _albums.Clear());
                return;
            }

            _libraryContext?.ApplyAlbumCookies();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            if (!IsClientSort(_activeSortKey))
            {
                var order = await client.Foto.GetAlbumListOrderAsync(cts.Token);
                if (order != null)
                    _albumOrder = order;
            }

            var useServerSort = !IsClientSort(_activeSortKey);
            var (sortBy, sortDir) = useServerSort
                ? ResolveSort(filter)
                : ("start_time", "desc");

            if (filter != AlbumListFilter.SharedWithMe && filter != _lastDisplayFilter)
            {
                await client.Foto.SetAlbumListDisplayAsync(
                    PhotosLibraryContext.MapAlbumDisplayType(filter),
                    cts.Token);
                _lastDisplayFilter = filter;
            }

            var albums = filter switch
            {
                AlbumListFilter.SharedWithMe => await client.Foto.ListSharedWithMeAlbumsAsync(0, 500, cts.Token),
                _ => await client.Foto.GetAlbumsAsync(
                    0, 100, PhotosLibraryContext.MapAlbumCategory(filter), sortBy, sortDir, cancellationToken: cts.Token)
            };

            var albumList = albums as IList<Album> ?? albums.ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ReplaceAlbums(albumList);
                if (filter == AlbumListFilter.SharedWithMe && useServerSort)
                    ApplySharedWithMeServerSort(sortBy, sortDir);
                else if (IsClientSort(_activeSortKey))
                    ApplyClientSort();
                UpdateAlbumCountLabel();
                _loadedForFilter = filter;
            });
        }
        catch (OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_albums.Count == 0 && previousAlbums.Count > 0)
                    ReplaceAlbums(previousAlbums);
            });

            if (SynologyManager.IsAddressSwitchErrorSuppressed)
                return;

            await ShowAlertAsync(
                "内外网均无法连接。请检查手机网络是否正常，并到「我的」确认内外网地址是否正确。");
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_albums.Count == 0 && previousAlbums.Count > 0)
                    ReplaceAlbums(previousAlbums);
            });

            AppLog.Error("加载 NAS 相册失败", ex);
            await UiFeedback.ShowApiErrorAsync(GetHostPage(), "相册", ex, $"加载失败：{ex.Message}");
        }
        finally
        {
            if (showBusyIndicator)
            {
                BusyIndicator.IsRunning = false;
                BusyIndicator.IsVisible = false;
            }
            AlbumsView.IsEnabled = true;
            _loadGate.Release();
        }
    }

    private void ReplaceAlbums(IEnumerable<Album> albums)
    {
        var sorted = albums as IList<Album> ?? albums.ToList();
        _albums.Clear();
        foreach (var album in sorted)
            _albums.Add(album);

        // Android CollectionView 在批量 Clear/Add 后偶发不刷新，强制重绑一次
        if (_albums.Count > 0)
        {
            AlbumsView.ItemsSource = null;
            AlbumsView.ItemsSource = _albums;
        }
    }

    private (string SortBy, string SortDir) ResolveSort(AlbumListFilter filter)
    {
        if (filter == AlbumListFilter.SharedWithMe)
        {
            return _activeSortKey switch
            {
                "share_asc" => ("share_modify_time", "asc"),
                "share_desc" => ("share_modify_time", "desc"),
                "start_asc" => ("start_time", "asc"),
                "start_desc" => ("start_time", "desc"),
                _ => (_albumOrder.SharedWithMeSortBy, _albumOrder.SharedWithMeSortDirection)
            };
        }

        return (_albumOrder.AlbumListSortBy, _albumOrder.AlbumListSortDirection);
    }

    private void ApplySharedWithMeServerSort(string sortBy, string sortDir)
    {
        IEnumerable<Album> sorted = sortBy == "share_modify_time"
            ? sortDir == "asc"
                ? _albums.OrderBy(a => a.Additional?.SharingInfo?.Mtime ?? a.StartTime)
                : _albums.OrderByDescending(a => a.Additional?.SharingInfo?.Mtime ?? a.StartTime)
            : sortDir == "asc"
                ? _albums.OrderBy(a => a.StartTime)
                : _albums.OrderByDescending(a => a.StartTime);
        ReplaceAlbums(sorted);
    }

    private void ApplyClientSort()
    {
        IEnumerable<Album> sorted = _activeSortKey switch
        {
            "count_desc" => _albums.OrderByDescending(a => a.ItemCount),
            "count_asc" => _albums.OrderBy(a => a.ItemCount),
            "name_asc" => _albums.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            "name_desc" => _albums.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            "create_desc" => _albums.OrderByDescending(a => a.CreateTime),
            "update_desc" => _albums.OrderByDescending(a => a.StartTime > 0 ? a.StartTime : a.CreateTime),
            _ => _albums.ToList()
        };

        ReplaceAlbums(sorted);
    }

    private async Task CreateAlbumInternalAsync()
    {
        var page = GetHostPage();
        if (page == null)
            return;

        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
        {
            await page.DisplayAlertAsync("未连接 NAS", "请先在首页登录或检查连接设置。", "确定");
            return;
        }

        var name = await page.DisplayPromptAsync("新建相册", "相册名称", "创建", "取消");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            await SynologyManager.Client.Foto.CreateNormalAlbumAsync(name.Trim());
            await LoadAlbumsAsync(force: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("创建相册失败", ex);
            await UiFeedback.ShowApiErrorAsync(page, "新建相册", ex, $"创建失败：{ex.Message}");
        }
    }

    private async void OnAlbumLongPressed(object? sender, LongPressBehavior.LongPressEventArgs e)
    {
        if (e.Context is not Album album)
            return;

        if (AlbumShareHelper.IsSharedWithMe(album))
            return;

        _suppressAlbumTap = true;

        var selected = await Dropdown.ShowAtWindowAsync(
        [
            new DropdownMenuItem("rename", "重命名"),
            new DropdownMenuItem("delete", "删除相册")
        ], e.WindowX, e.WindowY);

        if (string.IsNullOrEmpty(selected))
            return;

        if (selected == "rename")
            await RenameAlbumAsync(album);
        else if (selected == "delete")
            await DeleteAlbumAsync(album);
    }

    private async Task RenameAlbumAsync(Album album)
    {
        var page = GetHostPage();
        if (page == null)
            return;

        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
        {
            await page.DisplayAlertAsync("未连接 NAS", "请先在首页登录或检查连接设置。", "确定");
            return;
        }

        var newName = await page.DisplayPromptAsync("重命名相册", "新的相册名称", "确定", "取消", initialValue: album.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == album.Name)
            return;

        try
        {
            var renamed = await SynologyManager.Client.Foto.RenameAlbumAsync(album.Id, newName.Trim());
            album.Name = renamed.Name;
            var index = _albums.IndexOf(album);
            if (index >= 0)
            {
                _albums.RemoveAt(index);
                _albums.Insert(index, album);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("重命名相册失败", ex);
            await UiFeedback.ShowApiErrorAsync(page, "重命名相册", ex, $"重命名失败：{ex.Message}");
        }
    }

    private async Task DeleteAlbumAsync(Album album)
    {
        var page = GetHostPage();
        if (page == null)
            return;

        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
        {
            await page.DisplayAlertAsync("未连接 NAS", "请先在首页登录或检查连接设置。", "确定");
            return;
        }

        var confirm = await page.DisplayAlertAsync("删除相册",
            $"确定要删除相册「{album.Name}」？该操作不可恢复。", "删除", "取消");
        if (!confirm)
            return;

        try
        {
            await SynologyManager.Client.Foto.DeleteAlbumAsync(album.Id);
            _albums.Remove(album);
            UpdateAlbumCountLabel();
        }
        catch (Exception ex)
        {
            AppLog.Error("删除相册失败", ex);
            await UiFeedback.ShowApiErrorAsync(page, "删除相册", ex, $"删除失败：{ex.Message}");
        }
    }

    private async void OnAlbumTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressAlbumTap)
        {
            _suppressAlbumTap = false;
            return;
        }

        if (sender is not BindableObject bindable || bindable.BindingContext is not Album album)
            return;

        if (_connection == null)
            throw new InvalidOperationException("NasAlbumsView 未绑定 ConnectionService。");

        await ShellNavigation.PushAsync(new AlbumDetailPage(album, _connection));
    }

    private void OnThumbHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Image image && image.BindingContext is Album album)
            NasThumbnailLoader.TryLoadAlbumThumbnail(image, album);
    }

    private void UpdateAlbumCountLabel()
    {
        if (_albums.Count == 0)
        {
            AlbumCountLabel.Text = "";
            AlbumCountLabel.IsVisible = false;
            return;
        }

        var filter = PhotosLibraryContext.NormalizeAlbumFilter(
            _libraryContext?.CurrentAlbumFilter ?? AlbumListFilter.My);
        var library = PhotosLibraryContext.MapAlbumFilterToLibrary(filter);
        var libraryTitle = PhotosLibraryContext.GetLibraryTitle(library);
        AlbumCountLabel.Text = $"{_albums.Count} 个相册 · {libraryTitle}";
        AlbumCountLabel.IsVisible = true;
    }

    private Page? GetHostPage() =>
        Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    private Task ShowAlertAsync(string message)
    {
        var page = GetHostPage();
        return page == null ? Task.CompletedTask : page.DisplayAlertAsync("相册", message, "确定");
    }
}
