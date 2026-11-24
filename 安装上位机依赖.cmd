@echo off
cd /d "%~dp0"
if not exist ".venv-ble\Scripts\python.exe" (
  python -m venv .venv-ble
  if errorlevel 1 (
    echo Install Python 3.10 or newer, then run this script again.
    pause
    exit /b 1
  )
)
".venv-ble\Scripts\python.exe" -m pip install "pyocd==0.46.1"
if errorlevel 1 (
  pause
  exit /b 1
)
echo pyOCD is ready. Serial and BLE OTA are implemented natively in C#.
pause
