@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Practice.ps1"
set "PRACTICE_EXIT=%ERRORLEVEL%"
if not "%PRACTICE_EXIT%"=="0" (
  echo.
  echo Launcher stopped with exit code %PRACTICE_EXIT%. See error above.
)
pause
exit /b %PRACTICE_EXIT%
