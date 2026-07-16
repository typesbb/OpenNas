#if ANDROID
using Android.Views;
using Android.Widget;
using OpenNas.Platforms.Android;
using OpenNas.Services;
using AView = Android.Views.View;

namespace OpenNas.Controls;

public partial class ZoomableImageView
{
    private PhotoZoomTouchListener? _androidTouchListener;
    private AView? _androidTouchTarget;
    private ImageView? _androidImageView;
    private bool _androidAttachScheduled;

    partial void InitializePlatform()
    {
        PhotoImage.PropertyChanged += OnPhotoImagePropertyChanged;
        ScheduleAndroidTouchAttach();
    }

    private void OnPlatformLoaded(object? sender, EventArgs e) =>
        ScheduleAndroidTouchAttach();

    private void OnPhotoImagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != Image.SourceProperty.PropertyName)
            return;

        ScheduleAndroidTouchAttach();
        if (_androidTouchListener != null && _containerWidth > 1 && _containerHeight > 1)
            MainThread.BeginInvokeOnMainThread(() => _androidTouchListener.PrepareDisplay());
    }

    private void ScheduleAndroidTouchAttach()
    {
        if (_androidAttachScheduled)
            return;

        _androidAttachScheduled = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                for (var i = 0; i < 8 && !ManagedGesturesEnabled && _androidTouchListener == null; i++)
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
            if (PhotoImage.Handler?.PlatformView is not ImageView imageView)
                return false;

            var touchTarget = (TouchLayer.Handler?.PlatformView ?? Host.Handler?.PlatformView) as AView;
            if (touchTarget?.Context == null)
                return false;

            if (_androidTouchListener != null
                && ReferenceEquals(_androidTouchTarget, touchTarget)
                && ReferenceEquals(_androidImageView, imageView))
                return true;

            if (_androidTouchTarget != null)
                _androidTouchListener?.Detach(_androidTouchTarget);

            _androidTouchListener ??= new PhotoZoomTouchListener(this);
            _androidTouchTarget = touchTarget;
            _androidImageView = imageView;
            _androidTouchListener.Attach(imageView, touchTarget);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("大图触摸初始化失败，回退托管手势", ex);
            ManagedGesturesEnabled = true;
            EnableManagedGestures();
            return true;
        }
    }

    internal void NotifyNativeZoomChanged(bool isZoomed)
    {
        if (ManagedGesturesEnabled)
            return;

        _nativeZoomed = isZoomed;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OnNativeSlideOffset(float deltaY)
    {
        if (_nativeZoomed || _isNavigating)
            return;

        SlideHost.TranslationY = ApplyVerticalResistance(deltaY);
    }

    internal async Task OnNativeSlideCompletedAsync(float totalY)
    {
        if (_nativeZoomed || _isNavigating)
        {
            await SlideHost.TranslateToAsync(0, 0, 160, Easing.CubicOut);
            return;
        }

        _navPanY = totalY;
        await CompleteVerticalPanAsync();
    }

    partial void ResetPlatformTransform()
    {
        if (!ManagedGesturesEnabled)
            _androidTouchListener?.PrepareDisplay();
    }
}
#endif
