<#
.SYNOPSIS
    Build and deploy cloud server to Ubuntu
.USAGE
    .\deploy_cloud.ps1
    .\deploy_cloud.ps1 -Host 1.2.3.4
    .\deploy_cloud.ps1 -User root -Port 22
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

Write-Host "=== Cloud Server Deploy ===" -ForegroundColor Cyan

# -- Step 1: Build --
Write-Host "`n[1/4] Building (linux-x64)..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$RepoRoot\HearthBot.Cloud\HearthBot.Cloud.csproj" `
    -c Release -r linux-x64 --no-self-contained `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Remove files that should not be uploaded
$excludeFiles = @("appsettings.json", "cloud.db", "appsettings.Development.json")
foreach ($f in $excludeFiles) {
    $p = Join-Path $PublishDir $f
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "  Excluded $f" }
}

$sizeMB = [math]::Round(((Get-ChildItem $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "  Build done: ${sizeMB} MB" -ForegroundColor Green

# -- Step 2: Zip --
Write-Host "`n[2/4] Compressing..." -ForegroundColor Yellow
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
$zipMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Archive: ${zipMB} MB" -ForegroundColor Green

# -- Step 3: Upload and extract --
Write-Host "`n[3/4] Uploading to ${User}@${RemoteHost}..." -ForegroundColor Yellow

ssh -p $Port "$User@$RemoteHost" "systemctl stop $ServiceName 2>/dev/null; mkdir -p $RemotePath"
if ($LASTEXITCODE -ne 0) { throw "SSH connection failed" }

scp -P $Port "$ZipPath" "${User}@${RemoteHost}:/tmp/cloud-server.zip"
if ($LASTEXITCODE -ne 0) { throw "Upload failed" }

# Extract on server (preserve appsettings.json and cloud.db)
$extractCmd = @(
    "cd $RemotePath",
    "cp -f appsettings.json /tmp/hb_appsettings_bak.json 2>/dev/null",
    "cp -f cloud.db /tmp/hb_cloud_db_bak.db 2>/dev/null",
    "apt-get install -qq -y unzip >/dev/null 2>&1",
    "unzip -o /tmp/cloud-server.zip -d $RemotePath > /dev/null",
    "cp -f /tmp/hb_appsettings_bak.json $RemotePath/appsettings.json 2>/dev/null",
    "cp -f /tmp/hb_cloud_db_bak.db $RemotePath/cloud.db 2>/dev/null",
    "rm -f /tmp/cloud-server.zip",
    "echo done"
) -join "; "

ssh -p $Port "$User@$RemoteHost" $extractCmd

Write-Host "  Upload and extract done" -ForegroundColor Green

# -- Ensure .NET Runtime installed --
ssh -p $Port "$User@$RemoteHost" "dotnet --list-runtimes 2>/dev/null | grep -q 'Microsoft.AspNetCore.App 8' || (apt-get update -qq && apt-get install -qq -y aspnetcore-runtime-8.0)"

# -- Step 4: Restart service --
Write-Host "`n[4/4] Restarting service..." -ForegroundColor Yellow

# Generate unit file locally, scp to server
$unitFile = Join-Path $env:TEMP "hearthbot-cloud.service"
$unitLines = @(
    "[Unit]",
    "Description=HearthBot Cloud Server",
    "After=network.target",
    "",
    "[Service]",
    "WorkingDirectory=$RemotePath",
    "ExecStart=$RemotePath/HearthBot.Cloud --urls http://0.0.0.0:5000",
    "Restart=always",
    "RestartSec=5",
    "",
    "[Install]",
    "WantedBy=multi-user.target"
)
$unitLines -join "`n" | Set-Content -Path $unitFile -NoNewline -Encoding UTF8

ssh -p $Port "$User@$RemoteHost" "chmod +x $RemotePath/HearthBot.Cloud"

# Create service file only if it does not exist
ssh -p $Port "$User@$RemoteHost" "test -f /etc/systemd/system/$ServiceName.service"
if ($LASTEXITCODE -ne 0) {
    scp -P $Port $unitFile "${User}@${RemoteHost}:/etc/systemd/system/$ServiceName.service"
    ssh -p $Port "$User@$RemoteHost" "systemctl daemon-reload; systemctl enable $ServiceName"
    Write-Host "  systemd service created" -ForegroundColor Green
    Remove-Item $unitFile -Force -ErrorAction SilentlyContinue
}

ssh -p $Port "$User@$RemoteHost" "systemctl start $ServiceName; sleep 1; systemctl is-active $ServiceName"

if ($LASTEXITCODE -ne 0) {
    Write-Host "  Service may have failed. Check: ssh $User@$RemoteHost journalctl -u $ServiceName -n 20" -ForegroundColor Red
} else {
    Write-Host "  Service started" -ForegroundColor Green
}

Write-Host "`n=== Deploy complete ===" -ForegroundColor Cyan
Write-Host "Cloud URL: http://${RemoteHost}:5000"
