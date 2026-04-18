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
    [string]$AdminUser,
    [string]$AdminPass,
    [string]$Notes,
    [switch]$SkipObfuscation,
    [switch]$BuildOnly,
    [switch]$SkipBroadcast,
    [switch]$SkipNotesPrompt,
    [switch]$ResetCreds
)

$ErrorActionPreference = "Stop"

# 强制控制台与 PS 输出为 UTF-8，避免中文 Windows（CP936）下本脚本的中文输出乱码
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    [Console]::InputEncoding  = [System.Text.UTF8Encoding]::new($false)
    $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
} catch {}

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

# 收集 release notes：
#   1) -Notes 参数直接指定
#   2) publish/release-notes.txt > 仓库根 release-notes.txt（手写文件）
#   3) 弹 notepad 编辑，模板内置最近 8 条 git 提交作为参考
#   4) -SkipNotesPrompt 或没有 notepad 时静默用 git log
$notes = ""
$pubNotes = Join-Path $RepoRoot "publish\release-notes.txt"
$rootNotes = Join-Path $RepoRoot "release-notes.txt"

if (-not [string]::IsNullOrWhiteSpace($Notes)) {
    $notes = $Notes.Trim()
}
elseif (Test-Path $pubNotes) {
    $notes = [System.IO.File]::ReadAllText($pubNotes, [System.Text.Encoding]::UTF8).Trim()
}
elseif (Test-Path $rootNotes) {
    $notes = [System.IO.File]::ReadAllText($rootNotes, [System.Text.Encoding]::UTF8).Trim()
}
else {
    # 读取最近 8 条提交作为模板
    $gitLog = ""
    try {
        $g = git -C $RepoRoot log --oneline -n 8 2>$null
        if ($LASTEXITCODE -eq 0 -and $g) { $gitLog = ($g -join "`n").Trim() }
    } catch {}

    if ($SkipNotesPrompt) {
        $notes = $gitLog
    }
    else {
        Write-Host "`n[更新说明] 弹出对话框，请写本次更新内容；留空=用 git 提交记录；取消=中止部署" -ForegroundColor Cyan

        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing

        $form = New-Object System.Windows.Forms.Form
        $form.Text = "Hearthbot - 本次更新说明"
        $form.Size = New-Object System.Drawing.Size(600, 480)
        $form.StartPosition = "CenterScreen"
        $form.FormBorderStyle = "FixedDialog"
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        $form.TopMost = $true

        $lbl = New-Object System.Windows.Forms.Label
        $lbl.Location = New-Object System.Drawing.Point(12, 10)
        $lbl.Size = New-Object System.Drawing.Size(560, 18)
        $lbl.Text = "本次更新说明（多行；留空则使用下方 git 提交记录）："
        $form.Controls.Add($lbl)

        $txt = New-Object System.Windows.Forms.TextBox
        $txt.Location = New-Object System.Drawing.Point(12, 30)
        $txt.Size = New-Object System.Drawing.Size(560, 220)
        $txt.Multiline = $true
        $txt.ScrollBars = "Vertical"
        $txt.AcceptsReturn = $true
        $txt.AcceptsTab = $true
        $txt.Font = New-Object System.Drawing.Font("Microsoft YaHei", 10)
        $txt.WordWrap = $true
        $form.Controls.Add($txt)

        $lbl2 = New-Object System.Windows.Forms.Label
        $lbl2.Location = New-Object System.Drawing.Point(12, 256)
        $lbl2.Size = New-Object System.Drawing.Size(560, 18)
        $lbl2.Text = "参考 — 最近 8 条提交（仅供参考，不自动填入上面）："
        $lbl2.ForeColor = [System.Drawing.Color]::Gray
        $form.Controls.Add($lbl2)

        $ref = New-Object System.Windows.Forms.TextBox
        $ref.Location = New-Object System.Drawing.Point(12, 276)
        $ref.Size = New-Object System.Drawing.Size(560, 120)
        $ref.Multiline = $true
        $ref.ScrollBars = "Vertical"
        $ref.ReadOnly = $true
        $ref.Font = New-Object System.Drawing.Font("Consolas", 9)
        $ref.BackColor = [System.Drawing.Color]::FromArgb(246, 248, 250)
        $ref.Text = if ([string]::IsNullOrWhiteSpace($gitLog)) { "（无 git 历史）" } else { $gitLog }
        $form.Controls.Add($ref)

        $btnOk = New-Object System.Windows.Forms.Button
        $btnOk.Location = New-Object System.Drawing.Point(402, 406)
        $btnOk.Size = New-Object System.Drawing.Size(80, 30)
        $btnOk.Text = "确定"
        $btnOk.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Controls.Add($btnOk)
        $form.AcceptButton = $btnOk

        $btnCancel = New-Object System.Windows.Forms.Button
        $btnCancel.Location = New-Object System.Drawing.Point(492, 406)
        $btnCancel.Size = New-Object System.Drawing.Size(80, 30)
        $btnCancel.Text = "取消"
        $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
        $form.Controls.Add($btnCancel)
        $form.CancelButton = $btnCancel

        $txt.Add_KeyDown({
            if ($_.Control -and $_.KeyCode -eq [System.Windows.Forms.Keys]::Enter) {
                $btnOk.PerformClick()
                $_.SuppressKeyPress = $true
            }
        })

        $result = $form.ShowDialog()
        $typed = $txt.Text
        $form.Dispose()

        if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
            Write-Host "  用户取消，中止部署" -ForegroundColor Red
            exit 1
        }

        if ([string]::IsNullOrWhiteSpace($typed)) {
            Write-Host "  （未写更新说明，回退到 git 提交记录）" -ForegroundColor DarkGray
            $notes = $gitLog
        } else {
            $notes = $typed.Trim()
        }
    }
}

if ([string]::IsNullOrWhiteSpace($notes)) { $notes = "" }
$notesPreview = if ($notes.Length -gt 120) { $notes.Substring(0,120) + "..." } else { $notes }
Write-Host "  Notes: $($notesPreview -replace "`r?`n", ' | ')" -ForegroundColor DarkGray

$manifestObj = @{
    version = $version
    files = $manifest
    notes = $notes
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
    # 凭据来源优先级：命令行参数 > DPAPI 加密文件 > 交互式提示
    # 加密文件 .deploy-creds.xml 用 Export-Clixml + SecureString，只有当前 Windows 用户在当前机器能解密
    $credsFile = Join-Path $PSScriptRoot ".deploy-creds.xml"
    if ($ResetCreds -and (Test-Path $credsFile)) { Remove-Item $credsFile -Force }

    $credential = $null
    if (-not [string]::IsNullOrWhiteSpace($AdminUser) -and -not [string]::IsNullOrWhiteSpace($AdminPass)) {
        $sec = ConvertTo-SecureString $AdminPass -AsPlainText -Force
        $credential = New-Object System.Management.Automation.PSCredential($AdminUser, $sec)
    }
    elseif (Test-Path $credsFile) {
        try { $credential = Import-Clixml $credsFile }
        catch { Write-Host "  凭据文件损坏，将重新提示输入" -ForegroundColor DarkYellow }
    }

    if ($credential -eq $null) {
        Write-Host "`n[4/4] 首次广播需要管理员凭据（下次自动从 .deploy-creds.xml 读取，不存明文）" -ForegroundColor Cyan
        $credential = Get-Credential -Message "HearthBot 云控管理员账号"
        if ($credential -ne $null) {
            try {
                $credential | Export-Clixml $credsFile
                Write-Host "  凭据已保存到 $credsFile（DPAPI 加密，仅当前 Windows 账户可解密）" -ForegroundColor DarkGray
            }
            catch { Write-Host "  保存凭据失败: $_" -ForegroundColor DarkYellow }
        }
    }

    if ($credential -eq $null) {
        Write-Host "`n[4/4] 未提供凭据，跳过广播" -ForegroundColor DarkYellow
        Write-Host "       （客户端下次 Register 仍会收到 UpdateAvailable，不影响功能）" -ForegroundColor DarkGray
    }
    else {
        Write-Host "`n[4/4] Broadcasting UpdateAvailable via WSS..." -ForegroundColor Yellow
        $cloudBase = "http://${RemoteHost}:${CloudPort}"
        $plainPass = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($credential.Password))
        try {
            $loginBody = @{ Username = $credential.UserName; Password = $plainPass } | ConvertTo-Json
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
            Write-Host "  （若是凭据错误可用 -ResetCreds 重新输入；客户端下次 Register 时仍会收到通知）" -ForegroundColor DarkGray
        }
        finally {
            $plainPass = $null
        }
    }
}

Write-Host "`n=== Distribution Ready ===" -ForegroundColor Cyan
Write-Host "Manifest: http://${RemoteHost}:${CloudPort}/bot/manifest.json"
Write-Host "Full zip: http://${RemoteHost}:${CloudPort}/bot/Hearthbot.zip"
Write-Host "Files:    http://${RemoteHost}:${CloudPort}/bot/files/<path>"
