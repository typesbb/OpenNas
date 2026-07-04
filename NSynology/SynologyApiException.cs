namespace NSynology;

public sealed class SynologyApiException : Exception
{
    private static readonly HashSet<int> ReLoginErrorCodes = [106, 107];

    public int ErrorCode { get; }

    public bool RequiresReLogin => ReLoginErrorCodes.Contains(ErrorCode);

    public SynologyApiException(string message, int errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }

    public static bool IsSessionExpired(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SynologyApiException api && api.RequiresReLogin)
                return true;
        }

        return false;
    }

    internal static void ThrowIfApiError(string message, int errorCode)
    {
        var ex = new SynologyApiException(message, errorCode);
        if (ex.RequiresReLogin)
            SynologyManager.NotifySessionExpired(ex);
        throw ex;
    }
}
