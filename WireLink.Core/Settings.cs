using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WireLink.Core;

public sealed record Settings
{
    public string Transport { get; init; } = "USB";
    public string Host { get; init; } = "192.168.4.1";
    public string DapPort { get; init; } = "5555";
    public string SerialPort { get; init; } = "5556";
    public string BleName { get; init; } = "ESP-SWD";
    public string BleAddress { get; init; } = "";
    public string UsbId { get; init; } = "";
    public string Target { get; init; } = "stm32f103rc";
    public string Frequency { get; init; } = "1000";
    public string ConnectMode { get; init; } = "under-reset";
    public string Pack { get; init; } = "";
    public string Image { get; init; } = "";
    public string BaseAddress { get; init; } = "0x08000000";
    public string EraseMode { get; init; } = "sector";
    public bool ResetAfter { get; init; } = true;
    public string ComPort { get; init; } = "";
    public string WifiPort { get; init; } = "";
    public string Baud { get; init; } = "115200";
    public string Newline { get; init; } = "CRLF";
    public bool HexSend { get; init; }
    public bool HexReceive { get; init; }
    public string OtaImage { get; init; } = "";
    public string OtaSerial { get; init; } = "";
    public bool VerifyOnly { get; init; }
    public string Theme { get; init; } = "system";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static Settings Load(string path)
    {
        Settings? Read(string file)
        {
            try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Json); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }
        var current = Read(path);
        if (current != null)
        {
            try { File.Delete(path + ".tmp"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return current;
        }
        var recovered = Read(path + ".tmp");
        if (recovered == null) return new();
        try { File.Move(path + ".tmp", path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return recovered;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(JsonSerializer.Serialize(this, Json));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, true);
    }
}

public static class Validation
{
    public static long Number(string? text, string label, long min, long max)
    {
        text = text?.Trim() ?? "";
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!long.TryParse(hex ? text[2..] : text,
                hex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw new ArgumentException($"{label}须为 {min}～{max} 范围内的整数");
        return value;
    }

    public static string FilePath(string? path, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new ArgumentException("请选择存在的文件");
        if (!extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new ArgumentException("支持的文件类型：" + string.Join(" / ", extensions));
        if (new FileInfo(path).Length == 0) throw new ArgumentException("文件为空");
        return Path.GetFullPath(path);
    }

    public static void Link(Settings s)
    {
        if (s.Transport is not ("USB" or "WiFi" or "BLE")) throw new ArgumentException("请选择 USB、WiFi 或 BLE");
        if (s.Transport == "WiFi")
        {
            if (string.IsNullOrWhiteSpace(s.Host) || Regex.IsMatch(s.Host, @"[\s/:]"))
                throw new ArgumentException("请输入设备 IPv4 地址或主机名，端口单独填写");
            Number(s.DapPort, "DAP 端口", 1, 65535);
        }
        if (s.Transport == "BLE" && string.IsNullOrWhiteSpace(s.BleName) && string.IsNullOrWhiteSpace(s.BleAddress))
            throw new ArgumentException("请填写蓝牙设备名称或地址");
    }

    public static byte[] SerialData(string text, bool hex, string newline)
    {
        byte[] data;
        if (hex)
        {
            try { data = Convert.FromHexString(Regex.Replace(text, @"\s", "")); }
            catch (FormatException) { throw new ArgumentException("HEX 格式示例：01 A5 FF 0D 0A"); }
        }
        else data = Encoding.UTF8.GetBytes(text + (newline == "CRLF" ? "\r\n" : newline == "LF" ? "\n" : ""));
        if (data.Length == 0) throw new ArgumentException("发送内容为空");
        return data;
    }
}
