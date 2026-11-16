using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using WireLink.Core;

namespace WireLink.App.Services;

public interface IOtaSession : IOtaTransport, IAsyncDisposable { }

public sealed class OtaTransport(BleConnection connection) : IOtaSession
{
    public int Mtu => connection.Mtu;
    public ValueTask DisposeAsync() => connection.DisposeAsync();
    public async Task<JsonObject> InfoAsync(CancellationToken ct) =>
        JsonNode.Parse(Encoding.UTF8.GetString(await connection.ReadAsync("24", ct)))!.AsObject();
    public async Task<OtaStatus> StatusAsync(CancellationToken ct) => OtaStatus.Parse(await connection.ReadAsync("23", ct));
    public Task ControlAsync(byte[] data, CancellationToken ct) => connection.WriteAsync("21", data, true, ct);
    public Task DataAsync(byte[] data, CancellationToken ct) => connection.WriteAsync("22", data, true, ct);
}

public sealed class OtaService(Action<string> phase, Action<double> progress, Action<JsonObject> info)
{
    public async Task RunAsync(Settings settings, bool update, CancellationToken ct)
    {
        FirmwareImage? image = update ? FirmwareImage.Parse(await File.ReadAllBytesAsync(
            Validation.FilePath(settings.OtaImage, ".bin"), ct)) : null;
        phase($"正在连接 {settings.Transport} 升级设备…");
        ulong? address = settings.Transport == "BLE" ? await BleConnection.ResolveAsync(settings, ct) : null;
        async Task<IOtaSession> OpenAsync(CancellationToken token) => settings.Transport == "BLE"
            ? new OtaTransport(await BleConnection.ConnectAsync(address!.Value, "20", token))
            : await WiredOtaTransport.OpenAsync(settings, token);
        string previousSlot;
        await using (var transport = await OpenAsync(ct))
        {
            var current = await transport.InfoAsync(ct);
            if (!string.IsNullOrWhiteSpace(settings.OtaSerial) && current["serial"]?.GetValue<string>() != settings.OtaSerial)
                throw new ArgumentException("设备序列号不符，已停止操作");
            var status = await transport.StatusAsync(ct);
            info(new JsonObject { ["info"] = current.DeepClone(), ["status"] = System.Text.Json.JsonSerializer.SerializeToNode(status, Settings.Json) });
            if (!update) return;
            previousSlot = current["running"]!.GetValue<string>();
            // Attribute discovery/read triggers MTU negotiation; wait briefly if needed.
            for (var attempt = 0; transport.Mtu < 40 && attempt < 20; attempt++) await Task.Delay(100, ct);
            // Physical USB access is a local authorization channel.
            if (transport is WiredOtaTransport wired && settings.Transport == "USB") await wired.ArmAsync(ct);
            phase("正在传输 ESP32 应用镜像…");
            var updater = new OtaUpdater(transport);
            await updater.UpdateAsync(image!, settings.OtaSerial, settings.VerifyOnly, progress, ct);
        }
        if (settings.VerifyOnly) { phase("镜像校验完成，启动分区未切换"); return; }
        phase("传输完成，正在等待新固件重启和启动确认…");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 60)
        {
            await Task.Delay(1200, ct);
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(10));
                await using var transport = await OpenAsync(attempt.Token);
                var current = await transport.InfoAsync(attempt.Token);
                if (current["elf_sha256"]?.GetValue<string>() == image!.ElfSha256
                    && current["running"]?.GetValue<string>() != previousSlot
                    && current["running"]?.GetValue<string>() == current["boot"]?.GetValue<string>()
                    && current["image_state"]?.GetValue<int>() == 2)
                {
                    info(new JsonObject { ["info"] = current.DeepClone() });
                    phase("新固件启动确认通过，分区和 ELF SHA256 已核对");
                    return;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* Device is rebooting; reconnect. */ }
        }
        throw new TimeoutException("未能确认新固件启动，请重新读取设备状态；传输完成不等于启动成功");
    }
}
