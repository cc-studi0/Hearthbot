<#
.SYNOPSIS
    盒子受限侦察录制脚本（阶段0）。
.DESCRIPTION
    以指定场景标签启动 Frida，附加 HSAng.exe，加载 hsbox_limit_recon.js，
    录制指定秒数后自动停止；log 落盘到 docs/superpowers/recon/raw/。
.PARAMETER Mode
    场景标签：'wild'（狂野对局）或 'std-legend'（标传受限档对局）。
.PARAMETER DurationSec
    录制秒数，默认 60。
.EXAMPLE
    .\Scripts\recon_hsbox.ps1 -Mode wild
    .\Scripts\recon_hsbox.ps1 -Mode std-legend -DurationSec 90
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('wild', 'std-legend', 'smoke')]
    [string]$Mode,
    [int]$DurationSec = 60
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Script   = Join-Path $RepoRoot 'tools\active\hsbox_limit_recon.js'
$OutDir   = Join-Path $RepoRoot 'docs\superpowers\recon\raw'

if (-not (Test-Path $Script)) {
    throw "找不到 Frida 脚本：$Script"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Ts       = Get-Date -Format 'yyyyMMdd_HHmmss'
$OutFile  = Join-Path $OutDir "${Ts}_${Mode}.log"
$ErrFile  = "$OutFile.err"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  盒子受限侦察录制" -ForegroundColor Cyan
Write-Host "  场景:    $Mode" -ForegroundColor Cyan
Write-Host "  时长:    $DurationSec 秒" -ForegroundColor Cyan
Write-Host "  输出:    $OutFile" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "前置检查：" -ForegroundColor Yellow
Write-Host "  1. HSAng.exe 必须已启动" -ForegroundColor Yellow
Write-Host "  2. 本脚本必须在【管理员】PowerShell 中运行" -ForegroundColor Yellow
switch ($Mode) {
    'wild' {
        Write-Host "  3. 进入【狂野】对局；录制期间盒子应显示推荐" -ForegroundColor Yellow
    }
    'std-legend' {
        Write-Host "  3. 进入【标准+传说受限档】对局；录制期间盒子应显示受限页" -ForegroundColor Yellow
    }
    'smoke' {
        Write-Host "  3. smoke 模式：空跑验证脚本加载不崩即可；无需进对局" -ForegroundColor Yellow
    }
}
Write-Host ""
Read-Host "准备好后按回车开始录制"

$Launcher = Join-Path $PSScriptRoot 'recon_hsbox.py'
if (-not (Test-Path $Launcher)) {
    throw "找不到 Python 启动器：$Launcher"
}

$PyArgs = @(
    $Launcher,
    '--script', $Script,
    '--output', $OutFile,
    '--duration', $DurationSec.ToString(),
    '--target', 'HSAng.exe'
)

Write-Host "启动 Frida (via Python)..." -ForegroundColor Green
$proc = Start-Process -FilePath 'python' -ArgumentList $PyArgs `
    -RedirectStandardError $ErrFile `
    -NoNewWindow -PassThru

# Python 启动器自己会 sleep DurationSec，此处只等它结束，加 15s 余量
$timeout = $DurationSec + 15
$waited = 0
while (-not $proc.HasExited -and $waited -lt $timeout) {
    Start-Sleep -Seconds 1
    $waited++
}
if (-not $proc.HasExited) {
    Write-Warning "Python 启动器超时未退出，强制停止"
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 500
} elseif ($proc.ExitCode -ne 0) {
    Write-Warning "Python 启动器异常退出 (exit=$($proc.ExitCode))，查看 $ErrFile"
}

if (Test-Path $OutFile) {
    $sz = (Get-Item $OutFile).Length
} else {
    $sz = 0
}
Write-Host ""
Write-Host "完成。" -ForegroundColor Green
Write-Host "  Log: $OutFile  ($sz bytes)"
if (Test-Path $ErrFile) {
    $esz = (Get-Item $ErrFile).Length
    if ($esz -gt 0) {
        Write-Host "  Err: $ErrFile  ($esz bytes, 有错误输出，请检查)" -ForegroundColor Yellow
    }
}
