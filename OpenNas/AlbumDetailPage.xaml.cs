using System.Collections.ObjectModel;
using NSynology;
using NSynology.Foto;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas;

public partial class AlbumDetailPage : ContentPage
{
    private readonly Album _album;
    private readonly ObservableCollection<Photo> _photos = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly int _imageSize = 100;

    private int _offset;
    private bool _initialLoadDone;
    private double _w = 360;

    public AlbumDetailPage(Album album)
    {
        InitializeComponent();
        _album = album;
        Title = album.Name;
        BindingContext = _photos;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialLoadDone)
            return;

        _initialLoadDone = true;
        _w = Width > 0 ? Width : Application.Current?.Windows.FirstOrDefault()?.Width ?? 360;
        await LoadMorePhotosAsync();
    }

    private async void ScrollView_Scrolled(object sender, ScrolledEventArgs e)
    {
        if (sender is not ScrollView scrollView)
            return;

        _w = scrollView.Width > 0 ? scrollView.Width : _w;
        if (e.ScrollY >= scrollView.ContentSize.Height - scrollView.Height - 80)
            await LoadMorePhotosAsync();
    }

    private async Task LoadMorePhotosAsync()
    {
        if (_album.ItemCount <= _offset)
            return;

        if (!await _loadGate.WaitAsync(0))
            return;

        try
        {
            var columns = Math.Max(2, (int)Math.Floor(_w / _imageSize));
            var limit = Math.Min(60, columns * columns * 2);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var photos = (await SynologyManager.Client.Foto.GetPhotosAsync(_album, _offset, limit, cts.Token)).ToList();
            if (photos.Count == 0)
            {
                _offset = _album.ItemCount;
                return;
            }

            _offset += photos.Count;
            foreach (var photo in photos)
                _photos.Add(photo);
        }
        catch (Exception ex)
        {
            AppLog.Error($"加载相册照片失败 {_album.Name}", ex);
            await DisplayAlert(_album.Name, $"加载照片失败：{ex.Message}", "确定");
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private void OnImageHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not Image image)
            return;

        image.HeightRequest = _imageSize;
        image.WidthRequest = _imageSize;
        if (image.BindingContext is Photo photo)
            NasThumbnailLoader.TryLoadPhotoThumbnail(image, photo);
    }
}
