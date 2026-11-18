# WireLink 上位机 0.2.2

C# / .NET 8 WinForms + WebView2 远程烧录工作台。支持 USB、WiFi、BLE、目标串口、芯片选择、固件升级及 USB/BLE 配网。固件在独立仓库维护，本仓库不依赖其源码目录。

扫描 USB 探针后，在 CMSIS-DAP 序列号下拉列表中选择目标探针。原探针从扫描结果中消失时，相应选择会清空。OTA 则以首次连接读到的序列号为准，重启后核对同一设备的序列号、项目、协议及启动镜像。

## 源码构建

在本目录使用 Windows x64、.NET 8 SDK：

```powershell
dotnet build WireLink.App/WireLink.App.csproj -c Release
```

运行目标烧录功能需要 Python 3.10+ 和 pyOCD：执行 `安装上位机依赖.cmd` 创建本地 `.venv-ble`，或将 `WIRELINK_PYTHON` 指向已有的 pyOCD Python 解释器。源码直接运行使用 `dotnet run --project WireLink.App`。界面使用系统 WebView2 Runtime，缺少时先安装微软运行时。

## 便携 EXE 打包

本仓库及源码 release 不含构建环境、运行库、下载缓存或设备包二进制。先安装 Windows x64 Python（带 pip）、.NET 8 SDK 和 Visual Studio C++ Build Tools（含 Windows SDK），然后在本目录执行：

```powershell
python tools/prepare-assets.py
.\build.cmd
```

下载资源保存在被 Git 忽略的 `portable/`。Python 固定为 3.14.2，pyOCD 固定为 0.46.1，依赖清单见 `assets/runtime-requirements.txt`；运行时下载地址、SHA256 见 `assets/runtime-assets.json`；STM32 设备目录及设备包校验值见 `assets/`。设备包可能受各自许可约束。`prepare-assets.py` 需要联网，且会拒绝校验不符的文件；微软引导安装器更新时须核实签名并显式更新清单，不能自动跳过校验。发布前验证缓存及提取后的 Python，精简后验证 pyOCD 后端导入。脚本创建的旧发布备份保留最近 3 份，未标记的历史目录不自动删除。

输出在 `publish/`：保留所有文件后可以复制到其他 Windows 10/11 x64 设备，双击 `WireLink.exe`。已包含 .NET 与 pyOCD；使用系统 WebView2，缺少时调用微软签名的引导安装器，需要联网。Edge 浏览器的存在本身不保证 WebView2 Runtime 已安装。语言资源保留在 `app/lang/`。仅一个 `app/WebView2Loader.dll`。

固件镜像需手动选择；如需随 EXE 携带，打包时显式指定：

```powershell
./tools/Publish-Portable.ps1 -FirmwareImage C:/path/esp_swdlink.bin
```

## 验证与版本归档

```powershell
dotnet run --project WireLink.Tests -c Release
node --test tests/*.test.cjs
python -B tests/test_remote.py
```

C# 测试需要 pyOCD 解释器，可设置 `WIRELINK_PYTHON`。普通测试不会连接或写入硬件；硬件测试必须显式传入参数。

当前发行目录为 `release/0.2.2/`，包含源码 ZIP 和可直接使用的 `WireLink-0.2.2-win-x64.7z`。便携包需完整解压后双击 EXE，包含运行依赖及语言资源，不携带个人设置和固件镜像；校验值与文件清单位于同目录。大型便携包及其生成清单不进入 Git，源码、源码 ZIP、版本说明和压缩包校验值纳入版本管理。既有发行目录保留，不覆盖旧包。版本统一在 `Directory.Build.props` 设置。

0.2.2 配合固件 0.1.1 使用：WiFi/BLE remote 大块内存和 AP 请求由上位机分块，BLE OTA 在写入前请求 Windows 配对并强制加密；WiFi OTA 端口可配置，通过 USB/BLE 读取设备信息时可同步实际端口。窗口状态只表示设备存在授权窗口，无线操作请长按 BOOT 开放对应链路；USB 授权不开放无线。不改变固件报文或放宽固件校验，APv2 等能力仍以固件为准。BLE 实机配对与 OTA 仍需硬件验收。

0.2.1 的审阅项处理、验证边界和后续实机项目见 `docs/REPAIR_PLAN_0.2.1.md`。Windows CI 执行无硬件构建与回归；真实设备测试不自动执行。
