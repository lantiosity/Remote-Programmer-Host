using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;
using WireLink.Core;

namespace WireLink.App.Services;

public abstract class WiredOtaTransport : IOtaSession
{
    public abstract int Mtu { get; }
    public virtual int ChunkSize => Mtu - 7;
    private byte[]? committed;
    private readonly SemaphoreSlim exchangeGate = new(1, 1);
    private bool faulted;
    private async Task<byte[]> ExchangeAsync(byte kind, byte[] data, CancellationToken ct)
    {
        await exchangeGate.WaitAsync(ct);
        try
        {
            if (faulted) throw new IOException("传输已失效，请重新连接设备");
            return await RequestAsync(kind, data, ct).WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
        catch
        {
            faulted = true; committed = null;
            try { await DisposeAsync(); } catch { /* Preserve the request failure. */ }
            throw;
        }
        finally { exchangeGate.Release(); }
    }
    protected abstract Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct);
    public abstract ValueTask DisposeAsync();

    public async Task<JsonObject> InfoAsync(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        using var bytes = new MemoryStream();
        while (bytes.Length <= 512)
        {
            var offset = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(offset, (ushort)bytes.Length);
            var part = await ExchangeAsync(4, offset, limit.Token);
            if (part.Length > (Mtu == 63 ? 57 : 512) || bytes.Length + part.Length > 512)
                throw new InvalidDataException("OTA 设备信息超出协议上限");
            bytes.Write(part);
            if (part.Length < (Mtu == 63 ? 57 : 512))
                return JsonNode.Parse(Encoding.UTF8.GetString(bytes.ToArray()))!.AsObject();
        }
        throw new InvalidDataException("OTA 设备信息过长");
    }
    public async Task<OtaStatus> StatusAsync(CancellationToken ct)
    {
        var bytes = committed;
        committed = null;
        return OtaStatus.Parse(bytes ?? await ExchangeAsync(3, [], ct));
    }
    public async Task ControlAsync(byte[] data, CancellationToken ct) => committed = await ExchangeAsync(1, data, ct);
    public async Task DataAsync(byte[] data, CancellationToken ct) => committed = await ExchangeAsync(2, data, ct);
    public async Task ArmAsync(CancellationToken ct) => committed = await ExchangeAsync(5, [0x2c, 0x01], ct);
    public async Task WifiWriteAsync(byte[] command, CancellationToken ct)
    {
        if (Mtu != 63) throw new InvalidOperationException("WiFi 配置请使用原生 USB 或 BLE");
        for (var offset = 0; offset < command.Length; offset += 60)
        {
            var part = new byte[1 + Math.Min(60, command.Length - offset)];
            part[0] = offset == 0 ? (byte)1 : (byte)0;
            command.AsSpan(offset, part.Length - 1).CopyTo(part.AsSpan(1));
            await ExchangeAsync(6, part, ct);
        }
    }
    public async Task<string> WifiReadAsync(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        using var bytes = new MemoryStream();
        while (bytes.Length < 512)
        {
            var offset = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(offset, (ushort)bytes.Length);
            var part = await ExchangeAsync(7, offset, limit.Token);
            if (part.Length > 57 || bytes.Length + part.Length > 512) throw new IOException("WiFi 配置响应超出协议上限");
            bytes.Write(part);
            if (part.Length < 57) return Encoding.UTF8.GetString(bytes.ToArray());
        }
        throw new IOException("WiFi 配置响应过长");
    }

    public static async Task<WiredOtaTransport> OpenAsync(Settings settings, CancellationToken ct)
    {
        if (settings.Transport == "WiFi")
        {
            Validation.Link(settings);
            var tcp = new TcpClient { NoDelay = true };
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(8));
            var port = (int)Validation.Number(settings.OtaPort, "OTA 端口", 1, 65535);
            try { await tcp.ConnectAsync(settings.Host, port, limit.Token); return new TcpOta(tcp); }
            catch { tcp.Dispose(); throw; }
        }
        var expected = string.IsNullOrWhiteSpace(settings.OtaSerial) ? settings.UsbId : settings.OtaSerial;
        var selector = HidDevice.GetDeviceSelector(0xff00, 1, 0x0d28, 0x0204);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);
        if (devices.Count != 1 && string.IsNullOrWhiteSpace(expected))
            throw new IOException("请连接一台 ESP-SWD，或填写 USB / OTA 设备序列号");
        return await SelectUsbAsync(devices.Select(device => (Func<CancellationToken, Task<WiredOtaTransport?>>)(async token =>
        {
            var hid = await HidDevice.FromIdAsync(device.Id, FileAccessMode.ReadWrite).AsTask(token);
            return hid == null ? null : new UsbOta(hid);
        })), expected, ct);
    }

    public static async Task<WiredOtaTransport> SelectUsbAsync(
        IEnumerable<Func<CancellationToken, Task<WiredOtaTransport?>>> candidates, string expected, CancellationToken ct)
    {
        var failures = new List<Exception>();
        foreach (var open in candidates)
        {
            ct.ThrowIfCancellationRequested();
            WiredOtaTransport? transport = null;
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(5));
                transport = await open(attempt.Token);
                if (transport == null) { failures.Add(new IOException("USB 设备不可访问")); continue; }
                var info = await transport.InfoAsync(attempt.Token);
                if (string.IsNullOrWhiteSpace(expected) || info["serial"]?.GetValue<string>() == expected)
                { var selected = transport; transport = null; return selected; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or System.Text.Json.JsonException or TimeoutException)
            { failures.Add(ex); }
            finally
            {
                if (transport != null)
                    try { await transport.DisposeAsync(); }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException) { failures.Add(ex); }
            }
        }
        throw new IOException($"未找到指定 USB 设备（失败候选 {failures.Count} 个），请检查序列号、占用及固件支持。",
            failures.Count == 0 ? null : new AggregateException(failures));
    }

    private sealed class TcpOta(TcpClient client) : WiredOtaTransport
    {
        public override int Mtu => 1031;
        protected override async Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            var header = new byte[] { (byte)'W', (byte)'L', (byte)'O', (byte)'1', kind, 0, 0, 0 };
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)data.Length);
            var stream = client.GetStream();
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(data, token);
            await stream.ReadExactlyAsync(header, token);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
            if (Encoding.ASCII.GetString(header, 0, 4) != "WLO1" || header[4] != (kind | 0x80) || header[5] != 0 || length < 4 || length > 516)
                throw new InvalidDataException("WiFi OTA 响应无效");
            var reply = new byte[length];
            await stream.ReadExactlyAsync(reply, token);
            var error = BinaryPrimitives.ReadUInt32LittleEndian(reply);
            if (error != 0) throw new IOException($"OTA 请求被拒绝：0x{error:X8}");
            return reply[4..];
        }
        public override ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class UsbOta : WiredOtaTransport
    {
        private readonly HidDevice device;
        private int disposed;
        private Exception? receiveFailure;
        private readonly Channel<byte[]> reports = Channel.CreateBounded<byte[]>(8);
        public override int Mtu => 63;
        public UsbOta(HidDevice device)
        {
            this.device = device;
            device.InputReportReceived += Received;
        }
        private void Received(HidDevice sender, HidInputReportReceivedEventArgs args)
        {
            if (!reports.Writer.TryWrite(args.Report.Data.ToArray()))
                Interlocked.CompareExchange(ref receiveFailure, new IOException("USB 响应队列溢出，连接已失效"), null);
        }

        protected override async Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct)
        {
            if (data.Length > 61) throw new ArgumentException("USB OTA 报文过长");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            if (Volatile.Read(ref receiveFailure) is Exception failed) throw new IOException("USB 接收失败", failed);
            while (reports.Reader.TryRead(out _)) { }
            var report = device.CreateOutputReport(0);
            var packet = new byte[65]; // Windows HID includes report ID 0.
            packet[1] = 0x80; packet[2] = kind; packet[3] = (byte)data.Length;
            data.CopyTo(packet, 4);
            report.Data = packet.AsBuffer();
            await device.SendOutputReportAsync(report).AsTask(token);
            var bytes = await reports.Reader.ReadAsync(token);
            if (Volatile.Read(ref receiveFailure) is Exception failedRead) throw new IOException("USB 接收失败", failedRead);
            if (bytes.Length != 65 || bytes[1] != 0x80 || bytes[2] != kind || bytes[3] > 57)
                throw new InvalidDataException("USB OTA 响应无效，请检查设备连接及固件是否支持此接口");
            var error = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
            if (error != 0) throw new IOException($"OTA 请求被拒绝：0x{error:X8}");
            return bytes.AsSpan(8, bytes[3]).ToArray();
        }
        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return ValueTask.CompletedTask;
            device.InputReportReceived -= Received;
            device.Dispose();
            reports.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
