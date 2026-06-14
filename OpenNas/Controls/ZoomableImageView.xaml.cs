using NSynology.Foto;
using OpenNas.Helpers;

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
    private const double MaxScale = 5;
    private const double NavigateThreshold = 72;
    private const double DismissMinThreshold = 200;
    private const double DismissHeightRatio = 0.25;

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
    private PanMode _panMode = PanMode.None;
    internal double _navPanX;
    private double _navPanY;
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
            VerticalOptions = LayoutOptions.Fill
        };

        TransformHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = PhotoImage
        };

        SlideHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { TransformHost }
        };

        TouchLayer = new Grid
        {
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
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

        Host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { SlideHost, TouchLayer, LoadingIndicator }
        };

        Content = Host;
    }

    private Grid Host = null!;
    private Grid SlideHost = null!;
    private Grid TouchLayer = null!;
    private ContentView TransformHost = null!;
    private Image PhotoImage = null!;
    private ActivityIndicator LoadingIndicator = null!;

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
    }

    partial void InitializePlatform();

    public Photo? Photo
    {
        get => (Photo?)GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
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
    public Func<int, Task>? OnSwipeNavigateAsync { get; set; }
    public event EventHandler<double>? DismissDrag;
    public event EventHandler? DismissRequested;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        _containerWidth = width;
        _containerHeight = height;

        UpdateTransformHostLayout();
        if (ManagedGesturesEnabled && !_isPinching)
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

        if (newValue is not Photo photo)
        {
            view.PhotoImage.Source = null;
            return;
        }

        view.UpdateImageDimensions(photo);
        view.UpdateTransformHostLayout();
        NasThumbnailLoader.TryLoadPhotoThumbnail(view.PhotoImage, photo);
        NasOriginalLoader.TryLoad(
            view.PhotoImage,
            photo,
            loading => view.UpdateLoading(generation, loading),
            () => generation == view._loadGeneration);
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
        GetDisplayedSize(out _displayWidth, out _displayHeight);
        TransformHost.WidthRequest = _displayWidth;
        TransformHost.HeightRequest = _displayHeight;
    }

    private void UpdateLoading(int generation, bool loading)
    {
        if (generation != _loadGeneration)
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
                if (_currentScale < 1.02)
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
                _ = AnimatePanClampAsync();
                break;
        }
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
            TransformHost.ScaleTo(_currentScale, 220, Easing.CubicOut),
            TransformHost.TranslateTo(_panX, _panY, 220, Easing.CubicOut));
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AnimateResetZoomAsync()
    {
        await Task.WhenAll(
            TransformHost.ScaleTo(1, 200, Easing.CubicOut),
            TransformHost.TranslateTo(0, 0, 200, Easing.CubicOut));

        ResetImageTransform();
    }

    private async Task AnimatePanClampAsync()
    {
        ClampPan();
        await TransformHost.TranslateTo(_panX, _panY, 160, Easing.CubicOut);
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
                _navPanX = 0;
                _navPanY = 0;
                break;
            case GestureStatus.Running:
                _navPanX = e.TotalX;
                _navPanY = e.TotalY;

                if (_panMode == PanMode.None)
                {
                    if (Math.Abs(e.TotalX) > 20 && Math.Abs(e.TotalX) > Math.Abs(e.TotalY) * 1.15)
                        _panMode = PanMode.Horizontal;
                    else if (e.TotalY > 20 && e.TotalY > Math.Abs(e.TotalX) * 1.15)
                        _panMode = PanMode.Dismiss;
                }

                if (_panMode == PanMode.Horizontal)
                    SlideHost.TranslationX = ApplyHorizontalResistance(e.TotalX);
                else if (_panMode == PanMode.Dismiss && e.TotalY > 0)
                    DismissDrag?.Invoke(this, e.TotalY);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panMode == PanMode.Horizontal)
                    _ = CompleteHorizontalPanAsync();
                else if (_panMode == PanMode.Dismiss)
                {
                    if (_navPanY >= GetDismissThreshold())
                        DismissRequested?.Invoke(this, EventArgs.Empty);
                    else
                        DismissDrag?.Invoke(this, 0);
                }

                _panMode = PanMode.None;
                break;
        }
    }

    private double ApplyHorizontalResistance(double deltaX)
    {
        if (deltaX > 0 && !CanGoPrevious)
            return deltaX * 0.28;
        if (deltaX < 0 && !CanGoNext)
            return deltaX * 0.28;
        return deltaX;
    }

    internal async Task CompleteHorizontalPanAsync()
    {
        var width = GetContainerWidth();
        var shouldNavigate = Math.Abs(_navPanX) >= NavigateThreshold;
        var direction = _navPanX < 0 ? 1 : -1;

        if (shouldNavigate)
        {
            if ((direction < 0 && !CanGoPrevious) || (direction > 0 && !CanGoNext))
                shouldNavigate = false;
        }

        if (!shouldNavigate || OnSwipeNavigateAsync == null)
        {
            await SlideHost.TranslateTo(0, 0, 180, Easing.CubicOut);
            return;
        }

        _isNavigating = true;
        try
        {
            var exitX = direction > 0 ? -width : width;
            await SlideHost.TranslateTo(exitX, 0, 200, Easing.CubicOut);
            ResetImageTransform();
            SlideHost.TranslationX = direction > 0 ? width : -width;

            await OnSwipeNavigateAsync(direction);

            await SlideHost.TranslateTo(0, 0, 200, Easing.CubicOut);
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
        var cw = Math.Max(1, _containerWidth);
        var ch = Math.Max(1, _containerHeight);
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

    internal double GetDismissThreshold() =>
        Math.Max(DismissMinThreshold, _containerHeight > 0 ? _containerHeight * DismissHeightRatio : DismissMinThreshold);

    private double GetContainerWidth() =>
        _containerWidth > 0
            ? _containerWidth
            : Width > 0
                ? Width
                : 360;

    private enum PanMode
    {
        None,
        Horizontal,
        Dismiss
    }
}
