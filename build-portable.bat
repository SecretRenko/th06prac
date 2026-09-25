@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-portable.ps1"
if errorlevel 1 (
  echo Portable package build failed. See the message above.
  pause
  exit /b 1
)
exit /b 0
