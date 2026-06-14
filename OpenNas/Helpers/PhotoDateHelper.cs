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
}

public class PhotoDateGroup : List<Photo>
{
    public PhotoDateGroup(string dateLabel, IEnumerable<Photo> photos) : base(photos)
    {
        DateLabel = dateLabel;
    }

    public string DateLabel { get; }
}
