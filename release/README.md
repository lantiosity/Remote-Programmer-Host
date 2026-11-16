# 上位机版本归档

首版：`0.2.0/`。各版本目录保留源码 ZIP、逐文件 manifest、SHA256 和说明，不放旧 Python/WinUI3 上位机、EXE、运行库或构建环境。

在项目根目录完成版本修改、构建验证并暂存源码后，执行 `python tools/archive-source.py 版本号`，再将该版本目录加入提交。脚本不会覆盖已存在的版本目录。归档不包含 `release/` 自身，避免嵌套历史包。
