namespace OpenNas.Core.Models;

public class LocalMediaItem
{
    public string MediaStoreId { get; set; } = "";
    public string ContentUri { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Size { get; set; }
    public long DateModified { get; set; }
    public string MimeType { get; set; } = "image/jpeg";
    public bool IsVideo { get; set; }
    public string LocalAlbumId { get; set; } = "";
}
