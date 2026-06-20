namespace OpenNas.Core.Models;

public class BackupRule
{
    public int Id { get; set; }
    public string LocalAlbumId { get; set; } = "";
    public string LocalAlbumName { get; set; } = "";
    public int RemoteAlbumId { get; set; }
    public string RemoteAlbumName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool DeleteAfterBackup { get; set; }
}
