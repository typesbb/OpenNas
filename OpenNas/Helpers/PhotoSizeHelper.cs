using NSynology.Foto;

namespace OpenNas.Helpers;

/// <summary>
/// 照片文件大小区间分组。覆盖常见手机照片、RAW/全景，以及高清视频（1 GB / 2 GB 乃至更大）。
/// </summary>
public static class PhotoSizeHelper
{
    private static readonly SizeBucket[] Buckets =
    [
        new(0, 500 * 1024, "500 KB 以下"),
        new(500 * 1024, 2 * 1024 * 1024, "500 KB – 2 MB"),
        new(2 * 1024 * 1024, 10 * 1024 * 1024, "2 – 10 MB"),
        new(10 * 1024 * 1024, 30 * 1024 * 1024, "10 – 30 MB"),
        new(30 * 1024 * 1024, 100 * 1024 * 1024, "30 – 100 MB"),
        new(100 * 1024 * 1024, 300 * 1024 * 1024, "100 – 300 MB"),
        new(300 * 1024 * 1024, 500 * 1024 * 1024, "300 – 500 MB"),
        new(500 * 1024 * 1024, 1024L * 1024 * 1024, "500 MB – 1 GB"),
        new(1024L * 1024 * 1024, 2L * 1024 * 1024 * 1024, "1 – 2 GB"),
        new(2L * 1024 * 1024 * 1024, 5L * 1024 * 1024 * 1024, "2 – 5 GB"),
        new(5L * 1024 * 1024 * 1024, long.MaxValue, "5 GB 以上")
    ];

    /// <summary>
    /// 按服务器返回顺序分组；仅调整分组标题的先后，组内保持 API 分页顺序。
    /// </summary>
    public static IEnumerable<PhotoDateGroup> GroupBySize(IEnumerable<Photo> photos, bool sortDescending)
    {
        var list = photos as IList<Photo> ?? photos.ToList();
        if (list.Count == 0)
            return [];

        var grouped = list.GroupBy(p => GetBucketIndex(p.FileSize));

        var ordered = sortDescending
            ? grouped.OrderByDescending(g => g.Key)
            : grouped.OrderBy(g => g.Key);

        return ordered.Select(g => new PhotoDateGroup(Buckets[g.Key].Label, g));
    }

    public static string GetBucketLabel(long bytes) => Buckets[GetBucketIndex(bytes)].Label;

    private static int GetBucketIndex(long bytes)
    {
        for (var i = 0; i < Buckets.Length; i++)
        {
            if (bytes >= Buckets[i].Min && bytes < Buckets[i].Max)
                return i;
        }

        return Buckets.Length - 1;
    }

    private readonly record struct SizeBucket(long Min, long Max, string Label);
}
