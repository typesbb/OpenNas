namespace OpenNas.Behaviors;

/// <summary>
/// 跨平台长按检测（PointerGestureRecognizer + 延时），不依赖 Android 原生 Touch/Runnable。
/// </summary>
public class LongPressBehavior : Behavior<View>
{
    public class LongPressEventArgs : EventArgs
    {
        public object? Context { get; set; }
        /// <summary>触控点相对于被按压元素的 X 坐标。</summary>
        public double X { get; set; }
        /// <summary>触控点相对于被按压元素的 Y 坐标。</summary>
        public double Y { get; set; }
        /// <summary>触控点相对于窗口的 X 坐标。</summary>
        public double WindowX { get; set; }
        /// <summary>触控点相对于窗口的 Y 坐标。</summary>
        public double WindowY { get; set; }
        /// <summary>被按压的元素。</summary>
        public View? AttachedView { get; set; }
    }

    public static event EventHandler<LongPressEventArgs>? LongPressed;

    /// <summary>多选模式下不再检测长按，避免与点击选择冲突。</summary>
    public static bool DetectionEnabled { get; set; } = true;

    private const int DurationMs = 500;

    private View? _view;
    private PointerGestureRecognizer? _pointer;
    private bool _isPressed;
    private bool _longPressFired;
    private CancellationTokenSource? _cts;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _view = bindable;
        _pointer = new PointerGestureRecognizer();
        _pointer.PointerPressed += OnPointerPressed;
        _pointer.PointerReleased += OnPointerReleased;
        _pointer.PointerExited += OnPointerReleased;
        bindable.GestureRecognizers.Add(_pointer);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        CancelPendingLongPress();
        if (_pointer != null)
        {
            _pointer.PointerPressed -= OnPointerPressed;
            _pointer.PointerReleased -= OnPointerReleased;
            _pointer.PointerExited -= OnPointerReleased;
            bindable.GestureRecognizers.Remove(_pointer);
            _pointer = null;
        }

        _view = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (!DetectionEnabled)
            return;

        _isPressed = true;
        _longPressFired = false;
        CancelPendingLongPress();

        var view = _view;
        var context = view?.BindingContext;
        var position = e.GetPosition(view);
        var posX = position?.X ?? 0;
        var posY = position?.Y ?? 0;
        var windowPosition = e.GetPosition(null);
        var windowX = windowPosition?.X ?? 0;
        var windowY = windowPosition?.Y ?? 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DurationMs, token);
                if (token.IsCancellationRequested || !_isPressed || _longPressFired)
                    return;

                _longPressFired = true;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_isPressed)
                        LongPressed?.Invoke(this, new LongPressEventArgs
                        {
                            Context = context,
                            X = posX,
                            Y = posY,
                            WindowX = windowX,
                            WindowY = windowY,
                            AttachedView = view
                        });
                });
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        _isPressed = false;
        CancelPendingLongPress();
    }

    private void CancelPendingLongPress()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _cts = null;
    }
}
