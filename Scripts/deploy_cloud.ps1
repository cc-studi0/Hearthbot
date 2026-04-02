<#
.SYNOPSIS
    一键构建并部署云控服务器到 Ubuntu
.USAGE
    .\deploy_cloud.ps1                      # 使用默认配置
    .\deploy_cloud.ps1 -Host 1.2.3.4        # 指定服务器 IP
    .\deploy_cloud.ps1 -User root -Port 22  # 指定用户和端口
#>
param(
    [string]$RemoteHost = "70.39.201.9",
    [string]$User = "root",
    [int]$Port = 22,
    [string]$RemotePath = "/opt/hearthbot-cloud",
    [string]$ServiceName = "hearthbot-cloud"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "publish\cloud-server"

Write-Host "=== 云控服务器部署 ===" -ForegroundColor Cyan

# -- Step 1: 构建 --
Write-Host "`n[1/3] 构建云控服务器 (linux-x64)..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$RepoRoot\HearthBot.Cloud\HearthBot.Cloud.csproj" `
    -c Release -r linux-x64 --self-contained `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$sizeMB = [math]::Round(((Get-ChildItem $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "  构建完成: ${sizeMB} MB" -ForegroundColor Green

# -- Step 2: 上传 --
Write-Host "`n[2/3] 上传到 ${User}@${RemoteHost}:${RemotePath}..." -ForegroundColor Yellow

# 先停止服务，再上传
ssh -p $Port "${User}@${RemoteHost}" "systemctl stop $ServiceName 2>/dev/null; mkdir -p $RemotePath"
if ($LASTEXITCODE -ne 0) { throw "SSH 连接失败，请检查 SSH 密钥配置" }

# rsync 增量上传，比 scp 快很多
$rsyncAvailable = $false
try { rsync --version 2>$null | Out-Null; $rsyncAvailable = $true } catch {}

if ($rsyncAvailable) {
    # Windows 上 rsync 路径需要转换
    $unixPublish = ($PublishDir -replace '\\','/') -replace '^(.):','/$1'
    rsync -avz --delete -e "ssh -p $Port" "${unixPublish}/" "${User}@${RemoteHost}:${RemotePath}/"
} else {
    scp -P $Port -r "$PublishDir\*" "${User}@${RemoteHost}:${RemotePath}/"
}

if ($LASTEXITCODE -ne 0) { throw "文件上传失败" }
Write-Host "  上传完成" -ForegroundColor Green

# -- Step 3: 重启服务 --
Write-Host "`n[3/3] 重启服务..." -ForegroundColor Yellow

# 首次部署时创建 systemd 服务
ssh -p $Port "${User}@${RemoteHost}" @"
chmod +x $RemotePath/HearthBot.Cloud
if [ ! -f /etc/systemd/system/$ServiceName.service ]; then
    cat > /etc/systemd/system/$ServiceName.service <<UNIT
[Unit]
Description=HearthBot Cloud Server
After=network.target

[Service]
Type=notify
WorkingDirectory=$RemotePath
ExecStart=$RemotePath/HearthBot.Cloud --urls http://0.0.0.0:5000
Restart=always
RestartSec=5
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
UNIT
    systemctl daemon-reload
    systemctl enable $ServiceName
    echo 'systemd 服务已创建'
fi
systemctl start $ServiceName
sleep 1
systemctl is-active $ServiceName
"@

if ($LASTEXITCODE -ne 0) {
    Write-Host "  服务启动可能失败，请检查: ssh ${User}@${RemoteHost} journalctl -u $ServiceName -n 20" -ForegroundColor Red
} else {
    Write-Host "  服务已启动" -ForegroundColor Green
}

Write-Host "`n=== 部署完成 ===" -ForegroundColor Cyan
Write-Host "云控地址: http://${RemoteHost}:5000"
