using OpenNas.Services;

namespace OpenNas.Controls;

public partial class PhotosLibrarySpaceBar : ContentView
{
    private PhotosLibraryContext? _context;
    private PhotosViewMode _viewMode = PhotosViewMode.Explore;
    private bool _forceVisible;
    private Func<bool>? _customVisibility;

    public PhotosLibrarySpaceBar()
    {
        InitializeComponent();
    }

    public void Bind(PhotosLibraryContext context, PhotosViewMode? fixedViewMode = null, Func<bool>? isVisible = null)
    {
        Unbind();

        _context = context;
        _viewMode = fixedViewMode ?? context.CurrentViewMode;
        _forceVisible = fixedViewMode.HasValue;
        _customVisibility = isVisible;

        context.ExploreLibraryChanged += OnContextChanged;
        context.TimelineLibraryChanged += OnContextChanged;
        context.AlbumFilterChanged += OnContextChanged;
        context.ViewModeChanged += OnContextChanged;

        Refresh();
    }

    public void Unbind()
    {
        if (_context == null)
            return;

        _context.ExploreLibraryChanged -= OnContextChanged;
        _context.TimelineLibraryChanged -= OnContextChanged;
        _context.AlbumFilterChanged -= OnContextChanged;
        _context.ViewModeChanged -= OnContextChanged;
        _context = null;
        _customVisibility = null;
    }

    private void OnContextChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(Refresh);

    public void RefreshState() => Refresh();

    private void Refresh()
    {
        if (_context == null)
        {
            IsVisible = false;
            return;
        }

        var mode = _forceVisible ? _viewMode : _context.CurrentViewMode;
        IsVisible = _customVisibility?.Invoke() ?? _context.IsSpaceSwitcherVisible(mode);

        var selected = _context.GetActiveLibrary(mode);
        ApplySelection(selected);
    }

    private void ApplySelection(PhotosLibrary selected)
    {
        var isPersonal = selected == PhotosLibrary.PersonalSpace;

        PersonalOption.BackgroundColor = isPersonal
            ? GetThemeColor("BrandPrimary", "BrandPrimaryLight")
            : Colors.Transparent;
        SharedOption.BackgroundColor = isPersonal
            ? Colors.Transparent
            : GetThemeColor("BrandPrimary", "BrandPrimaryLight");

        PersonalLabel.TextColor = isPersonal
            ? Colors.White
            : GetThemeColor("TextPrimary", "TextPrimaryDark");
        SharedLabel.TextColor = isPersonal
            ? GetThemeColor("TextPrimary", "TextPrimaryDark")
            : Colors.White;
    }

    private void OnPersonalTapped(object? sender, TappedEventArgs e) => SelectLibrary(PhotosLibrary.PersonalSpace);

    private void OnSharedTapped(object? sender, TappedEventArgs e) => SelectLibrary(PhotosLibrary.SharedSpace);

    private void SelectLibrary(PhotosLibrary library)
    {
        if (_context == null)
            return;

        var mode = _forceVisible ? _viewMode : _context.CurrentViewMode;
        if (_context.GetActiveLibrary(mode) == library)
            return;

        _context.SetActiveLibrary(library, mode);
    }

    private Color GetThemeColor(string lightKey, string darkKey)
    {
        var resources = Application.Current?.Resources;
        if (resources == null)
            return Colors.Gray;

        var key = Application.Current?.RequestedTheme == AppTheme.Dark ? darkKey : lightKey;
        return resources.TryGetValue(key, out var value) && value is Color color ? color : Colors.Gray;
    }
}
