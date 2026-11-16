# WiFi 配置

上位机 0.2.0 支持 USB、BLE 配网及 CH343 控制台配网。当前固件首版 0.1.0 声明 `wifi_setup:1` 能力；USB 以该能力判断支持情况，并兼容未声明该字段的历史开发版 1.3.0。

在「WiFi 配置」选择链路，扫描 2.4 GHz 网络、选择 SSID 并输入密码后保存。USB 使用左侧烧录器 USB 标识；BLE 使用左侧选择的设备；控制台必须选择烧录器自身的 CH343 串口，不能选择目标 MCU 串口。

BLE 保存或清除前，运行状态下长按 BOOT 约 2 秒开放配置窗口。保存成功后设备会重启。密码不写入电脑设置和运行日志；设备使用 NVS 持久化。USB 与 BLE 配网实现为 C# 原生通信，无需 Python。

协议请求/响应处理见 `WireLink.Core/WifiSetupProtocol.cs`；链路实现见 `WireLink.App/Services/WifiSetupService.cs`。普通自动测试不连接硬件。
