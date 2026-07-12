namespace NSynology.Foto;

/// <summary>SYNO.Foto.Upload.Item v5 multipart <c>duplicate</c> 字段（JSON 引号字符串）。</summary>
public static class AppUploadDuplicate
{
    /// <summary>官方 App 抓包值；备份新文件时使用。已有条目重传会返回 <c>action=ignore</c> 且不更新缩略图。</summary>
    public const string Ignore = "\"ignore\"";

    /// <summary>冲突时重命名；修复已有条目坏缩略图时优先尝试（可能覆盖原条目 thumb）。</summary>
    public const string Rename = "\"rename\"";
}
