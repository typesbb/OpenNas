using Android.Runtime;
using Android.Views;
using OpenNas.Controls;
using AView = Android.Views.View;

namespace OpenNas.Platforms.Android;

[Preserve(AllMembers = true)]
public sealed class VideoGestureListener : GestureDetector.SimpleOnGestureListener
{
    private readonly VideoTouchListener _owner;

    public VideoGestureListener(VideoTouchListener owner) => _owner = owner;

    public override void OnLongPress(MotionEvent e) => _owner.OnLongPress();

    public override bool OnDoubleTap(MotionEvent e)
    {
        _owner.OnDoubleTap();
        return true;
    }

    public override bool OnSingleTapConfirmed(MotionEvent e)
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
    private const int ModeDismiss = 2;

    private readonly NasVideoPlayerView _view;
    private GestureDetector? _gestureDetector;
    private AView? _touchTarget;
    private int _mode = ModeNone;
    private float _startX;
    private float _startY;
    private bool _longPressActive;
    private bool _gestureConsumed;

    public VideoTouchListener(NasVideoPlayerView view) => _view = view;

    public void Attach(AView touchTarget)
    {
        var context = touchTarget.Context
            ?? throw new InvalidOperationException("Touch target context is null.");

        _touchTarget = touchTarget;
        _gestureDetector = new GestureDetector(context, new VideoGestureListener(this));
        touchTarget.SetOnTouchListener(this);
        touchTarget.Clickable = true;
        touchTarget.Focusable = true;
    }

    public void Detach(AView touchTarget)
    {
        touchTarget.SetOnTouchListener(null);
        _touchTarget = null;
        _gestureDetector = null;
        _mode = ModeNone;
        _longPressActive = false;
        _gestureConsumed = false;
    }

    internal void OnLongPress()
    {
        if (_gestureConsumed || _mode != ModeNone)
            return;

        _longPressActive = true;
        _gestureConsumed = true;
        _view.OnNativeLongPress();
    }

    internal void OnDoubleTap() => _view.OnNativeDoubleTap();

    internal void OnSingleTap() => _view.OnNativeSingleTap();

    public bool OnTouch(AView? v, MotionEvent? e)
    {
        if (v == null || e == null)
            return false;

        try
        {
            _gestureDetector?.OnTouchEvent(e);

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _mode = ModeNone;
                    _gestureConsumed = false;
                    _longPressActive = false;
                    _startX = e.GetX();
                    _startY = e.GetY();
                    break;

                case MotionEventActions.Move:
                    if (_longPressActive || _gestureConsumed)
                        break;

                    var dx = e.GetX() - _startX;
                    var dy = e.GetY() - _startY;

                    if (_mode == ModeNone)
                    {
                        if (Math.Abs(dx) > 20 && Math.Abs(dx) > Math.Abs(dy) * 1.15)
                            _mode = ModeNav;
                        else if (dy > 20 && dy > Math.Abs(dx) * 1.15)
                            _mode = ModeDismiss;
                    }

                    if (_mode == ModeNav)
                        _view.OnNativeSlideOffset(dx);
                    else if (_mode == ModeDismiss && dy > 0)
                        _view.OnNativeDismissOffset(dy);
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_longPressActive)
                        _view.OnNativeLongPressReleased();

                    if (_mode == ModeNav)
                    {
                        var totalX = e.GetX() - _startX;
                        _ = _view.OnNativeSlideCompletedAsync(totalX);
                    }
                    else if (_mode == ModeDismiss)
                    {
                        var totalY = e.GetY() - _startY;
                        _view.OnNativeDismissCompleted(totalY);
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
