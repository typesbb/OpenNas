namespace OpenNas.Controls;

public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyStateView), "");

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(EmptyStateView), "");

    public static readonly BindableProperty IconTextProperty =
        BindableProperty.Create(nameof(IconText), typeof(string), typeof(EmptyStateView), "",
            propertyChanged: OnIconTextChanged);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string IconText
    {
        get => (string)GetValue(IconTextProperty);
        set => SetValue(IconTextProperty, value);
    }

    public EmptyStateView()
    {
        InitializeComponent();
        TitleLabel.SetBinding(Label.TextProperty, new Binding(nameof(Title), source: this));
        MessageLabel.SetBinding(Label.TextProperty, new Binding(nameof(Message), source: this));
    }

    private static void OnIconTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not EmptyStateView view) return;
        var text = newValue as string ?? "";
        view.IconLabel.Text = text;
        view.IconLabel.IsVisible = !string.IsNullOrEmpty(text);
    }
}
