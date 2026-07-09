using NSynology.Foto;

namespace OpenNas.Helpers;

public static class PhotoDateHelper
{
    public static string FormatGroupLabel(long unixTimeSeconds)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(NormalizeUnixTime(unixTimeSeconds)).LocalDateTime;
        if (dt.Year == DateTime.Now.Year)
            return $"{dt.Month}月{dt.Day}日";

        return $"{dt.Year}年{dt.Month}月{dt.Day}日";
    }

    /// <summary>
    /// 按服务器返回顺序分组；仅调整分组标题的先后，组内保持 API 分页顺序。
    /// </summary>
    public static IEnumerable<PhotoDateGroup> GroupByDate(
        IEnumerable<Photo> photos,
        bool sortDescending)
    {
        var list = photos as IList<Photo> ?? photos.ToList();
        if (list.Count == 0)
            return [];

        var groups = list
            .GroupBy(p => DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeSeconds(NormalizeUnixTime(p.Time)).LocalDateTime))
            .Select(g => new { g.Key, Items = (IEnumerable<Photo>)g });

        var orderedGroups = sortDescending
            ? groups.OrderByDescending(g => g.Key)
            : groups.OrderBy(g => g.Key);

        return orderedGroups.Select(g =>
            new PhotoDateGroup(FormatGroupLabel(g.Items.First().Time), g.Items));
    }

    internal static long NormalizeUnixTime(long time) =>
        time > 10_000_000_000L ? time / 1000L : time;

    public static bool TryGetLocalDay(Photo photo, out int year, out int month, out int day)
    {
        var unix = photo.Time > 0 ? photo.Time : NormalizeUnixTime(photo.IndexedTime);
        if (unix <= 0)
        {
            year = month = day = 0;
            return false;
        }

        var dt = DateTimeOffset.FromUnixTimeSeconds(NormalizeUnixTime(unix)).LocalDateTime;
        year = dt.Year;
        month = dt.Month;
        day = dt.Day;
        return true;
    }

    public static bool BelongsToLocalDay(Photo photo, int year, int month, int day) =>
        TryGetLocalDay(photo, out var y, out var m, out var d) &&
        y == year && m == month && d == day;
}

public class PhotoDateGroup : List<Photo>
{
    public PhotoDateGroup(string dateLabel, IEnumerable<Photo> photos) : base(photos)
    {
        DateLabel = dateLabel;
    }

    public string DateLabel { get; }
}
