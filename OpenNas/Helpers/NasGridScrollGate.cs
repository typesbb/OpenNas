using System.Collections.Concurrent;

namespace OpenNas.Helpers;

/// <summary>
/// 相册网格滚动闸门：滚动中不刷新缩略图，停止后再批量显示。
/// </summary>
public static class NasGridScrollGate
{
    private static volatile bool _isScrolling;
    private static readonly ConcurrentQueue<Action> Deferred = new();
    private static int _flushScheduled;

    public static bool IsScrolling => _isScrolling;

    public static void NotifyScrolling() => _isScrolling = true;

    public static void NotifyIdle()
    {
        _isScrolling = false;
        ScheduleFlush();
    }

    public static void RunWhenIdle(Action action)
    {
        if (!_isScrolling)
        {
            action();
            return;
        }

        Deferred.Enqueue(action);
    }

    private static void ScheduleFlush()
    {
        if (Deferred.IsEmpty)
            return;

        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
            return;

        MainThread.BeginInvokeOnMainThread(FlushStep);
    }

    private static void FlushStep()
    {
        const int batch = 4;
        var applied = 0;
        while (applied < batch && Deferred.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch
            {
                // ignore single cell
            }

            applied++;
        }

        if (Deferred.IsEmpty || _isScrolling)
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_isScrolling && !Deferred.IsEmpty)
                ScheduleFlush();
            return;
        }

        _ = ContinueFlushAsync();
    }

    private static async Task ContinueFlushAsync()
    {
        await Task.Delay(16);
        if (_isScrolling)
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            return;
        }

        MainThread.BeginInvokeOnMainThread(FlushStep);
    }
}
