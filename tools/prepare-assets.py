"""Recreate ignored portable dependencies. Run with a Windows Python having pip."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[1]
assets = root / 'assets'
cache = root / 'portable'
cache.mkdir(exist_ok=True)

def download(url, path, sha256=None):
    if not path.exists():
        temporary = path.with_suffix(path.suffix + '.download')
        urllib.request.urlretrieve(url, temporary)
        temporary.replace(path)
    if sha256 and hashlib.sha256(path.read_bytes()).hexdigest() != sha256:
        raise RuntimeError(f'Checksum mismatch: {path}; remove this file and retry')

for name in ['targets.json', 'target-packs.json', 'packs-manifest.json', 'PORTABLE-README.txt']:
    shutil.copy2(assets/name, cache/name)
python = cache / 'python'
python.mkdir(exist_ok=True)
archive = cache / 'python-3.14.2-embed-amd64.zip'
download('https://www.python.org/ftp/python/3.14.2/python-3.14.2-embed-amd64.zip', archive)
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
download('https://go.microsoft.com/fwlink/p/?LinkId=2124703', cache/'MicrosoftEdgeWebview2Setup.exe')
print('Assets ready. Publish-Portable.ps1 verifies the Microsoft installer signature.')
