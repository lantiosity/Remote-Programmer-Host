# 上位机实现说明（0.2.0）

主程序为 C# / .NET 8 WinForms，WebView2 承载本地 HTML/CSS/JavaScript。设备扫描、BLE DAP 桥、USB/TCP/BLE 串口、ESP32 OTA 及配网是 C# 原生实现；目标 MCU Flash 算法和 CMSIS 设备支持包使用外部 pyOCD 引擎。

## 窗口和界面

支持浅色、深色和跟随系统。控制栏由应用绘制，只显示窗口按钮，不显示文字或图标；真实窗口标题仍供任务栏、Alt+Tab 和辅助工具读取。支持拖动、缩放、最大化、贴靠及 PerMonitorV2 DPI。

程序先绘制符合主题的加载画面，再异步初始化 WebView2。启动后后台静默加载一次芯片列表，当前进程不自动重复刷新，用户仍可手动刷新。

芯片菜单按厂商、系列和具体型号分级，HC32/MM32/MX32 按器件系列细分；芯片与 SWD 时钟选择使用一致的菜单风格。设备目录源数据在 `assets/targets.json`，具体算法映射在 `assets/target-packs.json`。

悬浮滚动条不占布局宽度，默认细灰色，悬停/操作时略变宽，离开且停止操作后延迟收细，始终无轨道背景。CSS 相对单位与实时几何计算适应 DPI/缩放。

## 运行和打包

构建、便携 EXE 及测试命令见根目录 README。源码构建与便携打包是两个步骤；发布源码不包含编译工具或运行库。便携包根目录为 `WireLink.exe`、`使用说明.txt`、`app/`、`data/`。

启动入口为带图标的原生 EXE，直接创建 `app/WireLink.exe`，无需自解压。发布版使用 `app/engine/python.exe`；源码模式使用本目录 `.venv-ble` 或 `WIRELINK_PYTHON`。语言程序集在 `app/lang/`，唯一的 WebView2Loader 在 `app/`。系统 WebView2 缺失时使用微软签名引导程序安装，需要网络。

便携设置位于 `data/settings.json`；WebView2 用户数据和启动记录在 `%LOCALAPPDATA%/WireLink`。复制到其他电脑后需要重新选择外部固件、额外设备包和设备地址。

## 通信与升级

USB 使用 CMSIS-DAP；WiFi 使用设备网络地址；BLE 连接使用左侧选择的蓝牙设备。串口功能支持 USB/TCP/BLE。

「烧录器固件升级」更新 ESP32 自身的应用镜像，支持 USB、WiFi 和 BLE；「目标烧录」处理目标 MCU 镜像，两者请分别选择。ESP32 镜像从独立固件仓库构建或显式随便携包携带，不隐式依赖相邻源码目录。

WiFi 配网支持 USB、BLE、CH343 控制台，见 [配网说明](WIFI_SETUP.md)。硬件测试需要显式指定设备，不应在普通自动测试中连接用户的烧录器。
