@echo off
setlocal
if exist "%~dp0runtime\host\fxr" goto bundled
set "APP=%~dp0dist-ui\XinDianPrac.Gui.exe"
if not exist "%APP%" (
  echo Practice UI not found. Run build-ui.bat first.
  pause
  exit /b 1
)
start "" "%APP%"
if errorlevel 1 (
  echo Failed to start Practice UI.
  pause
  exit /b 1
)
exit /b 0

:bundled
set "DOTNET_ROOT=%~dp0runtime"
set "DOTNET_ROOT_X64=%~dp0runtime"
set "PATH=%~dp0runtime;%PATH%"
set "APP=%~dp0dist-ui\XinDianPrac.Gui.dll"
if not exist "%APP%" (
  echo Practice UI not found in this folder.
  pause
  exit /b 1
)
start "" "%~dp0runtime\dotnet.exe" "%APP%"
exit /b 0
