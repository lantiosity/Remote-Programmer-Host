using System.Text.Json.Nodes;
using WireLink.App.Services;

internal static class ReviewTests
{
    public static void Register(List<(string Name, Func<Task> Run)> tests)
    {
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
                    SerialPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port.ToString() }, CancellationToken.None);
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
