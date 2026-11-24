# WireLink 上位机 0.3.1

C# / .NET 8 WinForms + WebView2 远程烧录工作台。支持 USB、WiFi、BLE、目标串口、芯片选择、固件升级及 USB/BLE 配网。固件在独立仓库维护，本仓库不依赖其源码目录。

当前源码版本为 **0.3.1**，适用于 Windows x64。请按下文构建上位机。ESP32 升级时可选择配套固件 **0.1.5** 构建生成的 `esp_swdlink.bin`。[ESP-SWD 固件仓库](https://github.com/lantiosity/Remote-Programmer-Firmware)

扫描 USB 探针后，在 CMSIS-DAP 序列号下拉列表中选择目标探针。原探针从扫描结果中消失时，相应选择会清空。OTA 则以首次连接读到的序列号为准，重启后核对同一设备的序列号、项目、协议及启动镜像。

## DFP 库与离线烧录

主动导入的 DFP 保存到 `data/dfp/`，同名不同版本按内容分别保存。配置记录相对路径，复制整个便携目录即可带走。导入后点击「加载芯片列表」查看型号。

「离线烧录」可管理 STM32H743VI（2 MiB）目标的 BIN 镜像，保存面板起始地址和复位选项，并按 128 KiB 扇区核算擦除范围。将镜像导出到 FAT32 microSD 根目录后，可在运行配套固件的离线面板上操作。地址和复位选项需在面板手动设置，板载镜像通过设备面板保存。

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

版本统一在 `Directory.Build.props` 设置。生成源码 ZIP 或便携包时，参见[本地打包流程](release/README.md)。

WiFi DAP、串口和 OTA 各自校验端口。WiFi/BLE remote 的大块内存和 AP 请求由上位机分块。BLE 配网和 OTA 均使用加密链路，首次使用请完成 Windows 配对。无线 OTA 或 BLE 配网保存前，请在设备运行时长按 BOOT 约 2 秒开放相应授权窗口。真实 BLE 配对、收发和 OTA 回滚仍待实板验收。

0.2.1 的审阅项处理、验证边界和后续实机项目见 `docs/REPAIR_PLAN_0.2.1.md`。Windows CI 执行无硬件构建与回归；真实设备测试不自动执行。
