using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NSynology.Foto
{
    public class Permission
    {
        [JsonPropertyName("db_id")]
        public int DbId { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
        public string Type { get; set; }
    }
}
