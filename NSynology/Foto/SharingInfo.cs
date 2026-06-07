using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NSynology.Foto
{
    public class SharingInfo
    {
        [JsonPropertyName("enable_password")]
        public bool EnablePassword { get; set; }
        public int Expiration { get; set; }
        [JsonPropertyName("is_expired")]
        public bool IsExpired { get; set; }
        public int Mtime { get; set; }
        //"owner": {
        //    "id": -1,
        //    "name": ""
        //},
        public string Passphrase { get; set; }
        public Permission[] Permission { get; set; }
        [JsonPropertyName("privacy_type")]
        public string PrivacyType { get; set; }
        [JsonPropertyName("sharing_link")]
        public string SharingLink { get; set; }
        public string Type { get; set; }
    }
}
