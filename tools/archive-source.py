"""Archive staged source files, excluding release history and ignored build output.

Usage: python tools/archive-source.py VERSION
Run after staging the release source with git add. Existing version directories
are never overwritten. Python and Git must be installed by the user.
"""
import hashlib
from datetime import date
import json
from pathlib import Path
import subprocess
import sys
import zipfile

root = Path(__file__).resolve().parents[1]
version = sys.argv[1]
archive_date = date.fromisoformat(sys.argv[2]) if len(sys.argv) > 2 else date.today()
if not version or any(c not in '0123456789.' for c in version):
    raise ValueError('Expected numeric version, e.g. 0.1.0')
destination = root/'release'/version
if destination.exists():
    raise FileExistsError(f'Release already exists: {destination}')
git = ['git', '-c', f'safe.directory={root.as_posix()}', '-C', str(root)]
paths = subprocess.check_output(git + ['ls-files', '--cached', '-z']).decode('utf-8').split('\0')
paths = sorted(p for p in paths if p and not p.startswith('release/'))
if not paths:
    raise RuntimeError('Stage the source files before archiving')
files = []
contents = {}
for name in paths:
    path = root/name
    if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(root):
        raise ValueError(f'Unexpected source path: {name}')
    # Use Git's staged blob so line-ending normalization matches the commit.
    data = subprocess.check_output(git + ['show', f':{name}'])
    contents[name] = data
    files.append({'path': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
destination.mkdir(parents=True)
archive = destination/f'{root.name}-{version}-source.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as package:
    for item in files:
        # Source content, stable paths and no machine-specific filesystem metadata.
        info = zipfile.ZipInfo(item['path'], (archive_date.year, archive_date.month, archive_date.day, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        package.writestr(info, contents[item['path']])
manifest = {'project': root.name, 'version': version, 'date': archive_date.isoformat(), 'kind': 'source-only',
            'archive': archive.name, 'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
            'files': files}
(destination/'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
(destination/'SHA256SUMS.txt').write_text(f"{manifest['archive_sha256']}  {archive.name}\n", encoding='utf-8')
(destination/'README.md').write_text(f'''# {root.name} {version} 源码归档

日期：{archive_date.isoformat()}。归档为源码与相关文档，不含构建环境、运行库、缓存或编译产物。
解压源码包后按其中 README 构建；文件清单和逐文件 SHA256 见 manifest.json。
版本档案保留在本项目独立仓库，后续新增版本目录，不覆盖本目录。
本版对应包含此目录的版本提交及标签 v{version}。仅本地归档，不发布 GitHub，不烧写设备。
''', encoding='utf-8')
print(f'{root.name} {version}: {len(files)} source files, {archive.stat().st_size} bytes')
