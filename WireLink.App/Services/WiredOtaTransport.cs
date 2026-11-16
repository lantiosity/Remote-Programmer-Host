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
    protected abstract Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct);
    public abstract ValueTask DisposeAsync();

    public async Task<JsonObject> InfoAsync(CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        while (bytes.Length <= 512)
        {
            var offset = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(offset, (ushort)bytes.Length);
            var part = await RequestAsync(4, offset, ct);
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
        return OtaStatus.Parse(bytes ?? await RequestAsync(3, [], ct));
    }
    public async Task ControlAsync(byte[] data, CancellationToken ct) => committed = await RequestAsync(1, data, ct);
    public async Task DataAsync(byte[] data, CancellationToken ct) => committed = await RequestAsync(2, data, ct);
    public async Task ArmAsync(CancellationToken ct) => committed = await RequestAsync(5, [0x2c, 0x01], ct);
    public async Task WifiWriteAsync(byte[] command, CancellationToken ct)
    {
        if (Mtu != 63) throw new InvalidOperationException("WiFi 配置请使用原生 USB 或 BLE");
        for (var offset = 0; offset < command.Length; offset += 60)
        {
            var part = new byte[1 + Math.Min(60, command.Length - offset)];
            part[0] = offset == 0 ? (byte)1 : (byte)0;
            command.AsSpan(offset, part.Length - 1).CopyTo(part.AsSpan(1));
            await RequestAsync(6, part, ct);
        }
    }
    public async Task<string> WifiReadAsync(CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        while (bytes.Length < 512)
        {
            var offset = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(offset, (ushort)bytes.Length);
            var part = await RequestAsync(7, offset, ct); bytes.Write(part);
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
            try { await tcp.ConnectAsync(settings.Host, 5557, ct); return new TcpOta(tcp); }
            catch { tcp.Dispose(); throw; }
        }
        var expected = string.IsNullOrWhiteSpace(settings.OtaSerial) ? settings.UsbId : settings.OtaSerial;
        var selector = HidDevice.GetDeviceSelector(0xff00, 1, 0x0d28, 0x0204);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);
        if (devices.Count != 1 && string.IsNullOrWhiteSpace(expected))
            throw new IOException("请连接一台 ESP-SWD，或填写 USB / OTA 设备序列号");
        foreach (var device in devices)
        {
            var hid = await HidDevice.FromIdAsync(device.Id, FileAccessMode.ReadWrite).AsTask(ct);
            if (hid == null) continue;
            var transport = new UsbOta(hid);
            try
            {
                var info = await transport.InfoAsync(ct);
                if (string.IsNullOrWhiteSpace(expected) || info["serial"]?.GetValue<string>() == expected) return transport;
            }
            catch { await transport.DisposeAsync(); throw; }
            await transport.DisposeAsync();
        }
        throw new IOException("未找到指定 USB OTA 设备，请先通过 ROM USB 安装 1.2.0 固件");
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
        private readonly Channel<byte[]> reports = Channel.CreateBounded<byte[]>(8);
        public override int Mtu => 63;
        public UsbOta(HidDevice device)
        {
            this.device = device;
            device.InputReportReceived += Received;
        }
        private void Received(HidDevice sender, HidInputReportReceivedEventArgs args) =>
            reports.Writer.TryWrite(args.Report.Data.ToArray());

        protected override async Task<byte[]> RequestAsync(byte kind, byte[] data, CancellationToken ct)
        {
            if (data.Length > 61) throw new ArgumentException("USB OTA 报文过长");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            var report = device.CreateOutputReport(0);
            var packet = new byte[65]; // Windows HID includes report ID 0.
            packet[1] = 0x80; packet[2] = kind; packet[3] = (byte)data.Length;
            data.CopyTo(packet, 4);
            report.Data = packet.AsBuffer();
            await device.SendOutputReportAsync(report).AsTask(token);
            var bytes = await reports.Reader.ReadAsync(token);
            if (bytes.Length != 65 || bytes[1] != 0x80 || bytes[2] != kind || bytes[3] > 57)
                throw new InvalidDataException("USB OTA 响应无效，请先通过 ROM USB 安装 1.2.0 固件");
            var error = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
            if (error != 0) throw new IOException($"OTA 请求被拒绝：0x{error:X8}");
            return bytes.AsSpan(8, bytes[3]).ToArray();
        }
        public override ValueTask DisposeAsync()
        {
            device.InputReportReceived -= Received;
            device.Dispose();
            reports.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
