namespace OpenNas.ViewModels;

public enum PhotosBrowseCategory
{
    RecentlyAdded,
    Person,
    Concept,
    Geocoding,
    GeneralTag,
    Video
}

public sealed class ExploreSectionViewModel
{
    public PhotosBrowseCategory Category { get; init; }
    public string Title { get; init; } = "";
    public bool IsVisible { get; set; } = true;
    public IReadOnlyList<object> PreviewItems { get; set; } = [];
}
