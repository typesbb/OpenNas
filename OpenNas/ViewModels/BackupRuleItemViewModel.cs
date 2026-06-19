using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenNas.Models;

namespace OpenNas.ViewModels;

public class BackupRuleItemViewModel : INotifyPropertyChanged
{
    private double _progress;
    private string _progressPercentText = "";
    private string _actionIcon = "▶";
    private bool _showProgress;
    private bool _isActiveRun;
    private int _failedCount;

    public BackupRuleItemViewModel(BackupRule rule, int failedCount = 0)
    {
        Rule = rule;
        _failedCount = failedCount;
        QueueRow0 = new BackupQueueItemViewModel();
        QueueRow1 = new BackupQueueItemViewModel();
        QueueRow2 = new BackupQueueItemViewModel();
    }

    public BackupRule Rule { get; }

    public int Id => Rule.Id;

    public BackupQueueItemViewModel QueueRow0 { get; }

    public BackupQueueItemViewModel QueueRow1 { get; }

    public BackupQueueItemViewModel QueueRow2 { get; }

    public bool ShowQueueItems =>
        QueueRow0.IsActive || QueueRow1.IsActive || QueueRow2.IsActive;

    public string SummaryLine => $"{Rule.LocalAlbumName} → {Rule.RemoteAlbumName}";

    public string MetaLine =>
        $"{(Rule.Enabled ? "已启用" : "已停用")} · {(Rule.DeleteAfterBackup ? "备份完成后删本地" : "保留本地")}";

    public int FailedCount
    {
        get => _failedCount;
        private set
        {
            if (_failedCount == value) return;
            _failedCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowRetry));
        }
    }

    public bool ShowRetry => FailedCount > 0 && !_isActiveRun;

    public double Progress
    {
        get => _progress;
        private set
        {
            if (Math.Abs(_progress - value) < 0.001) return;
            _progress = value;
            OnPropertyChanged();
        }
    }

    public string ProgressPercentText
    {
        get => _progressPercentText;
        private set
        {
            if (_progressPercentText == value) return;
            _progressPercentText = value;
            OnPropertyChanged();
        }
    }

    public string ActionIcon
    {
        get => _actionIcon;
        private set
        {
            if (_actionIcon == value) return;
            _actionIcon = value;
            OnPropertyChanged();
        }
    }

    public bool ShowProgress
    {
        get => _showProgress;
        private set
        {
            if (_showProgress == value) return;
            _showProgress = value;
            OnPropertyChanged();
        }
    }

    public void SetFailedCount(int count) => FailedCount = count;

    public void ApplyEngineState(
        bool isRunning,
        bool isPaused,
        int? activeRuleId,
        int completed,
        int total)
    {
        var isThisActive = isRunning && (activeRuleId == Id || activeRuleId is null);
        if (_isActiveRun != isThisActive)
        {
            _isActiveRun = isThisActive;
            OnPropertyChanged(nameof(ShowRetry));
        }

        ShowProgress = isThisActive && total > 0;
        var pct = total > 0 ? (double)completed / total : 0;
        Progress = pct;
        ProgressPercentText = isThisActive && total > 0
            ? $"{(int)(pct * 100)}% · {completed}/{total}"
            : "";

        ActionIcon = isRunning && activeRuleId == Id
            ? (isPaused ? "▶" : "⏸")
            : "▶";
    }

    public void SyncQueueItems(IReadOnlyList<BackupQueueItem> snapshot)
    {
        var wasVisible = ShowQueueItems;
        ApplyRow(QueueRow0, snapshot, 0);
        ApplyRow(QueueRow1, snapshot, 1);
        ApplyRow(QueueRow2, snapshot, 2);

        if (ShowQueueItems != wasVisible)
            OnPropertyChanged(nameof(ShowQueueItems));
    }

    public void ClearQueueItems()
    {
        var wasVisible = ShowQueueItems;
        QueueRow0.Clear();
        QueueRow1.Clear();
        QueueRow2.Clear();

        if (wasVisible)
            OnPropertyChanged(nameof(ShowQueueItems));
    }

    private static void ApplyRow(
        BackupQueueItemViewModel row,
        IReadOnlyList<BackupQueueItem> snapshot,
        int index)
    {
        if (index >= snapshot.Count)
        {
            if (row.IsActive)
                row.Clear();
            return;
        }

        row.UpdateFrom(snapshot[index]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
