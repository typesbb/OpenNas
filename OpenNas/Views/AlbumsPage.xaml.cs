using OpenNas.Controls;

namespace OpenNas.Views;

public partial class AlbumsPage : ContentPage
{
    public AlbumsPage()
    {
        InitializeComponent();
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var selected = await Dropdown.ShowAsync(AlbumsContent.GetSortMenuItems(), topMargin: 52, rightMargin: 52);
        if (!string.IsNullOrEmpty(selected))
            AlbumsContent.SetSortMode(selected);
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var selected = await Dropdown.ShowAsync(
            [new DropdownMenuItem("create", "新建相册")],
            topMargin: 52,
            rightMargin: 8);
        if (selected == "create")
            await AlbumsContent.CreateAlbumAsync();
    }
}
