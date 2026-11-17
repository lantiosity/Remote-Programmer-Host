param([string]$Output = 'publish', [switch]$IncludeBrowser, [string]$FirmwareImage)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$assets = Join-Path (Get-Location) 'portable'
foreach ($required in @('python/python.exe','targets.json','target-packs.json','packs-manifest.json')) {
    if (-not (Test-Path (Join-Path $assets $required))) { throw "Missing portable asset: $required" }
}
& "$assets/python/python.exe" -B tools/asset_integrity.py
if ($LASTEXITCODE -ne 0) { throw 'Portable asset integrity validation failed.' }
$browser = @()
if ($IncludeBrowser) {
    $browserCache = Join-Path $assets 'webview'
    if (-not (Test-Path -LiteralPath $browserCache)) { throw 'Fixed WebView2 cache is absent. Download it again before using -IncludeBrowser; the default slim build does not need it.' }
    $browser = @(Get-ChildItem -LiteralPath $browserCache -Directory)
    if ($browser.Count -ne 1) { throw 'Expected one fixed WebView2 runtime.' }
}
$workspace = (Get-Location).Path
$destination = [IO.Path]::GetFullPath((Join-Path $workspace $Output))
if (-not $destination.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must stay inside the workspace.' }
New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
$staging = Join-Path $workspace ('obj/portable-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $staging 'app'
New-Item -ItemType Directory -Force $staging | Out-Null
dotnet publish WireLink.App/WireLink.App.csproj -c Release --self-contained true -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force "$app/engine", "$app/setup", "$app/devices/packs", "$staging/data" | Out-Null
Copy-Item "$assets/python/*" "$app/engine" -Recurse -Force
if ($IncludeBrowser) {
    New-Item -ItemType Directory -Force "$app/browser" | Out-Null
    Copy-Item "$($browser[0].FullName)/*" "$app/browser" -Recurse -Force
    Copy-Item "$assets/webview-source.txt" "$app/browser" -Force
}
Copy-Item "$assets/MicrosoftEdgeWebview2Setup.exe" "$app/setup" -Force
if ($FirmwareImage) {
    New-Item -ItemType Directory -Force "$app/firmware" | Out-Null
    Copy-Item -LiteralPath $FirmwareImage -Destination "$app/firmware/esp_swdlink.bin"
}
$signature = Get-AuthenticodeSignature "$app/setup/MicrosoftEdgeWebview2Setup.exe"
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Expected the signed Microsoft WebView2 installer.' }
Copy-Item "$assets/packs/*.pack" "$app/devices/packs" -Force
Copy-Item "$assets/targets.json", "$assets/target-packs.json", "$assets/packs-manifest.json" "$app/devices" -Force
& "$app/engine/python.exe" tools/prune-publish.py $app
if ($LASTEXITCODE -ne 0) { throw "Publish cleanup failed." }
& "$app/engine/python.exe" -B -c "import pyocd; from pyocd.probe import cmsis_dap_probe, stlink_probe, jlink_probe; from pyocd.probe.tcp_client_probe import TCPClientProbe; from pyocd.target.pack import pack_target; from pyocd.utility import usb; print('Pruned pyOCD backends imported:', pyocd.__version__)"
if ($LASTEXITCODE -ne 0) { throw 'Pruned engine import smoke test failed.' }
Copy-Item "$workspace/assets/PORTABLE-README.txt" "$staging/使用说明.txt" -Force
foreach ($settings in @("$destination/data/settings.json", "$destination/.wirelink-desktop.json")) {
    if (Test-Path -LiteralPath $settings) { Copy-Item -LiteralPath $settings -Destination "$staging/data/settings.json"; break }
}
& tools/Build-Launcher.cmd $staging
if ($LASTEXITCODE -ne 0) { throw 'Native launcher build failed.' }
# Keep compiler intermediates out of the deliverable.
$intermediates = Join-Path $workspace ('obj/launcher-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $intermediates | Out-Null
Move-Item -LiteralPath "$staging/Launcher.obj", "$staging/Launcher.res" -Destination $intermediates
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $workspace ('.publish-backup/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null
    Move-Item -LiteralPath $destination -Destination $backup
    Set-Content -LiteralPath (Join-Path $backup '.wirelink-backup') -Value 'WireLink portable publish backup' -Encoding utf8
}
Move-Item -LiteralPath $staging -Destination $destination
& "$PSScriptRoot/Prune-PublishBackups.ps1" -Keep 3
Write-Host "Portable Windows x64 application: $Output"
