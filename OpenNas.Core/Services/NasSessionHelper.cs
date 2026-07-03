namespace OpenNas.Core.Services;

public static class NasSessionHelper
{
    public static bool IsSessionError(Exception ex) => RequiresReLogin(ex);

    public static bool RequiresReLogin(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (IsReLoginMessage(e.Message))
                return true;
        }

        return false;
    }

    public static bool IsReLoginMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return message.Contains("SID not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Session timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Session interrupted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Synology Photos API", StringComparison.OrdinalIgnoreCase)
            || message.Contains("当前 DSM 会话", StringComparison.Ordinal)
            || message.Contains("会话无效", StringComparison.Ordinal)
            || message.Contains("会话已过期", StringComparison.Ordinal)
            || message.Contains("请重新登录", StringComparison.Ordinal)
            || message.Contains("错误码 106", StringComparison.Ordinal)
            || message.Contains("错误码 107", StringComparison.Ordinal)
            || message.Contains("错误码 119", StringComparison.Ordinal)
            || message.Contains("(code 106)", StringComparison.OrdinalIgnoreCase)
            || message.Contains("(code 107)", StringComparison.OrdinalIgnoreCase)
            || message.Contains("code 119", StringComparison.OrdinalIgnoreCase)
            || message.Contains("code 106", StringComparison.OrdinalIgnoreCase)
            || message.Contains("code 107", StringComparison.OrdinalIgnoreCase);
    }
}
