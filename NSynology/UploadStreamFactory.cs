namespace NSynology;

/// <summary>每次上传前重新打开文件流（避免 HttpClient 关闭 Stream 后无法重试）。</summary>
public delegate Task<Stream> UploadStreamFactory(CancellationToken cancellationToken);
