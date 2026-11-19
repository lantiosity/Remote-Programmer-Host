# WireLink 上位机 0.2.3

发布日期为 2026-11-19，配套 ESP32-S3 固件 0.1.3。

- [Windows x64 便携包](WireLink-0.2.3-win-x64.7z)支持 Windows 10 2004 及以上版本和 Windows 11。完整解压后双击包内的 `WireLink.exe`。系统缺少 WebView2 时需联网安装。
- [源码 ZIP](host-0.2.3-source.zip)包含 80 个源码与文档文件。构建步骤见[仓库 README](../../README.md)。
- [SHA256](SHA256SUMS.txt)、[源码清单](manifest.json)及[便携文件清单](WireLink-0.2.3-win-x64.manifest.json)随包提供。

OTA 重启后核对首次读取的设备序列号、项目、协议和镜像，再确认启动成功。USB 扫描在原探针消失后清空选择。WiFi 串口、OTA 与 DAP 端口按功能校验。支持写应答的 BLE 串口可向用户报告发送队列拥塞。

便携包包含 .NET、pyOCD、30 个 STM32 设备包与语言资源。首次解压时 `data/` 为空，ESP32 应用镜像从固件 0.1.3 发布包单独选择。大型便携包和清单保存在本地发布目录。

来源提交为 `8015d54b8d5eaea8ab4c036403cd25b35d81f56f`，发布标签为 `v0.2.3`。Release 构建、38 项 C#、17 项前端和 4 项 pyOCD Python 回归通过。已核对 EXE 版本资源、便携 pyOCD 入口，并解包比对每个文件的 SHA256。实板 BLE 写应答、配对、烧录和 OTA 回滚仍待验收。
