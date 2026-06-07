namespace OpenNas.Models;

public class BackupProgressInfo
{
    private int _completed;
    private int _failed;

    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public int Total { get; set; }
    public int Completed => _completed;
    public int Failed => _failed;
    public string? CurrentFileName { get; set; }
    public string? LastError { get; set; }

    public void IncrementCompleted() => Interlocked.Increment(ref _completed);
    public void IncrementFailed() => Interlocked.Increment(ref _failed);
    public void ResetCounters()
    {
        Interlocked.Exchange(ref _completed, 0);
        Interlocked.Exchange(ref _failed, 0);
    }
}
