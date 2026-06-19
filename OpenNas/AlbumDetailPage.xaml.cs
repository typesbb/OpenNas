using System.Collections.ObjectModel;
using NSynology;
using NSynology.Foto;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.Views;
#if ANDROID
using OpenNas.Platforms.Android;
#endif

namespace OpenNas;

public partial class AlbumDetailPage : ContentPage, IDisposable
{

    private const int PageSize = 30;

    private readonly Album _album;
    private readonly List<Photo> _photos = [];
    private readonly ObservableCollection<PhotoDateGroup> _groups = [];
    private ObservableCollection<Photo> _flatPhotos = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private int _offset;
    private string _sortField = "time";
    private bool _sortDescending = true;
    private bool _uploading;
    private CancellationTokenSource? _uploadCts;
    private bool _hasMore = true;

    public void Dispose()
    {
        _loadGate?.Dispose();
        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        GC.SuppressFinalize(this);
    }

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
#if ANDROID
        AlbumGridUiHelper.TryOptimize(PhotosView);
#endif
        if (_photos.Count == 0)
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
                await DisplayAlertAsync(_album.Name, "未连接 NAS，请重新登录。", "确定");
                return;
            }

            _offset = 0;
            _hasMore = true;
            _photos.Clear();
            ClearDisplay();
            await SyncAlbumCountAsync();
            await Task.Run(() => SynologyManager.Client.Foto.WarmupAlbumForBackupAsync(_album.Id));

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
            await DisplayAlertAsync(_album.Name, $"加载照片失败：{ex.Message}", "确定");
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

        try
        {
            await LoadNextPhotoPageAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error($"加载相册照片失败 {_album.Name}", ex);
            await DisplayAlertAsync(_album.Name, $"加载照片失败：{ex.Message}", "确定");
        }
        finally
        {
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
        var photos = await Task.Run(async () =>
        {
            return (await SynologyManager.Client.Foto.GetPhotosAsync(
            _album, _offset, PageSize, _sortField, _sortDescending, cts.Token)).ToList();
        });

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
        var albums = await Task.Run(async () =>
        {
            return await SynologyManager.Client.Foto.GetAlbumsAsync(0, 200, cts.Token);
        });
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

    private void AppendToDisplay(List<Photo> page)
    {
        if (page.Count == 0)
            return;

        if (UsesGroups)
            AppendToGroups(page);
        else
        {
            _flatPhotos = new ObservableCollection<Photo>(
                _flatPhotos.Concat(page));
            PhotosView.ItemsSource = _flatPhotos;
        }
    }

    private void AppendToGroups(IReadOnlyList<Photo> page)
    {
        if (AlbumPhotoSort.UsesSizeGroups(_sortField))
        {
            RebuildGroups();
            return;
        }

        foreach (var photo in page)
        {
            var dateLabel = PhotoDateHelper.FormatGroupLabel(photo.Time);
            if (_groups.Count > 0 && _groups[^1].DateLabel == dateLabel)
            {
                var last = _groups[^1];
                _groups[^1] = new PhotoDateGroup(dateLabel, last.Append(photo));
                continue;
            }

            _groups.Add(new PhotoDateGroup(dateLabel, [photo]));
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
            await DisplayAlertAsync(_album.Name, "请允许访问照片和视频后再添加。", "确定");
            return;
        }
#endif

        try
        {
            var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "添加到相册",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "image/*", "video/*" } },
                    { DevicePlatform.iOS, new[] { "public.image", "public.movie" } },
                    { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".m4v", ".3gp" } },
                    { DevicePlatform.macOS, new[] { "public.image", "public.movie" } },
                })
            });

            if (picks == null || !picks.Any())
                return;

            await UploadPickedPhotosAsync(picks);
        }
        catch (Exception ex)
        {
            AppLog.Error($"选择文件失败 {_album.Name}", ex);
            await DisplayAlertAsync(_album.Name, ex.Message, "确定");
        }
    }

    private void OnCancelUploadClicked(object? sender, EventArgs e)
    {
        try { _uploadCts?.Cancel(); }
        catch (ObjectDisposedException) { }
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
            StatusLabel.IsVisible = true;
            UploadProgressArea.IsVisible = true;
            UploadProgressBar.Progress = 0;
            var progress = new Progress<(int current, int total, string fileName)>(p =>
            {
                StatusLabel.Text = $"正在上传 {p.current}/{p.total}…";
                UploadProgressBar.Progress = (double)p.current / p.total;
            });
            _uploadCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var uploaded = await AlbumPhotoUpload.UploadFilesAsync(_album, list, progress, cancellationToken: _uploadCts.Token);

            await ReloadPhotosAsync(retryAfterUpload: true);
            await DisplayAlertAsync(_album.Name, $"已添加 {uploaded} 张照片。", "确定");
        }
        catch (Exception ex)
        {
            AppLog.Error($"上传失败 {_album.Name}", ex);
            await DisplayAlertAsync(_album.Name, $"上传失败：{ex.Message}", "确定");
        }
        finally
        {
            StatusLabel.IsVisible = false;
            StatusLabel.Text = "";
            UploadProgressArea.IsVisible = false;
            _uploadCts?.Dispose();
            _uploadCts = null;
            _uploading = false;
            AddPhotoButton.IsEnabled = true;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
    }
}