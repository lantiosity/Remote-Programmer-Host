WireLink 0.2.0 便携版（Windows x64）

双击 WireLink.exe 启动。请复制整个文件夹，不要只复制 EXE。

目录结构：
  WireLink.exe  原生轻量启动入口，带应用图标
  使用说明.txt  本说明
  app/          主程序、.NET、Python / pyOCD、语言资源、WebView2 引导安装器、界面及设备包
  data/         用户设置 settings.json，复制目录时一起保留

适用于 Windows 10 2004 及以上 x64 和 Windows 11。无需另装 Python 或 .NET。默认精简版使用系统 WebView2，未安装时首次启动会通过 app/setup 中的微软官方签名引导安装器联网安装（/silent /install），然后继续启动。Edge 浏览器本身不等于 WebView2；首次安装失败可手动运行安装器。离线且没有 WebView2 时不能启动网页界面。请保留完整 app 目录，不要手工移动其中的 DLL 或子目录。精简版约 366 MiB。语言资源全部保留于 app/lang，由程序按文化名称加载。

固件及额外 .pack 文件仍为外部文件，复制到新电脑后请重新选择路径。目标电脑需具备相应 USB 驱动、蓝牙硬件及目标接线。浏览器缓存及启动计时位于 %LOCALAPPDATA%/WireLink，不占发布根目录。

内置 30 个官方 STM32 DFP，版本、来源和校验值位于 app/devices/packs-manifest.json。目录是当前设备包快照，未对全部芯片做实机测试；MP 系列及外部存储器设备仍受调试环境、pyOCD 支持及芯片状态限制。

工作台显示后延迟静默加载一次芯片目录，当次运行只在点击刷新时重新加载。

第三方许可保留在 app/engine、app/browser 及 app/devices/packs 的原始包文件中。构建脚本需要 .NET SDK 和 Visual Studio C++ 工具；普通使用不需要这些开发工具。

构建时可用 Publish-Portable.ps1 -IncludeBrowser 生成带固定版浏览器的离线完整包；这会额外增加约 678 MiB。默认不携带 browser 目录。

WiFi 配置：支持原生 USB、BLE 和 CH343 控制台。需要支持配网能力的固件，当前首版为 0.1.0。仅在打包时显式指定固件镜像才会提供 app/firmware/esp_swdlink.bin；也可手动选择外部应用镜像。BLE 保存和清除网络前长按 BOOT 约 2 秒开放窗口。保存后设备重启，稍后读取状态确认是否联网。仅支持 2.4 GHz 开放网络与个人密码网络，网络密码不保存到电脑设置或日志。
