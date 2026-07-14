using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using NSynology.Diagnostics;
using OpenNas.Core.Data;
using OpenNas.Services;
using OpenNas.Core.Services;
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
            .UseMauiCommunityToolkit()
#pragma warning disable CA1416 // isAndroidForegroundServiceEnabled is Android-only
            .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
#pragma warning restore CA1416
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                handlers.AddHandler(typeof(Shell), typeof(Platforms.Android.OpenNasShellRenderer));
#elif IOS
                handlers.AddHandler(typeof(Shell), typeof(Platforms.iOS.OpenNasShellRenderer));
#endif
            });

        builder.Services.AddSingleton<IAuthNavigation, AuthNavigation>();
        builder.Services.AddSingleton<ConnectionService>();
        builder.Services.AddSingleton<PhotosLibraryContext>();
        builder.Services.AddSingleton<AlbumsPageViewModel>();
        builder.Services.AddSingleton(new BackupDatabase(Path.Combine(FileSystem.AppDataDirectory, "opennas_backup.db")));
        builder.Services.AddSingleton<BackupEngine>();
        builder.Services.AddSingleton<BackupTaskViewModel>();
        builder.Services.AddSingleton(LogRepository.Instance);
        builder.Services.AddTransient<LogPageViewModel>();

        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<AlbumsPage>();
        builder.Services.AddTransient<FilesPage>();
        builder.Services.AddTransient<TasksPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<LogPage>();
        builder.Services.AddTransient<ConnectionSettingsPage>();
        builder.Services.AddTransient<BackupSettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if ANDROID
        Platforms.Android.AndroidUploadThumbnailFactory.Register();
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
        {
            if (handler.PlatformView is global::Android.Widget.EditText editText)
            {
                editText.Background = null;
                editText.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(
                    global::Android.Graphics.Color.Transparent);
            }
        });
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("NasSelfSignedSsl", (handler, _) =>
        {
            if (handler.PlatformView is Android.Webkit.WebView wv)
                wv.SetWebViewClient(new Platforms.Android.SslTolerantWebViewClient());
        });
#endif

        var app = builder.Build();
        App.ConfigureServices(app.Services);

#if DEBUG
        SynologyHttpTrace.Enable(SynologyDebugLog.Write);
#endif

        return app;
    }
}
