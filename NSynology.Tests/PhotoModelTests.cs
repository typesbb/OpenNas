using System.Text.Json;
using NSynology.Foto;
using Xunit;

namespace NSynology.Tests;

public class PhotoModelTests
{
    [Fact]
    public void Photo_deserializes_large_filesize_and_video_type()
    {
        const string json = """
            {
              "id": 1,
              "filename": "clip.mp4",
              "filesize": 3221225472,
              "time": 1700000000,
              "indexed_time": 1700000000000,
              "owner_user_id": 3,
              "folder_id": 10,
              "type": "video"
            }
            """;

        var photo = JsonSerializer.Deserialize<Photo>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(photo);
        Assert.Equal(3221225472L, photo.FileSize);
        Assert.True(photo.IsVideo);
    }
}
