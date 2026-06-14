using NSynology.Foto;

namespace OpenNas.Helpers;

/// <summary>
/// 照片文件大小区间分组。区间参考常见文件筛选预设（2 / 10 / 20 MB 档）
/// 并结合手机照片、RAW/全景/高清视频的常见体量做了扩展。
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
        new(100 * 1024 * 1024, long.MaxValue, "100 MB 以上")
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
