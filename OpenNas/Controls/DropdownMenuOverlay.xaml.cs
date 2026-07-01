namespace OpenNas.Controls;

using Microsoft.Maui.Layouts;
using OpenNas.Helpers;

public partial class DropdownMenuOverlay : ContentView
{
    private TaskCompletionSource<string?>? _tcs;
    private Action? _pendingPosition;

    public DropdownMenuOverlay()
    {
        InitializeComponent();
        OverlayRoot.SizeChanged += (_, _) => TryApplyPendingPosition();
    }

    public Task<string?> ShowAsync(IReadOnlyList<DropdownMenuItem> items, double topMargin = 52, double rightMargin = 8)
    {
        _tcs = new TaskCompletionSource<string?>();
        BuildItems(items);
        IsVisible = true;
        InputTransparent = false;
        RequestPosition(() => PositionMenuAtCorner(topMargin, rightMargin));
        return _tcs.Task;
    }

    public void Hide()
    {
        IsVisible = false;
        InputTransparent = true;
        ItemsStack.Children.Clear();
        _pendingPosition = null;
        AbsoluteLayout.SetLayoutBounds(MenuCard, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
    }

    /// <summary>在指定坐标弹出菜单（坐标系相对于 Overlay 自身）。</summary>
    public Task<string?> ShowAtAsync(IReadOnlyList<DropdownMenuItem> items, double x, double y)
    {
        _tcs = new TaskCompletionSource<string?>();
        BuildItems(items);
        IsVisible = true;
        InputTransparent = false;
        RequestPosition(() => PositionMenuAt(x, y));
        return _tcs.Task;
    }

    /// <summary>在窗口坐标弹出菜单（不受 CollectionView 滚动/虚拟化影响）。</summary>
    public Task<string?> ShowAtWindowAsync(IReadOnlyList<DropdownMenuItem> items, double windowX, double windowY)
    {
        var origin = VisualPositionHelper.GetOriginInWindow(this);
        return ShowAtAsync(items, windowX - origin.X, windowY - origin.Y);
    }

    private void BuildItems(IReadOnlyList<DropdownMenuItem> items)
    {
        ItemsStack.Children.Clear();
        foreach (var item in items)
        {
            var row = CreateRow(item);
            ItemsStack.Children.Add(row);
        }
    }

    private void RequestPosition(Action position)
    {
        _pendingPosition = position;
        TryApplyPendingPosition();
        if (_pendingPosition != null)
            Dispatcher.DispatchAsync(TryApplyPendingPosition);
    }

    private void TryApplyPendingPosition()
    {
        if (_pendingPosition == null || OverlayRoot.Width <= 0 || OverlayRoot.Height <= 0)
            return;

        _pendingPosition();
        _pendingPosition = null;
    }

    private void PositionMenuAt(double x, double y)
    {
        var menuWidth = MenuCard.MinimumWidthRequest;
        var menuHeight = Math.Max(MenuCard.Height, 88);
        var maxX = Math.Max(0, OverlayRoot.Width - menuWidth - 8);
        var maxY = Math.Max(0, OverlayRoot.Height - menuHeight - 8);
        x = Math.Clamp(x, 8, maxX);
        y = Math.Clamp(y, 8, maxY);

        AbsoluteLayout.SetLayoutBounds(MenuCard, new Rect(x, y, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(MenuCard, AbsoluteLayoutFlags.None);
    }

    private void PositionMenuAtCorner(double topMargin, double rightMargin)
    {
        var menuWidth = Math.Min(MenuCard.MaximumWidthRequest, Math.Max(MenuCard.MinimumWidthRequest, MenuCard.Width));
        var x = Math.Max(8, OverlayRoot.Width - menuWidth - rightMargin);
        AbsoluteLayout.SetLayoutBounds(MenuCard, new Rect(x, topMargin, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(MenuCard, AbsoluteLayoutFlags.None);
    }

    private View CreateRow(DropdownMenuItem item)
    {
        var grid = new Grid
        {
            Padding = new Thickness(16, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var label = new Label
        {
            Text = item.Text,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        label.SetAppThemeColor(Label.TextColorProperty,
            (Color)Application.Current!.Resources["TextPrimary"],
            (Color)Application.Current.Resources["TextPrimaryDark"]);

        if (!item.IsEnabled)
        {
            label.SetAppThemeColor(Label.TextColorProperty,
                (Color)Application.Current.Resources["TextSecondary"],
                (Color)Application.Current.Resources["TextSecondaryDark"]);
            grid.Opacity = 0.45;
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            return grid;
        }

        if (item.IsSelected)
            label.SetAppThemeColor(Label.TextColorProperty,
                (Color)Application.Current.Resources["BrandPrimary"],
                (Color)Application.Current.Resources["BrandPrimaryLight"]);

        var trailing = new Label
        {
            Text = item.TrailingIcon ?? (item.IsSelected ? "✓" : ""),
            FontSize = 15,
            IsVisible = !string.IsNullOrEmpty(item.TrailingIcon) || item.IsSelected,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        trailing.SetAppThemeColor(Label.TextColorProperty,
            (Color)Application.Current.Resources["BrandPrimary"],
            (Color)Application.Current.Resources["BrandPrimaryLight"]);

        Grid.SetColumn(label, 0);
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(label);
        grid.Children.Add(trailing);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Complete(item.Key);
        grid.GestureRecognizers.Add(tap);
        return grid;
    }

    private void Complete(string? key)
    {
        Hide();
        _tcs?.TrySetResult(key);
        _tcs = null;
    }

    private void OnDismissTapped(object? sender, TappedEventArgs e) => Complete(null);
}
