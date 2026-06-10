using NSynology;
using NSynology.Foto;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class NasAlbumsView : ContentView
{
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

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        await LoadAlbumsAsync();
        AlbumsRefreshView.IsRefreshing = false;
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadAlbumsAsync();

    private async Task LoadAlbumsAsync()
    {
        if (_loading)
            return;

        _loading = true;
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        StatusLabel.Text = "正在加载 NAS 相册…";
        AlbumsView.IsEnabled = false;

        try
        {
            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            {
                StatusLabel.Text = "未连接 NAS。请先在首页登录或检查连接设置。";
                AlbumsView.ItemsSource = null;
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var albums = (await SynologyManager.Client.Foto.GetAlbumsAsync(0, 100, cts.Token)).ToList();
            AlbumsView.ItemsSource = albums;
            StatusLabel.Text = albums.Count == 0 ? "暂无相册" : $"共 {albums.Count} 个相册";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "加载超时，请检查 NAS 地址与网络后点「刷新」。";
        }
        catch (Exception ex)
        {
            AppLog.Error("加载 NAS 相册失败", ex);
            if (NasSessionHelper.IsSessionError(ex))
            {
                StatusLabel.Text =
                    "无法访问 Synology Photos 相册（错误 119：当前 DSM 会话对 Photos API 无效）。\n" +
                    "请在 DSM 中为该用户启用 Photos 权限，或改用「更多」中的 File Station 备份路径。\n" +
                    "可在「更多」重新登录后重试。";
                AlbumsView.ItemsSource = null;
            }
            else
                StatusLabel.Text = $"加载失败：{ex.Message}";
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            AlbumsView.IsEnabled = true;
            _loading = false;
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
}
