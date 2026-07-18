using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.DocumentFile.Provider;
using OpenNas.Services;

#pragma warning disable IDE0130 // Namespace matches project convention for Android-specific files
namespace OpenNas
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, SupportsPictureInPicture = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        internal static MainActivity? Instance { get; private set; }
        public const string ExtraOpenTab = "open_tab";
        public static readonly int PickImageId = 1000;
        public TaskCompletionSource<Dictionary<string, string>>? PickImageTaskCompletionSource { set; get; }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            Instance = this!;
            base.OnCreate(savedInstanceState);
            HandleNavigationIntent(Intent);
        }

        protected override void OnNewIntent(Android.Content.Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent != null)
                Intent = intent;
            HandleNavigationIntent(intent);
        }

        protected override void OnResume()
        {
            base.OnResume();
            OpenNas.Platforms.Android.BackupPendingDeleteHelper.TryLaunchDeleteConfirmation(this);
            if (Platforms.Android.FullscreenOrientationHelper.WantImmersive)
                Platforms.Android.FullscreenOrientationHelper.Apply();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            // 系统在重新获得焦点时会清掉 Immersive 标志，必须在这里重设。
            if (hasFocus && Platforms.Android.FullscreenOrientationHelper.WantImmersive)
                Platforms.Android.FullscreenOrientationHelper.Apply();
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? intent)
        {
            base.OnActivityResult(requestCode, resultCode, intent);

            if (requestCode == OpenNas.Platforms.Android.BackupPendingDeleteHelper.DeleteRequestCode)
            {
                OpenNas.Platforms.Android.BackupPendingDeleteHelper.OnDeleteResult(resultCode);
                return;
            }

            if (requestCode == PickImageId)
            {
                if ((resultCode == Result.Ok) && (intent != null))
                {
                    var imageNames = intent.ClipData;

                    if (imageNames != null)
                    {
                        var uris = new List<Android.Net.Uri>();

                        for (int i = 0; i < imageNames.ItemCount; i++)
                        {
                            var imageUri = imageNames.GetItemAt(i)?.Uri;
                            if (imageUri != null)
                                uris.Add(imageUri);
                        }

                        var fileList = GetImageDicFromUris(uris) ?? [];
                        PickImageTaskCompletionSource?.SetResult(fileList);
                    }
                }
                else
                {
                    PickImageTaskCompletionSource?.SetResult([]);
                }
            }
        }
        protected static Dictionary<string, string> GetImageDicFromUris(List<Android.Net.Uri> list)
        {
            Dictionary<string, string> fileList = [];
            for (int i = 0; i < list.Count; i++)
            {
                var imageUri = list[i];
                var documentFile = DocumentFile.FromSingleUri(Instance, imageUri);
                if (documentFile != null)
                {
                    var stream = Instance?.ContentResolver?.OpenInputStream(imageUri);
                    if (stream == null) continue;
                    using (stream)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        var bs = new byte[stream.Length];
                        stream.ReadExactly(bs, 0, bs.Length);
                        var base64Str = Convert.ToBase64String(bs);
                        fileList.Add($"{Guid.NewGuid()}.{Path.GetExtension(documentFile.Name)}", base64Str);
                    }
                }
            }
            return fileList;
        }

        private void HandleNavigationIntent(Android.Content.Intent? intent)
        {
            var tab = intent?.GetStringExtra(ExtraOpenTab);
            if (string.IsNullOrWhiteSpace(tab))
                return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    if (Shell.Current != null)
                        break;
                    await Task.Delay(50);
                }

                if (Shell.Current == null)
                    return;

                try
                {
                    await Shell.Current.GoToAsync($"//{tab}");
                }
                catch (Exception ex)
                {
                    LogRepository.Instance.AppendError($"通知跳转 {tab} 失败", ex);
                }
            });
        }

    }
}
