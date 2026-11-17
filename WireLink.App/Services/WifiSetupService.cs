using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WireLink.Core;

namespace WireLink.App.Services;

public sealed record WifiSetupRequest(string Link, string Operation, string Ssid = "", string Password = "");

public static class WifiSetupService
{
    public static async Task<JsonObject> RunAsync(Settings settings, WifiSetupRequest request, CancellationToken ct)
    {
        if (request.Link is not ("USB" or "BLE" or "UART")) throw new ArgumentException("配网请选择 USB 或 BLE");
        WifiSetupProtocol.Command("00000000", request.Operation, request.Ssid, request.Password);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.Operation == "scan" ? 90 : 40));
        var token = timeout.Token;
        BleConnection? ble = null;
        WiredOtaTransport? usb = null;
        SerialPort? port = null;
        try
        {
            if (request.Link == "BLE")
            {
                var address = await BleConnection.ResolveAsync(settings, token);
                try { ble = await BleConnection.ConnectAsync(address, "30", token); }
                catch (IOException ex) when (ex.Message.Contains("缺少蓝牙服务"))
                { throw new IOException("此固件尚无 BLE 配网接口，请先安装支持配网功能的固件。", ex); }
            }
            else if (request.Link == "USB")
            {
                usb = await WiredOtaTransport.OpenAsync(settings with { Transport = "USB", OtaSerial = settings.UsbId }, token);
                var info = await usb.InfoAsync(token);
                if (!WifiSetupProtocol.SupportsUsbConfiguration(info))
                    throw new IOException("当前固件未声明 USB 配网能力，请安装支持此功能的固件（当前首版为 0.1.0）。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.WifiPort)) throw new ArgumentException("请选择烧录器 CH343 控制台串口；目标串口不可用于配网");
                port = new SerialPort(settings.WifiPort, 115200) { DtrEnable = false, RtsEnable = false,
                    Encoding = Encoding.UTF8, NewLine = "\r", WriteTimeout = 1500 };
                port.Open(); port.DiscardInBuffer(); port.Write("\r"); await Task.Delay(150, token); port.DiscardInBuffer();
            }

            async Task<JsonObject> Query(string operation, int page = 0)
            {
                var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                var command = WifiSetupProtocol.Command(id, operation, request.Ssid, request.Password, page);
                if (usb != null)
                {
                    await usb.WifiWriteAsync(Encoding.ASCII.GetBytes(command + "\n"), token);
                    while (true)
                    {
                        var response = WifiSetupProtocol.ParseResponse(await usb.WifiReadAsync(token), id);
                        if (response != null) return response;
                        await Task.Delay(120, token);
                    }
                }
                if (ble != null)
                {
                    var data = Encoding.ASCII.GetBytes(command + "\n");
                    var chunk = Math.Clamp(ble.Mtu - 3, 20, 244);
                    for (var offset = 0; offset < data.Length; offset += chunk)
                        await ble.WriteAsync("31", data.AsSpan(offset, Math.Min(chunk, data.Length - offset)).ToArray(), true, token);
                    while (true)
                    {
                        var response = WifiSetupProtocol.ParseResponse(Encoding.UTF8.GetString(await ble.ReadAsync("32", token)), id);
                        if (response != null) return response;
                        await Task.Delay(120, token);
                    }
                }
                // Never forward console echo: the command contains credentials.
                port!.WriteLine("wificfg " + command);
                var parser = new WifiConsoleParser();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var response = parser.Feed(port.ReadExisting(), id);
                    if (response != null) return response;
                    await Task.Delay(40, token);
                }
            }

            if (request.Operation is "save" or "clear")
            {
                var status = await Query("status");
                if (request.Link == "BLE" && status["armed"]?.GetValue<bool>() != true)
                    throw new IOException("请先长按烧录器 BOOT 约 2 秒，开放配置窗口，再保存或清除 WiFi。");
            }
            var result = await Query(request.Operation);
            if (request.Operation == "scan")
            {
                var pages = result["pages"]?.GetValue<int>() ?? 0;
                if (pages is < 0 or > 7) throw new IOException("网络列表页数无效");
                for (var page = 1; page < pages; ++page)
                {
                    var next = await Query("page", page);
                    foreach (var item in next["networks"]!.AsArray()) result["networks"]!.AsArray().Add(item?.DeepClone());
                }
            }
            result["operation"] = request.Operation;
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("配网响应超时，请检查控制台串口、固件版本及设备连接。"); }
        finally { port?.Dispose(); if (ble != null) await ble.DisposeAsync(); if (usb != null) await usb.DisposeAsync(); }
    }
}
