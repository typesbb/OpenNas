using NSynology.Foto;
using OpenNas.Controls;

namespace OpenNas.Views;

public partial class PhotoViewerPage : ContentPage
{
    private readonly IReadOnlyList<Photo> _photos;
    private readonly ZoomableImageView _imageView;
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
            VerticalOptions = LayoutOptions.Fill
        };
        _imageView.ZoomChanged += OnZoomChanged;
        _imageView.DismissDrag += OnDismissDrag;
        _imageView.DismissRequested += OnDismissRequested;
        _imageView.OnSwipeNavigateAsync = NavigateAsync;
        DismissHost.Children.Add(_imageView);

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

        _currentZoomed = false;
        UpdateNavigationBounds();
        _imageView.Photo = _photos[_index];
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
        _currentZoomed = false;
        UpdateNavigationBounds();
        _imageView.Photo = _photos[_index];
        return Task.CompletedTask;
    }

    private void OnZoomChanged(object? sender, EventArgs e)
    {
        if (sender is ZoomableImageView zoomable)
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
        var threshold = _imageView.GetDismissThreshold();
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
        DismissHost.TranslationY = 0;
        DismissHost.Opacity = 1;
    }
}
