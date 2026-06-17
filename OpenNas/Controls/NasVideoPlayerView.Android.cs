#if ANDROID
using Android.Views;
using OpenNas.Platforms.Android;
using AView = Android.Views.View;

namespace OpenNas.Controls;

public partial class NasVideoPlayerView
{
    private VideoTouchListener? _androidTouchListener;
    private AView? _androidTouchTarget;
    private bool _androidAttachScheduled;

    partial void InitializePlatform()
    {
        Loaded += OnPlatformLoaded;
        ScheduleAndroidTouchAttach();
    }

    private void OnPlatformLoaded(object? sender, EventArgs e) =>
        ScheduleAndroidTouchAttach();

    private void ScheduleAndroidTouchAttach()
    {
        if (_androidAttachScheduled)
            return;

        _androidAttachScheduled = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                for (var i = 0; i < 10 && _androidTouchListener == null; i++)
                {
                    if (TryAttachAndroidTouch())
                        break;

                    await Task.Delay(60);
                }
            }
            finally
            {
                _androidAttachScheduled = false;
            }
        });
    }

    private bool TryAttachAndroidTouch()
    {
        try
        {
            MediaPlayer.InputTransparent = true;

            var touchTarget = (TopTouchLayer.Handler?.PlatformView ?? Host.Handler?.PlatformView) as AView;
            if (touchTarget?.Context == null)
                return false;

            if (_androidTouchListener != null && ReferenceEquals(_androidTouchTarget, touchTarget))
                return true;

            if (_androidTouchTarget != null)
                _androidTouchListener?.Detach(_androidTouchTarget);

            _androidTouchListener ??= new VideoTouchListener(this);
            _androidTouchTarget = touchTarget;
            _androidTouchListener.Attach(touchTarget);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void OnNativeLongPress() => StartFastForward();

    internal void OnNativeLongPressReleased() => EndFastForward();

    internal void OnNativeDoubleTap() => TogglePlayPause();

    internal void OnNativeSlideOffset(float deltaX)
    {
        if (_isNavigating)
            return;

        SlideHost.TranslationX = ApplyHorizontalResistance(deltaX);
    }

    internal void OnNativeDismissOffset(float deltaY) =>
        DismissDrag?.Invoke(this, deltaY);

    internal void OnNativeDismissCompleted(float totalY)
    {
        if (totalY >= GetDismissThreshold() && totalY > 0)
            DismissRequested?.Invoke(this, EventArgs.Empty);
        else
            DismissDrag?.Invoke(this, 0);
    }

    internal async Task OnNativeSlideCompletedAsync(float totalX)
    {
        if (_isNavigating)
        {
            await SlideHost.TranslateTo(0, 0, 160, Easing.CubicOut);
            return;
        }

        _navPanX = totalX;
        await CompleteHorizontalPanAsync();
    }

    partial void OnPhotoReadyForPlatform() =>
        ScheduleAndroidTouchAttach();
}
#endif
