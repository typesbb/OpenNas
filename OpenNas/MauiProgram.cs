using Microsoft.Extensions.Logging;
using NSynology.Diagnostics;
using OpenNas.Data;
using OpenNas.Services;
using OpenNas.ViewModels;
using OpenNas.Views;

namespace OpenNas;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<ConnectionService>();
        builder.Services.AddSingleton<BackupDatabase>();
        builder.Services.AddSingleton<BackupEngine>();
        builder.Services.AddSingleton<BackupTaskViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<AlbumsPage>();
        builder.Services.AddTransient<FilesPage>();
        builder.Services.AddTransient<TasksPage>();
        builder.Services.AddTransient<ProfilePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if ANDROID
        Platforms.Android.AndroidUploadThumbnailFactory.Register();
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("NasSelfSignedSsl", (handler, _) =>
        {
            if (handler.PlatformView is Android.Webkit.WebView wv)
                wv.SetWebViewClient(new Platforms.Android.SslTolerantWebViewClient());
        });
#endif

        var app = builder.Build();
        AppServices.Init(app.Services);

#if DEBUG
        SynologyHttpTrace.Enable(SynologyDebugLog.Write);
#endif

        return app;
    }
}

