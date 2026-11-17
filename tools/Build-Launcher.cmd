@echo off
setlocal
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "WIRELINK_VS=%%i"
if not defined WIRELINK_VS exit /b 1
call "%WIRELINK_VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Write-VersionResource.ps1 -Output "%~1"
if errorlevel 1 exit /b 1
rc /nologo /fo "%~1\Launcher.res" "%~1\Launcher.generated.rc"
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /O2 /MT /EHsc /utf-8 /Fo"%~1\Launcher.obj" tools\Launcher.cpp "%~1\Launcher.res" /link /SUBSYSTEM:WINDOWS /OUT:"%~1\WireLink.exe" user32.lib
exit /b %errorlevel%
