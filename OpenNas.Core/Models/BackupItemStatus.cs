namespace OpenNas.Core.Models;

public enum BackupItemStatus
{
    Pending,
    Uploading,
    Uploaded,
    Failed,
    LocalDeleted,
    DeleteFailed
}
