# 上位机本地打包

打包脚本会在本目录生成源码 ZIP、Windows x64 便携包、逐文件清单和 SHA256。便携包首次解压后可双击 `WireLink.exe`。设备固件从配套固件仓库单独构建。

构建和验证完成后，暂存源码并执行 `python tools/archive-source.py 版本号`。使用 `tools/Publish-Portable.ps1` 生成便携目录，再执行 `python tools/archive-portable.py`。打包脚本会清空归档内的 `data/` 并解包核对摘要。
