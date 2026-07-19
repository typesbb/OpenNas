using NSynology.Foto;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas.Controls;

public partial class ZoomableImageView : ContentView
{
    public static readonly BindableProperty PhotoProperty = BindableProperty.Create(
        nameof(Photo),
        typeof(Photo),
        typeof(ZoomableImageView),
        propertyChanged: OnPhotoChanged);

    public static readonly BindableProperty CanGoPreviousProperty = BindableProperty.Create(
        nameof(CanGoPrevious),
        typeof(bool),
        typeof(ZoomableImageView),
        true);

    public static readonly BindableProperty CanGoNextProperty = BindableProperty.Create(
        nameof(CanGoNext),
        typeof(bool),
        typeof(ZoomableImageView),
        true);

    private const double MinScale = 1;
    private const double MaxScale = 40;
    private const double NavigateThreshold = 72;

    private double _currentScale = 1;
    private double _panX;
    private double _panY;
    private double _panStartX;
    private double _panStartY;
    private double _previousPinchScale = 1;
    private double _containerWidth;
    private double _containerHeight;
    private double _displayWidth;
    private double _displayHeight;
    private double _imageWidth;
    private double _imageHeight;
    private int _loadGeneration;
    private bool _showThumbnailOnly;
    private string? _seedThumbnailPath;
    private byte[]? _seedThumbnailBytes;
    private PanMode _panMode = PanMode.None;
    internal double _navPanY;
    internal bool _isNavigating;
    private bool _isPinching;
    internal bool ManagedGesturesEnabled;
#if ANDROID
    private bool _nativeZoomed;
#endif

    public ZoomableImageView()
    {
        InitializeComponent();
        BuildUi();
        Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, EventArgs e)
    {
        InitializePlatform();
#if !ANDROID
        EnableManagedGestures();
#endif
    }

    private void BuildUi()
    {
        PhotoImage = new Image
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        };

        TransformHost = new ContentView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = PhotoImage
        };

        SlideHost = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 1,
            Children = { TransformHost }
        };

        // 勿设 BackgroundColor：部分 Android 上 Transparent 会盖住下层。
        TouchLayer = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 10
        };

        LoadingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            InputTransparent = true,
            Color = Colors.White,
            WidthRequest = 32,
            HeightRequest = 32,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        DownloadProgressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = Colors.White,
            HeightRequest = 3,
            HorizontalOptions = LayoutOptions.Fill
        };
        DownloadLabel = new Label
        {
            Text = "加载中…",
            TextColor = Colors.White,
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center
        };
        DownloadChrome = new VerticalStackLayout
        {
            Spacing = 4,
            Margin = new Thickness(12, 0, 12, 28),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            IsVisible = false,
            InputTransparent = true,
            Children = { DownloadProgressBar, DownloadLabel }
        };

        Host = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { SlideHost, TouchLayer, LoadingIndicator, DownloadChrome }
        };

        Content = Host;
    }

    private Grid Host = null!;
    private Grid SlideHost = null!;
    private Grid TouchLayer = null!;
    private ContentView TransformHost = null!;
    private Image PhotoImage = null!;
    private ActivityIndicator LoadingIndicator = null!;
    private VerticalStackLayout DownloadChrome = null!;
    private ProgressBar DownloadProgressBar = null!;
    private Label DownloadLabel = null!;
    private CancellationTokenSource? _downloadCts;

    internal void EnableManagedGestures()
    {
        if (TouchLayer.GestureRecognizers.Count > 0)
            return;

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdatedManaged;
        TouchLayer.GestureRecognizers.Add(pinch);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdatedManaged;
        TouchLayer.GestureRecognizers.Add(pan);

        var tap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        tap.Tapped += OnDoubleTappedManaged;
        TouchLayer.GestureRecognizers.Add(tap);

        var singleTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        singleTap.Tapped += OnSingleTappedManaged;
        TouchLayer.GestureRecognizers.Add(singleTap);
    }

    partial void InitializePlatform();

    public Photo? Photo
    {
        get => (Photo?)GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    /// <summary>设置照片；可选传入网格正在显示的缩略图路径/字节，保证首帧占位。</summary>
    public void LoadPhoto(Photo? photo, string? seedThumbnailPath = null, byte[]? seedThumbnailBytes = null)
    {
        _seedThumbnailPath = seedThumbnailPath;
        _seedThumbnailBytes = seedThumbnailBytes is { Length: > 0 } ? seedThumbnailBytes : null;
        Photo = photo;
    }

    public bool CanGoPrevious
    {
        get => (bool)GetValue(CanGoPreviousProperty);
        set => SetValue(CanGoPreviousProperty, value);
    }

    public bool CanGoNext
    {
        get => (bool)GetValue(CanGoNextProperty);
        set => SetValue(CanGoNextProperty, value);
    }

#if ANDROID
    public bool IsZoomed => ManagedGesturesEnabled ? _currentScale > 1.05 : _nativeZoomed;
#else
    public bool IsZoomed => _currentScale > 1.05;
#endif

    public event EventHandler? ZoomChanged;
    public event EventHandler? SingleTapped;
    /// <summary>原图已贴到界面（可撤掉外部占位）。</summary>
    public event EventHandler? ContentReady;
    public Func<int, Task>? OnSwipeNavigateAsync { get; set; }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var hadSize = _containerWidth > 1 && _containerHeight > 1;
        var orientationFlipped = hadSize
            && ((_containerWidth < _containerHeight) != (width < height));
        var sizeChangedSignificantly = hadSize
            && (Math.Abs(_containerWidth - width) > 8 || Math.Abs(_containerHeight - height) > 8);

        _containerWidth = width;
        _containerHeight = height;

        UpdateTransformHostLayout();

        if (orientationFlipped || sizeChangedSignificantly)
        {
            // 旋转后清掉滑动残差，并按新窗口尺寸重新居中适配。
            SlideHost.TranslationX = 0;
            SlideHost.TranslationY = 0;
            if (ManagedGesturesEnabled)
            {
                _currentScale = 1;
                _panX = 0;
                _panY = 0;
                _previousPinchScale = 1;
                TransformHost.Scale = 1;
                TransformHost.TranslationX = 0;
                TransformHost.TranslationY = 0;
                UpdateTransformHostLayout();
                ApplyTransform();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
#if ANDROID
            else if (Photo != null)
            {
                ScheduleAndroidRefit();
            }
#endif
        }
#if ANDROID
        else if (!ManagedGesturesEnabled && width > 1 && height > 1 && (!hadSize || Photo != null))
        {
            NotifyDisplayReady();
        }
#endif

        if (ManagedGesturesEnabled && !_isPinching && !orientationFlipped && !sizeChangedSignificantly)
        {
            ClampPan();
            ApplyTransform();
        }
    }

    private static void OnPhotoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ZoomableImageView view)
            return;

        view.ResetImageTransform();
        view.SlideHost.TranslationX = 0;
        view.SlideHost.TranslationY = 0;
        view._loadGeneration++;
        var generation = view._loadGeneration;
        view._downloadCts?.Cancel();
        view.HideDownloadProgress();

        if (newValue is not Photo photo)
        {
            view.PhotoImage.Source = null;
#if ANDROID
            // 清掉原生位图
            if (view.PhotoImage.Handler?.PlatformView is global::Android.Widget.ImageView iv)
                iv.SetImageDrawable(null);
#endif
            return;
        }

        view.UpdateImageDimensions(photo);
        view.UpdateTransformHostLayout();
        view._showThumbnailOnly = true;
        view.ApplyThumbnailThenLoadOriginal(photo, generation);
    }

    private void ApplyThumbnailThenLoadOriginal(Photo photo, int generation)
    {
        // 同一张 Image：先贴缩略图，原图就绪后原地替换。绝不先清空成黑屏。
        var bytes = _seedThumbnailBytes;
        _seedThumbnailBytes = null;
        var path = _seedThumbnailPath;
        _seedThumbnailPath = null;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            if (NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var cached))
                path = cached;
        }

#if ANDROID
        PhotoImage.Source = null;
        if (bytes is { Length: > 0 })
            Platforms.Android.NativeImageBitmap.SetBitmapFromBytes(PhotoImage, bytes);
        else if (!string.IsNullOrEmpty(path) && File.Exists(path))
            Platforms.Android.NativeImageBitmap.SetBitmapFromFile(PhotoImage, path);
        // 无缩略图时保持空白，由页面级 FromFile 占位顶住；禁止 TryLoadPhotoThumbnail（会设 Source 清屏）。
#else
        if (bytes is { Length: > 0 })
        {
            var copy = bytes;
            PhotoImage.Source = ImageSource.FromStream(() => new MemoryStream(copy));
        }
        else if (!string.IsNullOrEmpty(path) && File.Exists(path))
            PhotoImage.Source = ImageSource.FromFile(path);
        else
            NasThumbnailLoader.TryLoadPhotoThumbnail(
                PhotoImage,
                photo,
                () => generation == _loadGeneration && _showThumbnailOnly,
                forGrid: false);
#endif

        AppLog.Debug(
            bytes is { Length: > 0 }
                ? $"大图先贴缩略图 bytes={bytes.Length} id={photo.Id}"
                : $"大图先贴缩略图 path={path} id={photo.Id}");

        if (NasMediaCache.TryGetOriginalFile(photo, out var cachedOriginal))
        {
            NasMediaCache.PrepareOriginalCacheForLoad(cachedOriginal);
            // 延后一帧再换原图，让缩略图先画出来。
            _ = SwapToOriginalAsync(cachedOriginal, generation);
            return;
        }

        NasMediaCache.PrepareOriginalCacheForLoad();
        _ = DownloadOriginalWithProgressAsync(photo, generation);
    }

    private async Task DownloadOriginalWithProgressAsync(Photo photo, int generation)
    {
        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (generation != _loadGeneration)
                return;
            ShowDownloadProgress(0, photo.FileSize > 0 ? photo.FileSize : null);
            UpdateLoading(generation, true);
        });

        try
        {
            var path = await NasOriginalLoader.EnsureCachedWithProgressAsync(
                photo,
                new Progress<NasDownloadProgress>(p =>
                {
                    if (generation != _loadGeneration)
                        return;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (generation != _loadGeneration)
                            return;
                        if (p.IsComplete)
                            HideDownloadProgress();
                        else
                            ShowDownloadProgress(p.BytesReceived, p.TotalBytes);
                    });
                }),
                token).ConfigureAwait(false);

            if (generation != _loadGeneration)
                return;

            if (string.IsNullOrEmpty(path))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    HideDownloadProgress();
                    UpdateLoading(generation, false);
                });
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != _loadGeneration)
                    return;
#if ANDROID
                Platforms.Android.NativeImageBitmap.SetBitmapFromFile(PhotoImage, path);
#else
                PhotoImage.Source = ImageSource.FromFile(path);
#endif
                HideDownloadProgress();
                UpdateLoading(generation, false);
                RevealOriginal(generation);
            });
        }
        catch (OperationCanceledException)
        {
            if (generation == _loadGeneration)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    HideDownloadProgress();
                    UpdateLoading(generation, false);
                });
            }
        }
        catch
        {
            if (generation == _loadGeneration)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    HideDownloadProgress();
                    UpdateLoading(generation, false);
                });
            }
        }
    }

    private void ShowDownloadProgress(long received, long? total)
    {
        DownloadChrome.IsVisible = true;
        if (total is > 0)
        {
            DownloadProgressBar.Progress = Math.Clamp(received / (double)total.Value, 0, 1);
            DownloadLabel.Text = $"加载 {received * 100 / total.Value}%";
        }
        else
        {
            DownloadProgressBar.Progress = 0;
            DownloadLabel.Text = $"加载 {NasMediaCache.FormatBytes(received)}";
        }
    }

    private void HideDownloadProgress()
    {
        DownloadChrome.IsVisible = false;
        DownloadProgressBar.Progress = 0;
    }

    private async Task SwapToOriginalAsync(string originalPath, int generation)
    {
        try
        {
            await Task.Delay(48);
            if (generation != _loadGeneration)
                return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != _loadGeneration)
                    return;
#if ANDROID
                Platforms.Android.NativeImageBitmap.SetBitmapFromFile(PhotoImage, originalPath);
#else
                PhotoImage.Source = ImageSource.FromFile(originalPath);
#endif
                RevealOriginal(generation);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RevealOriginal(int generation)
    {
        if (generation != _loadGeneration)
            return;

        _showThumbnailOnly = false;
        NotifyDisplayReady();
        _ = FinishRevealWhenPaintedAsync(generation);
    }

    private async Task FinishRevealWhenPaintedAsync(int generation)
    {
        try
        {
#if ANDROID
            var stable = 0;
            for (var i = 0; i < 80 && generation == _loadGeneration; i++)
            {
                var painted = false;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    painted = Platforms.Android.NativeImageBitmap.HasDrawable(PhotoImage);
                });
                if (painted)
                {
                    stable++;
                    if (stable >= 3)
                        break;
                }
                else
                    stable = 0;

                await Task.Delay(40);
            }

            if (stable < 3 || generation != _loadGeneration)
                return;
#else
            await Task.Delay(160);
#endif
            if (generation != _loadGeneration)
                return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation == _loadGeneration)
                    ContentReady?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal void NotifyDisplayReady()
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() => _androidTouchListener?.PrepareDisplay());
#endif
    }

    private void UpdateImageDimensions(Photo photo)
    {
        var res = photo.Additional?.Resolution;
        if (res == null || res.Width <= 0 || res.Height <= 0)
        {
            _imageWidth = 0;
            _imageHeight = 0;
            return;
        }

        var orientation = photo.Additional?.Orientation ?? 1;
        var swapped = orientation is 5 or 6 or 7 or 8;
        _imageWidth = swapped ? res.Height : res.Width;
        _imageHeight = swapped ? res.Width : res.Height;
    }

    private void UpdateTransformHostLayout()
    {
#if ANDROID
        if (!ManagedGesturesEnabled)
        {
            TransformHost.HorizontalOptions = LayoutOptions.Fill;
            TransformHost.VerticalOptions = LayoutOptions.Fill;
            TransformHost.WidthRequest = -1;
            TransformHost.HeightRequest = -1;
            _displayWidth = _containerWidth > 1 ? _containerWidth : 0;
            _displayHeight = _containerHeight > 1 ? _containerHeight : 0;
            return;
        }
#endif

        if (_containerWidth <= 1 || _containerHeight <= 1)
        {
            TransformHost.WidthRequest = -1;
            TransformHost.HeightRequest = -1;
            return;
        }

        GetDisplayedSize(out _displayWidth, out _displayHeight);
        TransformHost.HorizontalOptions = LayoutOptions.Center;
        TransformHost.VerticalOptions = LayoutOptions.Center;
        TransformHost.WidthRequest = _displayWidth;
        TransformHost.HeightRequest = _displayHeight;
    }

    private void UpdateLoading(int generation, bool loading)
    {
        if (generation != _loadGeneration)
            return;

        // 已有缩略图时不闪 loading
        if (loading && _showThumbnailOnly)
            return;

        LoadingIndicator.IsVisible = loading;
        LoadingIndicator.IsRunning = loading;

#if ANDROID
        if (!loading && generation == _loadGeneration)
            _androidTouchListener?.PrepareDisplay();
#endif
    }

    private void OnPinchUpdatedManaged(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (_isNavigating)
            return;

        switch (e.Status)
        {
            case GestureStatus.Started:
                _previousPinchScale = 1;
                _isPinching = true;
                _panMode = PanMode.None;
                break;
            case GestureStatus.Running:
            {
                if (_previousPinchScale <= 0)
                    _previousPinchScale = 1;

                var scaleFactor = e.Scale / _previousPinchScale;
                _previousPinchScale = e.Scale;

                var newScale = Math.Clamp(_currentScale * scaleFactor, MinScale, MaxScale);
                var actualFactor = newScale / _currentScale;
                if (Math.Abs(actualFactor - 1) < 0.00001)
                    break;

                var focalX = ToFocalOffsetX(e.ScaleOrigin.X);
                var focalY = ToFocalOffsetY(e.ScaleOrigin.Y);

                _panX = focalX - (focalX - _panX) * actualFactor;
                _panY = focalY - (focalY - _panY) * actualFactor;
                _currentScale = newScale;
                ApplyTransform();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
                break;
            }
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isPinching = false;
                if (_currentScale < 1.05)
                    _ = AnimateResetZoomAsync();
                else
                    _ = AnimatePanClampAsync();
                break;
        }
    }

    private void OnPanUpdatedManaged(object? sender, PanUpdatedEventArgs e)
    {
        if (_isNavigating || _isPinching)
            return;

        if (IsZoomed)
        {
            HandleZoomPan(e);
            return;
        }

        HandleNavigationPan(e);
    }

    private void HandleZoomPan(PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _panX;
                _panStartY = _panY;
                break;
            case GestureStatus.Running:
                _panX = _panStartX + e.TotalX;
                _panY = _panStartY + e.TotalY;
                ClampPan(allowRubberBand: true);
                ApplyTransform();
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SlideHost.TranslationY = 0;
                _ = AnimatePanClampAsync();
                break;
        }
    }

    internal void OnNativeSingleTap()
    {
        MainThread.BeginInvokeOnMainThread(() => SingleTapped?.Invoke(this, EventArgs.Empty));
    }

    private void OnSingleTappedManaged(object? sender, TappedEventArgs e)
    {
        if (_isNavigating || _isPinching)
            return;

        SingleTapped?.Invoke(this, EventArgs.Empty);
    }

    private async void OnDoubleTappedManaged(object? sender, TappedEventArgs e)
    {
        if (_isNavigating || _isPinching)
            return;

        if (IsZoomed)
        {
            await AnimateResetZoomAsync();
            return;
        }

        var targetScale = 2.5;
        if (e.GetPosition(Host) is { } point)
        {
            var focalX = ToFocalOffsetX(point.X);
            var focalY = ToFocalOffsetY(point.Y);
            var factor = targetScale / _currentScale;
            _panX = focalX - (focalX - _panX) * factor;
            _panY = focalY - (focalY - _panY) * factor;
        }

        _currentScale = targetScale;
        ClampPan();
        await Task.WhenAll(
            TransformHost.ScaleToAsync(_currentScale, 220, Easing.CubicOut),
            TransformHost.TranslateToAsync(_panX, _panY, 220, Easing.CubicOut));
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AnimateResetZoomAsync()
    {
        await Task.WhenAll(
            TransformHost.ScaleToAsync(1, 200, Easing.CubicOut),
            TransformHost.TranslateToAsync(0, 0, 200, Easing.CubicOut));

        ResetImageTransform();
    }

    private async Task AnimatePanClampAsync()
    {
        ClampPan();
        await TransformHost.TranslateToAsync(_panX, _panY, 160, Easing.CubicOut);
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        TransformHost.Scale = _currentScale;
        TransformHost.TranslationX = _panX;
        TransformHost.TranslationY = _panY;
    }

    private double ToFocalOffsetX(double originX) => originX - _containerWidth / 2;

    private double ToFocalOffsetY(double originY) => originY - _containerHeight / 2;

    private void HandleNavigationPan(PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panMode = PanMode.None;
                _navPanY = 0;
                break;
            case GestureStatus.Running:
                _navPanY = e.TotalY;

                if (_panMode == PanMode.None
                    && Math.Abs(e.TotalY) > 20
                    && Math.Abs(e.TotalY) > Math.Abs(e.TotalX) * 1.15)
                {
                    _panMode = PanMode.Vertical;
                }

                if (_panMode == PanMode.Vertical)
                    SlideHost.TranslationY = ApplyVerticalResistance(e.TotalY);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panMode == PanMode.Vertical)
                    _ = CompleteVerticalPanAsync();

                _panMode = PanMode.None;
                break;
        }
    }

    private double ApplyVerticalResistance(double deltaY)
    {
        // 下滑=上一张，上滑=下一张
        if (deltaY > 0 && !CanGoPrevious)
            return deltaY * 0.28;
        if (deltaY < 0 && !CanGoNext)
            return deltaY * 0.28;
        return deltaY;
    }

    internal async Task CompleteVerticalPanAsync()
    {
        var height = GetContainerHeight();
        var shouldNavigate = Math.Abs(_navPanY) >= NavigateThreshold;
        var direction = _navPanY < 0 ? 1 : -1;

        if (shouldNavigate)
        {
            if ((direction < 0 && !CanGoPrevious) || (direction > 0 && !CanGoNext))
                shouldNavigate = false;
        }

        if (!shouldNavigate || OnSwipeNavigateAsync == null)
        {
            await SlideHost.TranslateToAsync(0, 0, 180, Easing.CubicOut);
            return;
        }

        _isNavigating = true;
        try
        {
            var exitY = direction > 0 ? -height : height;
            await SlideHost.TranslateToAsync(0, exitY, 200, Easing.CubicOut);
            ResetImageTransform();
            SlideHost.TranslationY = direction > 0 ? height : -height;

            await OnSwipeNavigateAsync(direction);

            await SlideHost.TranslateToAsync(0, 0, 200, Easing.CubicOut);
        }
        finally
        {
            _isNavigating = false;
        }
    }

    partial void ResetPlatformTransform();

    private void ResetImageTransform()
    {
        if (!ManagedGesturesEnabled)
        {
#if !ANDROID
            _currentScale = 1;
            _panX = 0;
            _panY = 0;
            _previousPinchScale = 1;
            TransformHost.Scale = 1;
            TransformHost.TranslationX = 0;
            TransformHost.TranslationY = 0;
#endif
#if ANDROID
            _nativeZoomed = false;
#endif
        }
        else
        {
            _currentScale = 1;
            _panX = 0;
            _panY = 0;
            _previousPinchScale = 1;
            TransformHost.Scale = 1;
            TransformHost.TranslationX = 0;
            TransformHost.TranslationY = 0;
        }

        ResetPlatformTransform();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampPan(bool allowRubberBand = false)
    {
        if (_currentScale <= 1.01)
        {
            _panX = 0;
            _panY = 0;
            return;
        }

        var scaledW = _displayWidth * _currentScale;
        var scaledH = _displayHeight * _currentScale;
        var maxX = Math.Max(0, (scaledW - _containerWidth) / 2);
        var maxY = Math.Max(0, (scaledH - _containerHeight) / 2);

        if (allowRubberBand)
        {
            _panX = RubberBand(_panX, -maxX, maxX);
            _panY = RubberBand(_panY, -maxY, maxY);
            return;
        }

        _panX = Math.Clamp(_panX, -maxX, maxX);
        _panY = Math.Clamp(_panY, -maxY, maxY);
    }

    private static double RubberBand(double value, double min, double max)
    {
        if (value < min)
            return min + (value - min) * 0.35;
        if (value > max)
            return max + (value - max) * 0.35;
        return value;
    }

    private void GetDisplayedSize(out double width, out double height)
    {
        var cw = _containerWidth > 1 ? _containerWidth : 360;
        var ch = _containerHeight > 1 ? _containerHeight : 640;
        var iw = _imageWidth > 0 ? _imageWidth : cw;
        var ih = _imageHeight > 0 ? _imageHeight : ch;
        var imageAspect = iw / ih;
        var containerAspect = cw / ch;

        if (imageAspect > containerAspect)
        {
            width = cw;
            height = cw / imageAspect;
        }
        else
        {
            height = ch;
            width = ch * imageAspect;
        }
    }

    private double GetContainerHeight() =>
        _containerHeight > 0
            ? _containerHeight
            : Height > 0
                ? Height
                : 640;

    private enum PanMode
    {
        None,
        Vertical
    }
}
