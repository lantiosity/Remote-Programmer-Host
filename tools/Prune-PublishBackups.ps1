param([ValidateRange(0,100)][int]$Keep = 3)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backupRoot = Join-Path $workspace '.publish-backup'
if (-not (Test-Path -LiteralPath $backupRoot)) { return }
if ((Get-Item -LiteralPath $backupRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Backup root must not be a link.' }
$owned = @(Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object {
    $_.Name -match '^[0-9a-f]{32}$' -and
    -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
    (Test-Path -LiteralPath (Join-Path $_.FullName '.wirelink-backup'))
} | Sort-Object LastWriteTimeUtc -Descending)
foreach ($entry in @($owned | Select-Object -Skip $Keep)) {
    $resolved = [IO.Path]::GetFullPath($entry.FullName)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $backupRoot -or -not $resolved.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Backup deletion escaped the workspace.'
    }
    # Never follow directory links inside a backup during recursive cleanup.
    if (Get-ChildItem -LiteralPath $resolved -Recurse -Force -Attributes ReparsePoint | Select-Object -First 1) { continue }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
