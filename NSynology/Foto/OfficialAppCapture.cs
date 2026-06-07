using System.Reflection;

namespace NSynology.Foto;

/// <summary>官方 Synology Photos Android App SAZ 抓包字段（<c>2026-06-07-183330.saz</c>）。</summary>
internal static class OfficialAppCapture
{    public const string BrowseAlbumListAdditional =
        "[\"thumbnail\",\"sharing_info\",\"access_permission\",\"provider_count\"]";

    public const string BrowseAlbumGetAdditional =
        "[\"thumbnail\",\"sharing_info\",\"access_permission\",\"condition_object\",\"provider_count\"]";

    /// <summary>Session 3006 <c>Browse.Item list</c>（上传前预热）。</summary>
    public const string BrowseItemListAdditional =
        "[\"description\",\"exif\",\"resolution\",\"orientation\",\"address\",\"gps\",\"geocoding_id\",\"thumbnail\",\"tag\",\"video_meta\",\"mobile_cache_mtime\",\"folder\",\"provider_user_id\",\"rating\",\"motion_photo\",\"thumb_version\",\"duplicate_hash\"]";

    /// <summary>Session 3017 上传 multipart <c>raw_data</c> 首项（移动端推理占位）。</summary>
    public const string UploadRawDataStub =
        "[{\"confidence\":1.0,\"label\":\"_inference_by_mobile\"}]";

    private static string? _uploadRawDataJson;

    /// <summary>SAZ 3017 完整 <c>raw_data</c>（含 <c>_inference_by_mobile</c> 与标签分数）。</summary>
    public static string UploadRawDataJson =>
        _uploadRawDataJson ??= LoadEmbeddedUploadRawData();

    private static string LoadEmbeddedUploadRawData()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("OfficialAppUploadRawData.json", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            return UploadRawDataStub;

        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
            return UploadRawDataStub;

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(text) ? UploadRawDataStub : text;
    }
}
