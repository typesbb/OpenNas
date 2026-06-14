using Android.Graphics;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using OpenNas.Controls;
using AView = Android.Views.View;
using PointF = Android.Graphics.PointF;
using RectF = Android.Graphics.RectF;

namespace OpenNas.Platforms.Android;

[Preserve(AllMembers = true)]
public sealed class PhotoZoomScaleListener : Java.Lang.Object, ScaleGestureDetector.IOnScaleGestureListener
{
    private readonly PhotoZoomTouchListener _owner;

    public PhotoZoomScaleListener(PhotoZoomTouchListener owner) => _owner = owner;

    public bool OnScale(ScaleGestureDetector detector)
    {
        _owner.OnScale(detector);
        return true;
    }

    public bool OnScaleBegin(ScaleGestureDetector detector)
    {
        _owner.EnsureMatrixMode();
        _owner.SaveMatrixForScaleBegin();
        return true;
    }

    public void OnScaleEnd(ScaleGestureDetector detector) => _owner.OnScaleEnd();
}

[Preserve(AllMembers = true)]
public sealed class PhotoZoomGestureListener : GestureDetector.SimpleOnGestureListener
{
    private readonly PhotoZoomTouchListener _owner;

    public PhotoZoomGestureListener(PhotoZoomTouchListener owner) => _owner = owner;

    public override bool OnDoubleTap(MotionEvent e)
    {
        _owner.OnDoubleTap(e);
        return true;
    }
}

/// <summary>
/// 使用原生 Matrix 实现图片双指缩放/拖动，避免 MAUI PinchGesture 在 Android 上抖动。
/// </summary>
[Preserve(AllMembers = true)]
public sealed class PhotoZoomTouchListener : Java.Lang.Object, AView.IOnTouchListener
{
    private const int ModeNone = 0;
    private const int ModeDrag = 1;
    private const int ModeNav = 2;
    private const int ModeDismiss = 3;

    private readonly ZoomableImageView _view;
    private readonly Matrix _matrix = new();
    private readonly Matrix _savedMatrix = new();
    private readonly float[] _matrixValues = new float[9];
    private readonly PointF _start = new();
    private readonly RectF _drawRect = new();

    private ScaleGestureDetector? _scaleDetector;
    private GestureDetector? _gestureDetector;
    private ImageView? _imageView;
    private AView? _touchTarget;
    private float _minScale = 1f;
    private readonly float _maxScale = 5f;
    private int _mode = ModeNone;
    private float _navStartX;
    private float _navStartY;
    private bool _usesMatrix;
    private bool _isPinchZoomed;
    private int _initRetryCount;

    public PhotoZoomTouchListener(ZoomableImageView view) => _view = view;

    public void Attach(ImageView imageView, AView touchTarget)
    {
        var context = touchTarget.Context;
        if (context == null)
            throw new InvalidOperationException("Touch target context is null.");

        _imageView = imageView;
        _touchTarget = touchTarget;
        _scaleDetector = new ScaleGestureDetector(context, new PhotoZoomScaleListener(this));
        _gestureDetector = new GestureDetector(context, new PhotoZoomGestureListener(this));
        touchTarget.SetOnTouchListener(this);
        touchTarget.Clickable = true;
        touchTarget.Focusable = true;
        PrepareDisplay();
    }

    public void Detach(AView touchTarget)
    {
        touchTarget.SetOnTouchListener(null);
        _imageView = null;
        _touchTarget = null;
        _scaleDetector = null;
        _gestureDetector = null;
        _usesMatrix = false;
        _isPinchZoomed = false;
    }

    public void PrepareDisplay()
    {
        if (_imageView == null)
            return;

        _matrix.Reset();
        _usesMatrix = false;
        _isPinchZoomed = false;
        _mode = ModeNone;
        _initRetryCount = 0;
        _minScale = 1f;

        _imageView.SetScaleType(ImageView.ScaleType.FitCenter);
        _imageView.ImageMatrix = null;
        _view.NotifyNativeZoomChanged(false);
        ScheduleMatrixInit();
    }

    private void ScheduleMatrixInit()
    {
        _imageView?.Post(() => TryEnterMatrixBaseline());
    }

    private void TryEnterMatrixBaseline()
    {
        if (_imageView == null || _usesMatrix)
            return;

        if (!TryInitFitMatrix())
        {
            if (_initRetryCount++ < 30)
                _imageView.PostDelayed(() => TryEnterMatrixBaseline(), 50);
        }
    }

    private bool TryInitFitMatrix()
    {
        if (_imageView == null || _imageView.Width <= 0 || _imageView.Height <= 0)
            return false;

        var drawable = _imageView.Drawable;
        if (drawable == null || drawable.IntrinsicWidth <= 0 || drawable.IntrinsicHeight <= 0)
            return false;

        var viewW = _imageView.Width;
        var viewH = _imageView.Height;
        var imgW = drawable.IntrinsicWidth;
        var imgH = drawable.IntrinsicHeight;

        _matrix.Reset();
        var scale = Math.Min((float)viewW / imgW, (float)viewH / imgH);
        var dx = (viewW - imgW * scale) / 2f;
        var dy = (viewH - imgH * scale) / 2f;
        _matrix.SetScale(scale, scale);
        _matrix.PostTranslate(dx, dy);
        _minScale = scale;

        _imageView.SetScaleType(ImageView.ScaleType.Matrix);
        _imageView.ImageMatrix = _matrix;
        _usesMatrix = true;
        _isPinchZoomed = false;
        return true;
    }

    internal bool EnsureMatrixMode() => _usesMatrix || TryInitFitMatrix();

    public bool OnTouch(AView? v, MotionEvent? e)
    {
        if (v == null || e == null || _imageView == null)
            return false;

        try
        {
            _scaleDetector?.OnTouchEvent(e);
            _gestureDetector?.OnTouchEvent(e);

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _savedMatrix.Set(_matrix);
                    _start.Set(e.GetX(), e.GetY());
                    _navStartX = e.GetX();
                    _navStartY = e.GetY();
                    _mode = IsZoomed() ? ModeDrag : ModeNone;
                    break;

                case MotionEventActions.PointerDown:
                    _savedMatrix.Set(_matrix);
                    break;

                case MotionEventActions.Move:
                    if (_mode == ModeDrag && e.PointerCount == 1 && EnsureMatrixMode())
                    {
                        _matrix.Set(_savedMatrix);
                        _matrix.PostTranslate(e.GetX() - _start.X, e.GetY() - _start.Y);
                        EnforceBounds(allowOverscroll: true);
                        ApplyMatrix();
                    }
                    else if (_mode == ModeNone && e.PointerCount == 1 && !IsZoomed())
                    {
                        var dx = e.GetX() - _navStartX;
                        var dy = e.GetY() - _navStartY;
                        if (Math.Abs(dx) > 16 && Math.Abs(dx) > Math.Abs(dy) * 1.15)
                            _mode = ModeNav;
                        else if (dy > 16 && dy > Math.Abs(dx) * 1.15)
                            _mode = ModeDismiss;

                        if (_mode == ModeNav)
                            _view.OnNativeSlideOffset(dx);
                        else if (_mode == ModeDismiss && dy > 0)
                            _view.OnNativeDismissOffset(dy);
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.PointerUp:
                    if (_mode == ModeDrag)
                    {
                        EnforceBounds();
                        ApplyMatrix();
                        SnapToMinIfNeeded();
                        _view.NotifyNativeZoomChanged(IsZoomed());
                    }
                    else if (_mode == ModeNav)
                    {
                        var totalX = e.GetX() - _navStartX;
                        _ = _view.OnNativeSlideCompletedAsync(totalX);
                    }
                    else if (_mode == ModeDismiss)
                    {
                        var totalY = e.GetY() - _navStartY;
                        _view.OnNativeDismissCompleted(totalY);
                    }

                    _mode = ModeNone;
                    break;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    internal void OnScale(ScaleGestureDetector detector)
    {
        if (_imageView == null || !EnsureMatrixMode())
            return;

        var factor = detector.ScaleFactor;
        if (float.IsNaN(factor) || float.IsInfinity(factor) || Math.Abs(factor - 1f) < 0.001f)
            return;

        var focus = MapToImagePoint(detector.FocusX, detector.FocusY);
        _matrix.PostScale(factor, factor, focus.X, focus.Y);

        _matrix.GetValues(_matrixValues);
        var currentScale = _matrixValues[Matrix.MscaleX];
        var maxAllowed = _minScale * _maxScale;
        if (currentScale > maxAllowed)
        {
            var correction = maxAllowed / currentScale;
            _matrix.PostScale(correction, correction, focus.X, focus.Y);
        }

        EnforceBounds(allowOverscroll: true);
        ApplyMatrix();
        _isPinchZoomed = IsZoomed();
        _view.NotifyNativeZoomChanged(_isPinchZoomed);
    }

    internal void OnScaleEnd()
    {
        if (!_usesMatrix)
            return;

        EnforceBounds();
        ApplyMatrix();
        SnapToMinIfNeeded();
        _view.NotifyNativeZoomChanged(IsZoomed());
    }

    internal void OnDoubleTap(MotionEvent e)
    {
        if (_imageView == null)
            return;

        if (IsZoomed())
        {
            PrepareDisplay();
            return;
        }

        if (!EnsureMatrixMode())
            return;

        const float targetFactor = 2.5f;
        var focus = MapToImagePoint(e.GetX(), e.GetY());
        _matrix.PostScale(targetFactor, targetFactor, focus.X, focus.Y);
        EnforceBounds();
        ApplyMatrix();
        _isPinchZoomed = true;
        _view.NotifyNativeZoomChanged(true);
    }

    private void ApplyMatrix()
    {
        if (_imageView != null && _usesMatrix)
            _imageView.ImageMatrix = _matrix;
    }

    private bool IsZoomed()
    {
        if (!_usesMatrix)
            return _isPinchZoomed;

        _matrix.GetValues(_matrixValues);
        return _matrixValues[Matrix.MscaleX] > _minScale * 1.05f;
    }

    private void SnapToMinIfNeeded()
    {
        if (!_usesMatrix)
            return;

        _matrix.GetValues(_matrixValues);
        if (_matrixValues[Matrix.MscaleX] <= _minScale * 1.02f)
            PrepareDisplay();
        else
            _isPinchZoomed = IsZoomed();
    }

    private void EnforceBounds(bool allowOverscroll = false)
    {
        if (_imageView == null || !_usesMatrix)
            return;

        var drawable = _imageView.Drawable;
        if (drawable == null)
            return;

        _drawRect.Set(0, 0, drawable.IntrinsicWidth, drawable.IntrinsicHeight);
        _matrix.MapRect(_drawRect);

        var viewW = _imageView.Width;
        var viewH = _imageView.Height;
        var deltaX = 0f;
        var deltaY = 0f;

        if (_drawRect.Width() <= viewW)
            deltaX = viewW / 2f - (_drawRect.Left + _drawRect.Right) / 2f;
        else
        {
            if (_drawRect.Left > 0)
                deltaX = -_drawRect.Left;
            else if (_drawRect.Right < viewW)
                deltaX = viewW - _drawRect.Right;
        }

        if (_drawRect.Height() <= viewH)
            deltaY = viewH / 2f - (_drawRect.Top + _drawRect.Bottom) / 2f;
        else
        {
            if (_drawRect.Top > 0)
                deltaY = -_drawRect.Top;
            else if (_drawRect.Bottom < viewH)
                deltaY = viewH - _drawRect.Bottom;
        }

        if (allowOverscroll)
        {
            deltaX *= 0.65f;
            deltaY *= 0.65f;
        }

        if (Math.Abs(deltaX) > 0.01f || Math.Abs(deltaY) > 0.01f)
            _matrix.PostTranslate(deltaX, deltaY);
    }

    internal void SaveMatrixForScaleBegin() => _savedMatrix.Set(_matrix);

    private PointF MapToImagePoint(float x, float y)
    {
        if (_imageView == null || _touchTarget == null)
            return new PointF(x, y);

        var imageLoc = new int[2];
        var touchLoc = new int[2];
        _imageView.GetLocationOnScreen(imageLoc);
        _touchTarget.GetLocationOnScreen(touchLoc);
        return new PointF(
            x + touchLoc[0] - imageLoc[0],
            y + touchLoc[1] - imageLoc[1]);
    }
}
