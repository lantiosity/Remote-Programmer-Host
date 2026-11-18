using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using WireLink.App.Services;
using WireLink.Core;

internal static class ReviewTests
{
    public static void Register(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("DAP 端口无效时 OTA 仍使用自己的 TCP 端口", async () =>
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var serving = Task.Run(async () =>
                {
                    using var peer = await listener.AcceptTcpClientAsync();
                    var stream = peer.GetStream(); var header = new byte[8];
                    using var limit = new CancellationTokenSource(5000);
                    await stream.ReadExactlyAsync(header, limit.Token);
                    Check(System.Text.Encoding.ASCII.GetString(header, 0, 4) == "WLO1" && header[4] == 4);
                    var data = new byte[System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6))];
                    await stream.ReadExactlyAsync(data, limit.Token);
                    var json = System.Text.Encoding.UTF8.GetBytes("{\"serial\":\"port-test\",\"ota_port\":12345}");
                    header[4] |= 0x80;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)(json.Length + 4));
                    await stream.WriteAsync(header, limit.Token);
                    await stream.WriteAsync(new byte[4], limit.Token);
                    await stream.WriteAsync(json, limit.Token);
                });
                using var timeout = new CancellationTokenSource(5000);
                await using var link = await WiredOtaTransport.OpenAsync(new WireLink.Core.Settings { Transport = "WiFi", Host = "127.0.0.1",
                    DapPort = "", OtaPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port.ToString() }, timeout.Token);
                Check((await link.InfoAsync(timeout.Token))["serial"]?.GetValue<string>() == "port-test");
                await serving.WaitAsync(timeout.Token);
            }
            finally { listener.Stop(); }
        }));
        tests.Add(("OTA 重连拒绝其他设备（填写序列号）", () =>
            TestOtaReconnect("DEVICE-A", "DEVICE-B", "esp_swdlink", 1, "设备序列号不符")));
        tests.Add(("OTA 重连拒绝其他设备（序列号留空）", () =>
            TestOtaReconnect("", "DEVICE-B", "esp_swdlink", 1, "设备序列号不符")));
        tests.Add(("OTA 重连拒绝其他项目", () =>
            TestOtaReconnect("", "DEVICE-A", "other", 1, "项目或协议不匹配")));
        tests.Add(("OTA 重连拒绝不兼容协议", () =>
            TestOtaReconnect("", "DEVICE-A", "esp_swdlink", 2, "项目或协议不匹配")));
        tests.Add(("OTA 重连确认同一设备已启动新镜像", () =>
            TestOtaReconnect("", "DEVICE-A", "esp_swdlink", 1, null)));
        tests.Add(("BLE 桥释放后忽略迟到通知并允许重复释放", async () =>
        {
            var ble = new BleFake();
            var bridge = await BleDapBridge.StartAsync(ble, CancellationToken.None);
            await Task.WhenAll(bridge.DisposeAsync().AsTask(), bridge.DisposeAsync().AsTask());
            for (var i = 0; i < 600; i++) ble.Receive!(new byte[] { 1 });
            Check(bridge.Failure == null);
        }));
        tests.Add(("BLE DAP 通知溢出明确报告且停止当前桥", async () =>
        {
            var ble = new BleFake(); var errors = new List<string>();
            await using var bridge = await BleDapBridge.StartAsync(ble, CancellationToken.None, errors.Add);
            for (var i = 0; i < 520; i++) ble.Receive!(new byte[] { 1 });
            Check(bridge.Failure is IOException && errors.Count == 1 && errors[0].Contains("溢出"));
        }));
        tests.Add(("配置优先完整主文件并从中断的临时文件恢复", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wirelink-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "settings.json");
                new WireLink.Core.Settings { Theme = "dark" }.Save(path);
                File.WriteAllText(path + ".tmp", "broken");
                Check(WireLink.Core.Settings.Load(path).Theme == "dark" && !File.Exists(path + ".tmp"));
                File.Move(path, path + ".tmp");
                File.WriteAllText(path, "broken");
                Check(WireLink.Core.Settings.Load(path).Theme == "dark" && !File.Exists(path + ".tmp"));
                Check(WireLink.Core.Settings.Load(path).Theme == "dark");
            }
            finally { Directory.Delete(folder, true); }
            return Task.CompletedTask;
        }));
        tests.Add(("UART 配网过滤回显、残帧和旧请求并重组分片", () =>
        {
            var parser = new WireLink.Core.WifiConsoleParser();
            Check(parser.Feed("wificfg aaaaaaaa save secret\r\nWLWIFI {broken}\nWLWIFI {\"id\":\"old\",\"ok\":true}\nWL", "aaaaaaaa") == null);
            Check(parser.Feed("WIFI {\"id\":\"aaaaaaaa\",\"ok\":true", "aaaaaaaa") == null);
            Check(parser.Feed("}\r\n", "aaaaaaaa")?["ok"]?.GetValue<bool>() == true);
            Check(parser.Feed(new string('x', 20000) + "\nWLWIFI {\"id\":\"aaaaaaaa\",\"ok\":true}\n", "aaaaaaaa") != null);
            try { parser.Feed("WLWIFI {\"id\":\"aaaaaaaa\",\"ok\":false,\"error\":\"scan_failed\"}\n", "aaaaaaaa"); throw new Exception("Missing device error"); }
            catch (IOException) { }
            return Task.CompletedTask;
        }));
        tests.Add(("串口并发发送与重复释放有界结束", async () =>
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var accepting = listener.AcceptTcpClientAsync();
                var serial = new SerialService();
                await serial.OpenAsync(new WireLink.Core.Settings { Transport = "WiFi", Host = "127.0.0.1",
                    DapPort = "invalid", SerialPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port.ToString() }, CancellationToken.None);
                using var peer = await accepting;
                var sending = serial.SendAsync(new byte[4 * 1024 * 1024], CancellationToken.None);
                var first = serial.DisposeAsync().AsTask(); var second = serial.DisposeAsync().AsTask();
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
                try { await sending.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
                await serial.DisposeAsync();
            }
            finally { listener.Stop(); }
        }));
        tests.Add(("BLE DAP 请求使用带响应写入并转发通知", async () =>
        {
            var ble = new BleFake();
            await using var bridge = await BleDapBridge.StartAsync(ble, CancellationToken.None);
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, bridge.Port);
            await client.GetStream().WriteAsync(new byte[] { 1, 2, 3 });
            await ble.Written.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(ble.WithResponse);
            ble.Receive!(new byte[] { 4, 5 });
            var result = new byte[2];
            using var limit = new CancellationTokenSource(3000);
            await client.GetStream().ReadExactlyAsync(result, limit.Token);
            Check(result.SequenceEqual(new byte[] { 4, 5 }));
        }));
        tests.Add(("USB 枚举跳过故障设备和序列号不匹配设备", async () =>
        {
            var bad = new UsbFake { Fail = true }; var wrong = new UsbFake { Serial = "other" }; var wanted = new UsbFake();
            var selected = await WiredOtaTransport.SelectUsbAsync(new Func<CancellationToken, Task<WiredOtaTransport?>>[] {
                _ => Task.FromResult<WiredOtaTransport?>(bad), _ => Task.FromResult<WiredOtaTransport?>(wrong),
                _ => Task.FromResult<WiredOtaTransport?>(wanted) }, "wanted", CancellationToken.None);
            Check(ReferenceEquals(selected, wanted) && bad.Disposed && wrong.Disposed && !wanted.Disposed);
            await selected.DisposeAsync();
        }));
        tests.Add(("OTA 请求取消后连接失效，不能消费迟到响应", async () =>
        {
            var transport = new UsbFake { Slow = true };
            using var cancellation = new CancellationTokenSource(30);
            try { await transport.ArmAsync(cancellation.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            try { await transport.StatusAsync(CancellationToken.None); throw new Exception("Expected fault barrier"); }
            catch (IOException) { }
            Check(transport.Disposed && transport.Calls == 1);
        }));
        tests.Add(("设备信息满页响应受 512 字节硬上限约束", async () =>
        {
            var transport = new UsbFake { FullPages = true };
            try { await transport.InfoAsync(CancellationToken.None); throw new Exception("Expected size limit"); }
            catch (InvalidDataException) { }
            Check(transport.Calls == 9);
        }));
        tests.Add(("目录合并按 ID 去重，外部包覆盖同名条目", () =>
        {
            var merged = PyOcdService.MergeTargets(JsonNode.Parse("[{\"name\":\"part\",\"vendor\":\"builtin\"}]")!.AsArray(),
                JsonNode.Parse("[{\"name\":\"PART\",\"vendor\":\"custom\"},{\"name\":\"other\"},{}]")!.AsArray());
            Check(merged.Count == 2 && merged[0]!["vendor"]!.GetValue<string>() == "custom");
            return Task.CompletedTask;
        }));
        tests.Add(("pyOCD 子进程取消和快速退出不导致取消线程异常", async () =>
        {
            var service = new PyOcdService(Environment.CurrentDirectory, _ => { });
            for (var i = 0; i < 3; i++)
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                try { await service.RunAsync(["-c", "import time; time.sleep(2)"], cancellation.Token); throw new Exception("Cancellation was ignored"); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
            Check((await service.RunAsync(["-c", "print('done')"], CancellationToken.None, true)).Trim() == "done");
        }));
    }
    private static async Task TestOtaReconnect(string expectedSerial, string returningSerial, string project,
        int protocol, string? expectedError)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var path = Path.Combine(Path.GetTempPath(), $"wirelink-test-{Guid.NewGuid():N}.bin");
        var imageBytes = FakeOta.Image();
        var device = new FakeOta { Serial = "DEVICE-A" };
        var confirmed = false;
        try
        {
            await File.WriteAllBytesAsync(path, imageBytes, ct);
            var service = new OtaService(_ => { }, _ => { }, details => confirmed |= details["status"] is null);
            var update = service.RunAsync(new Settings
            {
                Transport = "WiFi", Host = "127.0.0.1", DapPort = "", OtaSerial = expectedSerial,
                OtaImage = path, OtaPort = ((IPEndPoint)listener.LocalEndpoint).Port.ToString()
            }, true, ct);
            using (var peer = await listener.AcceptTcpClientAsync(ct))
            {
                var initial = await device.InfoAsync(ct);
                initial["running"] = "ota_0";
                while (device.State != 6) await ReplyToOtaRequest(peer.GetStream(), device, initial, ct);
            }
            using (var peer = await listener.AcceptTcpClientAsync(ct))
            {
                var rebooted = await device.InfoAsync(ct);
                rebooted["serial"] = returningSerial;
                rebooted["project"] = project;
                rebooted["protocol"] = protocol;
                rebooted["elf_sha256"] = FirmwareImage.Parse(imageBytes).ElfSha256;
                rebooted["running"] = "ota_1";
                rebooted["boot"] = "ota_1";
                rebooted["image_state"] = 2;
                await ReplyToOtaRequest(peer.GetStream(), device, rebooted, ct);
            }
            try { await update.WaitAsync(ct); }
            catch (Exception ex) when (expectedError != null && ex.Message.Contains(expectedError, StringComparison.Ordinal))
            {
                Check(!confirmed);
                return;
            }
            Check(expectedError == null && confirmed);
        }
        finally { File.Delete(path); }
    }

    private static async Task ReplyToOtaRequest(NetworkStream stream, FakeOta device, JsonObject info, CancellationToken ct)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, ct);
        Check(Encoding.ASCII.GetString(header, 0, 4) == "WLO1");
        var request = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6))];
        await stream.ReadExactlyAsync(request, ct);
        byte[] reply;
        if (header[4] == 4)
        {
            reply = Encoding.UTF8.GetBytes(info.ToJsonString());
        }
        else
        {
            if (header[4] == 1) await device.ControlAsync(request, ct);
            if (header[4] == 2) await device.DataAsync(request, ct);
            Check(header[4] is 1 or 2 or 3);
            var status = await device.StatusAsync(ct);
            reply = new byte[20] { 1, status.State, status.Op, status.Flags, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(4), status.Received);
            BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(8), status.Total);
            BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(16), status.Session);
        }
        header[4] |= 0x80;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)(reply.Length + 4));
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(new byte[4], ct);
        await stream.WriteAsync(reply, ct);
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("Review regression assertion failed"); }
    private sealed class UsbFake : WiredOtaTransport
    {
        public override int Mtu => 63;
        public bool Fail, Slow, FullPages, Disposed;
        public int Calls;
        public string Serial = "wanted";
        protected override async Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct)
        {
            Calls++;
            if (Slow) await Task.Delay(1000, ct);
            if (Fail) throw new IOException("Device unavailable");
            return FullPages ? new byte[57] : System.Text.Encoding.UTF8.GetBytes("{\"serial\":\"" + Serial + "\"}");
        }
        public override ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class BleFake : IBleDapConnection
    {
        public Action<byte[]>? Receive;
        public bool WithResponse;
        public readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SubscribeAsync(string id, Action<byte[]> receive, CancellationToken ct) { Receive = receive; return Task.CompletedTask; }
        public Task WriteChunksAsync(string id, byte[] data, CancellationToken ct, bool response = false)
        { WithResponse = response; Written.TrySetResult(); return Task.CompletedTask; }
    }
}
