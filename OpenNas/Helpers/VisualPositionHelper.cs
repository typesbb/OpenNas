namespace OpenNas.Helpers;

/// <summary>VisualElement 坐标换算（用于浮动菜单等 overlay 定位）。</summary>
internal static class VisualPositionHelper
{
    public static Point GetOriginInWindow(VisualElement element)
    {
        if (element.Handler?.PlatformView != null)
        {
#if ANDROID
            if (element.Handler.PlatformView is Android.Views.View view)
            {
                var loc = new int[2];
                view.GetLocationOnScreen(loc);
                var density = Microsoft.Maui.Devices.DeviceDisplay.MainDisplayInfo.Density;
                return new Point(loc[0] / density, loc[1] / density);
            }
#elif IOS || MACCATALYST
            if (element.Handler.PlatformView is UIKit.UIView uiView && uiView.Window is { } window)
            {
                var point = uiView.ConvertPointToView(CoreGraphics.CGPoint.Empty, window);
                return new Point(point.X, point.Y);
            }
#elif WINDOWS
            if (element.Handler.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                var transform = fe.TransformToVisual(null);
                var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                return new Point(point.X, point.Y);
            }
#endif
        }

        double x = 0, y = 0;
        for (var current = element; current != null; current = current.Parent as VisualElement)
        {
            x += current.X;
            y += current.Y;
        }

        return new Point(x, y);
    }
}
