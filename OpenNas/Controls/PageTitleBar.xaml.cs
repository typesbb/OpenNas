namespace OpenNas.Controls;

public partial class PageTitleBar : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(PageTitleBar), "");

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public PageTitleBar()
    {
        InitializeComponent();
        TitleLabel.SetBinding(Label.TextProperty, new Binding(nameof(Title), source: this));
    }
}
