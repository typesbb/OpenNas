using OpenNas.Services;

namespace OpenNas.ViewModels;

public sealed class AlbumsPageViewModel(PhotosLibraryContext libraryContext)
{
    private readonly PhotosLibraryContext _libraryContext = libraryContext;

    public PhotosLibraryContext LibraryContext => _libraryContext;

    public string ViewModeTitle => PhotosLibraryContext.GetViewModeTitle(_libraryContext.CurrentViewMode);

    public bool CanCreateAlbum => _libraryContext.CurrentViewMode == PhotosViewMode.Albums;

    public bool ShowAlbumFilterButton => _libraryContext.CurrentViewMode == PhotosViewMode.Albums;

    public bool ShowSortButton => _libraryContext.CurrentViewMode == PhotosViewMode.Albums;

    public bool ShowTimelineSpaceButton =>
        _libraryContext.CurrentViewMode == PhotosViewMode.Timeline && _libraryContext.SharedSpaceEnabled;

    public string AlbumFilterTitle =>
        PhotosLibraryContext.GetAlbumFilterTitle(_libraryContext.CurrentAlbumFilter);

    public string TimelineLibraryTitle =>
        PhotosLibraryContext.GetLibraryTitle(_libraryContext.TimelineLibrary);

    public void RefreshTitles()
    {
        OnPropertyChanged(nameof(ViewModeTitle));
        OnPropertyChanged(nameof(CanCreateAlbum));
        OnPropertyChanged(nameof(ShowAlbumFilterButton));
        OnPropertyChanged(nameof(ShowSortButton));
        OnPropertyChanged(nameof(ShowTimelineSpaceButton));
        OnPropertyChanged(nameof(AlbumFilterTitle));
        OnPropertyChanged(nameof(TimelineLibraryTitle));
    }

    public event EventHandler<string>? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, name);
}
