# 上位机版本归档

首版：`0.2.0/`。各版本目录保留源码 ZIP、逐文件 manifest、SHA256 和说明；可直接使用的 Windows x64 便携版另以 `WireLink-版本-win-x64.7z` 提供，与源码包并列。源码包仍不含运行库或构建环境，便携包包含运行依赖，不含个人设置。旧 Python/WinUI3 实验版本不进入此发行目录。

在项目根目录完成版本修改、构建验证并暂存源码后，执行 `python tools/archive-source.py 版本号`，再将该版本目录加入提交。脚本不会覆盖已存在的版本目录。归档不包含 `release/` 自身，避免嵌套历史包。

当前版本为 [0.2.3](0.2.3/)。完成 `tools/Publish-Portable.ps1` 及源码归档后，执行 `python tools/archive-portable.py`，从 Directory.Build.props 读取版本并生成便携 7z、逐文件清单及校验码；会剔除 data 中的个人设置，验证 7z 完整性并解压逐文件比对。需要安装 7-Zip。脚本拒绝覆盖同版本便携包。

源码 ZIP、版本说明及 SHA256 纳入 Git；大型便携 7z 与其生成清单保存在本地 release 目录，通过 .gitignore 排除，不向远端自动上传。旧版文件保留。
