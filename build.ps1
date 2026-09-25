$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet'
$env:APPDATA = Join-Path $workspace '.appdata'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$project = Join-Path $workspace 'src\XinDianPrac\XinDianPrac.csproj'
$config = Join-Path $workspace 'NuGet.Config'
dotnet restore $project --configfile $config
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
dotnet build $project --no-restore -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

$source = Join-Path $workspace 'src\XinDianPrac\bin\Release\net10.0'
$dest = Join-Path $workspace 'dist'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
foreach ($name in @('XinDianPrac.exe', 'XinDianPrac.dll', 'XinDianPrac.runtimeconfig.json', 'XinDianPrac.deps.json')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $dest $name) -Force
}
$configDest = Join-Path $dest 'config'
New-Item -ItemType Directory -Path $configDest -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'config\entrances.json') -Destination (Join-Path $configDest 'entrances.json') -Force
Write-Host "Built: $dest\XinDianPrac.exe"
