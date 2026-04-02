<#
.SYNOPSIS
    构建 Bot 并上传到云控服务器，其他机器通过 URL 下载更新
.USAGE
    .\deploy_bot.ps1                          # 构建+上传
    .\deploy_bot.ps1 -SkipObfuscation         # 跳过混淆（快速调试）
    .\deploy_bot.ps1 -BuildOnly               # 只构建不上传
.NOTES
    上传后其他机器访问: http://<服务器IP>:5000/bot/Hearthbot.zip
    或在 bot 机器上执行:
      powershell -c "Invoke-WebRequest http://70.39.201.9:5000/bot/Hearthbot.zip -OutFile hb.zip; Expand-Archive hb.zip -Dest C:\Hearthbot -Force"
#>
param(
    [string]$RemoteHost = "70.39.201.9",
    [string]$User = "root",
    [int]$Port = 22,
    [string]$RemotePath = "/opt/hearthbot-cloud/wwwroot/bot",
    [switch]$SkipObfuscation,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ZipPath = Join-Path $RepoRoot "publish\Hearthbot.zip"

Write-Host "=== Bot 分发包构建 ===" -ForegroundColor Cyan

# -- Step 1: 调用已有的构建脚本 --
Write-Host "`n[1/2] 构建 Release 包..." -ForegroundColor Yellow
$buildArgs = @()
if ($SkipObfuscation) { $buildArgs += "-SkipObfuscation" }

& "$RepoRoot\Scripts\build_release.ps1" @buildArgs

if (!(Test-Path $ZipPath)) { throw "构建失败: 未找到 $ZipPath" }

$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Zip: ${zipSize} MB" -ForegroundColor Green

if ($BuildOnly) {
    Write-Host "`n构建完成 (跳过上传)" -ForegroundColor Cyan
    Write-Host "Zip: $ZipPath"
    return
}

# -- Step 2: 上传到云控服务器的静态文件目录 --
Write-Host "`n[2/2] 上传到 ${User}@${RemoteHost}..." -ForegroundColor Yellow

ssh -p $Port "${User}@${RemoteHost}" "mkdir -p $RemotePath"
scp -P $Port "$ZipPath" "${User}@${RemoteHost}:${RemotePath}/Hearthbot.zip"

if ($LASTEXITCODE -ne 0) { throw "上传失败" }

Write-Host "  上传完成" -ForegroundColor Green

Write-Host "`n=== 分发就绪 ===" -ForegroundColor Cyan
Write-Host "下载地址: http://${RemoteHost}:5000/bot/Hearthbot.zip"
Write-Host ""
Write-Host "其他机器一键更新 (PowerShell):" -ForegroundColor Yellow
Write-Host '  irm http://70.39.201.9:5000/bot/Hearthbot.zip -OutFile hb.zip; Expand-Archive hb.zip -Dest C:\Hearthbot -Force; del hb.zip'
