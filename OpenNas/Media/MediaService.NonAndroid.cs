#if !ANDROID

namespace OpenNas.Media;

public partial class MediaService
{
    public partial Task<List<string>> GetMediasAsync(string albumName) =>
        Task.FromResult(new List<string>());

    public partial Task<Dictionary<string, string>> GetImageAsync3() =>
        Task.FromResult(new Dictionary<string, string>());

    public partial Task<List<string>> GetVideosFromAlbumAsync(string albumName) =>
        Task.FromResult(new List<string>());
}

#endif
