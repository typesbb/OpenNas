namespace OpenNas.Services;

/// <summary>
/// 当前网格/大图加载应使用的照片库（个人 vs 共享）。由时间线、探索详情、预览页在加载前设置。
/// </summary>
public static class PhotosMediaLibraryScope
{
    public static PhotosLibrary Current { get; set; } = PhotosLibrary.PersonalSpace;

    public static IDisposable Use(PhotosLibrary library)
    {
        var previous = Current;
        Current = library;
        return new ScopeRestore(previous);
    }

    private sealed class ScopeRestore(PhotosLibrary previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}
