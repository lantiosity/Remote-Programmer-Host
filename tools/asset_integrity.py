"""Validate the pinned cache before copying it into a portable release."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile

def verify(path, expected):
    with path.open('rb') as source:
        actual = hashlib.file_digest(source, 'sha256').hexdigest()
    if actual != expected.lower():
        raise RuntimeError(f'Checksum mismatch: {path}; review the asset manifest before replacing a pinned release dependency')

def validate(root):
    cache = root / 'portable'
    for manifest, folder in [('runtime-assets.json', cache), ('packs-manifest.json', cache / 'packs')]:
        for item in json.loads((root / 'assets' / manifest).read_text(encoding='utf-8')):
            if Path(item['file']).name != item['file']:
                raise ValueError('Invalid asset filename')
            verify(folder / item['file'], item['sha256'])
    with zipfile.ZipFile(cache / 'python-3.14.2-embed-amd64.zip') as archive:
        for name in archive.namelist():
            if name.endswith('/') or name == 'python314._pth':
                continue
            if Path(name).name != name:
                raise ValueError('Unexpected Python archive member')
            expected = hashlib.sha256(archive.read(name)).hexdigest()
            verify(cache / 'python' / name, expected)
    if (cache / 'python' / 'python314._pth').read_text(encoding='utf-8').splitlines() != ['python314.zip', '.', 'Lib', 'import site']:
        raise RuntimeError('Unexpected embedded Python search path')
    print('Pinned runtime, extracted Python and device packs verified.')

if __name__ == '__main__':
    validate(Path(__file__).resolve().parents[1])
