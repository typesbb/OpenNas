namespace OpenNas.Controls;

public partial class LocalThumbnailView : ContentView
{
    private string? _requestedUri;
    private CancellationTokenSource? _loadCts;

    public static readonly BindableProperty ContentUriProperty =
        BindableProperty.Create(
            nameof(ContentUri),
            typeof(string),
            typeof(LocalThumbnailView),
            default(string),
            propertyChanged: OnContentUriChanged);

    public string? ContentUri
    {
        get => (string?)GetValue(ContentUriProperty);
        set => SetValue(ContentUriProperty, value);
    }

    public LocalThumbnailView()
    {
        InitializeComponent();
    }

    private static void OnContentUriChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not LocalThumbnailView view)
            return;

        var uri = newValue as string;
        if (string.Equals(view._requestedUri, uri, StringComparison.Ordinal))
            return;

        view._loadCts?.Cancel();
        view._loadCts?.Dispose();
        view._loadCts = new CancellationTokenSource();
        view._requestedUri = uri;

        var token = view._loadCts.Token;
        if (string.IsNullOrEmpty(uri))
        {
            view.ThumbImage.Source = null;
            return;
        }

        Helpers.LocalMediaThumbnailLoader.TryLoad(view.ThumbImage, uri, token);
    }
}
