using NSynology;

namespace OpenNas.Platforms.Android;

internal static class SynologyPhotosDeviceId
{
    /// <summary>恢复 NAS 加密登录下发的 <c>did</c>（勿用 AndroidId 冒充官方设备 id）。</summary>
    public static void ApplyTo(SynologyClient client) => client.RestorePersistedPhotosDeviceId();
}
