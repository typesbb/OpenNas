using NSynology.Foto;
using OpenNas.Controls;

namespace OpenNas.Views;

public partial class FullscreenMediaPage : ContentPage
{
    private const int ChromeAutoHideMs = 2800;

    private readonly IReadOnlyList<Photo> _photos;
    private readonly ZoomableImageView _imageView;
    private readonly NasVideoPlayerView _videoView;
    private int _index;
    private bool _isLandscape;
    private bool _initialized;
    private bool _chromeVisible = true;
    private bool _windowEventsHooked;
    private CancellationTokenSource? _chromeHideCts;

    public FullscreenMediaPage(IReadOnlyList<Photo> photos, int startIndex)
    {
        InitializeComponent();
        _photos = photos;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, photos.Count - 1));

        _imageView = new ZoomableImageView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = true
        };
        _imageView.OnSwipeNavigateAsync = NavigateAsync;
        _imageView.SingleTapped += OnMediaSingleTapped;

        _videoView = new NasVideoPlayerView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false,
            IsFullscreenHost = true
        };
        _videoView.OnSwipeNavigateAsync = NavigateAsync;
        _videoView.SingleTapped += OnMediaSingleTapped;

        MediaHost.Children.Add(_imageView);
        MediaHost.Children.Add(_videoView);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.EnterImmersive();
#endif
        HookWindowLifecycle();
        ShowChrome();
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
        _videoView.Stop();
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

    private void ShowCurrent()
    {
        if (_photos.Count == 0)
            return;

        var photo = _photos[_index];
        var isVideo = photo.IsVideo;

        _imageView.IsVisible = !isVideo;
        _videoView.IsVisible = isVideo;
        RotateButton.IsVisible = true;

        if (isVideo)
        {
            _imageView.LoadPhoto(null);
            _videoView.CanGoPrevious = _index > 0;
            _videoView.CanGoNext = _index < _photos.Count - 1;
            _videoView.Photo = photo;
            _videoView.ShowControls();
        }
        else
        {
            _videoView.Stop();
            _videoView.Photo = null;
            _imageView.CanGoPrevious = _index > 0;
            _imageView.CanGoNext = _index < _photos.Count - 1;
            string? seed = null;
            if (Helpers.NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var thumbPath))
                seed = thumbPath;
            _imageView.LoadPhoto(photo, seed);
        }

        ShowChrome();
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

    private void OnMediaSingleTapped(object? sender, EventArgs e)
    {
        if (sender is ZoomableImageView)
        {
            if (_chromeVisible)
                HideChrome();
            else
                ShowChrome();
            return;
        }

        ShowChrome();
    }

    private void ShowChrome()
    {
        _chromeVisible = true;
        BackButton.InputTransparent = false;
        RotateButton.InputTransparent = false;
        ChromeLayer.Opacity = 1;
        ScheduleHideChrome();
    }

    private void HideChrome(bool animated = true)
    {
        _chromeHideCts?.Cancel();
        _chromeVisible = false;
        BackButton.InputTransparent = true;
        RotateButton.InputTransparent = true;
        if (!animated)
        {
            ChromeLayer.Opacity = 0;
            return;
        }

        _ = ChromeLayer.FadeToAsync(0, 320, Easing.CubicOut);
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
                    if (!token.IsCancellationRequested)
                        HideChrome();
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync(animated: false);

    private void OnRotateClicked(object? sender, EventArgs e)
    {
        ShowChrome();
        _isLandscape = !_isLandscape;
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.SetLandscape(_isLandscape);
#endif
    }
}
