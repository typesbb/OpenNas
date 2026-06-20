using Android.Content;
using Android.Provider;
using OpenNas.Core.Models;
using OpenNas.Services;

namespace OpenNas.Media;

public class LocalMediaService : ILocalMediaService
{
    private readonly ContentResolver _resolver;

    public LocalMediaService()
    {
        var ctx = Platform.CurrentActivity ?? Android.App.Application.Context;
        _resolver = ctx.ContentResolver!;
    }

    public async Task<IReadOnlyList<LocalAlbumInfo>> GetLocalAlbumsAsync()
    {
        if (!await MediaPermissions.EnsureReadMediaAsync())
            return Array.Empty<LocalAlbumInfo>();

        var map = new Dictionary<string, LocalAlbumInfo>();
        ScanBuckets(MediaStore.Images.Media.ExternalContentUri, map, false);
        ScanBuckets(MediaStore.Video.Media.ExternalContentUri, map, true);
        return map.Values.OrderBy(a => a.Name).ToList();
    }

    private void ScanBuckets(Android.Net.Uri collection, Dictionary<string, LocalAlbumInfo> map, bool isVideo)
    {
        string[] projection =
        {
            MediaStore.IMediaColumns.BucketId,
            MediaStore.IMediaColumns.BucketDisplayName
        };

        using var cursor = _resolver.Query(collection, projection, null, null, null);
        if (cursor == null) return;

        var idCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.BucketId);
        var nameCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.BucketDisplayName);

        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(idCol) ?? "";
            var name = cursor.GetString(nameCol) ?? "未命名";
            if (string.IsNullOrEmpty(id)) continue;
            if (!map.TryGetValue(id, out var album))
            {
                album = new LocalAlbumInfo { Id = id, Name = name };
                map[id] = album;
            }
            album.ItemCount++;
        }
    }

    public async Task<IReadOnlyList<LocalMediaItem>> GetMediaItemsAsync(string albumId)
    {
        if (!await MediaPermissions.EnsureReadMediaAsync())
            return Array.Empty<LocalMediaItem>();

        var items = new List<LocalMediaItem>();
        AddItems(MediaStore.Images.Media.ExternalContentUri, albumId, items, false);
        AddItems(MediaStore.Video.Media.ExternalContentUri, albumId, items, true);
        return items;
    }

    private void AddItems(Android.Net.Uri collection, string albumId, List<LocalMediaItem> items, bool isVideo)
    {
        string[] projection =
        {
            "_id",
            MediaStore.IMediaColumns.DisplayName,
            MediaStore.IMediaColumns.Size,
            MediaStore.IMediaColumns.DateModified,
            MediaStore.IMediaColumns.MimeType,
            MediaStore.IMediaColumns.BucketId
        };

        var selection = $"{MediaStore.IMediaColumns.BucketId} = ?";
        var args = new[] { albumId };

        using var cursor = _resolver.Query(collection, projection, selection, args, $"{MediaStore.IMediaColumns.DateModified} DESC");
        if (cursor == null) return;

        var idCol = cursor.GetColumnIndexOrThrow("_id");
        var nameCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.DisplayName);
        var sizeCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.Size);
        var modCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.DateModified);
        var mimeCol = cursor.GetColumnIndexOrThrow(MediaStore.IMediaColumns.MimeType);

        while (cursor.MoveToNext())
        {
            var mediaId = cursor.GetLong(idCol);
            var contentUri = ContentUris.WithAppendedId(collection, mediaId).ToString()!;
            items.Add(new LocalMediaItem
            {
                MediaStoreId = mediaId.ToString(),
                ContentUri = contentUri,
                DisplayName = cursor.GetString(nameCol) ?? "",
                Size = cursor.GetLong(sizeCol),
                DateModified = cursor.GetLong(modCol),
                MimeType = cursor.GetString(mimeCol) ?? (isVideo ? "video/mp4" : "image/jpeg"),
                IsVideo = isVideo,
                LocalAlbumId = albumId
            });
        }
    }

    public Task<bool> DeleteMediaAsync(string contentUri)
    {
        try
        {
            var uri = Android.Net.Uri.Parse(contentUri);
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                var pi = Android.Provider.MediaStore.CreateDeleteRequest(_resolver, new List<Android.Net.Uri> { uri });
                if (pi != null)
                {
                    pi.Send();
                    return Task.FromResult(true);
                }
            }
            var rows = _resolver.Delete(uri, null, null);
            return Task.FromResult(rows > 0);
        }
        catch (Android.App.RecoverableSecurityException ex)
        {
            BackupLog.Warn($"删除需用户授权 {contentUri}");
            try { ex.UserAction.ActionIntent.Send(); return Task.FromResult(true); }
            catch (Exception inner) { BackupLog.Warn($"无法启动授权对话框: {inner.Message}"); return Task.FromResult(false); }
        }
        catch (Exception ex)
        {
            BackupLog.Warn($"删除失败 {contentUri}: {ex.GetType().Name} - {ex.Message}");
            return Task.FromResult(false);
        }
    }
}
