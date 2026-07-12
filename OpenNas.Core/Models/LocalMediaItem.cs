namespace OpenNas.Core.Models;

public class LocalMediaItem
{
    public string MediaStoreId { get; set; } = "";
    public string ContentUri { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Size { get; set; }
    /// <summary>MediaStore <c>DATE_TAKEN</c>（毫秒）或 0。</summary>
    public long DateTaken { get; set; }
    /// <summary>MediaStore <c>DATE_MODIFIED</c>（秒）。</summary>
    public long DateModified { get; set; }
    public string MimeType { get; set; } = "image/jpeg";
    public bool IsVideo { get; set; }
    public string LocalAlbumId { get; set; } = "";
}
