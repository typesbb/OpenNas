using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NSynology.Foto
{
    public class Metadata
    {
        public int Duration { get; set; }
        public int Orientation { get; set; }
        [JsonPropertyName("frame_bitrate")]
        public int FrameBitrate { get; set; }
        [JsonPropertyName("video_bitrate")]
        public int VideoBitrate { get; set; }
        [JsonPropertyName("audio_bitrate")]
        public int AudioBitrate { get; set; }
        public float Framerate { get; set; }
        [JsonPropertyName("resolution_x")]
        public int ResolutionX { get; set; }
        [JsonPropertyName("resolution_y")]
        public int ResolutionY { get; set; }
        [JsonPropertyName("video_codec")]
        public string VideoCodec { get; set; }
        [JsonPropertyName("audio_codec")]
        public string AudioCodec { get; set; }
        [JsonPropertyName("container_type")]
        public string ContainerType { get; set; }
        [JsonPropertyName("video_profile")]
        public int VideoProfile { get; set; }
        [JsonPropertyName("video_level")]
        public int VideoLevel { get; set; }
        [JsonPropertyName("audio_frequency")]
        public int AudioFrequency { get; set; }
        [JsonPropertyName("audio_channel")]
        public int AudioChannel { get; set; }
    }
}
