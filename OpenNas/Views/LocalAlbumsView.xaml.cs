#if ANDROID
using OpenNas.Media;
using OpenNas.Services;
#endif

namespace OpenNas.Views;

public partial class LocalAlbumsView : ContentView
{
    public LocalAlbumsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            if (!await MediaPermissions.EnsureReadMediaAsync())
            {
                var page = Application.Current?.Windows[0]?.Page;
                if (page != null)
                    await page.DisplayAlertAsync(
                        "需要权限",
                        "请允许访问照片和视频，以便读取本机相册。可在系统设置 → 应用 → OpenNas → 权限 中开启。",
                        "确定");
                return;
            }

            var service = new LocalMediaService();
            var albums = await service.GetLocalAlbumsAsync();
            AlbumsView.ItemsSource = albums;
        }
        catch (Exception ex)
        {
            AppLog.Error("加载本机相册失败", ex);
            var page = Application.Current?.Windows[0]?.Page;
            if (page != null)
                await page.DisplayAlertAsync("本机相册", ex.Message, "确定");
        }
#else
        var page = Application.Current?.Windows[0]?.Page;
        if (page != null)
            await page.DisplayAlertAsync("提示", "本机相册仅支持 Android。", "确定");
#endif
    }
}
