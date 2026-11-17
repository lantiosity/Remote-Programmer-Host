# WireLink 0.2.0 发行文件

日期：2026-11-17。

- `WireLink-0.2.0-win-x64.7z`：可直接使用的 Windows x64 便携版，包含 .NET、Python/pyOCD、设备包、语言资源和 WebView2 引导安装器。解压整个文件夹后双击 `WireLink.exe`；不要只取出 EXE。适用于 Windows 10 2004 及以上 x64 / Windows 11。缺少 WebView2 时首次启动需要联网安装，Edge 浏览器本身不保证已具备该运行时。
- `host-0.2.0-source.zip`：原有源码与文档快照，不含构建环境或运行库。解压后按其中 README 构建；源文件清单与逐文件 SHA256 见 `manifest.json`。
- `SHA256SUMS.txt`：压缩包 SHA-256 校验值。
- `WireLink-0.2.0-win-x64.manifest.json`：便携包逐文件校验清单。

便携版以当前上位机源码重新构建，不包含个人设备设置或固件镜像，不自动连接或烧写设备。解压后的顶层为 `WireLink.exe`、`使用说明.txt`、`app/`、`data/`。首次运行会在 data 中生成默认设置。本次仅生成上位机发行文件，未改动或烧写固件。
版本档案保留在本项目独立仓库，后续新增版本目录，不覆盖本目录。
源码快照对应首版提交及标签 v0.2.0。仅本地归档，不发布 GitHub。
