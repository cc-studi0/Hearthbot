<#
.SYNOPSIS
    Build Bot and upload to cloud server for incremental updates
.USAGE
    .\deploy_bot.ps1                          # Build + upload
    .\deploy_bot.ps1 -SkipObfuscation         # Skip obfuscation
    .\deploy_bot.ps1 -BuildOnly               # Build only, no upload
#>
param(
    [string]$RemoteHost = "70.39.201.9",
    [string]$User = "root",
    [int]$Port = 22,
    [string]$RemotePath = "/mnt/cloud-server/wwwroot/bot",
    [int]$CloudPort = 5000,
    [string]$AdminUser = $env:HB_ADMIN_USER,
    [string]$AdminPass = $env:HB_ADMIN_PASS,
    [switch]$SkipObfuscation,
    [switch]$BuildOnly,
    [switch]$SkipBroadcast
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PackageDir = Join-Path $RepoRoot "publish\Hearthbot"
$ZipPath = Join-Path $RepoRoot "publish\Hearthbot.zip"

Write-Host "=== Bot Distribution Build ===" -ForegroundColor Cyan

# -- Step 1: Build --
Write-Host "`n[1/3] Building release package..." -ForegroundColor Yellow
$buildArgs = @()
if ($SkipObfuscation) { $buildArgs += "-SkipObfuscation" }

& "$RepoRoot\Scripts\build_release.ps1" @buildArgs

if (!(Test-Path $ZipPath)) { throw "Build failed: $ZipPath not found" }

$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Zip: ${zipSize} MB" -ForegroundColor Green

if ($BuildOnly) {
    Write-Host "`nBuild complete (upload skipped)" -ForegroundColor Cyan
    Write-Host "Zip: $ZipPath"
    return
}

# -- Step 2: Generate manifest.json --
Write-Host "`n[2/3] Generating manifest..." -ForegroundColor Yellow

$md5Provider = [System.Security.Cryptography.MD5]::Create()
$manifest = @{}
$files = Get-ChildItem $PackageDir -Recurse -File
foreach ($f in $files) {
    $relativePath = $f.FullName.Substring($PackageDir.Length + 1).Replace('\', '/')
    $stream = [System.IO.File]::OpenRead($f.FullName)
    try {
        $hashBytes = $md5Provider.ComputeHash($stream)
        $hash = [BitConverter]::ToString($hashBytes).Replace('-','').ToLower()
    } finally {
        $stream.Close()
    }
    $manifest[$relativePath] = $hash
}

# Build version from overall zip hash
$stream = [System.IO.File]::OpenRead($ZipPath)
try {
    $hashBytes = $md5Provider.ComputeHash($stream)
    $version = [BitConverter]::ToString($hashBytes).Replace('-','').ToLower()
} finally {
    $stream.Close()
}

$manifestObj = @{
    version = $version
    files = $manifest
}

# Use .NET serializer for PS 5.1 compatibility
Add-Type -AssemblyName System.Web.Extensions -ErrorAction SilentlyContinue
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$serializer.MaxJsonLength = 100MB
$manifestJson = $serializer.Serialize($manifestObj)

$manifestFile = Join-Path $RepoRoot "publish\manifest.json"
[System.IO.File]::WriteAllText($manifestFile, $manifestJson, [System.Text.Encoding]::UTF8)

Write-Host "  Manifest: $($manifest.Count) files, version: $($version.Substring(0,8))..." -ForegroundColor Green

# -- Step 3: Upload --
Write-Host "`n[3/3] Uploading to $User@$RemoteHost..." -ForegroundColor Yellow

# Upload zip + manifest
ssh -p $Port "$User@$RemoteHost" "mkdir -p $RemotePath/files"
scp -P $Port "$ZipPath" "${User}@${RemoteHost}:${RemotePath}/Hearthbot.zip"
if ($LASTEXITCODE -ne 0) { throw "Upload zip failed" }

scp -P $Port "$manifestFile" "${User}@${RemoteHost}:${RemotePath}/manifest.json"
if ($LASTEXITCODE -ne 0) { throw "Upload manifest failed" }

# Also upload version.txt for backward compatibility
$versionFile = Join-Path $RepoRoot "publish\version.txt"
Set-Content -Path $versionFile -Value $version -NoNewline -Encoding UTF8
scp -P $Port "$versionFile" "${User}@${RemoteHost}:${RemotePath}/version.txt"

# Extract files on server for individual file downloads
# unzip exit code 1 = warning (e.g. backslash paths from Windows), not an error
ssh -p $Port "$User@$RemoteHost" "rm -rf $RemotePath/files; mkdir -p $RemotePath/files; unzip -o $RemotePath/Hearthbot.zip -d $RemotePath/files > /dev/null 2>&1; test `$? -le 1"
if ($LASTEXITCODE -ne 0) { throw "Server extract failed" }

Write-Host "  Upload complete (version: $($version.Substring(0,8))...)" -ForegroundColor Green

# -- Step 4: Broadcast via WSS (令所有在线客户端即时收到 UpdateAvailable) --
if (-not $SkipBroadcast) {
    if ([string]::IsNullOrWhiteSpace($AdminUser) -or [string]::IsNullOrWhiteSpace($AdminPass)) {
        Write-Host "`n[4/4] Skipping broadcast: AdminUser/AdminPass not provided" -ForegroundColor DarkYellow
        Write-Host "       (设置环境变量 HB_ADMIN_USER/HB_ADMIN_PASS 或 -AdminUser/-AdminPass 参数即可广播)"
    }
    else {
        Write-Host "`n[4/4] Broadcasting UpdateAvailable via WSS..." -ForegroundColor Yellow
        $cloudBase = "http://${RemoteHost}:${CloudPort}"
        try {
            $loginBody = @{ Username = $AdminUser; Password = $AdminPass } | ConvertTo-Json
            $loginResp = Invoke-RestMethod -Uri "$cloudBase/api/auth/login" -Method Post `
                -Body $loginBody -ContentType "application/json"
            $token = $loginResp.token
            if ([string]::IsNullOrWhiteSpace($token)) { throw "Empty token" }

            $bcastResp = Invoke-RestMethod -Uri "$cloudBase/api/command/broadcast-update" -Method Post `
                -Headers @{ Authorization = "Bearer $token" } `
                -Body (@{ Force = $false } | ConvertTo-Json) -ContentType "application/json"
            Write-Host "  Broadcast sent (version: $($bcastResp.version.Substring(0,8))...)" -ForegroundColor Green
        }
        catch {
            Write-Host "  Broadcast failed: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "  （客户端下次 Register 时仍会收到版本通知，不影响功能）" -ForegroundColor DarkGray
        }
    }
}

Write-Host "`n=== Distribution Ready ===" -ForegroundColor Cyan
Write-Host "Manifest: http://${RemoteHost}:${CloudPort}/bot/manifest.json"
Write-Host "Full zip: http://${RemoteHost}:${CloudPort}/bot/Hearthbot.zip"
Write-Host "Files:    http://${RemoteHost}:${CloudPort}/bot/files/<path>"
