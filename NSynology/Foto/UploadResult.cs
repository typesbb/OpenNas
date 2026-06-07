namespace NSynology.Foto;

public class UploadResult
{
    public bool Success { get; set; }
    public int PhotoId { get; set; }
    public string RawResponse { get; set; } = "";
    public bool VerifiedOnServer { get; set; }

    /// <summary>Search 命中同名同大小，未实际上传。</summary>
    public bool SkippedAsDuplicate { get; set; }
}
