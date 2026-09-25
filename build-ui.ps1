$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet'
$env:APPDATA = Join-Path $workspace '.appdata'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$project = Join-Path $workspace 'src\XinDianPrac.Gui\XinDianPrac.Gui.csproj'
$config = Join-Path $workspace 'NuGet.Config'
$output = Join-Path $workspace 'dist-ui'
dotnet restore $project --configfile $config
if ($LASTEXITCODE -ne 0) { throw 'UI restore failed' }
dotnet publish $project --no-restore -c Release -o $output
if ($LASTEXITCODE -ne 0) { throw 'UI publish failed' }
Write-Host "Built: $output\XinDianPrac.Gui.exe"
