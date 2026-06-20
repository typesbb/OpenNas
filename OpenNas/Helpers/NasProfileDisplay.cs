using OpenNas.Core.Models;

namespace OpenNas.Helpers;

public static class NasProfileDisplay
{
    public static string KindLabel(NetworkKind kind) =>
        kind == NetworkKind.Lan ? "内网" : "外网";

    public static string FormatTitle(NasProfile profile)
    {
        var kind = KindLabel(profile.NetworkKind);
        var name = (profile.DisplayName ?? "NAS").Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, kind, StringComparison.Ordinal))
            return kind;

        if (name.StartsWith(kind + " ·", StringComparison.Ordinal)
            || name.StartsWith(kind + "·", StringComparison.Ordinal))
            return name;

        return $"{kind} · {name}";
    }
}
