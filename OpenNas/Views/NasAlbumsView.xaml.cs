using NSynology;
using NSynology.Foto;
using OpenNas.Behaviors;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas.Views;

public enum AlbumSortMode
{
    CreateTime,
    UpdateTime,
    Name
}

public partial class NasAlbumsView : ContentView
{
    private const string AlbumSortModeKey = "album_sort_mode";
    private const string DefaultSortMode = "create";

    private readonly List<Album> _albums = [];
    private AlbumSortMode _sortMode;
    private bool _loading;
    private bool _suppressAlbumTap;

    public Task RefreshAsync()
    {
        _loading = false;
        return LoadAlbumsAsync();
    }

    public NasAlbumsView()
    {
        _sortMode = LoadSortMode();
        InitializeComponent();
        AlbumsRefreshView.Refreshing += OnPullRefreshing;
        LongPressBehavior.LongPressed += OnAlbumLongPressed;
        Loaded += async (_, _) => await LoadAlbumsAsync();
    }

    public Task CreateAlbumAsync() => CreateAlbumInternalAsync();

    private static AlbumSortMode LoadSortMode()
    {
        try { return Enum.Parse<AlbumSortMode>(Preferences.Default.Get(AlbumSortModeKey, DefaultSortMode)); }
        catch { return AlbumSortMode.CreateTime; }
    }

    private static void SaveSortMode(AlbumSortMode mode) =>
        Preferences.Default.Set(AlbumSortModeKey, mode.ToString());

    public IReadOnlyList<DropdownMenuItem> GetSortMenuItems() =>
    [
        new("create", "创建时间排序", _sortMode == AlbumSortMode.CreateTime),
        new("update", "更新时间排序", _sortMode == AlbumSortMode.UpdateTime),
        new("name", "按名称排序", _sortMode == AlbumSortMode.Name)
    ];

    public void SetSortMode(string key)
    {
        var previous = _sortMode;
        _sortMode = key switch
        {
            "update" => AlbumSortMode.UpdateTime,
            "name" => AlbumSortMode.Name,
            _ => AlbumSortMode.CreateTime
        };
        if (_sortMode != previous)
            SaveSortMode(_sortMode);
        ApplySort();
    }

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        await LoadAlbumsAsync();
        AlbumsRefreshView.IsRefreshing = false;
    }

    private async Task LoadAlbumsAsync()
    {
        if (_loading)
            return;

        _loading = true;
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        AlbumsView.IsEnabled = false;

        try
        {
            var client = SynologyManager.Client;
            if (client == null || string.IsNullOrEmpty(client.Sid))
            {
                _albums.Clear();
                AlbumsView.ItemsSource = null;
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _albums.Clear();
            var albums = await Task.Run(
                async () => await client.Foto.GetAlbumsAsync(0, 100, cts.Token),
                cts.Token);
            _albums.AddRange(albums);
            ApplySort();
        }
        catch (OperationCanceledException)
        {
            await ShowAlertAsync("加载超时，请检查 NAS 地址与网络后重试。");
        }
        catch (Exception ex)
        {
            AppLog.Error("加载 NAS 相册失败", ex);
            if (await NasSessionGuard.HandleIfNeededAsync(ex))
                return;

            await ShowAlertAsync($"加载失败：{ex.Message}");
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            AlbumsView.IsEnabled = true;
            _loading = false;
        }
    }

    private void ApplySort()
    {
        IEnumerable<Album> sorted = _sortMode switch
        {
            AlbumSortMode.UpdateTime => _albums.OrderByDescending(a => a.StartTime > 0 ? a.StartTime : a.CreateTime),
            AlbumSortMode.Name => _albums.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => _albums.OrderByDescending(a => a.CreateTime)
        };

        AlbumsView.ItemsSource = sorted.ToList();
        AlbumCountLabel.Text = _albums.Count == 0 ? "" : $"{_albums.Count} 个相册";
        AlbumCountLabel.IsVisible = _albums.Count > 0;
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
            await LoadAlbumsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("创建相册失败", ex);
            if (await NasSessionGuard.HandleIfNeededAsync(ex))
                return;

            await page.DisplayAlertAsync("新建相册", $"创建失败：{ex.Message}", "确定");
        }
    }

    private async void OnAlbumLongPressed(object? sender, LongPressBehavior.LongPressEventArgs e)
    {
        if (e.Context is not Album album)
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
            ApplySort();
        }
        catch (Exception ex)
        {
            AppLog.Error("重命名相册失败", ex);
            if (await NasSessionGuard.HandleIfNeededAsync(ex))
                return;

            await page.DisplayAlertAsync("重命名相册", $"重命名失败：{ex.Message}", "确定");
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
            ApplySort();
        }
        catch (Exception ex)
        {
            AppLog.Error("删除相册失败", ex);
            if (await NasSessionGuard.HandleIfNeededAsync(ex))
                return;

            await page.DisplayAlertAsync("删除相册", $"删除失败：{ex.Message}", "确定");
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

        await ShellNavigation.PushAsync(new AlbumDetailPage(album));
    }

    private void OnThumbHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Image image && image.BindingContext is Album album)
            NasThumbnailLoader.TryLoadAlbumThumbnail(image, album);
    }

    private Page? GetHostPage() =>
        Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    private Task ShowAlertAsync(string message)
    {
        var page = GetHostPage();
        return page == null ? Task.CompletedTask : page.DisplayAlertAsync("相册", message, "确定");
    }
}
