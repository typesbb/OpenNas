using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Controls;
using OpenNas.Helpers;

namespace OpenNas.Platforms.Android;

internal static class AlbumGridScrollHelper
{
    private static readonly HashSet<int> Attached = [];

    public static void TryAttach(CollectionView view)
    {
        if (view.Handler?.PlatformView is RecyclerView recyclerView)
        {
            AttachOnce(recyclerView);
            return;
        }

        _ = AttachWhenReadyAsync(view);
    }

    private static async Task AttachWhenReadyAsync(CollectionView view)
    {
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(50);
            if (view.Handler?.PlatformView is RecyclerView recyclerView)
            {
                AttachOnce(recyclerView);
                return;
            }
        }
    }

    private static void AttachOnce(RecyclerView recyclerView)
    {
        var id = recyclerView.GetHashCode();
        if (!Attached.Add(id))
            return;

        recyclerView.SetItemAnimator(null);
        recyclerView.AddOnScrollListener(new GridScrollListener());
    }

    private sealed class GridScrollListener : RecyclerView.OnScrollListener
    {
        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            if (newState == RecyclerView.ScrollStateIdle)
                NasGridScrollGate.NotifyIdle();
            else
                NasGridScrollGate.NotifyScrolling();
        }

        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy)
        {
            if (dy != 0)
                NasGridScrollGate.NotifyScrolling();
        }
    }
}
