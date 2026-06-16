using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using NSynology.Foto;
using OpenNas.Helpers;

namespace OpenNas.Controls;

public partial class NasVideoPlayerView : ContentView
{
    public static readonly BindableProperty PhotoProperty = BindableProperty.Create(
        nameof(Photo),
        typeof(Photo),
        typeof(NasVideoPlayerView),
        propertyChanged: OnPhotoChanged);

    public static readonly BindableProperty CanGoPreviousProperty = BindableProperty.Create(
        nameof(CanGoPrevious),
        typeof(bool),
        typeof(NasVideoPlayerView),
        true);

    public static readonly BindableProperty CanGoNextProperty = BindableProperty.Create(
        nameof(CanGoNext),
        typeof(bool),
        typeof(NasVideoPlayerView),
        true);

    private const double NavigateThreshold = 72;
    private const double DismissMinThreshold = 200;
    private const double DismissHeightRatio = 0.25;

    private PanMode _panMode = PanMode.None;
    private double _navPanX;
    private double _navPanY;
    private bool _isNavigating;
    private int _loadGeneration;

    private Grid Host = null!;
    private Grid SlideHost = null!;
    private Grid TouchLayer = null!;
    private Image PosterImage = null!;
    private MediaElement MediaPlayer = null!;
    private ActivityIndicator LoadingIndicator = null!;
    private Label ErrorLabel = null!;

    public NasVideoPlayerView()
    {
        InitializeComponent();
        BuildUi();
    }

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

    public Func<int, Task>? OnSwipeNavigateAsync { get; set; }
    public event EventHandler<double>? DismissDrag;
    public event EventHandler? DismissRequested;

    private void BuildUi()
    {
        PosterImage = new Image
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };

        MediaPlayer = new MediaElement
        {
            ShouldShowPlaybackControls = true,
            ShouldAutoPlay = true,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Black
        };
        MediaPlayer.MediaOpened += OnMediaOpened;
        MediaPlayer.MediaFailed += OnMediaFailed;

        SlideHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { PosterImage, MediaPlayer }
        };

        TouchLayer = new Grid
        {
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        TouchLayer.GestureRecognizers.Add(pan);

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

        ErrorLabel = new Label
        {
            IsVisible = false,
            TextColor = Colors.White,
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(24)
        };

        Host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { SlideHost, TouchLayer, LoadingIndicator, ErrorLabel }
        };

        Content = Host;
    }

    private static void OnPhotoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not NasVideoPlayerView view)
            return;

        view.SlideHost.TranslationX = 0;
        view.SlideHost.TranslationY = 0;
        view._loadGeneration++;
        var generation = view._loadGeneration;

        if (newValue is not Photo photo)
        {
            view.Stop();
            view.PosterImage.Source = null;
            return;
        }

        view.ErrorLabel.IsVisible = false;
        view.PosterImage.IsVisible = true;
        NasThumbnailLoader.TryLoadPhotoThumbnail(view.PosterImage, photo);
        _ = view.LoadVideoAsync(photo, generation);
    }

    private async Task LoadVideoAsync(Photo photo, int generation)
    {
        SetLoading(generation, true);
        try
        {
            var path = await NasOriginalLoader.EnsureCachedAsync(photo).ConfigureAwait(false);
            if (generation != _loadGeneration)
                return;

            if (string.IsNullOrEmpty(path))
            {
                ShowError(generation, "视频下载失败，请检查网络后重试。");
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
                MediaPlayer.Source = MediaSource.FromFile(path));
        }
        catch
        {
            if (generation == _loadGeneration)
                ShowError(generation, "视频加载失败。");
        }
        finally
        {
            if (generation == _loadGeneration)
                SetLoading(generation, false);
        }
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PosterImage.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        });
    }

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowError(_loadGeneration, string.IsNullOrWhiteSpace(e.ErrorMessage)
                ? "视频播放失败。"
                : $"视频播放失败：{e.ErrorMessage}");
        });
    }

    private void ShowError(int generation, string message)
    {
        if (generation != _loadGeneration)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ErrorLabel.Text = message;
            ErrorLabel.IsVisible = true;
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        });
    }

    private void SetLoading(int generation, bool loading)
    {
        if (generation != _loadGeneration)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingIndicator.IsVisible = loading;
            LoadingIndicator.IsRunning = loading;
        });
    }

    public void Stop()
    {
        _loadGeneration++;
        MediaPlayer.Stop();
        MediaPlayer.Source = null;
        ErrorLabel.IsVisible = false;
        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;
        PosterImage.IsVisible = true;
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_isNavigating)
            return;

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

    private async Task CompleteHorizontalPanAsync()
    {
        var width = Width > 0 ? Width : 360;
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
            MediaPlayer.Pause();
            var exitX = direction > 0 ? -width : width;
            await SlideHost.TranslateTo(exitX, 0, 200, Easing.CubicOut);
            SlideHost.TranslationX = direction > 0 ? width : -width;

            await OnSwipeNavigateAsync(direction);

            await SlideHost.TranslateTo(0, 0, 200, Easing.CubicOut);
        }
        finally
        {
            _isNavigating = false;
        }
    }

    internal double GetDismissThreshold() =>
        Math.Max(DismissMinThreshold, Height > 0 ? Height * DismissHeightRatio : DismissMinThreshold);

    private enum PanMode
    {
        None,
        Horizontal,
        Dismiss
    }
}
