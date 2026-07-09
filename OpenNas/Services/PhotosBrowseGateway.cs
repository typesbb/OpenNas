using NSynology;
using NSynology.Foto;
using OpenNas.ViewModels;

namespace OpenNas.Services;

/// <summary>
/// 个人空间走 <c>SYNO.Foto.Browse.*</c>，共享空间走 <c>SYNO.FotoTeam.Browse.*</c>（与官方 App 抓包一致）。
/// </summary>
public static class PhotosBrowseGateway
{
    public static bool UsesSharedTeamApi(PhotosLibrary library) =>
        library == PhotosLibrary.SharedSpace;

    public static Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(
        SynologyClient client,
        CancellationToken cancellationToken = default) =>
        client.FotoBrowse.GetCategoriesAsync(cancellationToken);

    public static Task<IReadOnlyList<Photo>> ListRecentlyAddedAsync(
        SynologyClient client,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        client.FotoBrowse.ListRecentlyAddedAsync(offset, limit, cancellationToken);

    public static Task<IReadOnlyList<Photo>> ListVideosAsync(
        SynologyClient client,
        PhotosLibrary library,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        UsesSharedTeamApi(library)
            ? client.FotoTeam.ListVideosAsync(offset, limit, cancellationToken)
            : client.FotoBrowse.ListVideosAsync(offset, limit, cancellationToken);

    public static Task<IReadOnlyList<Photo>> ListPersonPhotosAsync(
        SynologyClient client,
        PhotosLibrary library,
        int personId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        UsesSharedTeamApi(library)
            ? client.FotoTeam.ListPersonPhotosAsync(personId, offset, limit, cancellationToken)
            : client.FotoBrowse.ListPersonPhotosAsync(personId, offset, limit, cancellationToken);

    public static Task<IReadOnlyList<BrowseAlbumItem>> ListBrowseItemsAsync(
        SynologyClient client,
        PhotosLibrary library,
        PhotosBrowseCategory category,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (UsesSharedTeamApi(library))
        {
            return category switch
            {
                PhotosBrowseCategory.Person => client.FotoTeam.ListPersonsAsync(offset, limit, true, cancellationToken),
                PhotosBrowseCategory.Concept => client.FotoTeam.ListConceptsAsync(offset, limit, cancellationToken),
                PhotosBrowseCategory.Geocoding => client.FotoTeam.ListGeocodingsAsync(offset, limit, cancellationToken),
                PhotosBrowseCategory.GeneralTag => client.FotoTeam.ListGeneralTagsAsync(offset, limit, cancellationToken),
                _ => Task.FromResult<IReadOnlyList<BrowseAlbumItem>>([])
            };
        }

        return category switch
        {
            PhotosBrowseCategory.Person => client.FotoBrowse.ListPersonsAsync(offset, limit, true, cancellationToken),
            PhotosBrowseCategory.Concept => client.FotoBrowse.ListConceptsAsync(offset, limit, cancellationToken),
            PhotosBrowseCategory.Geocoding => client.FotoBrowse.ListGeocodingsAsync(offset, limit, cancellationToken),
            PhotosBrowseCategory.GeneralTag => client.FotoBrowse.ListGeneralTagsAsync(offset, limit, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<BrowseAlbumItem>>([])
        };
    }
}
