# WireLink 上位机 0.3.0

发布日期为 2026-11-24，配套 ESP32-S3 固件 0.1.4 重新发布版。

- 完整解压后双击 `WireLink.exe`。适用于 Windows 10 2004 及以上版本和 Windows 11 x64，系统缺少 WebView2 时需联网安装。[Windows x64 便携包](WireLink-0.3.0-win-x64.7z)
- 源码包含 89 个文件，构建方式见仓库首页。[源码 ZIP](host-0.3.0-source.zip) · [仓库 README](../../README.md)
- 两个包均提供 SHA256 及逐文件清单。[SHA256](SHA256SUMS.txt) · [源码清单](manifest.json) · [便携清单](WireLink-0.3.0-win-x64.manifest.json)

本版支持持久化 DFP 库、STM32H743VI 离线镜像管理和校验后导出 microSD。新增主题编辑和独立窗口、侧栏及控件外观设置。BLE 配网要求加密，桌面确认弹窗超时或显示失败时会取消操作。

便携包包含 .NET、pyOCD、30 个 STM32 设备包及语言资源。首次解压时 `data/` 为空。ESP32 应用镜像从固件发布包单独选择。大型便携包和清单保存在本地发布目录。

来源提交为 `2766e36c69328d53b4311cb7f1cbe82d92eded60`，发布标签为 `v0.3.0`。Release 构建、41 项 C#、19 项前端、4 项 Python 回归及跨仓库 pyOCD 契约检查通过。已核对启动器和应用版本，并解包逐文件校验。Windows 多 DPI、真实 WebView2 故障恢复和 BLE 配对仍待实机验收。
