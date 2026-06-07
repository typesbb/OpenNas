using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NSynology.Foto
{
    public class AlbumObject
    {
        [JsonPropertyName("album")]
        public Album Album { get; set; }
    }
}