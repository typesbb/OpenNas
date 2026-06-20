namespace OpenNas.Core.Models;

public class NasProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "NAS";
    public string BaseUrl { get; set; } = "";
    public NetworkKind NetworkKind { get; set; } = NetworkKind.Lan;
}
