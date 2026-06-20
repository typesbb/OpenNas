namespace OpenNas.Core.Services;

public static class NasSessionHelper
{
    public static bool IsSessionError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (IsSessionMessage(e.Message))
                return true;
        }

        return false;
    }

    public static bool IsSessionMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return message.Contains("SID not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Session timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("错误码 106", StringComparison.Ordinal)
            || message.Contains("错误码 107", StringComparison.Ordinal)
            || message.Contains("(code 106)", StringComparison.Ordinal)
            || message.Contains("(code 107)", StringComparison.Ordinal)
            || message.Contains("code 119", StringComparison.OrdinalIgnoreCase);
    }
}
