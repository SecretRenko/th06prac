$ErrorActionPreference = 'Stop'
# Build the Lite distribution package: no .NET runtime bundled, target machine
# must have the .NET 10 Desktop Runtime (x64) installed.
# Output: release\th06ncprac-Lite-<stamp>\ and release\th06ncprac-Lite-<stamp>.zip
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads BOM-less files as
# ANSI, so non-ASCII characters here can silently swallow the next line.
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet'
$env:APPDATA = Join-Path $workspace '.appdata'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$core = Join-Path $workspace 'src\XinDianPrac\XinDianPrac.csproj'
$gui = Join-Path $workspace 'src\XinDianPrac.Gui\XinDianPrac.Gui.csproj'
$config = Join-Path $workspace 'NuGet.Config'
$release = Join-Path $workspace 'release'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$packageName = "th06ncprac-Lite-$stamp"
$package = Join-Path $release $packageName

New-Item -ItemType Directory -Path $release -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $package 'dist') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $package 'dist-ui') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $package 'config') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $package 'dist\config') -Force | Out-Null

dotnet restore $core --configfile $config
if ($LASTEXITCODE -ne 0) { throw 'Core restore failed.' }
dotnet restore $gui --configfile $config
if ($LASTEXITCODE -ne 0) { throw 'UI restore failed.' }

# Core keeps its apphost (the GUI launches CoreExe = dist\XinDianPrac.exe); pdb dropped to save size.
dotnet publish $core --no-restore -c Release -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $package 'dist')
if ($LASTEXITCODE -ne 0) { throw 'Core publish failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $package 'dist\XinDianPrac.exe'))) { throw 'Core publish did not produce dist\XinDianPrac.exe' }

# GUI is published without an apphost: saves 162 KB, the system dotnet.exe loads the dll.
dotnet publish $gui --no-restore -c Release -p:UseAppHost=false -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $package 'dist-ui')
if ($LASTEXITCODE -ne 0) { throw 'UI publish failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $package 'dist-ui\XinDianPrac.Gui.dll'))) { throw 'UI publish did not produce dist-ui\XinDianPrac.Gui.dll' }

Copy-Item -LiteralPath (Join-Path $workspace 'config\entrances.json') -Destination (Join-Path $package 'config\entrances.json')
Copy-Item -LiteralPath (Join-Path $workspace 'config\entrances.json') -Destination (Join-Path $package 'dist\config\entrances.json')
Copy-Item -LiteralPath (Join-Path $workspace 'config\settings.portable.json') -Destination (Join-Path $package 'config\settings.json')
Copy-Item -LiteralPath (Join-Path $workspace 'config\game-target.portable.json') -Destination (Join-Path $package 'config\game-target.json')
Copy-Item -LiteralPath (Join-Path $workspace 'Start-Practice-Lite.bat') -Destination (Join-Path $package 'Start-Practice.bat')
Copy-Item -LiteralPath (Join-Path $workspace 'README-Lite.txt') -Destination (Join-Path $package 'README-Lite.txt')

$files = Get-ChildItem -LiteralPath $package -Recurse -Force -File
$total = ($files | Measure-Object Length -Sum).Sum
$archive = Join-Path $release "$packageName.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
# ZipFile::CreateFromDirectory takes a directory path, so paths containing [ ] are safe
# (Compress-Archive mis-handles them under Windows PowerShell 5.1).
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($package, $archive,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "Lite folder : $package"
Write-Host ("Lite size   : {0:N0} bytes ({1:N3} MB, {2} files)" -f $total, ($total / 1MB), $files.Count)
Write-Host ("Lite archive: {0} ({1:N0} bytes)" -f $archive, (Get-Item -LiteralPath $archive).Length)
