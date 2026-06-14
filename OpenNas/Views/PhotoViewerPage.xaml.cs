using NSynology.Foto;
using OpenNas.Controls;

namespace OpenNas.Views;

public partial class PhotoViewerPage : ContentPage
{
    private readonly IReadOnlyList<Photo> _photos;
    private readonly ZoomableImageView _imageView;
    private readonly NasVideoPlayerView _videoView;
    private int _index;
    private bool _currentZoomed;
    private bool _initialized;
    private bool _isDismissing;

    public PhotoViewerPage(IReadOnlyList<Photo> photos, int startIndex)
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
        _imageView.ZoomChanged += OnZoomChanged;
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

        var photo = _photos[_index];
        var isVideo = photo.IsVideo;

        _currentZoomed = false;
        _imageView.IsVisible = !isVideo;
        _videoView.IsVisible = isVideo;

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
            _currentZoomed = zoomable.IsZoomed;
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
            DismissHost.TranslateTo(0, height, 240, Easing.CubicIn),
            DismissHost.FadeTo(0, 240, Easing.CubicIn));
    }

    private Task AnimateDismissResetAsync()
    {
        return Task.WhenAll(
            DismissHost.TranslateTo(0, 0, 180, Easing.CubicOut),
            DismissHost.FadeTo(1, 180, Easing.CubicOut));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _videoView.Stop();
        DismissHost.TranslationY = 0;
        DismissHost.Opacity = 1;
    }
}
