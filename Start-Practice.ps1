$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolExe = Join-Path $workspace 'dist\XinDianPrac.exe'
$gameCopy = Join-Path $workspace 'isolated-game\th06nc.exe'
$settingsPath = Join-Path $workspace 'config\settings.json'
$entrancesPath = Join-Path $workspace 'config\entrances.json'
$env:XINDIANPRAC_ISOLATED = '1'

function Read-Settings {
    $data = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($data.lives -lt 0 -or $data.lives -gt 9 -or
        $data.bombs -lt 0 -or $data.bombs -gt 9 -or
        $data.power -lt 0 -or $data.power -gt 128) {
        throw 'config\settings.json 的资源值超出范围。'
    }
    return $data
}

function Read-Number($label, $current, $minimum, $maximum) {
    while ($true) {
        $value = Read-Host "$label [$current]"
        if ([string]::IsNullOrWhiteSpace($value)) { return [int]$current }
        $parsed = 0
        if ([int]::TryParse($value, [ref]$parsed) -and $parsed -ge $minimum -and $parsed -le $maximum) {
            return $parsed
        }
        Write-Host "请输入 $minimum 到 $maximum 之间的整数。" -ForegroundColor Yellow
    }
}

function Invoke-Tool([string[]]$arguments) {
    if (-not (Test-Path -LiteralPath $toolExe)) {
        throw '找不到 dist\XinDianPrac.exe。请先双击 build.bat 构建。'
    }
    & $toolExe @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "工具返回错误码 $LASTEXITCODE；请查看上面的具体原因。" -ForegroundColor Yellow
        return $false
    }
    return $true
}

function Ensure-GameCopy {
    if (Test-Path -LiteralPath $gameCopy) { return }
    Write-Host '隔离副本尚不存在，准备从已安装游戏复制到工作目录。原游戏文件不会被覆盖。'
    & (Join-Path $workspace 'prepare-copy.ps1')
    if (-not (Test-Path -LiteralPath $gameCopy)) {
        throw '隔离副本准备失败。'
    }
}

try {
    $settings = Read-Settings
    $entrances = Get-Content -LiteralPath $entrancesPath -Raw -Encoding UTF8 | ConvertFrom-Json
    while ($true) {
        Write-Host ''
        Write-Host '《东方红魔乡：新典》离线练习控制器' -ForegroundColor Cyan
        Write-Host "资源设定：$($settings.lives) 残、$($settings.bombs) Bomb、$($settings.power) Power"
        Write-Host '1  准备并启动隔离副本游戏'
        Write-Host '2  在 Practice 菜单临时开放当前难度/机体的整面入口'
        Write-Host '3  监视整面练习（进入关卡并暂停后使用）'
        Write-Host '4  监视 Boss 登场练习（进入关卡并暂停后使用）'
        Write-Host '5  查看当前进程状态（只读）'
        Write-Host '6  修改资源设定'
        Write-Host '0  退出控制器'
        $choice = Read-Host '选择'
        switch ($choice) {
            '1' {
                Ensure-GameCopy
                $running = @(Get-Process th06nc -ErrorAction SilentlyContinue | Where-Object {
                    try { [string]::Equals($_.Path, $gameCopy, [System.StringComparison]::OrdinalIgnoreCase) }
                    catch { $false }
                })
                if ($running.Count -eq 0) {
                    Start-Process -FilePath $gameCopy -WorkingDirectory (Split-Path -Parent $gameCopy)
                    Write-Host '隔离副本已启动。请在游戏中选择 Practice。'
                } else {
                    Write-Host "隔离副本已经运行，PID: $($running.Id -join ', ')。"
                }
            }
            '2' {
                Write-Host '请先在隔离副本 Practice 菜单选好难度、角色和机体，并停在关卡选择菜单。'
                Read-Host '准备好后按 Enter' | Out-Null
                $ok = Invoke-Tool @('unlock-copy-current')
                if ($ok) { Write-Host '菜单若未刷新，请退回上一层再进入 Practice。Easy 最多到 Stage 5。' }
            }
            '3' {
                Write-Host '请在隔离副本选定关卡，进入实际游玩后按 Esc 暂停。'
                Read-Host '准备好后按 Enter 开始监视' | Out-Null
                $null = Invoke-Tool @('monitor-set', "$($settings.lives)", "$($settings.bombs)", "$($settings.power)")
            }
            '4' {
                Write-Host 'Boss 入口表：'
                foreach ($entry in $entrances.bossEntrances | Where-Object { $_.stage -le 6 }) {
                    Write-Host ("  Stage {0}: {1}，{2}" -f $entry.stage, $entry.bossName, $entry.verification)
                }
                $stage = Read-Number '选择 Stage 1–6' 1 1 6
                Write-Host 'Boss 跳转跟随游戏内所选的四种常规难度、角色与 A/B 机体。请进入所选 Stage 的开场，按 Esc 暂停。'
                Write-Host 'Stage 3 已修正为先生成 Boss；Stage 3–5 的 Boss 入口已由用户确认正常。'
                Read-Host '准备好后按 Enter 开始 Boss 监视' | Out-Null
                $null = Invoke-Tool @('monitor-boss', "$stage", "$($settings.lives)", "$($settings.bombs)", "$($settings.power)")
            }
            '5' { $null = Invoke-Tool @('probe') }
            '6' {
                $settings.lives = Read-Number '残机' $settings.lives 0 9
                $settings.bombs = Read-Number 'Bomb' $settings.bombs 0 9
                $settings.power = Read-Number 'Power' $settings.power 0 128
                $settings | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
                Write-Host '已保存 config\settings.json。'
            }
            '0' { return }
            default { Write-Host '未识别的选项。' -ForegroundColor Yellow }
        }
    }
} catch {
    Write-Error $_
    exit 1
}
