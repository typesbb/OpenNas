using OpenNas.Services;

namespace OpenNas.ViewModels;

public sealed class AlbumsPageViewModel(PhotosLibraryContext libraryContext)
{
    private readonly PhotosLibraryContext _libraryContext = libraryContext;

    public PhotosLibraryContext LibraryContext => _libraryContext;

    public string ViewModeTitle => PhotosLibraryContext.GetViewModeTitle(_libraryContext.CurrentViewMode);

    public bool CanCreateAlbum => _libraryContext.CurrentViewMode == PhotosViewMode.Albums;

    public bool ShowSortButton => _libraryContext.CurrentViewMode == PhotosViewMode.Albums;

    public void RefreshTitles()
    {
        OnPropertyChanged(nameof(ViewModeTitle));
        OnPropertyChanged(nameof(CanCreateAlbum));
        OnPropertyChanged(nameof(ShowSortButton));
    }

    public event EventHandler<string>? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, name);
}
