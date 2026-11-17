param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$props = Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-component host version.' }
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Launcher.rc') -Raw
$icon = (Join-Path $workspace 'WireLink.App/Assets/WireLink.ico').Replace('\', '/')
$text = $template.Replace('@ICON_PATH@', $icon).Replace('@VERSION_COMMA@', $version.Replace('.', ',') + ',0').Replace('@VERSION@', $version)
[IO.File]::WriteAllText((Join-Path $Output 'Launcher.generated.rc'), $text, [Text.UTF8Encoding]::new($true))
