#if ANDROID
using System.Runtime.CompilerServices;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Controls;
using OpenNas.Helpers;

namespace OpenNas.Platforms.Android;

/// <summary>相册网格：关闭 item 动画 + 滚动状态通知，减轻快速滚动卡顿。</summary>
internal static class AlbumGridUiHelper
{
    private static readonly ConditionalWeakTable<RecyclerView, ScrollListener> Attached = new();

    public static void TryOptimize(CollectionView view)
    {
        if (AttachScrollListener(view))
            return;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            view.HandlerChanged -= OnHandlerChanged;
            AttachScrollListener(view);
        }

        view.HandlerChanged += OnHandlerChanged;
    }

    private static bool AttachScrollListener(CollectionView view)
    {
        if (view.Handler?.PlatformView is not RecyclerView recyclerView)
            return false;

        recyclerView.SetItemAnimator(null);

        if (Attached.TryGetValue(recyclerView, out _))
            return true;

        var listener = new ScrollListener();
        Attached.Add(recyclerView, listener);
        recyclerView.AddOnScrollListener(listener);
        return true;
    }

    private sealed class ScrollListener : RecyclerView.OnScrollListener
    {
        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            if (newState == RecyclerView.ScrollStateIdle)
                NasGridImageApplyScheduler.NotifyIdle();
            else
                NasGridImageApplyScheduler.NotifyScrolling();
        }
    }
}
#endif
