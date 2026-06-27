using OpenNas.Helpers;

namespace OpenNas.Controls;

public partial class AlbumSelectionCheckbox : ContentView
{
    public static readonly BindableProperty CheckStateProperty = BindableProperty.Create(
        nameof(CheckState),
        typeof(SelectionCheckState),
        typeof(AlbumSelectionCheckbox),
        SelectionCheckState.Unchecked,
        propertyChanged: OnCheckStateChanged);

    public SelectionCheckState CheckState
    {
        get => (SelectionCheckState)GetValue(CheckStateProperty);
        set => SetValue(CheckStateProperty, value);
    }

    public AlbumSelectionCheckbox()
    {
        InitializeComponent();
        ApplyVisual(CheckState);
    }

    private static void OnCheckStateChanged(BindableObject bindable, object _, object newValue)
    {
        if (bindable is AlbumSelectionCheckbox box && newValue is SelectionCheckState state)
            box.ApplyVisual(state);
    }

    private void ApplyVisual(SelectionCheckState state)
    {
        var brand = (Color)Application.Current!.Resources[
            Application.Current.RequestedTheme == AppTheme.Dark ? "BrandPrimaryLight" : "BrandPrimary"];
        var muted = (Color)Application.Current.Resources[
            Application.Current.RequestedTheme == AppTheme.Dark ? "TextSecondaryDark" : "TextSecondary"];

        switch (state)
        {
            case SelectionCheckState.Checked:
                Circle.BackgroundColor = brand;
                Circle.Stroke = brand;
                CheckMark.IsVisible = true;
                PartialMark.IsVisible = false;
                break;
            case SelectionCheckState.Partial:
                Circle.BackgroundColor = Colors.Transparent;
                Circle.Stroke = brand;
                CheckMark.IsVisible = false;
                PartialMark.IsVisible = true;
                break;
            default:
                Circle.BackgroundColor = Colors.Transparent;
                Circle.Stroke = muted;
                CheckMark.IsVisible = false;
                PartialMark.IsVisible = false;
                break;
        }
    }
}
