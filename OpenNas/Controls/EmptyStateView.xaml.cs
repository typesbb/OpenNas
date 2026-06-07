namespace OpenNas.Controls;

public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyStateView), "");

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(EmptyStateView), "");

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

    public EmptyStateView()
    {
        InitializeComponent();
        TitleLabel.SetBinding(Label.TextProperty, new Binding(nameof(Title), source: this));
        MessageLabel.SetBinding(Label.TextProperty, new Binding(nameof(Message), source: this));
    }
}
