"""Conservative cleanup; keep CLR assemblies outside the WPF-only allowlist."""
import pathlib
import shutil
import sys

app = pathlib.Path(sys.argv[1]).resolve()
workspace = pathlib.Path(__file__).resolve().parents[1]
assert app.is_relative_to(workspace / 'obj') and app.name == 'app'
before = sum(p.stat().st_size for p in app.rglob('*') if p.is_file())
# This is a fixed win-x64 application. Keep one loader beside the managed host.
loader = app / 'WebView2Loader.dll'
for duplicate in list((app / 'runtimes').rglob('WebView2Loader.dll')):
    if 'win-x64' not in duplicate.parts:
        raise RuntimeError('Unexpected loader architecture in win-x64 publish')
    if not loader.exists():
        shutil.copy2(duplicate, loader)
    if loader.read_bytes() != duplicate.read_bytes():
        raise RuntimeError('Conflicting WebView2 loader copies')
    duplicate.unlink()
if (app / 'runtimes').exists():
    for directory in sorted((app / 'runtimes').rglob('*'), key=lambda p: len(p.parts), reverse=True):
        if directory.is_dir() and not any(directory.iterdir()): directory.rmdir()
    if not any((app / 'runtimes').iterdir()): (app / 'runtimes').rmdir()
lang = app / 'lang'
lang.mkdir(exist_ok=True)
for directory in list(app.iterdir()):
    if directory.is_dir() and list(directory.glob('*.resources.dll')):
        shutil.move(str(directory), str(lang / directory.name))
wpf = {'WindowsBase', 'WindowsFormsIntegration', 'ReachFramework', 'System.Xaml',
       'System.Windows.Controls.Ribbon', 'System.Windows.Input.Manipulations', 'System.Windows.Presentation',
       'DirectWriteForwarder', 'wpfgfx_cor3', 'PresentationNative_cor3', 'D3DCompiler_47_cor3', 'PenImc_cor3'}
for file in list(app.iterdir()):
    if file.is_file() and (file.suffix in {'.pdb', '.xml'} or
       file.suffix == '.dll' and (file.stem in wpf or file.stem.startswith('Presentation'))):
        file.unlink()
library = app / 'engine' / 'Lib'
unused = ('pip', 'bleak', 'winrt', 'wirelink', '__editable__')
for file in list(library.iterdir()):
    if any(file.name == name or file.name.startswith(name + '-') or file.name.startswith(name + '_') or
           name == '__editable__' and file.name.startswith(name) for name in unused):
        if file.is_dir(): shutil.rmtree(file)
        else: file.unlink()
for directory in list((app / 'engine').rglob('__pycache__')):
    shutil.rmtree(directory)
after = sum(p.stat().st_size for p in app.rglob('*') if p.is_file())
print(f'Conservative cleanup: {(before-after)/1024**2:.1f} MiB saved; all language resources preserved.')
