namespace OpenNas.Helpers;

public static class NetworkHelper
{
    public static bool IsOnWifi() =>
        Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
}
