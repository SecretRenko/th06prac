$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = 'G:\TouHou\[th06nc] 东方红魔乡：新典　～ the Embodiment of Scarlet Devil\[th06nc] 东方红魔乡：新典　～ the Embodiment of Scarlet Devil'
$sourceExe = Join-Path $source 'th06nc.exe'
$destination = Join-Path $workspace 'isolated-game'
$destinationExe = Join-Path $destination 'th06nc.exe'
$expectedHash = '07850C8C6E469C0E82C13423E6D0D096A88D693455BDACACBB44C0AA3BCCE473'

if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw "找不到原始游戏：$sourceExe" }
if ((Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash -ne $expectedHash) {
    throw '原始游戏 EXE 哈希与本工具支持的版本不匹配，停止复制。'
}
$resolvedWorkspace = [System.IO.Path]::GetFullPath($workspace).TrimEnd('\')
$resolvedDestination = [System.IO.Path]::GetFullPath($destination).TrimEnd('\')
if (-not $resolvedDestination.StartsWith($resolvedWorkspace + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '隔离副本目录不在工作空间内，停止复制。'
}
if (Test-Path -LiteralPath $destination) {
    if (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1) {
        throw "隔离副本目录已有文件，拒绝覆盖：$destination"
    }
} else {
    New-Item -ItemType Directory -Path $destination | Out-Null
}
Write-Host "正在从只读来源复制游戏到：$destination"
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
if (-not (Test-Path -LiteralPath $destinationExe -PathType Leaf) -or
    (Get-FileHash -LiteralPath $destinationExe -Algorithm SHA256).Hash -ne $expectedHash) {
    throw '隔离副本 EXE 校验失败。请检查复制结果。'
}
Write-Host '隔离副本已准备好，原始游戏文件没有被覆盖。'
