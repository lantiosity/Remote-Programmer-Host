# 上位机发布包

[WireLink 0.3.1](0.3.1/README.md) 为本次公开发布候选，配套固件 0.1.5。

在同一版本目录提供源码 ZIP、Windows x64 便携包、逐文件清单和 SHA256。便携包首次解压后可双击 `WireLink.exe`。设备固件从固件仓库单独下载。

构建和验证完成后，暂存源码并执行 `python tools/archive-source.py 版本号`。使用 `tools/Publish-Portable.ps1` 生成便携目录，再执行 `python tools/archive-portable.py`。打包脚本会清空归档内的 `data/` 并解包核对摘要。
