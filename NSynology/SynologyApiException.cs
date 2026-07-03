namespace NSynology;

public sealed class SynologyApiException : Exception
{
    private static readonly HashSet<int> ReLoginErrorCodes = [105, 106, 107, 119];

    public int ErrorCode { get; }

    public bool RequiresReLogin => ReLoginErrorCodes.Contains(ErrorCode);

    public SynologyApiException(string message, int errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
