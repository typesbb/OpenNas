using System.Text.Json.Serialization;

namespace NSynology.Foto
{
    /// <summary>
    /// Photo model
    /// </summary>
    public class Photo
    {
        /// <summary>
        /// Name of the file as stored on the Synology filesystem
        /// </summary>
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        /// <summary>
        /// Size of the file
        /// </summary>
        [JsonPropertyName("filesize")]
        public long FileSize { get; set; }

        /// <summary>
        /// Folder identifier
        /// </summary>
        [JsonPropertyName("folder_id")]
        public int FolderId { get; set; }

        /// <summary>
        /// File identifier
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        //[JsonPropertyName("indexed_time")]
        //public int IndexedTime { get; set; }

        [JsonPropertyName("indexed_time")]
        public long IndexedTime { get; set; }

        /// <summary>
        /// User identifier of the owner
        /// </summary>
        [JsonPropertyName("owner_user_id")]
        public int OwnerUserId { get; set; }

        /// <summary>
        /// Time of the photo
        /// </summary>
        [JsonPropertyName("time")]
        public int Time { get; set; }

        /// <summary>
        /// File type
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonIgnore]
        public bool IsVideo => string.Equals(Type, "video", StringComparison.OrdinalIgnoreCase);

        public Additional Additional { get; set; }
    }
}
