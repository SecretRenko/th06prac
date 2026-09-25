@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-lite.ps1"
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
  echo.
  echo Lite build stopped with exit code %BUILD_EXIT%. See error above.
  pause
)
exit /b %BUILD_EXIT%
