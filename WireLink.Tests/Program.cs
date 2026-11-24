using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using WireLink.App.Services;
using WireLink.Core;

// Explicit opt-in hardware harness; regular tests never update a physical board.
// --ota-hardware USB|WiFi SERIAL info|IMAGE [HOST]
if (args.Length >= 4 && args[0] == "--ota-hardware")
{
    var hardware = new Settings { Transport = args[1], OtaSerial = args[2], UsbId = args[2],
        OtaImage = args[3], Host = args.Length > 4 ? args[4] : "192.168.4.1" };
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var reported = -1;
    await new OtaService(Console.WriteLine, n => { var step = (int)n / 10;
        if (step != reported) { reported = step; Console.WriteLine($"{n:F1}%"); } },
        value => Console.WriteLine(value.ToJsonString(Settings.Json)))
        .RunAsync(hardware, args[3] != "info", timeout.Token);
    return 0;
}

if (args.Length >= 3 && args[0] == "--wifi-hardware")
{
    var settingsWifi = new Settings { BleAddress = args[2], WifiPort = args[2], UsbId = args[2] };
    using var wifiTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var result = await WifiSetupService.RunAsync(settingsWifi, new WifiSetupRequest(args[1], "status"), wifiTimeout.Token);
    Console.WriteLine(result.ToJsonString());
    return 0;
}

var temp = Path.Combine(Path.GetTempPath(), "wirelink-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var file = Path.Combine(temp, "中文 空格固件.bin");
File.WriteAllBytes(file, [1, 2, 3, 4]);
var settings = new Settings { UsbId = "ESP-SWD-TEST", Image = file };
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action test) => tests.Add((name, () => { test(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> test) => tests.Add((name, test));
static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected argument rejection"); }
static async Task Reject(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or OperationCanceledException) { return; } throw new Exception("Expected rejection"); }

Test("WiFi 配网命令按 UTF-8 编码，密码和特殊字符不会注入命令", () => {
    var command = WifiSetupProtocol.Command("abcdef01", "save", "中文 网络", "a\"\\ b123");
    var fields = command.Split(' ');
    Check(fields.Length == 4 && Encoding.UTF8.GetString(Convert.FromHexString(fields[2])) == "中文 网络");
    Check(Encoding.UTF8.GetString(Convert.FromHexString(fields[3])) == "a\"\\ b123");
    Check(!command.Contains("中文") && !command.Contains("a\""));
    Check(WifiSetupProtocol.Command("abcdef01", "save", "open", "").EndsWith(" -"));
});
Test("WiFi SSID 和密码按字节限制，拒绝空字符与非法操作", () => {
    Throws(() => WifiSetupProtocol.Command("00000000", "save", new string('中', 11), "12345678"));
    Throws(() => WifiSetupProtocol.Command("00000000", "save", "", "12345678"));
    Throws(() => WifiSetupProtocol.Command("00000000", "save", "name", "1234567"));
    Throws(() => WifiSetupProtocol.Command("00000000", "save", "name\0", "12345678"));
    Throws(() => WifiSetupProtocol.Command("00000000", "page", page: 7));
    Throws(() => WifiSetupProtocol.Command("bad", "clear"));
});
Test("WiFi 响应过滤旧请求、繁忙结果及配置窗口错误", () => {
    Check(WifiSetupProtocol.ParseResponse("{\"id\":\"old\",\"ok\":true}", "abcdef01") == null);
    Check(WifiSetupProtocol.ParseResponse("{\"id\":\"abcdef01\",\"busy\":true}", "abcdef01") == null);
    var response = WifiSetupProtocol.ParseResponse("{\"id\":\"abcdef01\",\"ok\":true,\"ssid_hex\":\"e4b8ade69687\"}", "abcdef01");
    Check(response!["ssid"]!.GetValue<string>() == "中文");
    try { WifiSetupProtocol.ParseResponse("{\"id\":\"abcdef01\",\"ok\":false,\"error\":\"window_closed\"}", "abcdef01"); throw new Exception("Expected closed window rejection"); }
    catch (IOException ex) { Check(ex.Message.Contains("BOOT")); }
});

Test("BIN 地址与中文路径保持为独立参数", () =>
{
    var args = PyOcdCommand.Build(settings, "flash");
    Check(args[^1] == file && args[args.IndexOf("--base-address") + 1] == "0x8000000");
});
Test("HEX 使用文件内地址，AXF 使用 ELF 加载器", () =>
{
    foreach (var ext in new[] { ".hex", ".axf" })
    {
        var path = Path.ChangeExtension(file, ext); File.WriteAllText(path, "test");
        var args = PyOcdCommand.Build(settings with { Image = path }, "flash");
        Check(!args.Contains("--base-address"));
        Check(ext != ".axf" || args.Contains("elf"));
    }
});
Test("WiFi / BLE 探针地址正确", () =>
{
    Check(PyOcdCommand.Build(settings with { Transport = "WiFi", Host = "192.168.1.12" }, "flash").Contains("remote:192.168.1.12:5555"));
    Check(PyOcdCommand.Build(settings with { Transport = "BLE" }, "flash", 54321).Contains("remote:127.0.0.1:54321"));
});
Test("拒绝缺少探针、非法时钟地址和通用 Flash 目标", () =>
{
    foreach (var bad in new[] { settings with { UsbId = "" }, settings with { Frequency = "0" }, settings with { Frequency = "50001" },
        settings with { BaseAddress = "0x100000000" }, settings with { Target = "cortex_m" }, settings with { Image = "absent.bin" } })
        Throws(() => PyOcdCommand.Build(bad, "flash"));
});
Test("检测目标命令独立，禁用复位时不恢复运行", () =>
{
    Check(PyOcdCommand.Build(settings, "inspect").Count(x => x == "-c") == 2);
    var command = PyOcdCommand.Build(settings with { ResetAfter = false }, "flash");
    Check(command.Contains("--no-reset") && command.Contains("resume_on_disconnect=false"));
});
Test("UTF-8 / HEX 收发编码及换行", () =>
{
    Check(Validation.SerialData("中文", false, "CRLF").SequenceEqual(Encoding.UTF8.GetBytes("中文\r\n")));
    Check(Validation.SerialData("00 A5 ff", true, "CRLF").SequenceEqual(new byte[] { 0, 0xA5, 0xFF }));
    Throws(() => Validation.SerialData("bad hex", true, "无"));
});
Test("深色主题保存并恢复，兼容旧配置和损坏文件", () =>
{
    var path = Path.Combine(temp, "settings.json");
    var colors = new ThemeColors("#CD80F1", "#141827", "#304662");
    var effects = new ThemeAppearance(0, 61, "0123456789abcdef0123456789abcdef", CardOpacity: 0,
        WindowAcrylic: true, CardFrosted: false, KeepAcrylicWhenInactive: true,
        SidebarOpacity: 72, SidebarFrosted: true, ControlTransparency: 35);
    (settings with { Theme = "dark", CustomTheme = colors, ThemeAppearance = effects }).Save(path);
    var loaded = Settings.Load(path);
    Check(loaded.Theme == "dark" && ThemeColors.Validated(loaded.CustomTheme) == colors);
    Check(loaded.ThemeAppearance == effects && loaded.ThemeAppearance.Normalize() == effects);
    Check(new ThemeAppearance(-1, 400, "../image.png", CardOpacity: 400).Normalize()
        == new ThemeAppearance(0, 100, CardOpacity: 100, WindowAcrylic: false, CardFrosted: false, SidebarOpacity: 0));
    Check(new ThemeAppearance(SidebarOpacity: -100, ControlTransparency: 120).Normalize() is
        { SidebarOpacity: 0, ControlTransparency: 100 });
    Check(ThemeColors.Validated(colors with { Accent = "#12ZZ99" }) == null);
    (loaded with { CustomTheme = null }).Save(path);
    Check(Settings.Load(path).CustomTheme == null);
    File.WriteAllText(path, "{\"usb_id\":\"OLD\"}");
    var legacy = Settings.Load(path);
    Check(legacy.Theme == "system" && legacy.UsbId == "OLD" && legacy.ThemeAppearance == new ThemeAppearance());
    File.WriteAllText(path, "{\"theme_appearance\":{\"opacity\":35,\"frosted\":true,\"card_frosted\":false}}");
    Check(Settings.Load(path).ThemeAppearance.Normalize() ==
        new ThemeAppearance(35, WindowAcrylic: true, CardFrosted: false, SidebarOpacity: 35));
    File.WriteAllText(path, "bad JSON"); Check(Settings.Load(path) == new Settings());
});
Test("ESP32 镜像检查芯片、项目名和应用描述", () =>
{
    var image = FirmwareImage.Parse(FakeOta.Image()); Check(image.Version == "2.0.0" && image.Sha256.Length == 64);
    var wrong = FakeOta.Image(); wrong[12] = 1; Throws(() => FirmwareImage.Parse(wrong));
    wrong = FakeOta.Image(); wrong[80] = (byte)'X'; Throws(() => FirmwareImage.Parse(wrong));
    Throws(() => FirmwareImage.Parse([0, 1, 2]));
});
Test("OTA 状态长度和版本严格检查", () =>
{
    var data = new byte[20]; data[0] = 1; data[1] = 3; data[2] = 2;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 123);
    Check(OtaStatus.Parse(data).Received == 123);
    try { OtaStatus.Parse([1]); throw new Exception("Expected protocol error"); } catch (InvalidDataException) { }
});
AsyncTest("OTA 按已提交偏移推进且校验后激活", async () =>
{
    var link = new FakeOta(); var updater = new OtaUpdater(link);
    await updater.UpdateAsync(FirmwareImage.Parse(FakeOta.Image()), "TEST", false, _ => { }, default);
    Check(link.Controls.SequenceEqual(new byte[] { 1, 3, 4 }));
    Check(updater.Activated && !updater.OwnsTransfer && link.DataCount > 1);
});
AsyncTest("OTA verify-only 不切换启动分区", async () =>
{
    var link = new FakeOta(); var updater = new OtaUpdater(link);
    await updater.UpdateAsync(FirmwareImage.Parse(FakeOta.Image()), "TEST", true, _ => { }, default);
    Check(link.Controls.SequenceEqual(new byte[] { 1, 3, 5 }) && !updater.Activated);
});
AsyncTest("OTA 窗口锁定、序列号不符与其他会话不发送 ABORT", async () =>
{
    foreach (var link in new[] { new FakeOta { Flags = 0 }, new FakeOta { State = 3 }, new FakeOta { Serial = "OTHER" } })
    {
        var updater = new OtaUpdater(link);
        await Reject(() => updater.UpdateAsync(FirmwareImage.Parse(FakeOta.Image()), "TEST", false, _ => { }, default));
        Check(link.Controls.Count == 0 && !updater.OwnsTransfer);
    }
});
AsyncTest("OTA 错误偏移清理会话且不激活", async () =>
{
    var link = new FakeOta { CorruptOffset = true };
    await Reject(() => new OtaUpdater(link).UpdateAsync(FirmwareImage.Parse(FakeOta.Image()), "TEST", false, _ => { }, default));
    Check(link.Controls.SequenceEqual(new byte[] { 1, 5 }));
});
AsyncTest("OTA 取消发送 ABORT，保留原启动分区", async () =>
{
    using var cancellation = new CancellationTokenSource();
    var link = new FakeOta { Cancel = cancellation };
    await Reject(() => new OtaUpdater(link).UpdateAsync(FirmwareImage.Parse(FakeOta.Image()), "TEST", false, _ => { }, cancellation.Token));
    Check(link.Controls.SequenceEqual(new byte[] { 1, 5 }));
});
AsyncTest("原生 C# TCP 串口双向收发和取消关闭", async () =>
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var received = new TaskCompletionSource<byte[]>();
    await using var serial = new SerialService { Received = bytes => received.TrySetResult(bytes) };
    try
    {
        var accept = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
        await serial.OpenAsync(new Settings { Transport = "WiFi", Host = "127.0.0.1", SerialPort = port.ToString() }, timeout.Token);
        using var peer = await accept; var stream = peer.GetStream();
        await stream.WriteAsync(new byte[] { 0xA5, 0xFF }, timeout.Token);
        Check((await received.Task.WaitAsync(timeout.Token)).SequenceEqual(new byte[] { 0xA5, 0xFF }));
        await serial.SendAsync([0, 1, 2], timeout.Token);
        var bytes = new byte[3]; await stream.ReadExactlyAsync(bytes, timeout.Token); Check(bytes.SequenceEqual(new byte[] { 0, 1, 2 }));
    }
    finally { listener.Stop(); }
});
AsyncTest("真实 pyOCD 芯片列表可由 C# 异步调用", async () =>
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    // Resolve from cwd too, so dotnet run and a directly invoked test exe both work.
    if (!Directory.Exists(Path.Combine(root, ".venv-ble"))) root = Environment.CurrentDirectory;
    if (!File.Exists(new PyOcdService(root, _ => { }).Python)) throw new Exception("pyOCD test engine missing: set WIRELINK_PYTHON or install local dependencies");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var result = await new PyOcdService(root, _ => { }).CatalogAsync("targets", settings, timeout.Token);
    Check(result.AsArray().Any(item => item?["name"]?.GetValue<string>() == "stm32f103rc"));
});

Test("USB 配网按能力识别新首版并兼容开发版", () => {
    Check(WifiSetupProtocol.SupportsUsbConfiguration(new JsonObject { ["version"] = "0.1.0", ["wifi_setup"] = 1 }));
    Check(WifiSetupProtocol.SupportsUsbConfiguration(new JsonObject { ["version"] = "1.3.0" }));
    Check(!WifiSetupProtocol.SupportsUsbConfiguration(new JsonObject { ["version"] = "0.1.0" }));
    Check(!WifiSetupProtocol.SupportsUsbConfiguration(new JsonObject { ["version"] = "1.2.0" }));
    Check(!WifiSetupProtocol.SupportsUsbConfiguration(new JsonObject { ["version"] = "1.3.0", ["wifi_setup"] = 0 }));
});

AsyncTest("USB 配网长命令正确分片且响应按偏移完整重组", async () => {
    var transport = new FakeWifiUsb();
    var command = Encoding.ASCII.GetBytes(WifiSetupProtocol.Command("1234abcd", "save", new string('n',32), new string('p',63)) + "\n");
    await transport.WifiWriteAsync(command, CancellationToken.None);
    Check(transport.Writes.Count > 1 && transport.Writes.All(x => x.Length <= 61));
    Check(transport.Writes[0][0] == 1 && transport.Writes.Skip(1).All(x => x[0] == 0));
    Check(transport.Writes.SelectMany(x => x.Skip(1)).SequenceEqual(command));
    transport.Response = Encoding.UTF8.GetBytes("{\"name\":\"" + new string('中',50) + "\"}");
    Check(await transport.WifiReadAsync(CancellationToken.None) == Encoding.UTF8.GetString(transport.Response));
    Check(transport.Offsets.SequenceEqual(Enumerable.Range(0, transport.Offsets.Count).Select(i => i*57)));
});

ReviewTests.Register(tests);
LocalLibraryTests.Register(tests, temp);
var failures = 0;
try
{
    foreach (var (name, run) in tests)
    {
        try { await run(); Console.WriteLine("PASS  " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL  " + name + "\n" + ex); }
    }
    Console.WriteLine($"\n{tests.Count - failures}/{tests.Count} passed");
}
finally
{
    // Exact, independently created temp directory; never traverse the workspace.
    Directory.Delete(temp, true);
}
return failures == 0 ? 0 : 1;

sealed class FakeWifiUsb : WiredOtaTransport
{
    public override int Mtu => 63;
    public readonly List<byte[]> Writes = [];
    public readonly List<int> Offsets = [];
    public byte[] Response = [];
    protected override Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (kind == 6) { Writes.Add(data); return Task.FromResult(Array.Empty<byte>()); }
        if (kind != 7) throw new Exception("Unexpected WiFi transport opcode");
        var offset = BinaryPrimitives.ReadUInt16LittleEndian(data); Offsets.Add(offset);
        return Task.FromResult(Response.Skip(offset).Take(57).ToArray());
    }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class FakeOta : IOtaTransport
{
    public int Mtu => 128;
    public string Serial = "TEST";
    public byte State = 1, Flags = 1, Op;
    public uint Received, Total;
    public bool CorruptOffset;
    public CancellationTokenSource? Cancel;
    public List<byte> Controls = [];
    public int DataCount;
    public Task<JsonObject> InfoAsync(CancellationToken ct) => Task.FromResult(new JsonObject
        { ["protocol"] = 1, ["project"] = "esp_swdlink", ["serial"] = Serial, ["slot_size"] = 3 * 1024 * 1024 });
    public Task<OtaStatus> StatusAsync(CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(new OtaStatus(State, Op, Flags, Received, Total, CorruptOffset && State == 7 ? 0x7003u : 0, 42)); }
    public Task ControlAsync(byte[] data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Controls.Add(data[0]); Op = data[0];
        State = data[0] switch { 1 => 3, 3 => 5, 4 => 6, 5 => 0, _ => throw new Exception() };
        if (data[0] == 1) Total = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(1));
        return Task.CompletedTask;
    }
    public Task DataAsync(byte[] data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); DataCount++;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Received) throw new Exception("Offset mismatch");
        Received += (uint)data.Length - 4; Op = 2;
        if (CorruptOffset) State = 7;
        Cancel?.Cancel();
        return Task.CompletedTask;
    }
    public static byte[] Image()
    {
        var data = new byte[1500]; data[0] = 0xE9; data[12] = 9;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 0xABCD5432);
        Encoding.ASCII.GetBytes("esp_swdlink").CopyTo(data, 80);
        Encoding.ASCII.GetBytes("2.0.0").CopyTo(data, 48);
        return data;
    }
}
