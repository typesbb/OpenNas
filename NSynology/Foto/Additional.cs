using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NSynology.Foto
{
    public class Additional
    {
        public int Orientation { get; set; }
        [JsonPropertyName("orientation_original")]
        public int OrientationOriginal { get; set; }
        [JsonPropertyName("provider_user_id")]
        public int ProviderUserId { get; set; }
        [JsonPropertyName("sharing_info")]
        public SharingInfo SharingInfo { get; set; }
        public Resolution Resolution { get; set; }
        public Thumbnail Thumbnail { get; set; }
        [JsonPropertyName("video_meta")]
        public Metadata VideoMeta { get; set; }
        [JsonPropertyName("video_convert")]
        public IEnumerable<VideoConvert> VideoConvert { get; set; }
    }
}
