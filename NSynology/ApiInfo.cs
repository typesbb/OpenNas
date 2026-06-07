using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NSynology
{
    public class ApiInfo
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }
        [JsonPropertyName("minVersion")]
        public int MinVersion { get; set; }
        [JsonPropertyName("maxVersion")]
        public int MaxVersion { get; set; }
    }
}
