using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Views;

public partial class PhotoViewerPage : ContentPage
{
    private const int ChromeAutoHideMs = 5000;

    private readonly IReadOnlyList<Photo> _photos;
    private readonly ZoomableImageView _imageView;
    private readonly NasVideoPlayerView _videoView;
    private readonly ConnectionService _connection;
    private readonly PhotosLibrary _mediaLibrary;
    private string? _seedThumbnailPath;
    private byte[]? _seedThumbnailBytes;
    private int _index;
    private bool _initialized;
    private bool _actionBarVisible;
    private bool _chromeVisible = true;
    private bool _exporting;
    private bool _isLandscape;
    private bool _windowEventsHooked;
    private CancellationTokenSource? _exportCts;
    private CancellationTokenSource? _chromeHideCts;

    public PhotoViewerPage(
        IReadOnlyList<Photo> photos,
        int startIndex,
        ConnectionService connection,
        PhotosLibrary mediaLibrary = PhotosLibrary.PersonalSpace,
        string? seedThumbnailPath = null,
        byte[]? seedThumbnailBytes = null)
    {
        InitializeComponent();
        _photos = photos;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, photos.Count - 1));
        _connection = connection;
        _mediaLibrary = mediaLibrary;
        _seedThumbnailPath = seedThumbnailPath;
        _seedThumbnailBytes = seedThumbnailBytes is { Length: > 0 } ? seedThumbnailBytes : null;

        _imageView = new ZoomableImageView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = true,
            BackgroundColor = Colors.Transparent
        };
        _imageView.ZoomChanged += OnZoomChanged;
        _imageView.SingleTapped += OnImageSingleTapped;
        _imageView.ContentReady += OnImageContentReady;
        _imageView.OnSwipeNavigateAsync = NavigateAsync;

        _videoView = new NasVideoPlayerView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false
        };
        _videoView.OnSwipeNavigateAsync = NavigateAsync;
        _videoView.SingleTapped += OnVideoSingleTapped;
        _videoView.ZoomChanged += OnZoomChanged;
        _videoView.RotateRequested += OnRotateClicked;

        DismissHost.Children.Add(_imageView);
        DismissHost.Children.Add(_videoView);

        // 构造期就贴上占位（仅照片；视频用播放器内封面，避免叠两层）。
        var startPhoto = _photos.Count > 0 ? _photos[_index] : null;
        if (startPhoto is { IsVideo: false })
        {
            var path = ResolveSeedPath(_seedThumbnailPath, _seedThumbnailBytes, startPhoto.Id);
            if (string.IsNullOrEmpty(path)
                && NasThumbnailLoader.TryFindCachedThumbnailPath(startPhoto, out var found))
                path = found;
            if (!string.IsNullOrEmpty(path))
            {
                _seedThumbnailPath = path;
                ShowPageThumbnailPlaceholderPath(path);
            }
        }

        Loaded += OnLoaded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.EnterImmersive();
#endif
        HookWindowLifecycle();
        PhotosMediaLibraryScope.Current = _mediaLibrary;
        if (PhotosAlbumMediaScope.CurrentPassphrase != null)
            PhotosMediaLibraryScope.Current = PhotosLibrary.PersonalSpace;
        UpdateExportActionsVisibility();
        ShowChrome();
        // Modal 全屏后强制再测一次，让视频/图片按真实窗口高度居中。
        Dispatcher.Dispatch(() =>
        {
            InvalidateMeasure();
            DismissHost.InvalidateMeasure();
            _imageView.InvalidateMeasure();
            _videoView.InvalidateMeasure();
        });
        if (_initialized || _photos.Count == 0)
            return;

        _initialized = true;
        ShowCurrent();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnhookWindowLifecycle();
        _chromeHideCts?.Cancel();
        try { _exportCts?.Cancel(); } catch (ObjectDisposedException) { }
        _videoView.Stop();
        _isLandscape = false;
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.Reset();
#endif
    }

    private void HookWindowLifecycle()
    {
        if (_windowEventsHooked || Window == null)
            return;

        Window.Stopped += OnWindowStopped;
        Window.Resumed += OnWindowResumed;
        _windowEventsHooked = true;
    }

    private void UnhookWindowLifecycle()
    {
        if (!_windowEventsHooked || Window == null)
            return;

        Window.Stopped -= OnWindowStopped;
        Window.Resumed -= OnWindowResumed;
        _windowEventsHooked = false;
    }

    private void OnWindowStopped(object? sender, EventArgs e) =>
        _videoView.HandleAppSleep();

    private void OnWindowResumed(object? sender, EventArgs e)
    {
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.EnterImmersive();
#endif
        _videoView.HandleAppResume();
    }

    private void UpdateExportActionsVisibility()
    {
        // 时间线/探索等非共享相册上下文：始终允许导出；与我共享且不可下载时隐藏。
        var allowExport = PhotosAlbumMediaScope.CurrentPassphrase == null
            || PhotosAlbumMediaScope.AllowDownload;
        ActionButtons.IsVisible = allowExport;
        ActionBarDividerTop.IsVisible = allowExport;
        DownloadButton.IsVisible = allowExport;
        ShareButton.IsVisible = allowExport;
        ActionBarDivider.IsVisible = allowExport;
        if (!allowExport && !_actionBarVisible)
            DetailPanel.IsVisible = false;
    }

    private void UpdateDetailLabels()
    {
        if (_photos.Count == 0)
            return;

        var photo = _photos[_index];
        DetailTitleLabel.Text = PhotoDetailFormatter.FormatTitle(photo);
        DetailSubtitleLabel.Text = PhotoDetailFormatter.FormatSubtitle(photo);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        HookWindowLifecycle();
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
        UpdateExportActionsVisibility();
        UpdateDetailLabels();
        var photo = _photos[_index];
        var isVideo = photo.IsVideo;

        NasVideoThumbnailRepair.ScheduleRepairIfPlaceholder(photo);

        _imageView.IsVisible = !isVideo;
        _videoView.IsVisible = isVideo;
        PhotoRotateButton.IsVisible = !isVideo;
        DetailPanel.Margin = isVideo
            ? new Thickness(16, 0, 16, 72)
            : new Thickness(16, 0, 16, 96);

        if (isVideo)
        {
            _imageView.LoadPhoto(null);
            // 视频只留播放器内封面，不再叠页面级占位。
            HidePageThumbnailPlaceholder();

            var seedPath = _seedThumbnailPath;
            var seedBytes = _seedThumbnailBytes;
            _seedThumbnailPath = null;
            _seedThumbnailBytes = null;

            seedPath = ResolveSeedPath(seedPath, seedBytes, photo.Id);
            if (string.IsNullOrEmpty(seedPath)
                && NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var found))
                seedPath = found;

            _videoView.CanGoPrevious = _index > 0;
            _videoView.CanGoNext = _index < _photos.Count - 1;
            _videoView.LoadVideo(photo, seedPath);
            _videoView.ShowControls();
        }
        else
        {
            _videoView.Stop();
            _videoView.Photo = null;
            UpdateNavigationBounds();
            // 首张用点击捕获的缩略图；滑动切图走缓存。
            var seedPath = _seedThumbnailPath;
            var seedBytes = _seedThumbnailBytes;
            _seedThumbnailPath = null;
            _seedThumbnailBytes = null;

            seedPath = ResolveSeedPath(seedPath, seedBytes, photo.Id);
            if (string.IsNullOrEmpty(seedPath)
                && NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var found))
                seedPath = found;

            if (!string.IsNullOrEmpty(seedPath))
                ShowPageThumbnailPlaceholderPath(seedPath);
            else
                HidePageThumbnailPlaceholder();

            // 路径已落盘，不必再传 bytes。
            _imageView.LoadPhoto(photo, seedPath);
            PrefetchNeighbors(_index);
        }

        ShowChrome();
    }

    private static string? ResolveSeedPath(string? path, byte[]? bytes, int photoId)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;

        if (bytes is { Length: > 0 })
        {
            try
            {
                var dir = NasMediaCache.ThumbnailsDirectory;
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, photoId > 0 ? $"seed_{photoId}.jpg" : $"seed_tmp_{Guid.NewGuid():N}.jpg");
                File.WriteAllBytes(file, bytes);
                if (photoId > 0)
                    NasThumbnailLoader.RememberDisplayedThumbnail(photoId, file);
                return file;
            }
            catch (Exception ex)
            {
                AppLog.Debug("缩略图字节落盘失败", ex);
            }
        }

        return null;
    }

    private void ShowPageThumbnailPlaceholderPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        ThumbnailPlaceholder.Source = ImageSource.FromFile(path);
        ThumbnailPlaceholder.IsVisible = true;
#if ANDROID
        // MAUI AspectFit 在部分机型上会顶对齐，强制 FitCenter。
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ThumbnailPlaceholder.Handler?.PlatformView is Android.Widget.ImageView iv)
                iv.SetScaleType(Android.Widget.ImageView.ScaleType.FitCenter);
        });
#endif
        AppLog.Debug($"页面缩略图占位 path={path}");
    }

    private void HidePageThumbnailPlaceholder()
    {
        ThumbnailPlaceholder.IsVisible = false;
        ThumbnailPlaceholder.Source = null;
    }

    private void OnImageContentReady(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(HidePageThumbnailPlaceholder);

    private void PrefetchNeighbors(int centerIndex)
    {
        PrefetchPhoto(centerIndex - 1);
        PrefetchPhoto(centerIndex + 1);
    }

    private void PrefetchPhoto(int index)
    {
        if (index < 0 || index >= _photos.Count)
            return;

        var photo = _photos[index];
        if (photo.IsVideo)
            return;

        _ = NasThumbnailLoader.EnsureCachedAsync(photo);
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
        var zoomed = sender switch
        {
            ZoomableImageView image => image.IsZoomed,
            NasVideoPlayerView video => video.IsZoomed,
            _ => false
        };

        if (zoomed)
            HideActionBar();
    }

    private void OnImageSingleTapped(object? sender, EventArgs e)
    {
        if (_photos[_index].IsVideo || _exporting)
            return;

        if (_chromeVisible)
            HideChrome();
        else
            ShowChrome();
    }

    private void OnVideoSingleTapped(object? sender, EventArgs e)
    {
        if (!_photos[_index].IsVideo || _exporting)
            return;

        // 与视频控件显隐同步：播放条已在控件内切换，这里同步顶部按钮。
        if (_videoView.AreControlsVisible)
            ShowChrome();
        else
            HideChrome();
    }

    private void OnInfoClicked(object? sender, EventArgs e)
    {
        ShowChrome();
        ToggleActionBar();
    }

    private void ToggleActionBar()
    {
        _actionBarVisible = !_actionBarVisible;
        UpdateExportActionsVisibility();
        DetailPanel.IsVisible = _actionBarVisible;
        if (_actionBarVisible)
        {
            UpdateDetailLabels();
            _chromeHideCts?.Cancel();
        }
        else
        {
            ScheduleHideChrome();
        }
    }

    private void HideActionBar()
    {
        _actionBarVisible = false;
        DetailPanel.IsVisible = false;
    }

    private void ShowChrome()
    {
        _chromeVisible = true;
        SetChromeButtonsHitTest(true);
        ChromeLayer.Opacity = 1;
        ScheduleHideChrome();
    }

    private void HideChrome(bool animated = true)
    {
        _chromeHideCts?.Cancel();
        _chromeVisible = false;
        HideActionBar();
        SetChromeButtonsHitTest(false);
        if (!animated)
        {
            ChromeLayer.Opacity = 0;
            return;
        }

        _ = ChromeLayer.FadeToAsync(0, 320, Easing.CubicOut);
    }

    private void SetChromeButtonsHitTest(bool enabled)
    {
        BackButton.InputTransparent = !enabled;
        InfoButton.InputTransparent = !enabled;
        PhotoRotateButton.InputTransparent = !enabled;
    }

    private void ScheduleHideChrome()
    {
        _chromeHideCts?.Cancel();
        _chromeHideCts = new CancellationTokenSource();
        var token = _chromeHideCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ChromeAutoHideMs, token);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!token.IsCancellationRequested && !_actionBarVisible)
                        HideChrome();
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await DismissAsync();

    private void OnRotateClicked(object? sender, EventArgs e)
    {
        ShowChrome();
        _isLandscape = !_isLandscape;
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.SetLandscape(_isLandscape);
#endif
    }

    private Task DismissAsync()
    {
        if (Navigation.ModalStack.Count > 0 && ReferenceEquals(Navigation.ModalStack[^1], this))
            return ShellNavigation.PopModalAsync();

        return Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_actionBarVisible)
        {
            HideActionBar();
            return true;
        }

        _ = DismissAsync();
        return true;
    }

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
        _chromeHideCts?.Cancel();
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
            ShowChrome();
        }
    }

    private async Task ShareCurrentAsync()
    {
        if (_exporting)
            return;

        _exporting = true;
        HideActionBar();
        _chromeHideCts?.Cancel();
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
            ShowChrome();
        }
    }
}
