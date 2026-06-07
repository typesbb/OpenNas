using System.Text.Json;
using NSynology;

var cfg = NSynology.Tests.NasIntegrationSettings.Load();
var client = new SynologyClient(cfg.BaseUrl);
await client.Auth.LoginAsync(cfg.Username, cfg.Password);
var deviceId = client.GetPhotosDeviceId();
var check = NSynology.SynologyClient.FormatPhotosCheckValue("93b885adfe0da089cdf634904fd59f71");

async Task Dump(string label, string url)
{
    try {
        var body = await client.GetAsyncRawAsync(url);
        Console.WriteLine($"{label}: success={body.Success} err={body.Error?.Code} data={JsonSerializer.Serialize(body.Data)}");
    } catch (Exception ex) { Console.WriteLine($"{label}: EX {ex.Message}"); }
}

await Dump("API.Info Upload", "webapi/entry.cgi?api=SYNO.API.Info&version=1&method=query&query=SYNO.Foto.Upload.Item");
await Dump("API.Info UserInfo", "webapi/entry.cgi?api=SYNO.API.Info&version=1&method=query&query=SYNO.Foto.UserInfo");
await Dump("UserInfo me v1", "webapi/entry.cgi?api=SYNO.Foto.UserInfo&version=1&method=me&{0}");
await Dump("UserInfo me v2", "webapi/entry.cgi?api=SYNO.Foto.UserInfo&version=2&method=me&{0}");
await Dump("Setting.User get", "webapi/entry.cgi?api=SYNO.Foto.Setting.User&version=1&method=get&{0}");
await Dump("Setting.MobileCompatibility", "webapi/entry.cgi?api=SYNO.Foto.Setting.MobileCompatibility&version=1&method=get&{0}");
await Dump("grantByUser main", $"webapi/entry.cgi?api=SYNO.Foto.Upload.Item&version=1&method=grantByUser&device_id={Uri.EscapeDataString(deviceId)}&{{0}}");
await Dump("backup_check+check main", $"webapi/entry.cgi?api=SYNO.Foto.Upload.Item&version=1&method=backup_check&device_id={Uri.EscapeDataString(deviceId)}&name=probe.jpg&size=1&check={check}&{{0}}");

await client.Auth.EstablishPhotosUploadSessionForUploadAsync();
Console.WriteLine($"PhotosUploadSid={client.PhotosUploadSid ?? "(none)"}");
await Dump("grantByUser photos", $"webapi/entry.cgi?api=SYNO.Foto.Upload.Item&version=1&method=grantByUser&device_id={Uri.EscapeDataString(deviceId)}&" + "{0}".Replace("{0}", client.BuildPhotosUploadSessionQuery()));
