using OpenNas.Models;
using SQLite;

namespace OpenNas.Data;

[Table("backup_items")]
public class BackupRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int RuleId { get; set; }
    public string LocalMediaId { get; set; } = "";
    public string ContentUri { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public long DateModified { get; set; }
    public BackupItemStatus Status { get; set; }
    public string? LastError { get; set; }
    public int RemotePhotoId { get; set; }
    public DateTime? UploadedAt { get; set; }
}
