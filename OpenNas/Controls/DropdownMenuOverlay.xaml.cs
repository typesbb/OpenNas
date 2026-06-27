namespace OpenNas.Controls;

public partial class DropdownMenuOverlay : ContentView
{
    private TaskCompletionSource<string?>? _tcs;

    public DropdownMenuOverlay()
    {
        InitializeComponent();
    }

    public Task<string?> ShowAsync(IReadOnlyList<DropdownMenuItem> items, double topMargin = 52, double rightMargin = 8)
    {
        _tcs = new TaskCompletionSource<string?>();
        BuildItems(items);
        MenuCard.Margin = new Thickness(0, topMargin, rightMargin, 0);
        IsVisible = true;
        InputTransparent = false;
        return _tcs.Task;
    }

    public void Hide()
    {
        IsVisible = false;
        InputTransparent = true;
        ItemsStack.Children.Clear();
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
