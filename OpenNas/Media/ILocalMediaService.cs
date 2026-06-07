using OpenNas.Models;

namespace OpenNas.Media;

public interface ILocalMediaService
{
    Task<IReadOnlyList<LocalAlbumInfo>> GetLocalAlbumsAsync();
    Task<IReadOnlyList<LocalMediaItem>> GetMediaItemsAsync(string albumId);
    Task<bool> DeleteMediaAsync(string contentUri);
}

public class LocalAlbumInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int ItemCount { get; set; }
}
