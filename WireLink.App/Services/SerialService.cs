using System.IO.Ports;
using System.Net.Sockets;
using WireLink.Core;

namespace WireLink.App.Services;

public sealed class SerialService : IAsyncDisposable
{
    private SerialPort? serial;
    private TcpClient? tcp;
    private BleConnection? ble;
    private Stream? stream;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim writing = new(1, 1);
    private Task? reading;
    private readonly object disposalLock = new();
    private Task? disposal;
    public Action<byte[]>? Received { get; set; }
    public Action<string?>? Disconnected { get; set; }

    public async Task<string> OpenAsync(Settings s, CancellationToken ct)
    {
        Validation.Link(s);
        string label;
        if (s.Transport == "USB")
        {
            if (string.IsNullOrWhiteSpace(s.ComPort)) throw new ArgumentException("请选择 COM 口");
            serial = new(s.ComPort, (int)Validation.Number(s.Baud, "波特率", 300, 4000000))
            { DtrEnable = false, RtsEnable = false, ReadTimeout = 250, WriteTimeout = 1000 };
            serial.Open();
            stream = serial.BaseStream;
            label = $"{s.ComPort} · {s.Baud} baud";
        }
        else if (s.Transport == "WiFi")
        {
            tcp = new TcpClient { NoDelay = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await tcp.ConnectAsync(s.Host, (int)Validation.Number(s.SerialPort, "串口桥端口", 1, 65535), timeout.Token);
            stream = tcp.GetStream();
            label = $"{s.Host}:{s.SerialPort}";
        }
        else
        {
            var address = await BleConnection.ResolveAsync(s, ct);
            ble = await BleConnection.ConnectAsync(address, "01", ct);
            await ble.SubscribeAsync("03", data => Received?.Invoke(data), ct);
            label = "BLE NUS · " + BleConnection.FormatAddress(address);
        }
        reading = ReadAsync();
        return label;
    }

    private async Task ReadAsync()
    {
        string? error = null;
        try
        {
            var buffer = new byte[4096];
            while (!stop.IsCancellationRequested)
            {
                if (ble != null)
                {
                    await Task.Delay(300, stop.Token);
                    if (ble.Device.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Disconnected)
                        throw new IOException("蓝牙串口已断开");
                }
                else
                {
                    int count;
                    try { count = await stream!.ReadAsync(buffer, stop.Token); }
                    catch (TimeoutException) when (serial != null && !stop.IsCancellationRequested) { continue; }
                    if (count == 0) throw new IOException("设备已关闭串口连接");
                    Received?.Invoke(buffer.AsSpan(0, count).ToArray());
                }
            }
        }
        catch (Exception ex) { if (!stop.IsCancellationRequested) error = ex.Message; }
        finally { if (!stop.IsCancellationRequested) Disconnected?.Invoke(error); }
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
        await writing.WaitAsync(linked.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (ble != null) await ble.WriteChunksAsync("02", data, linked.Token);
            else await stream!.WriteAsync(data, linked.Token);
        }
        finally { writing.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalLock) return new(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        stop.Cancel();
        // Close the handle to unblock SerialPort's Windows overlapped read.
        serial?.Close();
        tcp?.Dispose();
        stream?.Dispose();
        if (reading != null) await reading;
        await writing.WaitAsync();
        try
        {
            if (ble != null) await ble.DisposeAsync();
            serial?.Dispose();
        }
        finally { writing.Release(); }
    }
}
