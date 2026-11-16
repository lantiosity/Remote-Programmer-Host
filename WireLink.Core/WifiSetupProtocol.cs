using System.Text;
using System.Text.Json.Nodes;

namespace WireLink.Core;

public static class WifiSetupProtocol
{
    // New baseline declares capability; older development firmware used version 1.3.0.
    public static bool SupportsUsbConfiguration(JsonObject info)
    {
        if (info["wifi_setup"] is JsonValue capability)
            return capability.TryGetValue<int>(out var level) && level >= 1;
        return Version.TryParse(info["version"]?.GetValue<string>(), out var version)
            && version >= new Version(1, 3, 0);
    }

    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Command(string id, string operation, string ssid = "", string password = "", int page = 0)
    {
        if (id.Length != 8 || id.Any(c => !"0123456789abcdef".Contains(c))) throw new ArgumentException("配网请求编号无效");
        if (operation is "status" or "scan" or "clear") return id + " " + operation;
        if (operation == "page" && page is >= 0 and <= 6) return $"{id} page {page}";
        if (operation != "save") throw new ArgumentException("配网操作无效");
        if (ssid.Contains('\0') || password.Contains('\0')) throw new ArgumentException("网络名称和密码不能包含空字符");
        var name = Utf8.GetBytes(ssid); var secret = Utf8.GetBytes(password);
        if (name.Length is < 1 or > 32) throw new ArgumentException("网络名称必须为 1～32 个 UTF-8 字节");
        if (secret.Length != 0 && secret.Length is < 8 or > 63) throw new ArgumentException("WiFi 密码须为 8～63 个 UTF-8 字节；开放网络请留空");
        return $"{id} save {Convert.ToHexString(name).ToLowerInvariant()} {(secret.Length == 0 ? "-" : Convert.ToHexString(secret).ToLowerInvariant())}";
    }

    public static JsonObject? ParseResponse(string text, string id)
    {
        var result = JsonNode.Parse(text)?.AsObject() ?? throw new IOException("配网响应为空");
        if (result["id"]?.GetValue<string>() != id || result["busy"]?.GetValue<bool>() == true) return null;
        if (result["ok"]?.GetValue<bool>() != true)
            throw new IOException(result["error"]?.GetValue<string>() switch {
                "window_closed" => "请先长按烧录器 BOOT 约 2 秒，开放配置窗口，再保存或清除 WiFi。",
                "device_busy" => "烧录器正忙，请等待升级或配置任务结束。",
                "scan_failed" => "网络扫描失败，请稍后重试。",
                "save_failed" => "WiFi 保存失败，请检查网络名称和密码。",
                _ => "烧录器拒绝配网请求，请检查固件版本或重试。"
            });
        if (result["ssid_hex"] is JsonValue value) result["ssid"] = Encoding.UTF8.GetString(Convert.FromHexString(value.GetValue<string>()));
        if (result["networks"] is JsonArray networks) foreach (var network in networks)
            network!["ssid"] = Encoding.UTF8.GetString(Convert.FromHexString(network["ssid_hex"]!.GetValue<string>()));
        return result;
    }
}
