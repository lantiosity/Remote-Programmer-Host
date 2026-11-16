@echo off
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Install the .NET 8 SDK to build WireLink.
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\Publish-Portable.ps1"
exit /b %errorlevel%
