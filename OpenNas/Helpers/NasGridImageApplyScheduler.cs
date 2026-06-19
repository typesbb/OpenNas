using System.Collections.Concurrent;
using OpenNas.Services;

namespace OpenNas.Helpers;

/// <summary>滚动中推迟缩略图加载/上屏，避免在 RecyclerView bind 路径上阻塞主线程。</summary>
internal static class NasGridImageApplyScheduler
{
    private static volatile bool _isScrolling;
    private static readonly ConcurrentQueue<Action> _pending = new();
    private static int _flushScheduled;

    public static bool IsScrolling => _isScrolling;

    public static void NotifyScrolling() => _isScrolling = true;

    public static void NotifyIdle()
    {
        _isScrolling = false;
        // 滚出 RecyclerView 回调栈后再开始 flush，避免在 onScrollStateChanged 路径上卡顿。
        MainThread.BeginInvokeOnMainThread(ScheduleFlush);
    }

    /// <summary>永远推迟到 bind 调用栈之外：滚动中直接入队，避免主线程消息队列堆积。</summary>
    public static void ScheduleLoad(Action action)
    {
        if (_isScrolling)
        {
            _pending.Enqueue(action);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => RunWhenIdle(action));
    }

    public static void RunWhenIdle(Action action)
    {
        if (_isScrolling)
        {
            _pending.Enqueue(action);
            return;
        }

        RunOnMainThread(action);
    }

    private static void RunOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    private static void ScheduleFlush()
    {
        if (_pending.IsEmpty)
            return;

        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
            return;

        MainThread.BeginInvokeOnMainThread(FlushStep);
    }

    private static void FlushStep()
    {
        const int batch = 2;
        var applied = 0;
        while (applied < batch && _pending.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Debug("网格缩略图延迟任务失败", ex);
            }

            applied++;
        }

        if (_pending.IsEmpty || _isScrolling)
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_isScrolling && !_pending.IsEmpty)
                ScheduleFlush();
            return;
        }

        // 每帧少量上屏，避免停下瞬间主线程长时间阻塞。
        MainThread.BeginInvokeOnMainThread(FlushStep);
    }
}
