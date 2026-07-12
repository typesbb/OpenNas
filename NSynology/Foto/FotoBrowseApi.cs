using System.Text.Json;

namespace NSynology.Foto;

public class FotoBrowseApi(SynologyClient synologyClient)
{
    private readonly SynologyClient _client = synologyClient;
    private const string ThumbnailAdditional = "[\"thumbnail\"]";
    internal const string RepairMetadataAdditional = "[\"thumbnail\",\"mobile_cache_mtime\"]";

    /// <summary>拉取单条媒体最新 metadata（含 <c>mobile_cache_mtime</c> / <c>cache_key</c>）。</summary>
    public async Task<Photo?> GetPhotoAsync(int photoId, CancellationToken cancellationToken = default)
    {
        if (photoId <= 0)
            return null;

        var parsed = await _client.PostAppFormAsync<ListObject<Photo>>(
            "SYNO.Foto.Browse.Item",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Item", 5),
            "get",
            [
                new("id", $"[{photoId}]"),
                new("additional", RepairMetadataAdditional),
                new("geocoding_accept_language", "chs")
            ],
            cancellationToken);
        return parsed?.List?.FirstOrDefault();
    }

    /// <summary>从图库删除媒体（非仅从相册移除）。</summary>
    public Task<bool> DeletePhotosAsync(
        IReadOnlyList<int> photoIds,
        CancellationToken cancellationToken = default)
    {
        if (photoIds.Count == 0)
            return Task.FromResult(true);

        return _client.TryPostAppFormAsync(
            "SYNO.Foto.Browse.Item",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Item", 1),
            "delete",
            [new KeyValuePair<string, string>("id", JsonSerializer.Serialize(photoIds))],
            cancellationToken);
    }

    public Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        _client.Foto.GetCategoriesAsync(cancellationToken);

    public Task<IReadOnlyList<Photo>> ListRecentlyAddedAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowsePhotosAsync("SYNO.Foto.Browse.RecentlyAdded", offset, limit, cancellationToken);

    public Task<IReadOnlyList<BrowseAlbumItem>> ListPersonsAsync(
        int offset,
        int limit,
        bool showMore = false,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("offset", offset.ToString()),
            new("limit", limit.ToString()),
            new("additional", ThumbnailAdditional)
        };
        if (showMore)
            fields.Add(new KeyValuePair<string, string>("show_more", "true"));

        return ListBrowseAlbumItemsAsync("SYNO.Foto.Browse.Person", fields, cancellationToken);
    }

    public Task<IReadOnlyList<BrowseAlbumItem>> ListConceptsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseAlbumItemsAsync(
            "SYNO.Foto.Browse.Concept",
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", ThumbnailAdditional)
            ],
            cancellationToken);

    public Task<IReadOnlyList<BrowseAlbumItem>> ListGeocodingsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseAlbumItemsAsync(
            "SYNO.Foto.Browse.Geocoding",
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", ThumbnailAdditional)
            ],
            cancellationToken);

    public Task<IReadOnlyList<BrowseAlbumItem>> ListGeneralTagsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseAlbumItemsAsync(
            "SYNO.Foto.Browse.GeneralTag",
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", ThumbnailAdditional)
            ],
            cancellationToken);

    public Task<IReadOnlyList<Photo>> ListVideosAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", ThumbnailAdditional),
                new("type", "\"video\"")
            ],
            cancellationToken);

    public Task<IReadOnlyList<Photo>> ListPersonPhotosAsync(
        int personId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", AppCapture.BrowseItemListAdditional),
                new("timeline_group_unit", "\"day\""),
                new("person_id", personId.ToString())
            ],
            cancellationToken);

    public Task<IReadOnlyList<Photo>> ListConceptPhotosAsync(
        int conceptId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", AppCapture.BrowseItemListAdditional),
                new("timeline_group_unit", "\"day\""),
                new("concept_id", conceptId.ToString())
            ],
            cancellationToken);

    public Task<IReadOnlyList<Photo>> ListGeocodingPhotosAsync(
        int geocodingId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", AppCapture.BrowseItemListAdditional),
                new("geocoding_accept_language", "chs"),
                new("timeline_group_unit", "\"day\""),
                new("geocoding_id", geocodingId.ToString())
            ],
            cancellationToken);

    public Task<IReadOnlyList<Photo>> ListGeneralTagPhotosAsync(
        int tagId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", AppCapture.BrowseItemListAdditional),
                new("timeline_group_unit", "\"day\""),
                new("tag_id", tagId.ToString())
            ],
            cancellationToken);

    public Task<IReadOnlyList<Photo>> ListTimelineSectionPhotosAsync(
        long startTimeUnix,
        int offset,
        int limit,
        long? endTimeUnix = null,
        CancellationToken cancellationToken = default)
    {
        var endTime = endTimeUnix ?? TimelineFarEndUnix();
        return ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", "[\"thumbnail\",\"resolution\",\"orientation\",\"video_convert\",\"video_meta\",\"address\"]"),
                new("geocoding_accept_language", "chs"),
                new("timeline_group_unit", "\"day\""),
                new("start_time", startTimeUnix.ToString()),
                new("end_time", endTime.ToString())
            ],
            cancellationToken);
    }

    public Task<IReadOnlyList<Photo>> ListTimelineDayPhotosAsync(
        int year,
        int month,
        int day,
        int offset,
        int limit,
        long? endTimeUnix = null,
        CancellationToken cancellationToken = default)
    {
        var (startTime, _) = TimelineDayRangeUnix(year, month, day);
        var endTime = endTimeUnix ?? TimelineFarEndUnix();
        return ListBrowseItemsAsync(
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", "[\"thumbnail\",\"resolution\",\"orientation\",\"video_convert\",\"video_meta\",\"address\"]"),
                new("geocoding_accept_language", "chs"),
                new("timeline_group_unit", "\"day\""),
                new("start_time", startTime.ToString()),
                new("end_time", endTime.ToString())
            ],
            cancellationToken);
    }

    public Task<TimelineData?> GetTimelineAsync(
        string timelineGroupUnit = "day",
        string? type = null,
        int? personId = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("timeline_group_unit", $"\"{timelineGroupUnit}\"")
        };
        if (!string.IsNullOrEmpty(type))
            fields.Add(new KeyValuePair<string, string>("type", $"\"{type}\""));
        if (personId.HasValue)
            fields.Add(new KeyValuePair<string, string>("person_id", personId.Value.ToString()));

        return _client.PostAppFormAsync<TimelineData>(
            "SYNO.Foto.Browse.Timeline",
            _client.GetMaxApiVersion("SYNO.Foto.Browse.Timeline", 5),
            "get",
            fields,
            cancellationToken);
    }

    private async Task<IReadOnlyList<Photo>> ListBrowsePhotosAsync(
        string api,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var parsed = await _client.PostAppFormAsync<ListObject<Photo>>(
            api,
            _client.GetMaxApiVersion(api, 2),
            "list",
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", ThumbnailAdditional)
            ],
            cancellationToken);
        return parsed?.List?.ToList() ?? [];
    }

    private Task<IReadOnlyList<Photo>> ListBrowseItemsAsync(
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken) =>
        ListBrowseItemsInternalAsync(_client, "SYNO.Foto.Browse.Item", fields, cancellationToken);

    internal static async Task<IReadOnlyList<Photo>> ListBrowseItemsInternalAsync(
        SynologyClient client,
        string api,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var parsed = await client.PostAppFormAsync<ListObject<Photo>>(
            api,
            client.GetMaxApiVersion(api, 5),
            "list",
            fields,
            cancellationToken);
        return parsed?.List?.ToList() ?? [];
    }

    private async Task<IReadOnlyList<Photo>> ListBrowseItemsAsync(
        string api,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken) =>
        await ListBrowseItemsInternalAsync(_client, api, fields, cancellationToken);

    private async Task<IReadOnlyList<BrowseAlbumItem>> ListBrowseAlbumItemsAsync(
        string api,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var version = api switch
        {
            "SYNO.Foto.Browse.Concept" => _client.GetMaxApiVersion(api, 2),
            _ => _client.GetMaxApiVersion(api, 1)
        };
        var parsed = await _client.PostAppFormAsync<ListObject<BrowseAlbumItem>>(
            api,
            version,
            "list",
            fields,
            cancellationToken);
        return parsed?.List?.ToList() ?? [];
    }

    internal static (long Start, long End) TimelineDayRangeUnix(int year, int month, int day)
    {
        var startLocal = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local);
        var endExclusive = startLocal.AddDays(1);
        return (
            new DateTimeOffset(startLocal).ToUnixTimeSeconds(),
            new DateTimeOffset(endExclusive).ToUnixTimeSeconds());
    }

    /// <summary>HAR 时间线批量拉取使用较远的 end_time，客户端再按日分组。</summary>
    internal static long TimelineFarEndUnix()
    {
        var endLocal = DateTime.Now.Date.AddYears(2);
        return new DateTimeOffset(endLocal).ToUnixTimeSeconds();
    }
}
