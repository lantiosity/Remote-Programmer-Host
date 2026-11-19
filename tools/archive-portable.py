"""Package an already validated publish/ without user settings; verify extraction.

Run after archive-source.py. Requires 7-Zip; does not publish to any server.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--publish-dir', type=Path, default=root / 'publish', help='Path to the verified portable build')
publish = parser.parse_args().publish_dir.resolve()
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
if not version or any(c not in '0123456789.' for c in version):
    raise ValueError('Invalid host version')
destination = root / 'release' / version
if not (destination / f'host-{version}-source.zip').is_file():
    raise RuntimeError('Archive staged source first')
name = f'WireLink-{version}-win-x64'
archive = destination / (name + '.7z')
manifest_path = destination / (name + '.manifest.json')
if archive.exists() or manifest_path.exists():
    raise FileExistsError('Portable release already exists; never overwrite a version')
sevenzip = shutil.which('7z') or str(Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / '7-Zip' / '7z.exe')
if not Path(sevenzip).is_file():
    raise FileNotFoundError('Install 7-Zip before packaging')
if {p.name for p in publish.iterdir()} != {'WireLink.exe', '使用说明.txt', 'app', 'data'}:
    raise RuntimeError('Unexpected publish root entries')
if (publish / 'app' / 'firmware').exists():
    raise RuntimeError('This host-only release must not carry a firmware image')

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def files_in(folder):
    return [{'path': p.relative_to(folder).as_posix(), 'size': p.stat().st_size, 'sha256': digest(p)}
            for p in sorted(folder.rglob('*')) if p.is_file()]

(root / 'obj').mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='archive-', dir=root / 'obj') as temporary:
    staging = Path(temporary)
    package = staging / name
    shutil.copytree(publish, package, ignore=lambda folder, names: {'data'} if Path(folder) == publish else set())
    (package / 'data').mkdir()
    expected = files_in(package)
    # Empty data/ is preserved; no local settings or device history enters the archive.
    candidate = staging / (name + '.7z')
    subprocess.run([sevenzip, 'a', '-t7z', '-mx=7', '-mmt=2', '-bsp0', str(candidate), name], cwd=staging, check=True)
    subprocess.run([sevenzip, 't', '-bsp0', str(candidate)], check=True)
    unpacked = staging / 'verified'
    subprocess.run([sevenzip, 'x', '-y', '-bsp0', f'-o{unpacked}', str(candidate)], check=True)
    restored = unpacked / name
    if files_in(restored) != expected or list((restored / 'data').iterdir()):
        raise RuntimeError('Extracted portable files differ from package input')
    manifest = {'version': version, 'platform': 'Windows x64', 'minimum_windows': 'Windows 10 2004',
                'archive': archive.name, 'archive_size': candidate.stat().st_size, 'sha256': digest(candidate),
                'unpacked_size': sum(p['size'] for p in expected), 'file_count': len(expected), 'files': expected}
    shutil.move(candidate, archive)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    with (destination / 'SHA256SUMS.txt').open('a', encoding='utf-8') as sums:
        sums.write(f"{manifest['sha256']}  {archive.name}\n")
    print(f"Verified {len(expected)} extracted files; {archive.stat().st_size / 1024**2:.1f} MiB archive.")
