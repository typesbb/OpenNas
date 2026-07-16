using Android.Runtime;
using Android.Views;
using OpenNas.Controls;
using AView = Android.Views.View;

namespace OpenNas.Platforms.Android;

[Preserve(AllMembers = true)]
public sealed class VideoScaleListener : Java.Lang.Object, ScaleGestureDetector.IOnScaleGestureListener
{
    private readonly VideoTouchListener _owner;

    public VideoScaleListener(VideoTouchListener owner) => _owner = owner;

    public bool OnScale(ScaleGestureDetector detector)
    {
        _owner.OnScale(detector);
        return true;
    }

    public bool OnScaleBegin(ScaleGestureDetector detector)
    {
        _owner.OnScaleBegin();
        return true;
    }

    public void OnScaleEnd(ScaleGestureDetector detector) => _owner.OnScaleEnd();
}

[Preserve(AllMembers = true)]
public sealed class VideoGestureListener : GestureDetector.SimpleOnGestureListener
{
    private readonly VideoTouchListener _owner;

    public VideoGestureListener(VideoTouchListener owner) => _owner = owner;

    public override void OnLongPress(MotionEvent e) => _owner.OnLongPress();

    // 视频已无双击逻辑，用 SingleTapUp 避免 OnSingleTapConfirmed 的约 300ms 延迟。
    public override bool OnSingleTapUp(MotionEvent e)
    {
        _owner.OnSingleTap();
        return true;
    }
}

[Preserve(AllMembers = true)]
public sealed class VideoTouchListener : Java.Lang.Object, AView.IOnTouchListener
{
    private const int ModeNone = 0;
    private const int ModeNav = 1;
    private const int ModeZoomDrag = 2;

    private readonly NasVideoPlayerView _view;
    private GestureDetector? _gestureDetector;
    private ScaleGestureDetector? _scaleDetector;
    private AView? _touchTarget;
    private float _density = 1f;
    private int _mode = ModeNone;
    private float _startX;
    private float _startY;
    private bool _longPressActive;
    private bool _gestureConsumed;
    private bool _isPinching;

    public VideoTouchListener(NasVideoPlayerView view) => _view = view;

    /// <summary>Android 触点是像素，MAUI Translation/Scale 使用 DIP，需换算。</summary>
    private float ToDip(float pixels) => pixels / _density;

    public void Attach(AView touchTarget)
    {
        var context = touchTarget.Context
            ?? throw new InvalidOperationException("Touch target context is null.");

        _touchTarget = touchTarget;
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        _density = density > 0.01f ? density : 1f;
        _gestureDetector = new GestureDetector(context, new VideoGestureListener(this));
        _scaleDetector = new ScaleGestureDetector(context, new VideoScaleListener(this));
        touchTarget.SetOnTouchListener(this);
        touchTarget.Clickable = true;
        touchTarget.Focusable = true;
    }

    public void Detach(AView touchTarget)
    {
        touchTarget.SetOnTouchListener(null);
        _touchTarget = null;
        _gestureDetector = null;
        _scaleDetector = null;
        _mode = ModeNone;
        _longPressActive = false;
        _gestureConsumed = false;
        _isPinching = false;
    }

    internal void OnLongPress()
    {
        if (_gestureConsumed || _mode != ModeNone || _view.IsZoomed || _isPinching)
            return;

        _longPressActive = true;
        _gestureConsumed = true;
        _view.OnNativeLongPress();
    }

    internal void OnSingleTap()
    {
        if (_view.IsZoomed)
            return;

        _view.OnNativeSingleTap();
    }

    internal void OnScaleBegin()
    {
        _isPinching = true;
        _mode = ModeNone;
        _gestureConsumed = true;
        _longPressActive = false;
    }

    internal void OnScale(ScaleGestureDetector detector)
    {
        var factor = detector.ScaleFactor;
        if (float.IsNaN(factor) || float.IsInfinity(factor) || Math.Abs(factor - 1f) < 0.001f)
            return;

        // FocusX/Y 是像素；换算到 DIP 后与 MAUI 布局尺寸一致，缩放才能贴合双指。
        var focalX = _view.ToFocalOffsetFromPixels(
            ToDip(detector.FocusX),
            ToDip(detector.FocusY),
            out var focalY);
        _view.ApplyZoomFactor(factor, focalX, focalY);
    }

    internal void OnScaleEnd()
    {
        _isPinching = false;
        if (!_view.IsZoomed)
            _view.ResetZoomFromNative();
    }

    public bool OnTouch(AView? v, MotionEvent? e)
    {
        if (v == null || e == null)
            return false;

        try
        {
            _scaleDetector?.OnTouchEvent(e);
            _gestureDetector?.OnTouchEvent(e);

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _mode = _view.IsZoomed ? ModeZoomDrag : ModeNone;
                    _gestureConsumed = false;
                    _longPressActive = false;
                    _startX = ToDip(e.GetX());
                    _startY = ToDip(e.GetY());
                    if (_mode == ModeZoomDrag)
                        _view.BeginZoomPan();
                    break;

                case MotionEventActions.Move:
                    if (_isPinching || _longPressActive || (_gestureConsumed && _mode != ModeZoomDrag))
                        break;

                    var dx = ToDip(e.GetX()) - _startX;
                    var dy = ToDip(e.GetY()) - _startY;

                    if (_mode == ModeZoomDrag)
                    {
                        _view.UpdateZoomPan(dx, dy);
                        break;
                    }

                    if (_mode == ModeNone)
                    {
                        // 上下滑动切换；放大时仅拖移画面。
                        if (Math.Abs(dy) > 20 && Math.Abs(dy) > Math.Abs(dx) * 1.15)
                            _mode = ModeNav;
                    }

                    if (_mode == ModeNav)
                        _view.OnNativeSlideOffset(dy);
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_longPressActive)
                        _view.OnNativeLongPressReleased();

                    if (_mode == ModeZoomDrag)
                    {
                        _view.FinishZoomPan();
                    }
                    else if (_mode == ModeNav)
                    {
                        var totalY = ToDip(e.GetY()) - _startY;
                        _ = _view.OnNativeSlideCompletedAsync(totalY);
                    }

                    _mode = ModeNone;
                    _longPressActive = false;
                    _gestureConsumed = false;
                    break;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }
}
