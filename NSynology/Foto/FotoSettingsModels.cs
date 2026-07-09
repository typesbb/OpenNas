using System.Text.Json.Serialization;

namespace NSynology.Foto;

public class PhotoCategory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

public class TeamSpaceSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("enable_person")]
    public bool EnablePerson { get; set; }

    [JsonPropertyName("enable_concept")]
    public bool EnableConcept { get; set; }
}

public class AlbumListOrder
{
    [JsonPropertyName("album_list_sort_by")]
    public string AlbumListSortBy { get; set; } = "start_time";

    [JsonPropertyName("album_list_sort_direction")]
    public string AlbumListSortDirection { get; set; } = "desc";

    [JsonPropertyName("shared_with_me_sort_by")]
    public string SharedWithMeSortBy { get; set; } = "share_modify_time";

    [JsonPropertyName("shared_with_me_sort_direction")]
    public string SharedWithMeSortDirection { get; set; } = "desc";
}

public class AlbumListDisplay
{
    [JsonPropertyName("album_display_type")]
    public string AlbumDisplayType { get; set; } = "all_album";
}

public class TimelineDayEntry
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }
}

public class TimelineSection
{
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("list")]
    public List<TimelineDayEntry> List { get; set; } = [];
}

public class TimelineData
{
    [JsonPropertyName("section")]
    public List<TimelineSection> Sections { get; set; } = [];
}

public class BrowseAlbumItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("cover")]
    public int Cover { get; set; }

    [JsonPropertyName("show")]
    public bool Show { get; set; } = true;

    [JsonPropertyName("additional")]
    public Additional? Additional { get; set; }
}
