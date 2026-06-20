namespace OpenNas.Core.Models;

public readonly record struct BackupSummarySnapshot(
    bool IsRunning,
    bool IsPaused,
    int Completed,
    int Total,
    int Failed,
    int? ActiveRuleId,
    string? CurrentFileName)
{
    public BackupSummarySnapshot(BackupProgressInfo p)
        : this(
            p.IsRunning,
            p.IsPaused,
            p.Completed,
            p.Total,
            p.Failed,
            p.ActiveRuleId,
            p.CurrentFileName)
    {
    }
}
