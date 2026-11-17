using System.Text.Json.Nodes;
using WireLink.App.Services;

internal static class ReviewTests
{
    public static void Register(List<(string Name, Func<Task> Run)> tests)
    {
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
}
