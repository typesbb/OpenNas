using NSynology.Foto;
using OpenNas.Controls;

namespace OpenNas.Views;

public partial class FullscreenMediaPage : ContentPage
{
    private readonly IReadOnlyList<Photo> _photos;
    private readonly ZoomableImageView _imageView;
    private readonly NasVideoPlayerView _videoView;
    private int _index;
    private bool _isLandscape;
    private bool _initialized;

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

        _videoView = new NasVideoPlayerView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false,
            IsFullscreenHost = true
        };
        _videoView.OnSwipeNavigateAsync = NavigateAsync;

        MediaHost.Children.Add(_imageView);
        MediaHost.Children.Add(_videoView);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized || _photos.Count == 0)
            return;

        _initialized = true;
        ShowCurrent();
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
            _imageView.Photo = null;
            _videoView.CanGoPrevious = _index > 0;
            _videoView.CanGoNext = _index < _photos.Count - 1;
            _videoView.Photo = photo;
        }
        else
        {
            _videoView.Stop();
            _videoView.Photo = null;
            _imageView.CanGoPrevious = _index > 0;
            _imageView.CanGoNext = _index < _photos.Count - 1;
            _imageView.Photo = photo;
        }
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

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private void OnRotateClicked(object? sender, EventArgs e)
    {
        _isLandscape = !_isLandscape;
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.SetLandscape(_isLandscape);
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _videoView.Stop();
#if ANDROID
        Platforms.Android.FullscreenOrientationHelper.Reset();
#endif
    }
}
