using System.Collections.ObjectModel;

using NSynology;

using NSynology.Foto;

using OpenNas.Helpers;

using OpenNas.Services;

namespace OpenNas.Views;

public partial class TimelineView : ContentView
{
    private const int InitialSectionLoadCount = 2;
    private const int SectionLoadBatch = 2;

    private ConnectionService? _connection;
    private PhotosLibraryContext? _libraryContext;
    private readonly ObservableCollection<TimelinePhotoGroup> _groups = [];
    private readonly List<PendingTimelineSection> _pendingSections = [];
    private readonly List<Photo> _flatPhotos = [];
    private bool _loading;
    private bool _loadingSections;
    private int _nextSectionIndex;
    private PhotosLibrary? _loadedForLibrary;

    public void InvalidateCache() => _loadedForLibrary = null;

    public void Bind(ConnectionService connection, PhotosLibraryContext libraryContext)
    {
        _connection = connection;
        _libraryContext = libraryContext;
        _libraryContext.TimelineLibraryChanged += OnTimelineLibraryChanged;
        TimelineCollection.ItemsSource = _groups;
    }

    public TimelineView()
    {
        InitializeComponent();
        RefreshHost.Refreshing += OnPullRefreshing;
    }

    private void OnTimelineLibraryChanged(object? sender, EventArgs e)
    {
        InvalidateCache();
        if (!IsVisible)
            return;

        MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync(force: true));
    }

    private async void OnPullRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync(force: true, showBusyIndicator: false);
        RefreshHost.IsRefreshing = false;
    }

    public async Task RefreshAsync(bool force = false, bool showBusyIndicator = true)
    {
        var library = _libraryContext?.TimelineLibrary ?? PhotosLibrary.PersonalSpace;
        if (!force && _groups.Count > 0 && _loadedForLibrary == library)
            return;

        if (_loading)
            return;

        if (_connection != null)
            await _connection.EnsureBestEndpointAsync();

        _loading = true;
        if (showBusyIndicator)
        {
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
        }

        try
        {
            var client = SynologyManager.Client;
            if (client == null || string.IsNullOrEmpty(client.Sid))
            {
                _groups.Clear();
                _pendingSections.Clear();
                _flatPhotos.Clear();
                return;
            }

            _libraryContext?.ApplyTimelineCookies();
            if (IsVisible)
                PhotosMediaLibraryScope.Current = _libraryContext?.TimelineLibrary ?? PhotosLibrary.PersonalSpace;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            TimelineData? timeline = _libraryContext?.TimelineLibrary == PhotosLibrary.SharedSpace
                ? await client.FotoTeam.GetTimelineAsync(cancellationToken: cts.Token)
                : await client.FotoBrowse.GetTimelineAsync(cancellationToken: cts.Token);

            _groups.Clear();
            _pendingSections.Clear();
            _flatPhotos.Clear();
            _nextSectionIndex = 0;

            if (timeline?.Sections == null)
                return;

            foreach (var section in timeline.Sections)
            {
                var groups = new List<TimelinePhotoGroup>();
                foreach (var day in section.List)
                {
                    if (day.ItemCount <= 0)
                        continue;

                    var label = PhotoDateHelper.FormatGroupLabel(
                        new DateTimeOffset(new DateTime(day.Year, day.Month, day.Day)).ToUnixTimeSeconds());
                    groups.Add(new TimelinePhotoGroup(label, day.Year, day.Month, day.Day, day.ItemCount));
                }

                if (groups.Count == 0)
                    continue;

                // HAR：start_time 取 section 内最早一天（list 按新→旧排列，最后一项最早）
                var oldest = groups[^1];
                var (startTime, _) = FotoBrowseApi.TimelineDayRangeUnix(oldest.Year, oldest.Month, oldest.Day);
                _pendingSections.Add(new PendingTimelineSection
                {
                    Limit = Math.Max(section.Limit, groups.Sum(g => g.ExpectedCount)),
                    StartTimeUnix = startTime,
                    Groups = groups
                });
            }

            await LoadNextSectionBatchAsync(Math.Min(InitialSectionLoadCount, _pendingSections.Count));
            _loadedForLibrary = library;
        }
        catch (Exception ex)
        {
            AppLog.Error("加载时间线失败", ex);
        }
        finally
        {
            if (showBusyIndicator)
            {
                BusyIndicator.IsRunning = false;
                BusyIndicator.IsVisible = false;
            }
            _loading = false;
        }
    }

    private async void OnLoadMore(object? sender, EventArgs e)
    {
        if (_loadingSections || _nextSectionIndex >= _pendingSections.Count)
            return;

        await LoadNextSectionBatchAsync(SectionLoadBatch);
    }

    private async Task LoadNextSectionBatchAsync(int count)
    {
        if (_connection == null || count <= 0)
            return;

        _loadingSections = true;
        var client = SynologyManager.Client;
        if (client == null)
        {
            _loadingSections = false;
            return;
        }

        try
        {
            var end = Math.Min(_nextSectionIndex + count, _pendingSections.Count);
            for (var i = _nextSectionIndex; i < end; i++)
            {
                var section = _pendingSections[i];
                if (section.IsLoaded)
                    continue;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                IReadOnlyList<Photo> rawPhotos = _libraryContext?.TimelineLibrary == PhotosLibrary.SharedSpace
                    ? await client.FotoTeam.ListTimelineSectionPhotosAsync(
                        section.StartTimeUnix, 0, section.Limit, cancellationToken: cts.Token)
                    : await client.FotoBrowse.ListTimelineSectionPhotosAsync(
                        section.StartTimeUnix, 0, section.Limit, cancellationToken: cts.Token);

                DistributePhotosToGroups(rawPhotos, section.Groups);

                var expectedTotal = section.Groups.Sum(g => g.ExpectedCount);
                var loadedTotal = section.Groups.Sum(g => g.Count);
                if (loadedTotal < expectedTotal)
                    await FillMissingGroupsPerDayAsync(client, section.Groups, cts.Token);

                foreach (var group in section.Groups)
                {
                    group.IsLoaded = true;
                    _groups.Add(group);
                    _flatPhotos.AddRange(group);
                }

                section.IsLoaded = true;
            }

            _nextSectionIndex = end;
        }
        catch (Exception ex)
        {
            AppLog.Error("加载时间线照片失败", ex);
        }
        finally
        {
            _loadingSections = false;
        }
    }

    private async Task FillMissingGroupsPerDayAsync(
        SynologyClient client,
        IReadOnlyList<TimelinePhotoGroup> groups,
        CancellationToken cancellationToken)
    {
        foreach (var group in groups)
        {
            if (group.Count >= group.ExpectedCount)
                continue;

            var limit = Math.Max(group.ExpectedCount, 1);
            IReadOnlyList<Photo> dayPhotos = _libraryContext?.TimelineLibrary == PhotosLibrary.SharedSpace
                ? await client.FotoTeam.ListTimelineDayPhotosAsync(
                    group.Year, group.Month, group.Day, 0, limit, cancellationToken: cancellationToken)
                : await client.FotoBrowse.ListTimelineDayPhotosAsync(
                    group.Year, group.Month, group.Day, 0, limit, cancellationToken: cancellationToken);

            foreach (var photo in dayPhotos)
            {
                if (group.Any(p => p.Id == photo.Id))
                    continue;

                group.Add(photo);
            }
        }
    }

    private static void DistributePhotosToGroups(IReadOnlyList<Photo> photos, IReadOnlyList<TimelinePhotoGroup> groups)
    {
        var lookup = groups.ToDictionary(g => (g.Year, g.Month, g.Day));
        foreach (var photo in photos)
        {
            if (!PhotoDateHelper.TryGetLocalDay(photo, out var year, out var month, out var day))
                continue;

            if (!lookup.TryGetValue((year, month, day), out var group))
                continue;

            if (group.Any(p => p.Id == photo.Id))
                continue;

            group.Add(photo);
        }
    }

    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not Photo photo || _connection == null)
            return;

        var index = _flatPhotos.FindIndex(p => p.Id == photo.Id);
        if (index < 0)
            index = 0;

        PhotosAlbumMediaScope.Clear();
        var thumbBytes = GridThumbnailCapture.TryCapture(sender, photo);
        NasThumbnailLoader.TryFindCachedThumbnailPath(photo, out var thumbPath);
        await ShellNavigation.PushModalAsync(new PhotoViewerPage(
            _flatPhotos, index, _connection,
            _libraryContext?.TimelineLibrary ?? PhotosLibrary.PersonalSpace,
            seedThumbnailPath: thumbPath,
            seedThumbnailBytes: thumbBytes));
    }

    private sealed class PendingTimelineSection
    {
        public required int Limit { get; init; }
        public required long StartTimeUnix { get; init; }
        public required List<TimelinePhotoGroup> Groups { get; init; }
        public bool IsLoaded { get; set; }
    }
}
