# WireLink 0.2.2

日期：2026-11-18。仅更新上位机，不携带、修改或烧写固件；保留旧版本归档。

- `WireLink-0.2.2-win-x64.7z`：Windows x64 便携版。完整解压后双击文件夹内的 WireLink.exe。
- `host-0.2.2-source.zip` / `manifest.json`：79 个源码文件及逐文件 SHA256。
- `SHA256SUMS.txt` / `WireLink-0.2.2-win-x64.manifest.json`：压缩包及便携文件的校验值。

修复 WiFi/BLE remote 大块内存及 AP 请求分块；BLE OTA 请求 Windows 配对并强制加密写入；支持独立 OTA 端口及 USB/BLE 信息同步；授权提示明确区分 USB 与无线链路。保持固件校验和安全要求，不改变报文协议或伪装支持 APv2。

验证：C# 33/33、Python 4/4、Node 16/16；便携构建、精简引擎及实际 pyOCD 入口冒烟通过。无硬件重放已验证大块全 FF/全 FFFFFFFF 和原先超限读取可以分块通过当前固件解析器。界面已观察到 0.2.2、OTA 端口 5557 和配对引导。真实 BLE 配对、加密协商、烧录和 OTA 仍待实机验收，未把本机模拟视为实测通过。

包内保留 .NET、pyOCD、设备包与 app/lang 语言资源。适用于 Windows 10 2004 及以上 x64 / Windows 11；使用系统 WebView2，缺少时需联网安装。data/ 初始为空，不携带个人设置或固件镜像。7z 在生成时执行完整性测试和解压逐文件 SHA256 比对。

源码及版本元数据纳入本地提交和标签 v0.2.2；大型便携包及其生成清单保存在本目录并被 Git 忽略。未上传远端。核对报告和跨仓库核对脚本不进入版本及源码归档。
