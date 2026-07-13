using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NSynology;
using NSynology.Foto;
using OpenNas.Behaviors;
using OpenNas.Controls;
using OpenNas.Helpers;
using OpenNas.Services;
using OpenNas.Views;
#if ANDROID
using OpenNas.Platforms.Android;
#endif

namespace OpenNas.Views;

public partial class AlbumDetailPage : ContentPage, INotifyPropertyChanged, IDisposable
{
    private const string SortFieldKey = "album_detail_sort_field";
    private const string SortDescKey = "album_detail_sort_desc";
    private const string DefaultSortField = "time";

    private const int PageSize = 30;

    private readonly Album _album;
    private readonly ConnectionService _connection;
    private readonly List<Photo> _photos = [];
    private readonly ObservableCollection<SelectablePhotoGroup> _groups = [];
    private readonly Dictionary<int, SelectablePhoto> _selectableById = [];
    private ObservableCollection<SelectablePhoto> _flatPhotos = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private int _offset;
    private string _sortField;
    private bool _sortDescending;
    private bool _uploading;
    private bool _downloading;
    private CancellationTokenSource? _uploadCts;
    private CancellationTokenSource? _downloadCts;
    private bool _hasMore = true;
    private bool _isSelecting;
    private bool _suppressNextTap;
    private bool _retainAlbumScopeOnDisappear;
    private bool _canDownload;
    private bool _canManage;
    private bool _canUpload;

    public bool IsSelecting
    {
        get => _isSelecting;
        private set
        {
            if (_isSelecting == value)
                return;
            _isSelecting = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        PhotosAlbumMediaScope.Clear();
        LongPressBehavior.LongPressed -= OnPhotoLongPressBehavior;
        ClearSelectableRegistry();
        _loadGate?.Dispose();
        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    public AlbumDetailPage(Album album, ConnectionService connection)
    {
        _sortField = LoadSortField();
        _sortDescending = LoadSortDescending();
        InitializeComponent();
        _album = album;
        _connection = connection;
        TitleLabel.Text = album.Name;
        PhotosRefreshView.Refreshing += OnPullRefreshing;
        LongPressBehavior.LongPressed += OnPhotoLongPressBehavior;
        ApplyViewMode();
    }

    private static string LoadSortField() =>
        Preferences.Default.Get(SortFieldKey, DefaultSortField);

    private static bool LoadSortDescending() =>
        Preferences.Default.Get(SortDescKey, true);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var sharedWithMe = AlbumShareHelper.IsSharedWithMe(_album);
        var passphrase = sharedWithMe ? AlbumShareHelper.ResolvePassphrase(_album) : null;
        _canDownload = AlbumShareHelper.CanDownload(_album);
        _canManage = AlbumShareHelper.CanManage(_album);
        _canUpload = AlbumShareHelper.CanUpload(_album);
        PhotosAlbumMediaScope.Set(_album.Id, passphrase, _canDownload);
        PhotosMediaLibraryScope.Current = PhotosLibrary.PersonalSpace;
#if ANDROID
        AlbumGridUiHelper.TryOptimize(PhotosView);
#endif
        LongPressBehavior.DetectionEnabled = !IsSelecting;
        UpdateSelectionUi();
        if (_photos.Count == 0)
            await ReloadPhotosAsync();

        UpdateSelectionUi();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_retainAlbumScopeOnDisappear)
            PhotosAlbumMediaScope.Clear();
        _retainAlbumScopeOnDisappear = false;
    }

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        await ReloadPhotosAsync(showBusyIndicator: false);
        PhotosRefreshView.IsRefreshing = false;
    }

    private async void OnLoadMore(object? sender, EventArgs e) => await LoadMorePhotosAsync();

    private async Task ReloadPhotosAsync(bool retryAfterUpload = false, bool showBusyIndicator = true)
    {
        await _loadGate.WaitAsync();
        if (showBusyIndicator)
        {
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
        }

        try
        {
            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            {
                await DisplayAlertAsync(_album.Name, "未连接 NAS，请重新登录。", "确定");
                return;
            }

            _offset = 0;
            _hasMore = true;
            ExitSelectionMode();
            _photos.Clear();
            ClearSelectableRegistry();
            ClearDisplay();
            await SyncAlbumCountAsync();

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
            await UiFeedback.ShowApiErrorAsync(this, _album.Name, ex, $"加载照片失败：{ex.Message}");
        }
        finally
        {
            if (showBusyIndicator)
            {
                BusyIndicator.IsRunning = false;
                BusyIndicator.IsVisible = false;
            }
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
            await UiFeedback.ShowApiErrorAsync(this, _album.Name, ex, $"加载照片失败：{ex.Message}");
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

        if (AlbumShareHelper.RequiresSharePassphrase(_album))
        {
            var shared = await Task.Run(async () =>
                await SynologyManager.Client.Foto.ListSharedWithMeAlbumsAsync(0, 500, cts.Token));
            var fresh = shared.FirstOrDefault(a => a.Id == _album.Id);
            if (fresh != null)
                _album.ItemCount = fresh.ItemCount;
            return;
        }

        var albums = await Task.Run(async () =>
            await SynologyManager.Client.Foto.GetAlbumsAsync(0, 200, cancellationToken: cts.Token));
        var updated = albums.FirstOrDefault(a => a.Id == _album.Id);
        if (updated != null)
            _album.ItemCount = updated.ItemCount;
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

    private void ClearSelectableRegistry()
    {
        foreach (var item in _selectableById.Values)
            item.PropertyChanged -= OnSelectablePhotoPropertyChanged;
        _selectableById.Clear();
    }

    private SelectablePhoto Wrap(Photo photo)
    {
        if (!_selectableById.TryGetValue(photo.Id, out var item))
        {
            item = new SelectablePhoto(photo);
            item.PropertyChanged += OnSelectablePhotoPropertyChanged;
            _selectableById[photo.Id] = item;
        }
        return item;
    }

    private void AppendToDisplay(List<Photo> page)
    {
        if (page.Count == 0)
            return;

        if (UsesGroups)
            AppendToGroups(page);
        else
        {
            foreach (var photo in page)
                _flatPhotos.Add(Wrap(photo));
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
            var item = Wrap(photo);
            var dateLabel = PhotoDateHelper.FormatGroupLabel(photo.Time);
            if (_groups.Count > 0 && _groups[^1].DateLabel == dateLabel)
            {
                var last = _groups[^1];
                _groups[^1] = new SelectablePhotoGroup(dateLabel, last.Append(item));
                continue;
            }

            _groups.Add(new SelectablePhotoGroup(dateLabel, [item]));
        }
    }

    private void RebuildGroups()
    {
        _groups.Clear();
        var groups = AlbumPhotoSort.UsesSizeGroups(_sortField)
            ? PhotoSizeHelper.GroupBySize(_photos, _sortDescending)
            : PhotoDateHelper.GroupByDate(_photos, _sortDescending);

        foreach (var group in groups)
            _groups.Add(new SelectablePhotoGroup(group.DateLabel, group.Select(Wrap)));
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
        var previousField = _sortField;
        var previousDesc = _sortDescending;

        if (_sortField == field)
            _sortDescending = !_sortDescending;
        else
        {
            _sortField = field;
            _sortDescending = field != "name";
        }

        if (_sortField != previousField || _sortDescending != previousDesc)
        {
            Preferences.Default.Set(SortFieldKey, _sortField);
            Preferences.Default.Set(SortDescKey, _sortDescending);
        }
    }

    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not SelectablePhoto item)
            return;

        if (_photos.Count == 0)
            return;

        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            return;
        }

        if (IsSelecting)
        {
            item.IsSelected = !item.IsSelected;
            return;
        }

        var index = _photos.FindIndex(p => p.Id == item.Id);
        if (index < 0)
            index = 0;

        _retainAlbumScopeOnDisappear = true;
        await Navigation.PushAsync(new PhotoViewerPage(_photos, index, _connection));
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (IsSelecting)
        {
            ExitSelectionMode();
            return;
        }

        await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (IsSelecting)
        {
            ExitSelectionMode();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        var items = BuildMenuItems();
        var selected = await Dropdown.ShowAsync(items, topMargin: 52, rightMargin: 8);
        if (string.IsNullOrEmpty(selected))
            return;

        switch (selected)
        {
            case "time" or "name" or "size":
                UpdateSortSelection(selected);
                ApplyViewMode();
                await ReloadPhotosAsync();
                break;
        }
    }

    private IReadOnlyList<DropdownMenuItem> BuildMenuItems() => BuildSortMenuItems();

    private void OnPhotoLongPressBehavior(object? sender, LongPressBehavior.LongPressEventArgs e)
    {
        if (e.Context is not SelectablePhoto item || IsSelecting)
            return;

        if (!_canDownload && !_canManage)
            return;

        EnterSelectionMode(item, suppressNextTap: true);
    }

    private void OnSelectAllTapped(object? sender, TappedEventArgs e) => ToggleSelectAll();

    private void OnGroupHeaderCheckTapped(object? sender, TappedEventArgs e)
    {
        if (!IsSelecting)
            return;

        if ((sender as BindableObject)?.BindingContext is SelectablePhotoGroup group)
            ToggleGroup(group);
    }

    private void OnCancelSelectionClicked(object? sender, EventArgs e) => ExitSelectionMode();

    private async void OnDownloadSelectedClicked(object? sender, EventArgs e) => await DownloadSelectedAsync();

    private async void OnMoveSelectedClicked(object? sender, EventArgs e) => await MoveSelectedAsync();

    private async void OnDeleteSelectedClicked(object? sender, EventArgs e) => await DeleteSelectedAsync();

    private void EnterSelectionMode(SelectablePhoto item, bool suppressNextTap = false)
    {
        IsSelecting = true;
        foreach (var selectable in _selectableById.Values)
            selectable.IsSelected = false;
        item.IsSelected = true;
        _suppressNextTap = suppressNextTap;
        LongPressBehavior.DetectionEnabled = false;
        UpdateSelectionUi();
    }

    private void ExitSelectionMode()
    {
        IsSelecting = false;
        foreach (var selectable in _selectableById.Values)
            selectable.IsSelected = false;
        _suppressNextTap = false;
        LongPressBehavior.DetectionEnabled = true;
        UpdateSelectionUi();
    }

    private void ToggleSelectAll()
    {
        if (!IsSelecting || _selectableById.Count == 0)
            return;

        var selectAll = ComputeAllCheckState() != SelectionCheckState.Checked;
        foreach (var item in _selectableById.Values)
            item.IsSelected = selectAll;
    }

    private void ToggleGroup(SelectablePhotoGroup group)
    {
        var selectAll = group.CheckState != SelectionCheckState.Checked;
        foreach (var item in group)
            item.IsSelected = selectAll;
        group.RefreshCheckState();
    }

    private void OnSelectablePhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectablePhoto.IsSelected))
            return;

        RefreshGroupCheckStates();
        UpdateSelectionUi();
    }

    private void RefreshGroupCheckStates()
    {
        foreach (var group in _groups)
            group.RefreshCheckState();
    }

    private void UpdateSelectionUi()
    {
        var count = GetSelectedCount();
        var hasSelection = count > 0;

        SelectedCountLabel.Text = $"已选 {count}";
        SelectAllCheckbox.CheckState = ComputeAllCheckState();

        BackButton.IsVisible = !IsSelecting;
        TitleArea.IsVisible = !IsSelecting;
        MoreButton.IsVisible = !IsSelecting;

        CancelSelectionButton.IsVisible = IsSelecting;
        SelectedCountLabel.IsVisible = IsSelecting;
        SelectAllArea.IsVisible = IsSelecting;
        SelectionActionBar.IsVisible = IsSelecting;

        AddPhotoButton.IsVisible = !IsSelecting && _canUpload;
        DownloadSelectedButton.IsVisible = _canDownload;
        MoveSelectedButton.IsVisible = _canManage;
        DeleteSelectedButton.IsVisible = _canManage;
        DownloadSelectedButton.IsEnabled = hasSelection && !_downloading;
        MoveSelectedButton.IsEnabled = hasSelection && !_downloading;
        DeleteSelectedButton.IsEnabled = hasSelection && !_downloading;
    }

    private int GetSelectedCount() =>
        _selectableById.Values.Count(x => x.IsSelected);

    private List<Photo> GetSelectedPhotos() =>
        _selectableById.Values.Where(x => x.IsSelected).Select(x => x.Photo).ToList();

    private SelectionCheckState ComputeAllCheckState()
    {
        if (_selectableById.Count == 0)
            return SelectionCheckState.Unchecked;

        var selected = GetSelectedCount();
        if (selected == 0)
            return SelectionCheckState.Unchecked;
        if (selected == _selectableById.Count)
            return SelectionCheckState.Checked;
        return SelectionCheckState.Partial;
    }

    private async Task DownloadSelectedAsync()
    {
        if (_downloading)
            return;

        var selected = GetSelectedPhotos();
        if (selected.Count == 0)
            return;

        if (!await AppPermissionBootstrap.EnsureDownloadAllowedAsync(this, _connection))
            return;

        if (NasPhotoDownloadService.ShouldConfirmBatch(selected))
        {
            var confirm = await DisplayAlertAsync(_album.Name, NasPhotoDownloadService.BuildConfirmMessage(selected), "下载", "取消");
            if (!confirm)
                return;
        }

        _downloading = true;
        UpdateSelectionUi();
        StatusLabel.IsVisible = true;
        DownloadProgressArea.IsVisible = true;
        DownloadProgressBar.Progress = 0;
        _downloadCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<NasBatchDownloadProgress>(p =>
            {
                StatusLabel.Text = p.CurrentFileName != null
                    ? $"正在下载 {p.CompletedCount + 1}/{p.TotalCount}…"
                    : $"正在下载 {p.CompletedCount}/{p.TotalCount}…";

                if (p.TotalCount > 0)
                {
                    var itemProgress = p.CurrentFileTotal is > 0
                        ? p.BytesReceived / (double)p.CurrentFileTotal.Value
                        : p.IsComplete ? 1 : 0;
                    DownloadProgressBar.Progress = Math.Clamp((p.CompletedCount + itemProgress) / p.TotalCount, 0, 1);
                }
            });

            var result = await NasPhotoDownloadService.DownloadBatchAsync(
                selected,
                progress,
                _downloadCts.Token);

            if (result.Cancelled)
            {
                await DisplayAlertAsync(_album.Name, $"下载已取消，已完成 {result.SuccessCount} 项。", "确定");
                return;
            }

            if (result.FailedCount == 0)
            {
                await DisplayAlertAsync(_album.Name, $"已下载 {result.SuccessCount} 项到本机。", "确定");
                return;
            }

            var detail = result.Failures.Count <= 3
                ? string.Join("\n", result.Failures.Select(f => f.Photo.Filename ?? f.Photo.Id.ToString()))
                : string.Join("\n", result.Failures.Take(3).Select(f => f.Photo.Filename ?? f.Photo.Id.ToString())) + "\n…";
            await DisplayAlertAsync(_album.Name,
                $"已下载 {result.SuccessCount} 项，{result.FailedCount} 项失败。\n{detail}",
                "确定");
        }
        catch (Exception ex)
        {
            AppLog.Error($"下载照片失败 {_album.Name}", ex);
            await UiFeedback.ShowApiErrorAsync(this, _album.Name, ex, $"下载失败：{ex.Message}");
        }
        finally
        {
            StatusLabel.IsVisible = false;
            StatusLabel.Text = "";
            DownloadProgressArea.IsVisible = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            _downloading = false;
            UpdateSelectionUi();
        }
    }

    private void OnCancelDownloadClicked(object? sender, EventArgs e)
    {
        try { _downloadCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task MoveSelectedAsync()
    {
        var selected = GetSelectedPhotos();
        if (selected.Count == 0)
            return;

        if (SynologyManager.Client == null)
        {
            await DisplayAlertAsync(_album.Name, "未连接 NAS，请重新登录。", "确定");
            return;
        }

        var allAlbums = await Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await SynologyManager.Client.Foto.GetAlbumsAsync(0, 200, cancellationToken: cts.Token);
        });
        var otherAlbums = allAlbums.Where(a => a.Id != _album.Id).ToList();

        if (otherAlbums.Count == 0)
        {
            await DisplayAlertAsync(_album.Name, "没有其他相册可供移动。", "确定");
            return;
        }

        var targetName = await DisplayActionSheetAsync("移动到相册", "取消", null,
            otherAlbums.Select(a => a.Name).ToArray());

        var target = otherAlbums.FirstOrDefault(a => a.Name == targetName);
        if (target == null)
            return;

        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        try
        {
            var added = await SynologyManager.Client.Foto.AddPhotosToAlbumAsync(target.Id, selected);
            if (!added)
                throw new InvalidOperationException("添加到目标相册失败。");

            var removed = await SynologyManager.Client.Foto.RemovePhotosFromAlbumAsync(_album.Id, selected);
            if (!removed)
                throw new InvalidOperationException("从当前相册移除失败。");

            ExitSelectionMode();
            await ReloadPhotosAsync();
            await DisplayAlertAsync(_album.Name, $"已将 {selected.Count} 张照片移动到 {target.Name}。", "确定");
        }
        catch (Exception ex)
        {
            AppLog.Error($"移动照片失败 {_album.Name}", ex);
            await UiFeedback.ShowApiErrorAsync(this, _album.Name, ex, $"移动失败：{ex.Message}");
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = GetSelectedPhotos();
        if (selected.Count == 0)
            return;

        if (SynologyManager.Client == null)
        {
            await DisplayAlertAsync(_album.Name, "未连接 NAS，请重新登录。", "确定");
            return;
        }

        var confirm = await DisplayAlertAsync(_album.Name,
            $"确定要从 {_album.Name} 中移除 {selected.Count} 张照片？", "移除", "取消");

        if (!confirm)
            return;

        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        try
        {
            var removed = await SynologyManager.Client.Foto.RemovePhotosFromAlbumAsync(_album.Id, selected);
            if (!removed)
                throw new InvalidOperationException("从相册移除照片失败。");

            ExitSelectionMode();
            await ReloadPhotosAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error($"删除照片失败 {_album.Name}", ex);
            await UiFeedback.ShowApiErrorAsync(this, _album.Name, ex, $"删除失败：{ex.Message}");
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
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
            await UiFeedback.ShowApiErrorAsync(this, _album.Name, ex, $"上传失败：{ex.Message}");
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

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
