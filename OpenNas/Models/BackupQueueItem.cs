namespace OpenNas.Models;

public enum BackupQueueStage
{
    Loading,
    Ready,
    Uploading
}

public class BackupQueueItem
{
    public int SlotIndex { get; init; }
    public string Key { get; init; } = "";
    public string FileName { get; init; } = "";
    public string ContentUri { get; init; } = "";
    public int RuleId { get; init; }
    public BackupQueueStage Stage { get; set; } = BackupQueueStage.Loading;
    public double Progress { get; set; }

    public string StatusText => Stage switch
    {
        BackupQueueStage.Loading => "准备中",
        BackupQueueStage.Ready => "待上传",
        BackupQueueStage.Uploading => "上传中",
        _ => ""
    };

    public bool ShowRowProgress => true;

    public bool ShowStatusText => true;

    public int StatusOrder => Stage switch
    {
        BackupQueueStage.Uploading => 0,
        BackupQueueStage.Ready => 1,
        BackupQueueStage.Loading => 2,
        _ => 3
    };
}
