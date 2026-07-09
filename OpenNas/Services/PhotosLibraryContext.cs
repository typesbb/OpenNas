using NSynology;
using OpenNas.ViewModels;

namespace OpenNas.Services;

public enum PhotosLibrary
{
    PersonalSpace,
    SharedSpace
}

public enum PhotosViewMode
{
    Explore,
    Albums,
    Timeline
}

public enum AlbumListFilter
{
    All,
    My,
    SharedWithMe
}

public sealed class PhotosLibraryContext
{
    private const string ExploreLibraryKey = "photos_explore_library";
    private const string TimelineLibraryKey = "photos_timeline_library";
    private const string ViewModeKey = "photos_view_mode";
    private const string AlbumFilterKey = "photos_album_filter";

    public PhotosLibrary ExploreLibrary { get; private set; } = LoadLibrary(ExploreLibraryKey);
    public PhotosLibrary TimelineLibrary { get; private set; } = LoadLibrary(TimelineLibraryKey);
    public PhotosViewMode CurrentViewMode { get; private set; } = LoadViewMode();
    public AlbumListFilter CurrentAlbumFilter { get; private set; } = LoadAlbumFilter();

    public bool SharedSpaceEnabled { get; private set; }

    public event EventHandler? ExploreLibraryChanged;
    public event EventHandler? TimelineLibraryChanged;
    public event EventHandler? ViewModeChanged;
    public event EventHandler? AlbumFilterChanged;

    public static string GetViewModeTitle(PhotosViewMode mode) => mode switch
    {
        PhotosViewMode.Albums => "相册",
        PhotosViewMode.Timeline => "时间线",
        _ => "探索"
    };

    public void ApplyExploreCookies()
    {
        ApplyCookies(ExploreLibrary, "folder");
    }

    public void ApplyTimelineCookies()
    {
        ApplyCookies(TimelineLibrary, "timeline");
    }

    /// <summary>相册列表无个人/共享空间区分（HAR：<c>/album</c>）。</summary>
    public void ApplyAlbumCookies()
    {
        ApplyCookies(PhotosLibrary.PersonalSpace, "folder");
    }

    public void SetExploreLibrary(PhotosLibrary library)
    {
        if (ExploreLibrary == library)
            return;

        ExploreLibrary = library;
        Preferences.Default.Set(ExploreLibraryKey, library.ToString());
        ApplyExploreCookies();
        ExploreLibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTimelineLibrary(PhotosLibrary library)
    {
        if (TimelineLibrary == library)
            return;

        TimelineLibrary = library;
        Preferences.Default.Set(TimelineLibraryKey, library.ToString());
        ApplyTimelineCookies();
        TimelineLibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetViewMode(PhotosViewMode mode)
    {
        if (CurrentViewMode == mode)
            return;

        CurrentViewMode = mode;
        Preferences.Default.Set(ViewModeKey, mode.ToString());
        ViewModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAlbumFilter(AlbumListFilter filter)
    {
        filter = NormalizeAlbumFilter(filter);
        if (CurrentAlbumFilter == filter)
            return;

        CurrentAlbumFilter = filter;
        Preferences.Default.Set(AlbumFilterKey, filter.ToString());
        AlbumFilterChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshTeamSpaceSettingsAsync(CancellationToken cancellationToken = default)
    {
        var client = SynologyManager.Client;
        if (client == null || string.IsNullOrEmpty(client.Sid))
        {
            SharedSpaceEnabled = false;
            return;
        }

        try
        {
            var settings = await client.Foto.GetTeamSpaceSettingsAsync(cancellationToken);
            SharedSpaceEnabled = settings?.Enabled ?? false;
            if (!SharedSpaceEnabled)
            {
                if (ExploreLibrary == PhotosLibrary.SharedSpace)
                    SetExploreLibrary(PhotosLibrary.PersonalSpace);
                if (TimelineLibrary == PhotosLibrary.SharedSpace)
                    SetTimelineLibrary(PhotosLibrary.PersonalSpace);
            }
        }
        catch
        {
            SharedSpaceEnabled = false;
        }
    }

    public static string MapAlbumCategory(AlbumListFilter filter) => filter switch
    {
        AlbumListFilter.My => "normal",
        AlbumListFilter.SharedWithMe => "normal_share_with_me",
        _ => "all"
    };

    public static string MapAlbumDisplayType(AlbumListFilter filter) => filter switch
    {
        AlbumListFilter.My => "my_album",
        _ => "all_album"
    };

    public static string GetAlbumFilterTitle(AlbumListFilter filter) => filter switch
    {
        AlbumListFilter.My => "我的",
        AlbumListFilter.SharedWithMe => "与我共享",
        _ => "我的"
    };

    public static AlbumListFilter NormalizeAlbumFilter(AlbumListFilter filter) =>
        filter == AlbumListFilter.All ? AlbumListFilter.My : filter;

    public static bool CategorySupportsSharedSpace(PhotosBrowseCategory category) => category switch
    {
        PhotosBrowseCategory.Person or PhotosBrowseCategory.Concept
            or PhotosBrowseCategory.Geocoding or PhotosBrowseCategory.GeneralTag => true,
        _ => false
    };

    public static string GetLibraryTitle(PhotosLibrary library) =>
        library == PhotosLibrary.SharedSpace ? "共享空间" : "个人空间";

    private static void ApplyCookies(PhotosLibrary library, string viewType)
    {
        var client = SynologyManager.Client;
        if (client == null)
            return;

        var cookie = library == PhotosLibrary.SharedSpace ? "shared_space" : "personal_space";
        client.ApplyPhotosWebCookies(cookie, viewType);
    }

    private static PhotosLibrary LoadLibrary(string key)
    {
        try { return Enum.Parse<PhotosLibrary>(Preferences.Default.Get(key, nameof(PhotosLibrary.PersonalSpace))); }
        catch { return PhotosLibrary.PersonalSpace; }
    }

    private static PhotosViewMode LoadViewMode()
    {
        try { return Enum.Parse<PhotosViewMode>(Preferences.Default.Get(ViewModeKey, nameof(PhotosViewMode.Explore))); }
        catch { return PhotosViewMode.Explore; }
    }

    private static AlbumListFilter LoadAlbumFilter()
    {
        try
        {
            var saved = Enum.Parse<AlbumListFilter>(
                Preferences.Default.Get(AlbumFilterKey, nameof(AlbumListFilter.My)));
            return NormalizeAlbumFilter(saved);
        }
        catch { return AlbumListFilter.My; }
    }
}
