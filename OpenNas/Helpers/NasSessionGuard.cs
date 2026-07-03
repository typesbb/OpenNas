using NSynology;
using OpenNas.Core.Services;

namespace OpenNas.Helpers;

public static class NasSessionGuard
{
    public static bool RequiresReLogin(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SynologyApiException api && api.RequiresReLogin)
                return true;
        }

        return NasSessionHelper.RequiresReLogin(ex);
    }

    public static Task<bool> HandleIfNeededAsync(Exception ex) =>
        AppServices.GetRequired<Services.ConnectionService>().TryHandleSessionFailureAsync(ex);
}
