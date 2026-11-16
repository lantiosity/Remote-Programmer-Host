using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using WireLink.Core;

namespace WireLink.App.Services;

public sealed record BleDevice(string Name, string Address, short Rssi, ulong NumericAddress);

public sealed class BleConnection : IAsyncDisposable
{
    public static Guid Uuid(string id) => Guid.Parse($"6e4000{id}-b5a3-f393-e0a9-e50e24dcca9e");
    public BluetoothLEDevice Device { get; }
    private readonly GattDeviceService service;
    private readonly GattSession session;
    private readonly Dictionary<string, GattCharacteristic> characteristics = [];
    private readonly List<(GattCharacteristic Characteristic, Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> Handler)> subscriptions = [];
    public int Mtu => session.MaxPduSize;
    public ulong Address => Device.BluetoothAddress;

    private BleConnection(BluetoothLEDevice device, GattDeviceService service, GattSession session)
    { Device = device; this.service = service; this.session = session; }

    public static string FormatAddress(ulong value) => string.Join(":", value.ToString("X12").Chunk(2).Select(s => new string(s)));

    public static async Task<IReadOnlyList<BleDevice>> ScanAsync(TimeSpan duration, CancellationToken ct)
    {
        var devices = new ConcurrentDictionary<ulong, BleDevice>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        Exception? failure = null;
        watcher.Received += (_, args) =>
        {
            var name = args.Advertisement.LocalName;
            if (name.Contains("ESP-SWD", StringComparison.OrdinalIgnoreCase) || args.Advertisement.ServiceUuids.Contains(Uuid("10"))
                || devices.ContainsKey(args.BluetoothAddress))
            {
                if (string.IsNullOrEmpty(name) && devices.TryGetValue(args.BluetoothAddress, out var previous)) name = previous.Name;
                devices[args.BluetoothAddress] = new(name, FormatAddress(args.BluetoothAddress), args.RawSignalStrengthInDBm, args.BluetoothAddress);
            }
        };
        watcher.Stopped += (_, args) => { if (args.Error != BluetoothError.Success) failure = new IOException($"蓝牙扫描失败：{args.Error}"); };
        try
        {
            watcher.Start();
            await Task.Delay(duration, ct);
            if (failure != null) throw failure;
            return devices.Values.OrderByDescending(d => d.Rssi).ToArray();
        }
        finally { watcher.Stop(); }
    }

    public static async Task<ulong> ResolveAsync(Settings s, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(s.BleAddress))
        {
            var text = s.BleAddress.Replace(":", "").Replace("-", "");
            if (text.Length != 12 || !ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
                throw new ArgumentException("蓝牙地址格式应为 1C:29:04:3E:C7:CA");
            return address;
        }
        var devices = await ScanAsync(TimeSpan.FromSeconds(6), ct);
        var matches = devices.Where(d => d.Name == s.BleName).ToArray();
        return matches.Length switch
        {
            0 => throw new IOException($"未发现蓝牙设备 {s.BleName}，请确认已上电"),
            1 => matches[0].NumericAddress,
            _ => throw new IOException("发现多台同名蓝牙设备，请扫描并选择明确地址")
        };
    }

    // Called from the UI synchronization context: Windows may request device consent.
    public static async Task<BleConnection> ConnectAsync(ulong address, string serviceId, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        var token = limit.Token;
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(token);
        if (device == null)
        {
            // Unpaired devices must first exist in Windows' advertisement cache.
            await ScanAsync(TimeSpan.FromSeconds(2), token);
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(token);
        }
        if (device == null) throw new IOException("无法访问蓝牙设备，请检查电脑蓝牙和设备电源");
        GattSession? session = null;
        GattDeviceService? service = null;
        try
        {
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(token);
            if (session == null) throw new IOException("无法创建蓝牙 GATT 会话");
            session.MaintainConnection = true;
            // Some Windows stacks fail filtered discovery with ERROR_BAD_COMMAND
            // for the OTA service while full discovery succeeds on the same device.
            var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(token);
            Ensure(result.Status);
            service = result.Services.FirstOrDefault(s => s.Uuid == Uuid(serviceId));
            foreach (var extra in result.Services.Where(s => !ReferenceEquals(s, service))) extra.Dispose();
            if (service == null) throw new IOException($"设备缺少蓝牙服务 {Uuid(serviceId)}，请检查固件版本");
            return new(device, service, session);
        }
        catch (Exception ex)
        {
            service?.Dispose(); session?.Dispose(); device.Dispose();
            if (ex is OperationCanceledException && !ct.IsCancellationRequested)
                throw new TimeoutException("蓝牙服务连接超时，请确认烧录器已上电且未被其他程序占用", ex);
            throw;
        }
    }

    public static void Ensure(GattCommunicationStatus status)
    {
        if (status != GattCommunicationStatus.Success) throw new IOException($"蓝牙 GATT 操作失败：{status}");
    }

    private async Task<GattCharacteristic> CharacteristicAsync(string id, CancellationToken ct)
    {
        if (characteristics.TryGetValue(id, out var found)) return found;
        var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        Ensure(result.Status);
        return characteristics[id] = result.Characteristics.FirstOrDefault(c => c.Uuid == Uuid(id)) ?? throw new IOException($"缺少蓝牙特征 {id}");
    }

    public async Task<byte[]> ReadAsync(string id, CancellationToken ct)
    {
        var characteristic = await CharacteristicAsync(id, ct);
        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        Ensure(result.Status);
        return result.Value.ToArray();
    }

    public async Task WriteAsync(string id, byte[] data, bool response, CancellationToken ct)
    {
        var characteristic = await CharacteristicAsync(id, ct);
        var status = await characteristic.WriteValueAsync(data.AsBuffer(), response
            ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse).AsTask(ct);
        Ensure(status);
    }

    public async Task SubscribeAsync(string id, Action<byte[]> receive, CancellationToken ct)
    {
        var characteristic = await CharacteristicAsync(id, ct);
        Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (_, args) => receive(args.CharacteristicValue.ToArray());
        characteristic.ValueChanged += handler;
        subscriptions.Add((characteristic, handler));
        Ensure(await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct));
    }

    public async Task WriteChunksAsync(string id, byte[] data, CancellationToken ct)
    {
        var chunk = Math.Clamp(Mtu - 3, 20, 244);
        for (var offset = 0; offset < data.Length; offset += chunk)
            await WriteAsync(id, data.AsSpan(offset, Math.Min(chunk, data.Length - offset)).ToArray(), false, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (characteristic, handler) in subscriptions)
        {
            characteristic.ValueChanged -= handler;
            try
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None).AsTask(limit.Token);
            }
            catch { }
        }
        subscriptions.Clear();
        session.MaintainConnection = false;
        service.Dispose();
        session.Dispose();
        Device.Dispose();
    }
}
