namespace NSynology.Foto;

public class FotoTeamApi(SynologyClient synologyClient)
{
    private readonly SynologyClient _client = synologyClient;
    private const string ThumbnailAdditional = "[\"thumbnail\"]";

    public Task<TimelineData?> GetTimelineAsync(
        string timelineGroupUnit = "day",
        string? type = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("timeline_group_unit", $"\"{timelineGroupUnit}\"")
        };
        if (!string.IsNullOrEmpty(type))
            fields.Add(new KeyValuePair<string, string>("type", $"\"{type}\""));

        return _client.PostAppFormAsync<TimelineData>(
            "SYNO.FotoTeam.Browse.Timeline",
            _client.GetMaxApiVersion("SYNO.FotoTeam.Browse.Timeline", 5),
            "get",
            fields,
            cancellationToken);
    }

    public Task<IReadOnlyList<Photo>> ListRecentlyAddedAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowsePhotosAsync("SYNO.FotoTeam.Browse.RecentlyAdded", offset, limit, cancellationToken);

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
            new("additional", ThumbnailAdditional),
            new("show_hidden", "false")
        };
        if (showMore)
            fields.Add(new KeyValuePair<string, string>("show_more", "true"));

        return ListBrowseAlbumItemsAsync("SYNO.FotoTeam.Browse.Person", fields, cancellationToken);
    }

    public Task<IReadOnlyList<BrowseAlbumItem>> ListConceptsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseAlbumItemsAsync(
            "SYNO.FotoTeam.Browse.Concept",
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
            "SYNO.FotoTeam.Browse.Geocoding",
            [
                new("offset", offset.ToString()),
                new("limit", limit.ToString()),
                new("additional", ThumbnailAdditional),
                new("accept_language", "chs")
            ],
            cancellationToken);

    public Task<IReadOnlyList<BrowseAlbumItem>> ListGeneralTagsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListBrowseAlbumItemsAsync(
            "SYNO.FotoTeam.Browse.GeneralTag",
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
                new("geocoding_accept_language", "chs"),
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
                new("geocoding_accept_language", "chs"),
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
                new("geocoding_accept_language", "chs"),
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
                new("geocoding_accept_language", "chs"),
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
        var endTime = endTimeUnix ?? FotoBrowseApi.TimelineFarEndUnix();
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
        var (startTime, _) = FotoBrowseApi.TimelineDayRangeUnix(year, month, day);
        var endTime = endTimeUnix ?? FotoBrowseApi.TimelineFarEndUnix();
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

    private async Task<IReadOnlyList<Photo>> ListBrowsePhotosAsync(
        string api,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var parsed = await _client.PostAppFormAsync<ListObject<Photo>>(
            api,
            _client.GetMaxApiVersion(api, 4),
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
        FotoBrowseApi.ListBrowseItemsInternalAsync(
            _client,
            "SYNO.FotoTeam.Browse.Item",
            fields,
            cancellationToken);

    private async Task<IReadOnlyList<BrowseAlbumItem>> ListBrowseAlbumItemsAsync(
        string api,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var version = api switch
        {
            "SYNO.FotoTeam.Browse.Concept" => _client.GetMaxApiVersion(api, 2),
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

    public async Task<Stream> GetThumbnailAsync(
        int id,
        string cacheKey,
        string size = "sm",
        CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.FotoTeam.Thumbnail", 1);
        var url =
            $"{SynologyClient.DsmWebApiEntry}?api=SYNO.FotoTeam.Thumbnail&version={version}&method=get&id={id}&size={size}&cache_key={cacheKey}&type=unit&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    public async Task<Stream> GetDownloadPhotoAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        var version = _client.GetMaxApiVersion("SYNO.FotoTeam.Download", 1);
        var url =
            $"{SynologyClient.DsmWebApiEntry}?api=SYNO.FotoTeam.Download&version={version}&method=download&unit_id=[{photo.Id}]&{{0}}";
        return await _client.GetStreamAsync(url, cancellationToken);
    }

    public string GetDownloadUrl(Photo photo)
    {
        var version = _client.GetMaxApiVersion("SYNO.FotoTeam.Download", 1);
        var url =
            $"{SynologyClient.DsmWebApiEntry}?api=SYNO.FotoTeam.Download&version={version}&method=download&unit_id=[{photo.Id}]&{{0}}";
        return _client.BuildApiUri(_client.BuildAuthenticatedApiUrl(url)).AbsoluteUri;
    }
}
