namespace NSynology;

public class SynologyUploadException : Exception
{
    public int ErrorCode { get; }
    public string RawResponse { get; }

    public SynologyUploadException(int errorCode, string message, string rawResponse = "")
        : base(message)
    {
        ErrorCode = errorCode;
        RawResponse = rawResponse ?? "";
    }
}
