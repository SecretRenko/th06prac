@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-ui.ps1"
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
  echo.
  echo UI build stopped with exit code %BUILD_EXIT%. See error above.
  pause
)
exit /b %BUILD_EXIT%
