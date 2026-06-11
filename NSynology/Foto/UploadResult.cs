namespace NSynology.Foto;

public class UploadResult
{
    public bool Success { get; set; }
    public int PhotoId { get; set; }
    public string RawResponse { get; set; } = "";
    public bool VerifiedOnServer { get; set; }

    /// <summary>官方响应 data.action，如 new / ignore。</summary>
    public string Action { get; set; } = "";

    /// <summary>库中已有同名文件被跳过实际上传，但已确认在目标相册。</summary>
    public bool SkippedAsDuplicate { get; set; }
}
