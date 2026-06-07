using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NSynology
{
    public class ListObject<T>
    {
        [JsonPropertyName("list")]
        public IEnumerable<T> List { get; set; }
    }
}
