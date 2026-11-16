using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace WireLink.Core;

public sealed record FirmwareImage(byte[] Bytes, string Version, string ElfSha256, string Sha256)
{
    public static FirmwareImage Parse(byte[] data)
    {
        if (data.Length < 288 || data[0] != 0xE9 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)) != 9
            || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32)) != 0xABCD5432)
            throw new ArgumentException("请选择 ESP32-S3 应用 esp_swdlink.bin，不支持 bootloader、ELF 或合并镜像");
        static string Text(byte[] bytes, int start, int length) => Encoding.ASCII.GetString(bytes, start, length).Split('\0')[0];
        if (Text(data, 80, 32) != "esp_swdlink") throw new ArgumentException("固件项目名不是 esp_swdlink");
        return new(data, Text(data, 48, 32), Convert.ToHexString(data.AsSpan(176, 32)).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
    }
}

public sealed record OtaStatus(byte State, byte Op, byte Flags, uint Received, uint Total, uint Error, uint Session)
{
    public static OtaStatus Parse(byte[] data)
    {
        if (data.Length != 20 || data[0] != 1) throw new InvalidDataException("不支持的 OTA 状态协议");
        return new(data[1], data[2], data[3], BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)));
    }
}

public interface IOtaTransport
{
    int Mtu { get; }
    int ChunkSize => Math.Min(480, Mtu - 7);
    Task<JsonObject> InfoAsync(CancellationToken ct);
    Task<OtaStatus> StatusAsync(CancellationToken ct);
    Task ControlAsync(byte[] data, CancellationToken ct);
    Task DataAsync(byte[] data, CancellationToken ct);
}

public sealed class OtaUpdater(IOtaTransport link)
{
    public bool OwnsTransfer { get; private set; }
    public bool Activated { get; private set; }

    private async Task<OtaStatus> WaitAsync(Func<OtaStatus, bool> predicate, CancellationToken ct, int seconds = 15)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var status = await link.StatusAsync(ct);
            if (status.State == 7) throw new IOException($"OTA 错误 0x{status.Error:X8}，偏移 {status.Received}");
            if ((status.Flags & 2) == 0 && predicate(status)) return status;
            await Task.Delay(25, ct);
        }
        throw new TimeoutException("等待设备确认 OTA 写入超时");
    }

    public async Task<JsonObject> UpdateAsync(FirmwareImage image, string expectedSerial, bool verifyOnly,
        Action<double> progress, CancellationToken ct)
    {
        var info = await link.InfoAsync(ct);
        if (!string.IsNullOrWhiteSpace(expectedSerial) && info["serial"]?.GetValue<string>() != expectedSerial)
            throw new ArgumentException("设备序列号不符，已停止升级");
        if (info["protocol"]?.GetValue<int>() != 1 || info["project"]?.GetValue<string>() != "esp_swdlink")
            throw new InvalidDataException("设备 OTA 项目或协议不匹配");
        var status = await link.StatusAsync(ct);
        if ((status.Flags & 2) != 0 || status.State is >= 2 and <= 6) throw new InvalidOperationException("其他 OTA 会话正在进行");
        if ((status.Flags & 1) == 0) throw new InvalidOperationException("升级窗口未开放：请长按 BOOT 约 2 秒或执行 ota arm");
        if (image.Bytes.Length > info["slot_size"]!.GetValue<int>()) throw new ArgumentException("镜像超过 OTA 分区大小");
        if (link.Mtu < 40) throw new IOException($"BLE MTU {link.Mtu} 太小，OTA 要求至少 40");
        var begin = new byte[37];
        begin[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(1), (uint)image.Bytes.Length);
        SHA256.HashData(image.Bytes).CopyTo(begin, 5);
        OwnsTransfer = true;
        try
        {
            await link.ControlAsync(begin, ct);
            var initial = await WaitAsync(s => s.State == 3 && s.Op == 1 && s.Received == 0, ct);
            var session = initial.Session;
            var chunk = link.ChunkSize;
            for (var offset = 0; offset < image.Bytes.Length; offset += chunk)
            {
                var length = Math.Min(chunk, image.Bytes.Length - offset);
                var packet = new byte[length + 4];
                BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)offset);
                image.Bytes.AsSpan(offset, length).CopyTo(packet.AsSpan(4));
                await link.DataAsync(packet, ct);
                await WaitAsync(s => s.Session == session && s.Op == 2 && s.Received == offset + length, ct);
                progress((offset + length) * 100d / image.Bytes.Length);
            }
            await link.ControlAsync([3], ct);
            await WaitAsync(s => s.Session == session && s.State == 5 && s.Op == 3, ct, 30);
            if (verifyOnly)
            {
                await link.ControlAsync([5], ct);
                await WaitAsync(s => s.Session == session && s.State == 0 && s.Op == 5, ct);
                OwnsTransfer = false;
            }
            else
            {
                await link.ControlAsync([4], ct);
                await WaitAsync(s => s.Session == session && s.State == 6 && s.Op == 4, ct);
                Activated = true;
                OwnsTransfer = false;
            }
            return info;
        }
        catch
        {
            if (OwnsTransfer)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await link.ControlAsync([5], cleanup.Token); } catch { /* Disconnection/timeout aborts on device. */ }
            }
            throw;
        }
    }
}
