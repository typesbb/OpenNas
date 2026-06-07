using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Android.Database;
using Android.Provider;

#if ANDROID

namespace OpenNas.Media;

public partial class MediaService
{        public partial async Task<List<string>> GetMediasAsync(string albumName)
        {
            var photos = new List<string>();

            var uri = MediaStore.Images.Media.ExternalContentUri;
            // 指定想要获取的列
            string[] projection = {
                MediaStore.Images.Media.InterfaceConsts.Id,
                MediaStore.Images.Media.InterfaceConsts.Data,
                MediaStore.Images.Media.InterfaceConsts.BucketDisplayName
            };

            // 查询条件，根据相册名称筛选
            string selection = $"{MediaStore.Images.Media.InterfaceConsts.BucketDisplayName} = ?";
            string[] selectionArgs = { albumName };

            using (ICursor cursor = _mauiContext.Context!.ContentResolver!.Query(uri, projection, selection, selectionArgs, null)!)
            {
                if (cursor.MoveToFirst())
                {
                    do
                    {
                        int index = cursor.GetColumnIndexOrThrow(MediaStore.Images.Media.InterfaceConsts.Data);
                        photos.Add(cursor.GetString(index));
                    }
                    while (cursor.MoveToNext());
                }
            }

            return photos;
        }

        public partial async Task<Dictionary<string, string>> GetImageAsync3()
        {
            Intent intent = new Intent(Intent.ActionPick);
            intent.SetDataAndType(MediaStore.Images.Media.ExternalContentUri, "image/*");
            intent.PutExtra(Intent.ExtraAllowMultiple, true);
            //var activity = ActivityStateManager.Default.GetCurrentActivity()!;

            MainActivity.Instance.StartActivityForResult(Intent.CreateChooser(intent, "Select Picture"),
                MainActivity.PickImageId);
            MainActivity.Instance.PickImageTaskCompletionSource = new TaskCompletionSource<Dictionary<string, string>>();
            return await MainActivity.Instance.PickImageTaskCompletionSource.Task;
        }

        public partial async Task<List<string>> GetVideosFromAlbumAsync(string albumName)
        {
            var videos = new List<string>();

            var uri = MediaStore.Video.Media.ExternalContentUri;
            string[] projection = { MediaStore.Video.Media.InterfaceConsts.Data };
            string selection = $"{MediaStore.Video.Media.InterfaceConsts.BucketDisplayName} = ?";
            string[] selectionArgs = { albumName };

            using (ICursor cursor = _mauiContext.Context!.ContentResolver!.Query(uri, projection, selection, selectionArgs, null))
            {
                if (cursor.MoveToFirst())
                {
                    do
                    {
                        int index = cursor.GetColumnIndexOrThrow(MediaStore.Video.Media.InterfaceConsts.Data);
                        videos.Add(cursor.GetString(index));
                    }
                    while (cursor.MoveToNext());
                }
            }

            return videos;
        }
}

#endif