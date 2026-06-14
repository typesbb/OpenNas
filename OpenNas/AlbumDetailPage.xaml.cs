using System.Collections.ObjectModel;
using NSynology;
using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.Views;

namespace OpenNas;

public partial class AlbumDetailPage : ContentPage
{
    private const int PageSize = 60;

    private readonly Album _album;
    private readonly List<Photo> _photos = [];
    private readonly ObservableCollection<PhotoDateGroup> _groups = [];
    private readonly ObservableCollection<Photo> _flatPhotos = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private int _offset;
    private string _sortField = "time";
    private bool _sortDescending = true;
    private bool _uploading;
    private bool _hasMore = true;

    public AlbumDetailPage(Album album)
    {
        InitializeComponent();
        _album = album;
        TitleLabel.Text = album.Name;
        PhotosRefreshView.Refreshing += OnPullRefreshing;
        ApplyViewMode();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_photos.Count > 0)
            return;

        await ReloadPhotosAsync();
    }

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        await ReloadPhotosAsync();
        PhotosRefreshView.IsRefreshing = false;
    }

    private async void OnLoadMore(object? sender, EventArgs e) => await LoadMorePhotosAsync();

    private async Task ReloadPhotosAsync(bool retryAfterUpload = false)
    {
        await _loadGate.WaitAsync();
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;

        try
        {
            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            {
                await DisplayAlert(_album.Name, "未连接 NAS，请重新登录。", "确定");
                return;
            }

            _offset = 0;
            _hasMore = true;
            _photos.Clear();
            ClearDisplay();
            await SyncAlbumCountAsync();
            await SynologyManager.Client.Foto.WarmupAlbumForBackupAsync(_album.Id);

            var maxAttempts = retryAfterUpload ? 4 : 1;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (retryAfterUpload && attempt > 0)
                    await Task.Delay(1500);

                await LoadNextPhotoPageAsync();
                if (_photos.Count > 0 || _album.ItemCount == 0)
                    break;

                await SyncAlbumCountAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"刷新相册照片失败 {_album.Name}", ex);
            await DisplayAlert(_album.Name, $"加载照片失败：{ex.Message}", "确定");
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            _loadGate.Release();
        }
    }

    private async Task LoadMorePhotosAsync()
    {
        if (!_hasMore || _album.ItemCount <= _offset)
            return;

        if (!await _loadGate.WaitAsync(0))
            return;

        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;

        try
        {
            await LoadNextPhotoPageAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error($"加载相册照片失败 {_album.Name}", ex);
            await DisplayAlert(_album.Name, $"加载照片失败：{ex.Message}", "确定");
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            _loadGate.Release();
        }
    }

    private async Task<int> LoadNextPhotoPageAsync()
    {
        if (!_hasMore || _album.ItemCount <= _offset)
            return 0;

        if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            return 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var photos = (await SynologyManager.Client.Foto.GetPhotosAsync(
            _album, _offset, PageSize, _sortField, _sortDescending, cts.Token)).ToList();

        if (photos.Count == 0)
        {
            _hasMore = false;
            return 0;
        }

        _offset += photos.Count;
        _hasMore = photos.Count >= PageSize && _offset < _album.ItemCount;
        _photos.AddRange(photos);
        AppendToDisplay(photos);
        return photos.Count;
    }

    private async Task SyncAlbumCountAsync()
    {
        if (SynologyManager.Client == null)
            return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var albums = await SynologyManager.Client.Foto.GetAlbumsAsync(0, 200, cts.Token);
        var fresh = albums.FirstOrDefault(a => a.Id == _album.Id);
        if (fresh != null)
            _album.ItemCount = fresh.ItemCount;
    }

    private bool UsesGroups => AlbumPhotoSort.UsesGroups(_sortField);

    private void ApplyViewMode()
    {
        PhotosView.IsGrouped = UsesGroups;
        PhotosView.ItemsSource = UsesGroups ? _groups : _flatPhotos;
    }

    private void ClearDisplay()
    {
        _groups.Clear();
        _flatPhotos.Clear();
    }

    private void AppendToDisplay(IReadOnlyList<Photo> page)
    {
        if (UsesGroups)
            RebuildGroups();
        else
        {
            foreach (var photo in page)
                _flatPhotos.Add(photo);
        }
    }

    private void RebuildGroups()
    {
        _groups.Clear();
        var groups = AlbumPhotoSort.UsesSizeGroups(_sortField)
            ? PhotoSizeHelper.GroupBySize(_photos, _sortDescending)
            : PhotoDateHelper.GroupByDate(_photos, _sortDescending);

        foreach (var group in groups)
            _groups.Add(group);
    }

    private IReadOnlyList<DropdownMenuItem> BuildSortMenuItems() =>
    [
        new("time", "拍摄时间", _sortField == "time", TrailingArrow("time")),
        new("name", "文件名称", _sortField == "name", TrailingArrow("name")),
        new("size", "文件大小", _sortField == "size", TrailingArrow("size"))
    ];

    private string? TrailingArrow(string field) =>
        _sortField == field ? (_sortDescending ? "↓" : "↑") : null;

    private void UpdateSortSelection(string field)
    {
        if (_sortField == field)
            _sortDescending = !_sortDescending;
        else
        {
            _sortField = field;
            _sortDescending = field is not "name";
        }
    }

    private void OnImageHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not Image image)
            return;

        image.BindingContextChanged -= OnPhotoBindingContextChanged;
        image.BindingContextChanged += OnPhotoBindingContextChanged;
        LoadPhotoThumbnail(image);
    }

    private void OnPhotoBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is Image image)
            LoadPhotoThumbnail(image);
    }

    private static void LoadPhotoThumbnail(Image image)
    {
        if (image.BindingContext is Photo photo)
            NasThumbnailLoader.TryLoadPhotoThumbnail(image, photo);
    }

    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not Photo photo)
            return;

        if (_photos.Count == 0)
            return;

        var index = _photos.FindIndex(p => p.Id == photo.Id);
        if (index < 0)
            index = 0;

        await Navigation.PushAsync(new PhotoViewerPage(_photos, index));
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        var selected = await Dropdown.ShowAsync(BuildSortMenuItems(), topMargin: 52, rightMargin: 8);
        if (string.IsNullOrEmpty(selected))
            return;

        if (selected is not ("time" or "name" or "size"))
            return;

        UpdateSortSelection(selected);
        ApplyViewMode();
        await ReloadPhotosAsync();
    }

    private async void OnAddPhotoClicked(object? sender, EventArgs e)
    {
        if (_uploading)
            return;

#if ANDROID
        if (!await MediaPermissions.EnsureReadMediaAsync())
        {
            await DisplayAlert(_album.Name, "请允许访问照片和视频后再添加。", "确定");
            return;
        }
#endif

        try
        {
            var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "添加到相册",
                FileTypes = FilePickerFileType.Images
            });

            if (picks == null || !picks.Any())
                return;

            await UploadPickedPhotosAsync(picks);
        }
        catch (Exception ex)
        {
            AppLog.Error($"选择照片失败 {_album.Name}", ex);
            await DisplayAlert(_album.Name, ex.Message, "确定");
        }
    }

    private async Task UploadPickedPhotosAsync(IEnumerable<FileResult> files)
    {
        _uploading = true;
        AddPhotoButton.IsEnabled = false;
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;

        try
        {
            var list = files.ToList();
            var progress = new Progress<string>(msg => TitleLabel.Text = msg);
            var uploaded = await AlbumPhotoUpload.UploadFilesAsync(_album, list, progress);
            TitleLabel.Text = _album.Name;

            await ReloadPhotosAsync(retryAfterUpload: true);
            await DisplayAlert(_album.Name, $"已添加 {uploaded} 张照片。", "确定");
        }
        catch (Exception ex)
        {
            AppLog.Error($"上传照片失败 {_album.Name}", ex);
            TitleLabel.Text = _album.Name;
            await DisplayAlert(_album.Name, $"上传失败：{ex.Message}", "确定");
        }
        finally
        {
            _uploading = false;
            AddPhotoButton.IsEnabled = true;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
    }
}
