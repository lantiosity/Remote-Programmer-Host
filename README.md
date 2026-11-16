# WireLink 上位机 0.2.0

C# / .NET 8 WinForms + WebView2 远程烧录工作台。支持 USB、WiFi、BLE、目标串口、芯片选择、固件升级及 USB/BLE 配网。固件在独立仓库维护，本仓库不依赖其源码目录。

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

下载资源保存在被 Git 忽略的 `portable/`。Python 固定为 3.14.2，pyOCD 固定为 0.46.1，依赖清单见 `assets/runtime-requirements.txt`；STM32 设备目录及设备包版本、下载地址和 SHA256 见 `assets/`。设备包可能受各自许可约束。`prepare-assets.py` 需要联网，未在本次整理中重新下载全部资源。

输出在 `publish/`：保留所有文件后可以复制到其他 Windows 10/11 x64 设备，双击 `WireLink.exe`。已包含 .NET 与 pyOCD；使用系统 WebView2，缺少时调用微软签名的引导安装器，需要联网。Edge 浏览器的存在本身不保证 WebView2 Runtime 已安装。语言资源保留在 `app/lang/`。仅一个 `app/WebView2Loader.dll`。

固件镜像需手动选择；如需随 EXE 携带，打包时显式指定：

```powershell
./tools/Publish-Portable.ps1 -FirmwareImage C:/path/esp_swdlink.bin
```

## 验证与版本归档

```powershell
dotnet run --project WireLink.Tests -c Release
node --test tests/pickers.test.cjs tests/scrollbars.test.cjs
```

C# 测试需要 pyOCD 解释器，可设置 `WIRELINK_PYTHON`。普通测试不会连接或写入硬件；硬件测试必须显式传入参数。

`release/0.2.0/` 是首版源码归档，包含源文件清单、SHA256 和说明，不包含 EXE。后续版本分别新增版本目录，不覆盖既有归档。修改源码后重新生成新版本归档，不能将旧快照视为当前工作区的自动镜像。版本统一在 `Directory.Build.props` 设置。
