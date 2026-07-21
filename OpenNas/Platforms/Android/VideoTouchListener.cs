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
    private const int ModeSeek = 2;
    private const int ModeZoomDrag = 3;

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
        AbortActiveSeekOrNav(applySeek: false);
        _mode = ModeNone;
        _longPressActive = false;
        _gestureConsumed = false;
        _isPinching = false;
    }

    internal void OnLongPress()
    {
        // 缩放态也要能长按 3 倍速；仅在已进入切换/seek/捏合时忽略。
        if (_gestureConsumed || _isPinching)
            return;
        if (_mode is ModeNav or ModeSeek)
            return;

        _longPressActive = true;
        _gestureConsumed = true;
        _mode = ModeNone;
        _view.OnNativeLongPress();
    }

    internal void OnSingleTap()
    {
        // 滑切换/seek 松手时 GestureDetector 仍可能报 SingleTapUp，需忽略。
        // 缩放后也要能唤出控件，不再因 IsZoomed 直接丢掉点击。
        if (_mode == ModeNav || _mode == ModeSeek || _longPressActive)
            return;

        _view.OnNativeSingleTap();
    }

    internal void OnScaleBegin()
    {
        // 捏合会清掉 ModeSeek；必须先结束手势 seek，否则 _isGestureSeeking 卡住。
        AbortActiveSeekOrNav(applySeek: true);
        _isPinching = true;
        _mode = ModeNone;
        _gestureConsumed = true;
        if (_longPressActive)
        {
            _longPressActive = false;
            _view.OnNativeLongPressReleased();
        }

        // 捏合一开始就藏状态栏（改 Modal Dialog.Window）。
        FullscreenOrientationHelper.SetZoomImmersive(true);
        _view.NotifyPinchStarted();
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
        {
            _view.ResetZoomFromNative();
        }
        else
        {
            // 结束捏合但保持放大：同步一次 zoom 状态（清掉仅 pinch 的临时态由控件处理）
            _view.NotifyPinchEnded();
        }
    }

    /// <summary>手势模式被中断时，清掉可能残留的 seek / 竖滑状态。</summary>
    private void AbortActiveSeekOrNav(bool applySeek)
    {
        if (_mode == ModeSeek || _view.IsGestureSeeking)
        {
            if (applySeek)
                _ = _view.EndGestureSeekAsync();
            else
                _view.CancelGestureSeek();
        }

        if (_mode == ModeNav)
            _ = _view.OnNativeSlideCompletedAsync(0);
        if (_mode == ModeZoomDrag)
            _view.FinishZoomPan();
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
                    // 上一轮若漏掉 Up/Cancel，先清掉卡住的 seek。
                    AbortActiveSeekOrNav(applySeek: false);
                    _mode = ModeNone;
                    _gestureConsumed = false;
                    _longPressActive = false;
                    _startX = ToDip(e.GetX());
                    _startY = ToDip(e.GetY());
                    break;

                case MotionEventActions.PointerDown:
                    // 第二指介入：结束单指 seek/nav，交给捏合。
                    AbortActiveSeekOrNav(applySeek: true);
                    _mode = ModeNone;
                    break;

                case MotionEventActions.Move:
                    if (_isPinching || _longPressActive)
                        break;

                    var dx = ToDip(e.GetX()) - _startX;
                    var dy = ToDip(e.GetY()) - _startY;

                    if (_view.IsZoomed)
                    {
                        // 缩放态：单指拖动画布；长按仍可 3 倍速。
                        if (_mode == ModeNone && (Math.Abs(dx) > 12 || Math.Abs(dy) > 12))
                        {
                            _mode = ModeZoomDrag;
                            _view.BeginZoomPan();
                        }

                        if (_mode == ModeZoomDrag)
                            _view.UpdateZoomPan(dx, dy);
                        break;
                    }

                    if (_gestureConsumed)
                        break;

                    if (_mode == ModeNone)
                    {
                        // 上下切换媒体；左右相对滑动调节进度（非绝对位置映射）。
                        if (Math.Abs(dy) > 20 && Math.Abs(dy) > Math.Abs(dx) * 1.15)
                            _mode = ModeNav;
                        else if (Math.Abs(dx) > 20 && Math.Abs(dx) > Math.Abs(dy) * 1.15
                                 && _view.BeginGestureSeek())
                            _mode = ModeSeek;
                    }

                    if (_mode == ModeNav)
                        _view.OnNativeSlideOffset(dy);
                    else if (_mode == ModeSeek)
                        _view.UpdateGestureSeek(dx);
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                case MotionEventActions.PointerUp:
                    if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
                    {
                        if (_longPressActive)
                            _view.OnNativeLongPressReleased();

                        if (_mode == ModeZoomDrag)
                            _view.FinishZoomPan();
                        else if (_mode == ModeNav)
                            _ = _view.OnNativeSlideCompletedAsync(ToDip(e.GetY()) - _startY);
                        else if (_mode == ModeSeek || _view.IsGestureSeeking)
                            _ = _view.EndGestureSeekAsync();

                        _mode = ModeNone;
                        _longPressActive = false;
                        _gestureConsumed = false;
                    }
                    else if (_mode == ModeSeek || _view.IsGestureSeeking)
                    {
                        // 多指时首指抬起也可能需要结束 seek。
                        _ = _view.EndGestureSeekAsync();
                        if (_mode == ModeSeek)
                            _mode = ModeNone;
                    }
                    break;
            }
        }
        catch
        {
            // 异常时也尽量清状态，避免 seek 提示永久卡住。
            try
            {
                AbortActiveSeekOrNav(applySeek: false);
                if (_longPressActive)
                    _view.OnNativeLongPressReleased();
            }
            catch
            {
                // ignore
            }

            _mode = ModeNone;
            _longPressActive = false;
            _gestureConsumed = false;
            _isPinching = false;
            return false;
        }

        return true;
    }
}
