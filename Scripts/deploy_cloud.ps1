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
    [string]$RemotePath = "/mnt/cloud-server",
    [string]$ServiceName = "hearthbot-cloud"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "publish\cloud-server"
$ZipPath = Join-Path $RepoRoot "publish\cloud-server.zip"

Write-Host "=== 云控服务器部署 ===" -ForegroundColor Cyan

# -- Step 1: 构建 --
Write-Host "`n[1/4] 构建云控服务器 (linux-x64)..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$RepoRoot\HearthBot.Cloud\HearthBot.Cloud.csproj" `
    -c Release -r linux-x64 --self-contained `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 删除不需要上传的文件
$excludeFiles = @("appsettings.json", "cloud.db", "appsettings.Development.json")
foreach ($f in $excludeFiles) {
    $p = Join-Path $PublishDir $f
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "  已排除 $f" }
}

$sizeMB = [math]::Round(((Get-ChildItem $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "  构建完成: ${sizeMB} MB" -ForegroundColor Green

# -- Step 2: 打包 --
Write-Host "`n[2/4] 打包..." -ForegroundColor Yellow
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
$zipMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  压缩包: ${zipMB} MB" -ForegroundColor Green

# -- Step 3: 上传并解压 --
Write-Host "`n[3/4] 上传到 ${User}@${RemoteHost}..." -ForegroundColor Yellow

ssh -p $Port "${User}@${RemoteHost}" "systemctl stop $ServiceName 2>/dev/null; mkdir -p $RemotePath"
if ($LASTEXITCODE -ne 0) { throw "SSH 连接失败，请检查 SSH 密钥配置" }

scp -P $Port "$ZipPath" "${User}@${RemoteHost}:/tmp/cloud-server.zip"
if ($LASTEXITCODE -ne 0) { throw "上传失败" }

# 服务器端解压（保留 appsettings.json 和 cloud.db）
ssh -p $Port "${User}@${RemoteHost}" @"
cd $RemotePath
# 备份配置和数据
cp -f appsettings.json /tmp/hb_appsettings_bak.json 2>/dev/null
cp -f cloud.db /tmp/hb_cloud_db_bak.db 2>/dev/null
# 解压覆盖
apt-get install -qq -y unzip >/dev/null 2>&1
unzip -o /tmp/cloud-server.zip -d $RemotePath > /dev/null
# 还原配置和数据
cp -f /tmp/hb_appsettings_bak.json $RemotePath/appsettings.json 2>/dev/null
cp -f /tmp/hb_cloud_db_bak.db $RemotePath/cloud.db 2>/dev/null
rm -f /tmp/cloud-server.zip
echo 'done'
"@

Write-Host "  上传解压完成" -ForegroundColor Green

# -- Step 4: 重启服务 --
Write-Host "`n[4/4] 重启服务..." -ForegroundColor Yellow

ssh -p $Port "${User}@${RemoteHost}" @"
chmod +x $RemotePath/HearthBot.Cloud
if [ ! -f /etc/systemd/system/$ServiceName.service ]; then
    cat > /etc/systemd/system/$ServiceName.service <<UNIT
[Unit]
Description=HearthBot Cloud Server
After=network.target

[Service]
WorkingDirectory=$RemotePath
ExecStart=$RemotePath/HearthBot.Cloud --urls http://0.0.0.0:5000
Restart=always
RestartSec=5

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
