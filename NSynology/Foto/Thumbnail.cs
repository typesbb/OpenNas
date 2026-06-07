using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NSynology.Foto
{
    public class Thumbnail
    {
        [JsonPropertyName("cache_key")]
        public string CacheKey { get; set; }
        public string M { get; set; }
        public string Preview { get; set; }
        public string Sm { get; set; }
        [JsonPropertyName("unit_id")]
        public int UnitId { get; set; }
        public string Xl { get; set; }
    }
}
