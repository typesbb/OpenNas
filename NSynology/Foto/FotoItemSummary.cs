using System.Text.Json.Serialization;

namespace NSynology.Foto;

/// <summary>SYNO.Foto.Search.Search list_item 或 Browse.Item 列表项摘要。</summary>
public class FotoItemSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("filesize")]
    public long FileSize { get; set; }

    [JsonPropertyName("folder_id")]
    public int FolderId { get; set; }
}
