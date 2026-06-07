using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.DocumentFile.Provider;

namespace OpenNas
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        internal static MainActivity Instance { get; private set; }
        public static readonly int PickImageId = 1000;
        public TaskCompletionSource<Dictionary<string, string>> PickImageTaskCompletionSource { set; get; }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            Instance = this;
            base.OnCreate(savedInstanceState);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent intent)
        {
            base.OnActivityResult(requestCode, resultCode, intent);

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
                            var imageUri = imageNames.GetItemAt(i).Uri;
                            uris.Add(imageUri);
                        }

                        var fileList = Instance.GetImageDicFromUris(uris);
                        PickImageTaskCompletionSource.SetResult(fileList);
                    }
                }
                else
                {
                    PickImageTaskCompletionSource.SetResult(new Dictionary<string, string>());
                }
            }
        }
        protected Dictionary<string, string> GetImageDicFromUris(List<Android.Net.Uri> list)
        {
            Dictionary<string, string> fileList = new Dictionary<string, string>();
            for (int i = 0; i < list.Count; i++)
            {
                var imageUri = list[i];
                var documentFile = DocumentFile.FromSingleUri(Instance, imageUri);
                if (documentFile != null)
                {
                    using (var stream = Instance.ContentResolver.OpenInputStream(imageUri))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        var bs = new byte[stream.Length];
                        var log = Convert.ToInt32(stream.Length);
                        stream.Read(bs, 0, log);
                        var base64Str = Convert.ToBase64String(bs);
                        fileList.Add($"{Guid.NewGuid()}.{Path.GetExtension(documentFile.Name)}", base64Str);
                    }
                }
            }
            return fileList;
        }

    }
}
