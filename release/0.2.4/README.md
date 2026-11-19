# WireLink 上位机 0.2.4

发布日期为 2026-11-19，配套 ESP32-S3 固件 0.1.3。

整片擦除、擦除后烧录、固件升级、WiFi 配置和退出确认统一使用当前主题。擦除和清除配置使用红色按钮。弹窗默认聚焦“取消”，并用具体动作标明确认后会发生什么。

- [Windows x64 便携包](WireLink-0.2.4-win-x64.7z)约 203.5 MiB。完整解压后双击 `WireLink.exe`。系统缺少 WebView2 Runtime 时需联网安装。
- [源码 ZIP](host-0.2.4-source.zip)包含 81 个源码和文档文件，构建参见[仓库 README](../../README.md)。
- [压缩包 SHA256](SHA256SUMS.txt)、[源码清单](manifest.json)和[便携文件清单](WireLink-0.2.4-win-x64.manifest.json)随包提供。

Release 构建与 38 项 C#、17 项前端、4 项 Python 回归通过。已检查浅色和深色弹窗呈现，并验证取消清除配置时未启动任务。7z 完整性测试通过，解压后逐一核对了 1501 个文件。便携包的 `data/` 初始为空。
