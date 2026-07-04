using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class PhotoViewerPage : ContentPage
{
    private readonly IReadOnlyList<Photo> _photos;
    private readonly ZoomableImageView _imageView;
    private readonly NasVideoPlayerView _videoView;
    private readonly ConnectionService _connection;
    private int _index;
    private bool _currentZoomed;
    private bool _initialized;
    private bool _isDismissing;
    private bool _actionBarVisible;
    private bool _exporting;
    private CancellationTokenSource? _exportCts;

    public PhotoViewerPage(IReadOnlyList<Photo> photos, int startIndex)
    {
        InitializeComponent();
        _photos = photos;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, photos.Count - 1));
        _connection = AppServices.GetRequired<ConnectionService>();

        _imageView = new ZoomableImageView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = true
        };
        _imageView.ZoomChanged += OnZoomChanged;
        _imageView.SingleTapped += OnImageSingleTapped;
        _imageView.DismissDrag += OnDismissDrag;
        _imageView.DismissRequested += OnDismissRequested;
        _imageView.OnSwipeNavigateAsync = NavigateAsync;

        _videoView = new NasVideoPlayerView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false
        };
        _videoView.DismissDrag += OnDismissDrag;
        _videoView.DismissRequested += OnDismissRequested;
        _videoView.OnSwipeNavigateAsync = NavigateAsync;
        _videoView.FullscreenRequested += OnVideoFullscreenRequested;
        _videoView.SingleTapped += OnVideoSingleTapped;

        DismissHost.Children.Add(_imageView);
        DismissHost.Children.Add(_videoView);

        Loaded += OnLoaded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized || _photos.Count == 0)
            return;

        _initialized = true;
        ShowCurrent();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (_photos.Count == 0)
            return;

        HideActionBar();
        var photo = _photos[_index];
        var isVideo = photo.IsVideo;

        _currentZoomed = false;
        _imageView.IsVisible = !isVideo;
        _videoView.IsVisible = isVideo;
        FullscreenButton.IsVisible = !isVideo;
        ActionBar.IsVisible = false;

        if (isVideo)
        {
            _imageView.Photo = null;
            _videoView.CanGoPrevious = _index > 0;
            _videoView.CanGoNext = _index < _photos.Count - 1;
            _videoView.Photo = photo;
        }
        else
        {
            _videoView.Stop();
            _videoView.Photo = null;
            UpdateNavigationBounds();
            _imageView.Photo = photo;
        }
    }

    private void UpdateNavigationBounds()
    {
        _imageView.CanGoPrevious = _index > 0;
        _imageView.CanGoNext = _index < _photos.Count - 1;
    }

    private Task NavigateAsync(int direction)
    {
        var next = _index + direction;
        if (next < 0 || next >= _photos.Count)
            return Task.CompletedTask;

        _index = next;
        ShowCurrent();
        return Task.CompletedTask;
    }

    private void OnZoomChanged(object? sender, EventArgs e)
    {
        if (sender is ZoomableImageView zoomable && _photos[_index].IsVideo == false)
        {
            _currentZoomed = zoomable.IsZoomed;
            if (_currentZoomed)
                HideActionBar();
        }
    }

    private void OnImageSingleTapped(object? sender, EventArgs e)
    {
        if (_photos[_index].IsVideo || _currentZoomed || _exporting)
            return;

        ToggleActionBar();
    }

    private void OnVideoSingleTapped(object? sender, EventArgs e)
    {
        if (!_photos[_index].IsVideo || _exporting)
            return;

        ToggleActionBar();
    }

    private void ToggleActionBar()
    {
        _actionBarVisible = !_actionBarVisible;
        ActionBar.IsVisible = _actionBarVisible;
    }

    private void HideActionBar()
    {
        _actionBarVisible = false;
        ActionBar.IsVisible = false;
    }

    private void OnDismissDrag(object? sender, double totalY)
    {
        if (_currentZoomed || _isDismissing)
            return;

        if (totalY <= 0)
        {
            _ = AnimateDismissResetAsync();
            return;
        }

        HideActionBar();
        DismissHost.TranslationY = totalY;
        var threshold = _photos[_index].IsVideo
            ? _videoView.GetDismissThreshold()
            : _imageView.GetDismissThreshold();
        DismissHost.Opacity = Math.Clamp(1 - totalY / (threshold * 1.6), 0.35, 1);
    }

    private async void OnDismissRequested(object? sender, EventArgs e)
    {
        if (_currentZoomed || _isDismissing)
        {
            await AnimateDismissResetAsync();
            return;
        }

        _isDismissing = true;
        try
        {
            await AnimateDismissOutAsync();
            await Navigation.PopAsync();
        }
        finally
        {
            _isDismissing = false;
        }
    }

    private Task AnimateDismissOutAsync()
    {
        var height = Height > 0 ? Height : DismissHost.Height > 0 ? DismissHost.Height : 800;
        return Task.WhenAll(
            DismissHost.TranslateToAsync(0, height, 240, Easing.CubicIn),
            DismissHost.FadeToAsync(0, 240, Easing.CubicIn));
    }

    private Task AnimateDismissResetAsync()
    {
        return Task.WhenAll(
            DismissHost.TranslateToAsync(0, 0, 180, Easing.CubicOut),
            DismissHost.FadeToAsync(1, 180, Easing.CubicOut));
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnFullscreenClicked(object? sender, EventArgs e) =>
        await FullscreenMediaLauncher.OpenAsync(this, _photos, _index);

    private async void OnVideoFullscreenRequested(object? sender, EventArgs e) =>
        await FullscreenMediaLauncher.OpenAsync(this, _photos, _index);

    private async void OnDownloadClicked(object? sender, EventArgs e) =>
        await DownloadCurrentAsync();

    private async void OnShareClicked(object? sender, EventArgs e) =>
        await ShareCurrentAsync();

    private Photo CurrentPhoto => _photos[_index];

    private async Task DownloadCurrentAsync()
    {
        if (_exporting)
            return;

        if (!await AppPermissionBootstrap.EnsureDownloadAllowedAsync(this, _connection))
            return;

        _exporting = true;
        HideActionBar();
        ExportProgressPanel.IsVisible = true;
        ExportStatusLabel.Text = "正在下载…";
        ExportProgressBar.Progress = 0;
        _exportCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<NasDownloadProgress>(p =>
            {
                ExportStatusLabel.Text = p.TotalBytes is > 0
                    ? $"正在下载 {p.BytesReceived * 100 / p.TotalBytes.Value}%"
                    : $"正在下载 {NasMediaCache.FormatBytes(p.BytesReceived)}";
                if (p.TotalBytes is > 0)
                    ExportProgressBar.Progress = Math.Clamp(p.BytesReceived / (double)p.TotalBytes.Value, 0, 1);
            });

            var saved = await NasPhotoDownloadService.DownloadSingleToGalleryAsync(
                CurrentPhoto,
                progress,
                _exportCts.Token);

            if (saved)
            {
                await UiFeedback.ToastAsync("已保存到本机");
                return;
            }

            await DisplayAlertAsync("下载失败", "无法保存到本机，请稍后重试。", "确定");
        }
        catch (OperationCanceledException)
        {
            await UiFeedback.ToastAsync("下载已取消");
        }
        catch (Exception ex)
        {
            AppLog.Error("预览页下载失败", ex);
            await UiFeedback.ShowApiErrorAsync(this, "下载失败", ex);
        }
        finally
        {
            ExportProgressPanel.IsVisible = false;
            _exportCts?.Dispose();
            _exportCts = null;
            _exporting = false;
        }
    }

    private async Task ShareCurrentAsync()
    {
        if (_exporting)
            return;

        _exporting = true;
        HideActionBar();
        ExportProgressPanel.IsVisible = true;
        ExportStatusLabel.Text = "正在准备分享…";
        ExportProgressBar.Progress = 0;
        _exportCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<NasDownloadProgress>(p =>
            {
                if (p.TotalBytes is > 0)
                    ExportProgressBar.Progress = Math.Clamp(p.BytesReceived / (double)p.TotalBytes.Value, 0, 1);
            });

            var shared = await NasMediaShareHelper.ShareAsync(CurrentPhoto, progress, _exportCts.Token);
            if (!shared)
                await DisplayAlertAsync("分享失败", "无法准备分享文件，请稍后重试。", "确定");
        }
        catch (OperationCanceledException)
        {
            await UiFeedback.ToastAsync("已取消");
        }
        catch (Exception ex)
        {
            AppLog.Error("预览页分享失败", ex);
            await UiFeedback.ShowApiErrorAsync(this, "分享失败", ex);
        }
        finally
        {
            ExportProgressPanel.IsVisible = false;
            _exportCts?.Dispose();
            _exportCts = null;
            _exporting = false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        try { _exportCts?.Cancel(); } catch (ObjectDisposedException) { }
        _videoView.Stop();
        DismissHost.TranslationY = 0;
        DismissHost.Opacity = 1;
    }
}
