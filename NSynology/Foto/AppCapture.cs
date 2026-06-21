
namespace NSynology.Foto;

/// <summary>官方 Synology Photos Android App SAZ 抓包字段（<c>2026-06-07-183330.saz</c>）。</summary>
internal static class AppCapture
{    public const string BrowseAlbumListAdditional =
        "[\"thumbnail\",\"sharing_info\",\"access_permission\",\"provider_count\"]";


    /// <summary>Session 3006 <c>Browse.Item list</c>（上传前预热）。</summary>
    public const string BrowseItemListAdditional =
        "[\"description\",\"exif\",\"resolution\",\"orientation\",\"address\",\"gps\",\"geocoding_id\",\"thumbnail\",\"tag\",\"video_meta\",\"mobile_cache_mtime\",\"folder\",\"provider_user_id\",\"rating\",\"motion_photo\",\"thumb_version\",\"duplicate_hash\"]";

    /// <summary>Session 3017 上传 multipart <c>raw_data</c> 首项（移动端推理占位）。</summary>
    public const string UploadRawDataStub =
        "[{\"confidence\":1.0,\"label\":\"_inference_by_mobile\"}]";

    /// <summary>上传 multipart <c>raw_data</c>（移动端推理占位标识）。</summary>
    public static string UploadRawDataJson => UploadRawDataStub;
}
