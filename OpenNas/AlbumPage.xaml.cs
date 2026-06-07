using System.Collections.ObjectModel;
using NSynology;
using NSynology.Foto;
using OpenNas.Helpers;
using OpenNas.Services;

namespace OpenNas;

public partial class AlbumPage : ContentPage
{
    private readonly ObservableCollection<Album> _albums = new();
    private bool _loaded;

    public AlbumPage()
    {
        InitializeComponent();
        BindingContext = _albums;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
            return;

        _loaded = true;
        await LoadAlbumsAsync();
    }

    private async Task LoadAlbumsAsync()
    {
        Title = "���";
        try
        {
            if (SynologyManager.Client == null || string.IsNullOrEmpty(SynologyManager.Client.Sid))
            {
                await DisplayAlert("NAS ���", "���ȵ�¼ NAS�����ڡ����ࡹ�м���������á�", "ȷ��");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var albums = await SynologyManager.Client.Foto.GetAlbumsAsync(0, 100, cts.Token);
            _albums.Clear();
            foreach (var album in albums)
                _albums.Add(album);
        }
        catch (Exception ex)
        {
            AppLog.Error("���� NAS ����б�ʧ��", ex);
            await DisplayAlert("NAS ���", $"����ʧ�ܣ�{ex.Message}", "ȷ��");
        }
    }

    private void OnImageHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Image image && image.BindingContext is Album album)
            NasThumbnailLoader.TryLoadAlbumThumbnail(image, album);
    }

    private async void OnAlbumTapped(object sender, TappedEventArgs e)
    {
        if (sender is not Frame item || item.BindingContext is not Album album)
            return;

        await ShellNavigation.PushAsync(new AlbumDetailPage(album));
    }
}
