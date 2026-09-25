$ErrorActionPreference = 'Stop'
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
$packageName = "th06ncprac-Portable-$stamp"
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

$publish = @('-p:UseAppHost=false', '-p:DebugType=None', '-p:DebugSymbols=false')
dotnet publish $core --no-restore -c Release @publish -o (Join-Path $package 'dist')
if ($LASTEXITCODE -ne 0) { throw 'Core publish failed.' }
dotnet publish $gui --no-restore -c Release @publish -o (Join-Path $package 'dist-ui')
if ($LASTEXITCODE -ne 0) { throw 'UI publish failed.' }

Copy-Item -LiteralPath (Join-Path $workspace 'config\entrances.json') -Destination (Join-Path $package 'config\entrances.json')
Copy-Item -LiteralPath (Join-Path $workspace 'config\entrances.json') -Destination (Join-Path $package 'dist\config\entrances.json')
Copy-Item -LiteralPath (Join-Path $workspace 'config\settings.portable.json') -Destination (Join-Path $package 'config\settings.json')
Copy-Item -LiteralPath (Join-Path $workspace 'config\game-target.portable.json') -Destination (Join-Path $package 'config\game-target.json')
Copy-Item -LiteralPath (Join-Path $workspace 'Start-Practice.bat') -Destination $package
Copy-Item -LiteralPath (Join-Path $workspace 'README-Portable.txt') -Destination $package

$dotnetRoot = Split-Path -Parent (Get-Command dotnet.exe).Source
$runtimeRoot = Join-Path $package 'runtime'
$coreRuntime = Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory |
    Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$desktopRuntime = Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.WindowsDesktop.App') -Directory |
    Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $coreRuntime -or $null -eq $desktopRuntime) { throw 'Installed .NET 10 runtime or Windows Desktop runtime was not found.' }
$hostFxr = Get-ChildItem (Join-Path $dotnetRoot 'host\fxr') -Directory |
    Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $hostFxr) { throw 'Installed .NET 10 hostfxr was not found.' }
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "host\fxr\$($hostFxr.Name)") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "shared\Microsoft.NETCore.App\$($coreRuntime.Name)") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runtimeRoot "shared\Microsoft.WindowsDesktop.App\$($desktopRuntime.Name)") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -Destination $runtimeRoot
Copy-Item -Path (Join-Path $hostFxr.FullName '*') -Destination (Join-Path $runtimeRoot "host\fxr\$($hostFxr.Name)") -Recurse
Copy-Item -Path (Join-Path $coreRuntime.FullName '*') -Destination (Join-Path $runtimeRoot "shared\Microsoft.NETCore.App\$($coreRuntime.Name)") -Recurse
Copy-Item -Path (Join-Path $desktopRuntime.FullName '*') -Destination (Join-Path $runtimeRoot "shared\Microsoft.WindowsDesktop.App\$($desktopRuntime.Name)") -Recurse
foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $noticePath = Join-Path $dotnetRoot $notice
    if (Test-Path -LiteralPath $noticePath) { Copy-Item -LiteralPath $noticePath -Destination $runtimeRoot }
}

$archive = Join-Path $release "$packageName.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($package, $archive,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable folder: $package"
Write-Host "Portable archive: $archive"
