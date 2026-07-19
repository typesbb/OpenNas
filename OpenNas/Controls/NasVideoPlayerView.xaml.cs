using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using NSynology.Foto;
using OpenNas.Helpers;
using OpenNas.Services;

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
    private const double MinScale = 1;
    private const double MaxScale = 40;
    private const int ChromeAutoHideMs = 2800;

    private PanMode _panMode = PanMode.None;
    private double _navPanY;
    private bool _isNavigating;
    private int _loadGeneration;
    private bool _isScrubbing;
    private bool _isGestureSeeking;
    private double _seekStartSeconds;
    private double _seekTargetSeconds;
    private bool _wasPlayingBeforeScrub;
    private bool _wasPlayingBeforeSleep;
    private CancellationTokenSource? _progressCts;
    private bool _isFastForwarding;
    private CancellationTokenSource? _chromeHideCts;
    private long _lastProgressUiTick;
    private string? _playbackPath;
    private string? _seedPosterPath;
    private double _durationSeconds;
    private double _currentScale = 1;
    private double _panX;
    private double _panY;
    private double _panStartX;
    private double _panStartY;
    private double _previousPinchScale = 1;
    private bool _isPinching;
    private double _containerWidth;
    private double _containerHeight;

    private const double NormalSpeed = 1;
    private const double FastSpeed = 3;

    private Grid Host = null!;
    private Grid SlideHost = null!;
    private ContentView TransformHost = null!;
    private Grid TopTouchLayer = null!;
    private Image PosterImage = null!;
    private MediaElement MediaPlayer = null!;
    private ActivityIndicator LoadingIndicator = null!;
    private Label ErrorLabel = null!;
    private Grid ControlsOverlay = null!;
    private Grid PlaybackChrome = null!;
    private VerticalStackLayout DownloadChrome = null!;
    private ProgressBar DownloadProgressBar = null!;
    private Label DownloadLabel = null!;
    private Slider ProgressSlider = null!;
    private Label CurrentTimeLabel = null!;
    private Label DurationLabel = null!;
    private Button PlayPauseButton = null!;
    private Button RotateButton = null!;
    private Label SpeedHintLabel = null!;
    private bool _controlsVisible = true;

    public NasVideoPlayerView()
    {
        InitializeComponent();
        BuildUi();
        Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, EventArgs e) =>
        InitializePlatform();

    partial void InitializePlatform();

    partial void OnPhotoReadyForPlatform();

#if !ANDROID
#pragma warning disable CA1822 // Partial methods: shared declaration can't be static due to Android impl
    partial void InitializePlatform() { }

    partial void OnPhotoReadyForPlatform() { }
#pragma warning restore CA1822
#endif

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
    public event EventHandler? SingleTapped;
    public event EventHandler? ZoomChanged;
    public event EventHandler? RotateRequested;

    public bool IsZoomed => _currentScale > 1.05;

    public bool AreControlsVisible => _controlsVisible;

    public bool IsDownloading => DownloadChrome.IsVisible;

    /// <summary>设置视频；可选传入网格缩略图路径作缓冲封面。</summary>
    public void LoadVideo(Photo? photo, string? seedPosterPath = null)
    {
        _seedPosterPath = seedPosterPath;
        Photo = photo;
    }

    private void BuildUi()
    {
        PosterImage = new Image
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            ZIndex = 2
        };

        MediaPlayer = new MediaElement
        {
            ShouldShowPlaybackControls = false,
            ShouldAutoPlay = true,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Black,
            InputTransparent = true,
            // 下载/打开完成前隐藏，否则黑底会盖住封面缩略图。
            IsVisible = false,
            ZIndex = 1
        };
        MediaPlayer.MediaOpened += OnMediaOpened;
        MediaPlayer.MediaFailed += OnMediaFailed;
        MediaPlayer.PositionChanged += OnPositionChanged;
        MediaPlayer.StateChanged += OnStateChanged;

        TransformHost = new ContentView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = new Grid
            {
                SafeAreaEdges = SafeAreaEdges.None,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                Children = { PosterImage, MediaPlayer }
            }
        };

        SlideHost = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { TransformHost }
        };

        TopTouchLayer = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

#if !ANDROID
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        TopTouchLayer.GestureRecognizers.Add(pinch);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        TopTouchLayer.GestureRecognizers.Add(pan);

        var singleTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        singleTap.Tapped += OnSingleTappedManaged;
        TopTouchLayer.GestureRecognizers.Add(singleTap);
#endif

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
            HorizontalOptions = LayoutOptions.End,
            HorizontalTextAlignment = TextAlignment.End
        };

        DurationLabel = new Label
        {
            Text = "0:00",
            TextColor = Colors.White,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            HorizontalTextAlignment = TextAlignment.Start
        };

        ProgressSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            MinimumTrackColor = Colors.White,
            MaximumTrackColor = Color.FromArgb("#66FFFFFF"),
            ThumbColor = Colors.White,
            HorizontalOptions = LayoutOptions.Fill,
#if ANDROID
            Margin = new Thickness(-10, 0)
#else
            Margin = new Thickness(-4, 0)
#endif
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
        PlayPauseButton.Clicked += (_, _) =>
        {
            TogglePlayPause();
            ShowControls();
        };

        RotateButton = new Button
        {
            Text = "↻",
            FontSize = 18,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center
        };
        RotateButton.Clicked += (_, _) =>
        {
            ShowControls();
            RotateRequested?.Invoke(this, EventArgs.Empty);
        };

        SpeedHintLabel = new Label
        {
            Text = "3倍速",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#99000000"),
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 48, 0, 0),
            IsVisible = false,
            InputTransparent = true
        };

        var timeRow = new Grid
        {
            ColumnSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(8, 0, 8, 0),
            Children = { PlayPauseButton, CurrentTimeLabel, ProgressSlider, DurationLabel, RotateButton }
        };
        Grid.SetColumn(PlayPauseButton, 0);
        Grid.SetColumn(CurrentTimeLabel, 1);
        Grid.SetColumn(ProgressSlider, 2);
        Grid.SetColumn(DurationLabel, 3);
        Grid.SetColumn(RotateButton, 4);
        PlaybackChrome = timeRow;

        DownloadChrome = new VerticalStackLayout
        {
            Spacing = 4,
            Margin = new Thickness(12, 0, 12, 8),
            HorizontalOptions = LayoutOptions.Fill,
            IsVisible = false,
            Children = { DownloadProgressBar, DownloadLabel }
        };

        // 播放控件可自动隐藏；下载进度条独立，加载中不跟着藏。
        ControlsOverlay = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            CascadeInputTransparent = false,
            Padding = new Thickness(0, 0, 0, 20),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Children = { DownloadChrome, PlaybackChrome }
        };
        Grid.SetRow(DownloadChrome, 0);
        Grid.SetRow(PlaybackChrome, 1);

        // 控制条叠在视频上，不要单独占一行——否则底部会永远空出一截黑边。
        Host = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { SlideHost, TopTouchLayer, LoadingIndicator, ErrorLabel, SpeedHintLabel, ControlsOverlay }
        };

        Content = Host;
        ShowControls(autoHide: false);
    }

    public void ShowControls(bool autoHide = true)
    {
        _controlsVisible = true;
        PlaybackChrome.Opacity = 1;
        PlaybackChrome.InputTransparent = false;
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.InputTransparent = false;
        if (autoHide)
            ScheduleHideControls();
        else
            _chromeHideCts?.Cancel();
    }

    public void ToggleControls()
    {
        if (_controlsVisible)
            HideControls();
        else
            ShowControls();
    }

    public void HideControls(bool animated = true)
    {
        if (_isScrubbing || _isGestureSeeking)
            return;

        _chromeHideCts?.Cancel();
        _controlsVisible = false;
        PlaybackChrome.InputTransparent = true;

        // 有下载进度时保留整层可显示进度；否则穿透点击。
        if (!IsDownloading)
            ControlsOverlay.InputTransparent = true;

        if (!animated)
        {
            PlaybackChrome.Opacity = 0;
            return;
        }

        _ = PlaybackChrome.FadeToAsync(0, 320, Easing.CubicOut);
    }

    private void ScheduleHideControls()
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
                    if (!token.IsCancellationRequested)
                        HideControls();
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>息屏/进后台时暂停；回前台时若原先在播则恢复。</summary>
    public void HandleAppSleep()
    {
        _wasPlayingBeforeSleep = MediaPlayer.CurrentState == MediaElementState.Playing;
        if (_wasPlayingBeforeSleep)
            MediaPlayer.Pause();
        UpdatePlayPauseButton();
    }

    public void HandleAppResume()
    {
        if (!_wasPlayingBeforeSleep)
            return;

        _wasPlayingBeforeSleep = false;
        MediaPlayer.Play();
        UpdatePlayPauseButton();
    }

    private void ApplyDefaultSpeed()
    {
        MediaPlayer.Speed = _isFastForwarding ? FastSpeed : NormalSpeed;
    }

    private void StartFastForward()
    {
        if (_isFastForwarding || MediaPlayer.Source == null)
            return;

        _isFastForwarding = true;
        MediaPlayer.Speed = FastSpeed;
        SpeedHintLabel.Text = "3倍速";
        SpeedHintLabel.IsVisible = true;

        if (MediaPlayer.CurrentState != MediaElementState.Playing)
            MediaPlayer.Play();
    }

    private void EndFastForward()
    {
        if (!_isFastForwarding)
            return;

        _isFastForwarding = false;
        MediaPlayer.Speed = NormalSpeed;
        SpeedHintLabel.IsVisible = false;
    }

    private static void OnPhotoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not NasVideoPlayerView view)
            return;

        view.SlideHost.TranslationX = 0;
        view.SlideHost.TranslationY = 0;
        view.ResetZoomTransform();
        view._loadGeneration++;
        view._progressCts?.Cancel();
        view._progressCts = null;
        view.EndFastForward();
        view.ReleasePlaybackPath();
        view._durationSeconds = 0;
        var generation = view._loadGeneration;

        if (newValue is not Photo photo)
        {
            view.Stop();
            view.PosterImage.Source = null;
            return;
        }

        view.ErrorLabel.IsVisible = false;
        view.MediaPlayer.IsVisible = false;
        view.PosterImage.IsVisible = true;
        view.HideDownloadProgress();
        view.ApplyPosterSeed(photo);
        view.OnPhotoReadyForPlatform();
        _ = view.LoadVideoAsync(photo, generation);
    }

    private void ApplyPosterSeed(Photo photo)
    {
        var path = _seedPosterPath;
        _seedPosterPath = null;

        if ((string.IsNullOrEmpty(path) || !File.Exists(path))
            && NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var cached))
            path = cached;

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            PosterImage.Source = ImageSource.FromFile(path);
            AppLog.Debug($"视频封面占位 path={path} id={photo.Id}");
            return;
        }

        NasThumbnailLoader.TryLoadPhotoThumbnail(PosterImage, photo);
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
                // 同一文件再次打开不淘汰；换片时淘汰且永不删当前文件。
                NasMediaCache.ProtectPath(cached);
                NasMediaCache.PrepareOriginalCacheForLoad(cached);
                await AssignSourceAsync(cached, generation);
                return;
            }

            NasMediaCache.PrepareOriginalCacheForLoad();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != _loadGeneration)
                    return;
                ShowDownloadProgress(0, photo.FileSize > 0 ? photo.FileSize : null);
                // 播放条仍可自动隐藏；仅下载进度条常显。
                ShowControls();
            });

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
            await MainThread.InvokeOnMainThreadAsync(HideDownloadProgress);
        }
        catch (OperationCanceledException)
        {
            if (generation == _loadGeneration)
                SetLoading(generation, false);
        }
        catch
        {
            if (generation == _loadGeneration)
                ShowError(generation, "视频加载失败。");
        }
    }

    private Task AssignSourceAsync(string path, int generation)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (generation != _loadGeneration)
                return;

            MediaPlayer.Source = MediaSource.FromFile(path);
            MediaPlayer.IsVisible = true;
            _playbackPath = path;
            NasMediaCache.ProtectPath(path);
            ApplyDefaultSpeed();
            RefreshDuration();
        });
    }

    private void ReleasePlaybackPath()
    {
        if (!string.IsNullOrEmpty(_playbackPath))
            NasMediaCache.UnprotectPath(_playbackPath);
        _playbackPath = null;
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
        DownloadChrome.IsVisible = true;
        DownloadProgressBar.IsVisible = true;
        DownloadLabel.IsVisible = true;
        // 进度条独立显示；播放条仍可按普通逻辑自动隐藏。
        ControlsOverlay.Opacity = 1;
        if (!_controlsVisible)
            ControlsOverlay.InputTransparent = true;

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
        DownloadChrome.IsVisible = false;
        DownloadProgressBar.IsVisible = false;
        DownloadLabel.IsVisible = false;
        if (!_controlsVisible)
            ControlsOverlay.InputTransparent = true;
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MediaPlayer.IsVisible = true;
            PosterImage.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
            HideDownloadProgress();

            RefreshDuration(MediaPlayer.Duration);

            UpdatePlayPauseButton();
            ApplyDefaultSpeed();
            ShowControls();
        });
    }

    private void RefreshDuration(TimeSpan? candidate = null)
    {
        var best = candidate is { TotalSeconds: > 0 } c ? c.TotalSeconds : 0;

        var playerDuration = MediaPlayer.Duration;
        if (playerDuration.TotalSeconds > best)
            best = playerDuration.TotalSeconds;

#if ANDROID
        if (!string.IsNullOrEmpty(_playbackPath))
        {
            var fileDuration = Platforms.Android.VideoDurationHelper.TryGetFromFile(_playbackPath);
            if (fileDuration is { TotalSeconds: > 0 } fd && fd.TotalSeconds > best)
                best = fd.TotalSeconds;
        }

        var photoDuration = Platforms.Android.VideoDurationHelper.TryGetFromPhoto(Photo);
        if (photoDuration is { TotalSeconds: > 0 } pd && pd.TotalSeconds > best)
            best = pd.TotalSeconds;
#else
        var photoDuration = TryGetPhotoDurationFallback(Photo);
        if (photoDuration is { TotalSeconds: > 0 } pd && pd.TotalSeconds > best)
            best = pd.TotalSeconds;
#endif

        if (best <= 0)
            return;

        if (Math.Abs(best - _durationSeconds) < 0.5 && _durationSeconds > 0)
            return;

        _durationSeconds = best;
        ProgressSlider.Maximum = best;
        DurationLabel.Text = FormatTime(TimeSpan.FromSeconds(best));
    }

#if !ANDROID
    private static TimeSpan? TryGetPhotoDurationFallback(Photo? photo)
    {
        var raw = photo?.Additional?.VideoMeta?.Duration ?? 0;
        if (raw <= 0)
            return null;

        var duration = TimeSpan.FromMilliseconds(raw);
        if (duration.TotalSeconds < 1 && raw is >= 1 and < 86_400)
            return TimeSpan.FromSeconds(raw);

        return duration;
    }
#endif

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowError(_loadGeneration, string.IsNullOrWhiteSpace(e.ErrorMessage)
                ? "视频播放失败。"
                : $"视频播放失败：{e.ErrorMessage}");
        });
    }

    private void OnStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        UpdatePlayPauseButton();
        RefreshDuration();
    }

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        if (_isScrubbing || _isGestureSeeking)
            return;

        // 播放进度回调很频繁，节流后再刷 UI，减轻主线程压力。
        var now = Environment.TickCount64;
        if (now - _lastProgressUiTick < 200)
            return;
        _lastProgressUiTick = now;

        var seconds = e.Position.TotalSeconds;
        var text = FormatTime(e.Position);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressSlider.Value = seconds;
            CurrentTimeLabel.Text = text;
        });
    }

    private void UpdatePlayPauseButton()
    {
        var playing = MediaPlayer.CurrentState == MediaElementState.Playing;
        PlayPauseButton.Text = playing ? "❚❚" : "▶";
    }

    private async void TogglePlayPause()
    {
        if (MediaPlayer.CurrentState == MediaElementState.Playing)
        {
            MediaPlayer.Pause();
            UpdatePlayPauseButton();
            return;
        }

        // 播完后停在末尾，需先 Seek 到开头才能再次 Play。
        var duration = MediaPlayer.Duration;
        var position = MediaPlayer.Position;
        var atEnd = duration.TotalSeconds > 0.5
            && position >= duration - TimeSpan.FromMilliseconds(500);
        if (atEnd)
        {
            try
            {
                await MediaPlayer.SeekTo(TimeSpan.Zero);
                ProgressSlider.Value = 0;
                CurrentTimeLabel.Text = "0:00";
            }
            catch
            {
                // ignore seek errors
            }
        }

        MediaPlayer.Play();
        UpdatePlayPauseButton();
    }

    private void OnSingleTappedManaged(object? sender, TappedEventArgs e)
    {
        TogglePlayPause();
        ToggleControls();
        SingleTapped?.Invoke(this, EventArgs.Empty);
    }

    private void OnScrubDragStarted(object? sender, EventArgs e)
    {
        _isScrubbing = true;
        _chromeHideCts?.Cancel();
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

        ShowControls();
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
            ShowControls();
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
            if (loading)
                ShowControls();
        });
    }

    public void Stop()
    {
        _loadGeneration++;
        _progressCts?.Cancel();
        _progressCts = null;
        _chromeHideCts?.Cancel();
        _wasPlayingBeforeSleep = false;
        _isGestureSeeking = false;
        SpeedHintLabel.IsVisible = false;
        EndFastForward();
        ReleasePlaybackPath();
        _durationSeconds = 0;
        ResetZoomTransform();
        MediaPlayer.Stop();
        MediaPlayer.Source = null;
        MediaPlayer.IsVisible = false;
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
#if !ANDROID
        if (_isNavigating || _isPinching)
            return;

        // 底部滑块拖动中时忽略页面手势；手势 seek 本身会置 _isGestureSeeking。
        if (_isScrubbing && !_isGestureSeeking)
            return;

        if (IsZoomed)
        {
            HandleZoomPan(e);
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panMode = PanMode.None;
                _navPanY = 0;
                break;
            case GestureStatus.Running:
                _navPanY = e.TotalY;

                if (_panMode == PanMode.None)
                {
                    if (Math.Abs(e.TotalY) > 20 && Math.Abs(e.TotalY) > Math.Abs(e.TotalX) * 1.15)
                        _panMode = PanMode.Vertical;
                    else if (Math.Abs(e.TotalX) > 20 && Math.Abs(e.TotalX) > Math.Abs(e.TotalY) * 1.15
                             && BeginGestureSeek())
                        _panMode = PanMode.Seek;
                }

                if (_panMode == PanMode.Vertical)
                    SlideHost.TranslationY = ApplyVerticalResistance(e.TotalY);
                else if (_panMode == PanMode.Seek)
                    UpdateGestureSeek(e.TotalX);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panMode == PanMode.Vertical)
                    _ = CompleteVerticalPanAsync();
                else if (_panMode == PanMode.Seek)
                    _ = EndGestureSeekAsync();

                _panMode = PanMode.None;
                break;
        }
#endif
    }

    /// <summary>
    /// 抖音式相对 seek：以按下时进度为基准，用水平滑动距离换算偏移，而非按触点在屏幕上的绝对位置映射进度。
    /// 右滑前进，左滑后退；横向滑满一屏约等于整段时长。
    /// </summary>
    internal bool BeginGestureSeek()
    {
        if (_isGestureSeeking || IsZoomed || _isNavigating)
            return false;

        var duration = GetSeekableDurationSeconds();
        if (duration <= 0.5)
            return false;

        _isGestureSeeking = true;
        _chromeHideCts?.Cancel();
        _wasPlayingBeforeScrub = MediaPlayer.CurrentState == MediaElementState.Playing;
        if (_wasPlayingBeforeScrub)
            MediaPlayer.Pause();

        _seekStartSeconds = Math.Clamp(MediaPlayer.Position.TotalSeconds, 0, duration);
        if (_seekStartSeconds <= 0 && ProgressSlider.Value > 0)
            _seekStartSeconds = Math.Clamp(ProgressSlider.Value, 0, duration);

        _seekTargetSeconds = _seekStartSeconds;
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.InputTransparent = false;
        UpdateGestureSeekUi();
        return true;
    }

    internal void UpdateGestureSeek(double deltaX)
    {
        if (!_isGestureSeeking)
            return;

        var duration = GetSeekableDurationSeconds();
        if (duration <= 0)
            return;

        var width = Width > 1 ? Width : 360;
        // 相对起点的偏移量，不是 fingerX/width 绝对映射。
        var offsetSeconds = deltaX / width * duration;
        _seekTargetSeconds = Math.Clamp(_seekStartSeconds + offsetSeconds, 0, duration);
        UpdateGestureSeekUi();
    }

    internal async Task EndGestureSeekAsync()
    {
        if (!_isGestureSeeking)
            return;

        var target = _seekTargetSeconds;
        _isGestureSeeking = false;
        SpeedHintLabel.IsVisible = false;

        try
        {
            await MediaPlayer.SeekTo(TimeSpan.FromSeconds(target));
            ProgressSlider.Value = target;
            CurrentTimeLabel.Text = FormatTime(TimeSpan.FromSeconds(target));
            if (_wasPlayingBeforeScrub)
                MediaPlayer.Play();
        }
        catch
        {
            // ignore seek errors
        }

        UpdatePlayPauseButton();
        ShowControls();
    }

    private void UpdateGestureSeekUi()
    {
        ProgressSlider.Value = _seekTargetSeconds;
        CurrentTimeLabel.Text = FormatTime(TimeSpan.FromSeconds(_seekTargetSeconds));

        var delta = _seekTargetSeconds - _seekStartSeconds;
        var sign = delta >= 0 ? "+" : "";
        SpeedHintLabel.Text = $"{sign}{delta:0}s  {FormatTime(TimeSpan.FromSeconds(_seekTargetSeconds))}";
        SpeedHintLabel.IsVisible = true;
    }

    private double GetSeekableDurationSeconds()
    {
        if (_durationSeconds > 0.5)
            return _durationSeconds;
        if (ProgressSlider.Maximum > 0.5)
            return ProgressSlider.Maximum;
        var player = MediaPlayer.Duration.TotalSeconds;
        return player > 0.5 ? player : 0;
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
#if !ANDROID
        if (_isNavigating || _isScrubbing)
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
                ApplyZoomFactor(scaleFactor, ToFocalOffsetX(e.ScaleOrigin.X), ToFocalOffsetY(e.ScaleOrigin.Y));
                break;
            }
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isPinching = false;
                if (_currentScale < 1.05)
                    ResetZoomTransform();
                else
                    ClampZoomPan();
                ApplyZoomTransform();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
#endif
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
                ClampZoomPan(allowRubberBand: true);
                ApplyZoomTransform();
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SlideHost.TranslationY = 0;
                ClampZoomPan();
                ApplyZoomTransform();
                break;
        }
    }

    internal void ApplyZoomFactor(double factor, double focalX, double focalY)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || Math.Abs(factor - 1) < 0.001)
            return;

        var newScale = Math.Clamp(_currentScale * factor, MinScale, MaxScale);
        var actualFactor = newScale / _currentScale;
        if (Math.Abs(actualFactor - 1) < 0.00001)
            return;

        _panX = focalX - (focalX - _panX) * actualFactor;
        _panY = focalY - (focalY - _panY) * actualFactor;
        _currentScale = newScale;
        ClampZoomPan(allowRubberBand: true);
        ApplyZoomTransform();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void BeginZoomPan()
    {
        _panStartX = _panX;
        _panStartY = _panY;
    }

    internal void UpdateZoomPan(double totalX, double totalY)
    {
        _panX = _panStartX + totalX;
        _panY = _panStartY + totalY;
        ClampZoomPan(allowRubberBand: true);
        ApplyZoomTransform();
    }

    internal void FinishZoomPan()
    {
        ClampZoomPan();
        ApplyZoomTransform();
        SlideHost.TranslationY = 0;
    }

    private void ApplyZoomTransform()
    {
        TransformHost.Scale = _currentScale;
        TransformHost.TranslationX = _panX;
        TransformHost.TranslationY = _panY;
    }

    private void ResetZoomTransform()
    {
        _currentScale = 1;
        _panX = 0;
        _panY = 0;
        _previousPinchScale = 1;
        _isPinching = false;
        TransformHost.Scale = 1;
        TransformHost.TranslationX = 0;
        TransformHost.TranslationY = 0;
        SlideHost.TranslationX = 0;
        SlideHost.TranslationY = 0;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampZoomPan(bool allowRubberBand = false)
    {
        EnsureContainerSize();
        if (_currentScale <= 1.01)
        {
            _panX = 0;
            _panY = 0;
            return;
        }

        var maxX = Math.Max(0, (_containerWidth * (_currentScale - 1)) / 2);
        var maxY = Math.Max(0, (_containerHeight * (_currentScale - 1)) / 2);

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

    private void EnsureContainerSize()
    {
        if (_containerWidth > 1 && _containerHeight > 1)
            return;

        _containerWidth = Width > 1 ? Width : 360;
        _containerHeight = Height > 1 ? Height : 640;
    }

    private double ToFocalOffsetX(double originX)
    {
        EnsureContainerSize();
        return originX * _containerWidth - _containerWidth / 2;
    }

    private double ToFocalOffsetY(double originY)
    {
        EnsureContainerSize();
        return originY * _containerHeight - _containerHeight / 2;
    }

    internal double ToFocalOffsetFromPixels(double x, double y, out double focalY)
    {
        EnsureContainerSize();
        focalY = y - _containerHeight / 2;
        return x - _containerWidth / 2;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var hadSize = _containerWidth > 1 && _containerHeight > 1;
        var orientationFlipped = hadSize
            && ((_containerWidth < _containerHeight) != (width < height));

        _containerWidth = width;
        _containerHeight = height;

        if (orientationFlipped)
        {
            SlideHost.TranslationX = 0;
            SlideHost.TranslationY = 0;
            ResetZoomTransform();
        }
        else if (!_isPinching && IsZoomed)
        {
            ClampZoomPan();
            ApplyZoomTransform();
        }
    }

    private double ApplyVerticalResistance(double deltaY)
    {
        if (deltaY > 0 && !CanGoPrevious)
            return deltaY * 0.28;
        if (deltaY < 0 && !CanGoNext)
            return deltaY * 0.28;
        return deltaY;
    }

    internal async Task CompleteVerticalPanAsync()
    {
        var height = Height > 0 ? Height : 640;
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
            if (IsZoomed)
            {
                ClampZoomPan();
                ApplyZoomTransform();
            }

            return;
        }

        _isNavigating = true;
        try
        {
            MediaPlayer.Pause();
            var exitY = direction > 0 ? -height : height;
            await SlideHost.TranslateToAsync(0, exitY, 200, Easing.CubicOut);
            ResetZoomTransform();
            SlideHost.TranslationY = direction > 0 ? height : -height;

            await OnSwipeNavigateAsync(direction);

            await SlideHost.TranslateToAsync(0, 0, 200, Easing.CubicOut);
            ShowControls();
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        return $"{time.Minutes}:{time.Seconds:D2}";
    }

    private enum PanMode
    {
        None,
        Vertical,
        Seek
    }
}
