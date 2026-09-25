@echo off
setlocal
rem th06ncprac Lite - no bundled .NET runtime; needs .NET 10 Desktop Runtime (x64).
set "APP=%~dp0th06ncprac.exe"
if not exist "%APP%" (
  echo [ERROR] Missing th06ncprac.exe
  pause
  exit /b 1
)
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if exist "%DOTNET%" goto run
set "DOTNET="
for /f "delims=" %%I in ('where dotnet.exe 2^>nul') do if not defined DOTNET set "DOTNET=%%I"
if not defined DOTNET goto nodotnet
:run
start "th06ncprac" "%APP%"
exit /b 0
:nodotnet
echo [ERROR] dotnet.exe not found.
echo This Lite build needs the .NET 10 Desktop Runtime (x64):
echo   https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b 1
