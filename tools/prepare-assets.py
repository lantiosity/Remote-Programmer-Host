"""Recreate ignored portable dependencies. Run with a Windows Python having pip."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from asset_integrity import verify

root = Path(__file__).resolve().parents[1]
assets = root / 'assets'
cache = root / 'portable'
cache.mkdir(exist_ok=True)

def download(url, path, sha256=None):
    if not path.exists():
        temporary = path.with_suffix(path.suffix + '.download')
        urllib.request.urlretrieve(url, temporary)
        temporary.replace(path)
    if sha256:
        verify(path, sha256)

for name in ['targets.json', 'target-packs.json', 'packs-manifest.json', 'PORTABLE-README.txt']:
    shutil.copy2(assets/name, cache/name)
python = cache / 'python'
python.mkdir(exist_ok=True)
for item in json.loads((assets/'runtime-assets.json').read_text(encoding='utf-8')):
    if Path(item['file']).name != item['file']:
        raise ValueError('Invalid runtime filename')
    download(item['download'], cache/item['file'], item['sha256'])
archive = cache / 'python-3.14.2-embed-amd64.zip'
with zipfile.ZipFile(archive) as package:
    package.extractall(python)
(python/'python314._pth').write_text('python314.zip\n.\nLib\nimport site\n', encoding='utf-8')
subprocess.run([sys.executable, '-m', 'pip', 'install', '--upgrade', '--target', str(python/'Lib'),
                '--only-binary=:all:', '--python-version', '3.14', '--platform', 'win_amd64',
                '--implementation', 'cp', '--abi', 'cp314', '-r', str(assets/'runtime-requirements.txt')], check=True)
packs = cache/'packs'
packs.mkdir(exist_ok=True)
for item in json.loads((assets/'packs-manifest.json').read_text(encoding='utf-8')):
    name = item['file']
    if Path(name).name != name:
        raise ValueError('Invalid pack filename')
    download(item['download'], packs/name, item['sha256'])
print('Assets ready. Publish-Portable.ps1 verifies the Microsoft installer signature.')
