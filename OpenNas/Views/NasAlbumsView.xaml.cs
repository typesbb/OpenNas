using NSynology;
using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public enum AlbumSortMode
{
    CreateTime,
    UpdateTime,
    Name
}

public partial class NasAlbumsView : ContentView
{
    private readonly List<Album> _albums = [];
    private AlbumSortMode _sortMode = AlbumSortMode.CreateTime;
    private bool _loading;

    public Task RefreshAsync()
    {
        _loading = false;
        return LoadAlbumsAsync();
    }

    public NasAlbumsView()
    {
        InitializeComponent();
        AlbumsRefreshView.Refreshing += OnPullRefreshing;
        Loaded += async (_, _) => await LoadAlbumsAsync();
    }

    public Task CreateAlbumAsync() => CreateAlbumInternalAsync();

    public IReadOnlyList<DropdownMenuItem> GetSortMenuItems() =>
    [
        new("create", "创建时间排序", _sortMode == AlbumSortMode.CreateTime),
        new("update", "更新时间排序", _sortMode == AlbumSortMode.UpdateTime),
        new("name", "按名称排序", _sortMode == AlbumSortMode.Name)
    ];

    public void SetSortMode(string key)
    {
        _sortMode = key switch
        {
            "update" => AlbumSortMode.UpdateTime,
            "name" => AlbumSortMode.Name,
            _ => AlbumSortMode.CreateTime
        };
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
            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            {
                _albums.Clear();
                AlbumsView.ItemsSource = null;
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _albums.Clear();
            _albums.AddRange(await SynologyManager.Client.Foto.GetAlbumsAsync(0, 100, cts.Token));
            ApplySort();
        }
        catch (OperationCanceledException)
        {
            await ShowAlertAsync("加载超时，请检查 NAS 地址与网络后重试。");
        }
        catch (Exception ex)
        {
            AppLog.Error("加载 NAS 相册失败", ex);
            if (NasSessionHelper.IsSessionError(ex))
            {
                _albums.Clear();
                AlbumsView.ItemsSource = null;
                await ShowAlertAsync(
                    "无法访问 Synology Photos 相册（错误 119：当前 DSM 会话对 Photos API 无效）。\n" +
                    "请在 DSM 中为该用户启用 Photos 权限，或在「更多」重新登录后重试。");
            }
            else
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
            await page.DisplayAlertAsync("新建相册", $"创建失败：{ex.Message}", "确定");
        }
    }

    private void OnThumbHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Image image && image.BindingContext is Album album)
            NasThumbnailLoader.TryLoadAlbumThumbnail(image, album);
    }

    private async void OnAlbumSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Album album)
            return;

        AlbumsView.SelectedItem = null;
        await ShellNavigation.PushAsync(new AlbumDetailPage(album));
    }

    private Page? GetHostPage() =>
        Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    private Task ShowAlertAsync(string message)
    {
        var page = GetHostPage();
        return page == null ? Task.CompletedTask : page.DisplayAlertAsync("相册", message, "确定");
    }
}
