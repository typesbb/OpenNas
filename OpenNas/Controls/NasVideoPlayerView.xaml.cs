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
    private bool _isScrubbing;
    private bool _wasPlayingBeforeScrub;
    private CancellationTokenSource? _progressCts;

    private Grid Host = null!;
    private Grid SlideHost = null!;
    private Grid TopTouchLayer = null!;
    private Image PosterImage = null!;
    private MediaElement MediaPlayer = null!;
    private ActivityIndicator LoadingIndicator = null!;
    private Label ErrorLabel = null!;
    private Grid ControlsOverlay = null!;
    private ProgressBar DownloadProgressBar = null!;
    private Label DownloadLabel = null!;
    private Slider ProgressSlider = null!;
    private Label CurrentTimeLabel = null!;
    private Label DurationLabel = null!;
    private Button PlayPauseButton = null!;
    private ImageButton FullscreenButton = null!;

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

    public bool IsFullscreenHost { get; set; }

    public Func<int, Task>? OnSwipeNavigateAsync { get; set; }
    public event EventHandler<double>? DismissDrag;
    public event EventHandler? DismissRequested;
    public event EventHandler? FullscreenRequested;

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
            ShouldShowPlaybackControls = false,
            ShouldAutoPlay = true,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Black
        };
        MediaPlayer.MediaOpened += OnMediaOpened;
        MediaPlayer.MediaFailed += OnMediaFailed;
        MediaPlayer.PositionChanged += OnPositionChanged;
        MediaPlayer.StateChanged += OnStateChanged;

        SlideHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { PosterImage, MediaPlayer }
        };

        TopTouchLayer = new Grid
        {
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        TopTouchLayer.GestureRecognizers.Add(pan);

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += OnDoubleTapped;
        TopTouchLayer.GestureRecognizers.Add(doubleTap);

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

        DownloadProgressBar = new ProgressBar
        {
            IsVisible = false,
            Progress = 0,
            HeightRequest = 3,
            Margin = new Thickness(16, 0, 16, 4)
        };

        DownloadLabel = new Label
        {
            IsVisible = false,
            TextColor = Color.FromArgb("#CCFFFFFF"),
            FontSize = 11,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(16, 0, 16, 2)
        };

        CurrentTimeLabel = new Label
        {
            Text = "0:00",
            TextColor = Colors.White,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 44
        };

        DurationLabel = new Label
        {
            Text = "0:00",
            TextColor = Colors.White,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
            WidthRequest = 44
        };

        ProgressSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            MinimumTrackColor = Colors.White,
            MaximumTrackColor = Color.FromArgb("#66FFFFFF"),
            ThumbColor = Colors.White
        };
        ProgressSlider.DragStarted += OnScrubDragStarted;
        ProgressSlider.DragCompleted += OnScrubDragCompleted;
        ProgressSlider.ValueChanged += OnScrubValueChanged;

        PlayPauseButton = new Button
        {
            Text = "❚❚",
            FontSize = 13,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        PlayPauseButton.Clicked += (_, _) => TogglePlayPause();

        FullscreenButton = new ImageButton
        {
            Source = "icon_fullscreen.png",
            BackgroundColor = Colors.Transparent,
            WidthRequest = 32,
            HeightRequest = 32,
            Padding = 4
        };
        FullscreenButton.Clicked += (_, _) => FullscreenRequested?.Invoke(this, EventArgs.Empty);

        var timeRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(40)),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(40))
            },
            Margin = new Thickness(8, 0, 8, 0),
            Children = { PlayPauseButton, CurrentTimeLabel, ProgressSlider, DurationLabel, FullscreenButton }
        };
        Grid.SetColumn(PlayPauseButton, 0);
        Grid.SetColumn(CurrentTimeLabel, 1);
        Grid.SetColumn(ProgressSlider, 2);
        Grid.SetColumn(DurationLabel, 3);
        Grid.SetColumn(FullscreenButton, 4);

        ControlsOverlay = new Grid
        {
            VerticalOptions = LayoutOptions.End,
            Padding = new Thickness(0, 0, 0, 12),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Children = { DownloadProgressBar, DownloadLabel, timeRow }
        };
        Grid.SetRow(DownloadLabel, 1);
        Grid.SetRow(timeRow, 2);

        Host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            Children = { SlideHost, TopTouchLayer, LoadingIndicator, ErrorLabel, ControlsOverlay }
        };
        Grid.SetRow(TopTouchLayer, 0);
        Grid.SetRow(SlideHost, 0);
        Grid.SetRow(LoadingIndicator, 0);
        Grid.SetRow(ErrorLabel, 0);
        Grid.SetRow(ControlsOverlay, 1);

        Content = Host;
        UpdateChromeVisibility();
    }

    private void UpdateChromeVisibility()
    {
        FullscreenButton.IsVisible = !IsFullscreenHost;
    }

    private static void OnPhotoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not NasVideoPlayerView view)
            return;

        view.SlideHost.TranslationX = 0;
        view.SlideHost.TranslationY = 0;
        view._loadGeneration++;
        view._progressCts?.Cancel();
        view._progressCts = null;
        var generation = view._loadGeneration;

        if (newValue is not Photo photo)
        {
            view.Stop();
            view.PosterImage.Source = null;
            return;
        }

        view.ErrorLabel.IsVisible = false;
        view.PosterImage.IsVisible = true;
        view.HideDownloadProgress();
        view.UpdateChromeVisibility();
        NasThumbnailLoader.TryLoadPhotoThumbnail(view.PosterImage, photo);
        _ = view.LoadVideoAsync(photo, generation);
    }

    private async Task LoadVideoAsync(Photo photo, int generation)
    {
        SetLoading(generation, true);
        _progressCts = new CancellationTokenSource();
        var token = _progressCts.Token;

        try
        {
            if (NasMediaCache.TryGetOriginalFile(photo, out var cached))
            {
                await AssignSourceAsync(cached, generation);
                return;
            }

            ShowDownloadProgress(0, photo.FileSize > 0 ? photo.FileSize : null);

            var path = await NasOriginalLoader.EnsureCachedWithProgressAsync(
                photo,
                new Progress<NasDownloadProgress>(p => ReportDownloadProgress(generation, p)),
                cancellationToken: token).ConfigureAwait(false);

            if (generation != _loadGeneration)
                return;

            if (string.IsNullOrEmpty(path))
            {
                ShowError(generation, "视频下载失败，请检查网络后重试。");
                return;
            }

            await AssignSourceAsync(path, generation);
            HideDownloadProgress();
        }
        catch (OperationCanceledException)
        {
            // ignore
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

    private Task AssignSourceAsync(string path, int generation)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (generation != _loadGeneration)
                return;

            MediaPlayer.Source = MediaSource.FromFile(path);
        });
    }

    private void ReportDownloadProgress(int generation, NasDownloadProgress progress)
    {
        if (generation != _loadGeneration)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (generation != _loadGeneration)
                return;

            if (progress.IsComplete)
            {
                HideDownloadProgress();
                return;
            }

            ShowDownloadProgress(progress.BytesReceived, progress.TotalBytes);
        });
    }

    private void ShowDownloadProgress(long received, long? total)
    {
        DownloadProgressBar.IsVisible = true;
        DownloadLabel.IsVisible = true;

        if (total is > 0)
        {
            DownloadProgressBar.Progress = Math.Clamp(received / (double)total.Value, 0, 1);
            DownloadLabel.Text = $"缓冲 {received * 100 / total.Value}%";
        }
        else
        {
            DownloadProgressBar.Progress = 0;
            DownloadLabel.Text = $"缓冲 {NasMediaCache.FormatBytes(received)}";
        }
    }

    private void HideDownloadProgress()
    {
        DownloadProgressBar.IsVisible = false;
        DownloadLabel.IsVisible = false;
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PosterImage.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
            HideDownloadProgress();

            var duration = MediaPlayer.Duration;
            if (duration > TimeSpan.Zero)
            {
                ProgressSlider.Maximum = duration.TotalSeconds;
                DurationLabel.Text = FormatTime(duration);
            }

            UpdatePlayPauseButton();
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

    private void OnStateChanged(object? sender, MediaStateChangedEventArgs e) =>
        UpdatePlayPauseButton();

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        if (_isScrubbing)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = e.Position.TotalSeconds;
            CurrentTimeLabel.Text = FormatTime(e.Position);
        });
    }

    private void UpdatePlayPauseButton()
    {
        var playing = MediaPlayer.CurrentState == MediaElementState.Playing;
        PlayPauseButton.Text = playing ? "❚❚" : "▶";
    }

    private void TogglePlayPause()
    {
        if (MediaPlayer.CurrentState == MediaElementState.Playing)
            MediaPlayer.Pause();
        else
            MediaPlayer.Play();

        UpdatePlayPauseButton();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => TogglePlayPause();

    private void OnScrubDragStarted(object? sender, EventArgs e)
    {
        _isScrubbing = true;
        _wasPlayingBeforeScrub = MediaPlayer.CurrentState == MediaElementState.Playing;
        if (_wasPlayingBeforeScrub)
            MediaPlayer.Pause();
    }

    private async void OnScrubDragCompleted(object? sender, EventArgs e)
    {
        _isScrubbing = false;
        try
        {
            await MediaPlayer.SeekTo(TimeSpan.FromSeconds(ProgressSlider.Value));
            if (_wasPlayingBeforeScrub)
                MediaPlayer.Play();
        }
        catch
        {
            // ignore seek errors
        }
    }

    private void OnScrubValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isScrubbing)
            return;

        CurrentTimeLabel.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
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
            HideDownloadProgress();
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
        _progressCts?.Cancel();
        _progressCts = null;
        MediaPlayer.Stop();
        MediaPlayer.Source = null;
        ErrorLabel.IsVisible = false;
        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;
        PosterImage.IsVisible = true;
        HideDownloadProgress();
        ProgressSlider.Value = 0;
        CurrentTimeLabel.Text = "0:00";
        DurationLabel.Text = "0:00";
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_isNavigating || _isScrubbing)
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

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        return $"{time.Minutes}:{time.Seconds:D2}";
    }

    private enum PanMode
    {
        None,
        Horizontal,
        Dismiss
    }
}
