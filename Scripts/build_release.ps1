<#
.SYNOPSIS
    Build, obfuscate and package Hearthbot for release.
.NOTES
    混淆器: ConfuserEx 2 (mkaring fork)
    下载:  https://github.com/mkaring/ConfuserEx/releases
    解压至 <RepoRoot>\tools\confuserex\ , 或设置 $env:CONFUSEREX_CLI 指向 Confuser.CLI.exe
#>

param(
    [switch]$SkipObfuscation,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

# 强制控制台与 PS 输出为 UTF-8，避免中文 Windows（CP936）下本脚本的中文输出乱码
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    [Console]::InputEncoding  = [System.Text.UTF8Encoding]::new($false)
    $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
} catch {}

$RepoRoot = Split-Path -Parent $PSScriptRoot

$PublishDir    = Join-Path $RepoRoot "publish\app"
$PackageDir    = if ($OutputDir) { $OutputDir } else { Join-Path $RepoRoot "publish\Hearthbot" }
$ObfuscateTmp  = "C:\temp\hb_obfuscate"
$ObfuscatedTmp = "C:\temp\confused"
$ZipPath       = Join-Path $RepoRoot "publish\Hearthbot.zip"

Write-Host "=== Hearthbot Release Build ===" -ForegroundColor Cyan
Write-Host "Repo root: $RepoRoot"

# -- Step 1: Clean & Publish --
Write-Host "`n[1/6] Publishing (framework-dependent, x86)..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$RepoRoot\BotMain\BotMain.csproj" `
    -c Release --no-self-contained `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Publish OK" -ForegroundColor Green

# -- Step 2: Obfuscate (ConfuserEx 2) --
if (-not $SkipObfuscation) {
    Write-Host "`n[2/6] Obfuscating BotMain.dll + BotCore.dll (ConfuserEx 2)..." -ForegroundColor Yellow

    # 定位 Confuser.CLI.exe
    $ConfuserCli = $env:CONFUSEREX_CLI
    if (-not $ConfuserCli -or -not (Test-Path $ConfuserCli)) {
        $ConfuserCli = Join-Path $RepoRoot "tools\confuserex\Confuser.CLI.exe"
    }
    if (-not (Test-Path $ConfuserCli)) {
        throw @"
找不到 Confuser.CLI.exe.
解决办法 (任选其一):
  1) 从 https://github.com/mkaring/ConfuserEx/releases 下载 ConfuserEx-CLI.zip
     解压到 $RepoRoot\tools\confuserex\ (需包含 Confuser.CLI.exe)
  2) 设置环境变量: `$env:CONFUSEREX_CLI = 'D:\path\to\Confuser.CLI.exe'
"@
    }
    Write-Host "  ConfuserEx: $ConfuserCli" -ForegroundColor DarkGray

    # ConfuserEx 对 Unicode 路径敏感, 复制到英文临时目录
    if (Test-Path $ObfuscateTmp)  { Remove-Item $ObfuscateTmp  -Recurse -Force }
    if (Test-Path $ObfuscatedTmp) { Remove-Item $ObfuscatedTmp -Recurse -Force }
    New-Item -ItemType Directory -Path $ObfuscateTmp  -Force | Out-Null
    New-Item -ItemType Directory -Path $ObfuscatedTmp -Force | Out-Null

    # 复制项目 DLL
    Copy-Item "$PublishDir\*.dll" $ObfuscateTmp

    # .NET 8 运行时依赖不在 publish 目录 (framework-dependent)
    # ConfuserEx 不支持 probePath 项目元素 / -probe CLI 参数
    # 直接把运行时 DLL 复制进临时目录, 让 dnlib 从同目录解析
    $dotnetRoot = Split-Path (Get-Command dotnet).Source
    $wpfRuntime = Get-ChildItem "$dotnetRoot\shared\Microsoft.WindowsDesktop.App\8.*" -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $aspRuntime = Get-ChildItem "$dotnetRoot\shared\Microsoft.NETCore.App\8.*" -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $wpfRuntime) { throw ".NET 8 Desktop Runtime not found. Please install it first." }

    Write-Host "  Copying runtime refs from:" -ForegroundColor DarkGray
    Write-Host "    $($wpfRuntime.FullName)" -ForegroundColor DarkGray
    Write-Host "    $($aspRuntime.FullName)" -ForegroundColor DarkGray
    # -Force:覆盖重复, 不覆盖项目自身 DLL 是因为运行时目录不含它们
    Copy-Item "$($aspRuntime.FullName)\*.dll" $ObfuscateTmp -Force -ErrorAction SilentlyContinue
    Copy-Item "$($wpfRuntime.FullName)\*.dll" $ObfuscateTmp -Force -ErrorAction SilentlyContinue

    # 生成运行时 .crproj (只改 baseDir / outputDir, 用字符串替换避免 XmlDocument 破坏命名空间)
    $template = Get-Content "$PSScriptRoot\confuserex.crproj" -Raw -Encoding UTF8
    $modified = $template `
        -replace 'baseDir="[^"]*"',   "baseDir=""$ObfuscateTmp""" `
        -replace 'outputDir="[^"]*"', "outputDir=""$ObfuscatedTmp"""
    $projPath = Join-Path $ObfuscateTmp "confuserex.crproj"
    [System.IO.File]::WriteAllText($projPath, $modified, [System.Text.UTF8Encoding]::new($false))

    & $ConfuserCli -n $projPath
    if ($LASTEXITCODE -ne 0) { throw "ConfuserEx failed (exit $LASTEXITCODE)" }

    # 替换为混淆后的 DLL
    Copy-Item "$ObfuscatedTmp\BotMain.dll" $PublishDir -Force
    Copy-Item "$ObfuscatedTmp\BotCore.dll" $PublishDir -Force

    Remove-Item $ObfuscateTmp  -Recurse -Force
    Remove-Item $ObfuscatedTmp -Recurse -Force

    Write-Host "Obfuscation OK" -ForegroundColor Green
} else {
    Write-Host "`n[2/6] Skipping obfuscation (-SkipObfuscation)" -ForegroundColor DarkYellow
}

# -- Step 3: Assemble package --
Write-Host "`n[3/6] Assembling release package..." -ForegroundColor Yellow
if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }
New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null
Copy-Item "$PublishDir\*" $PackageDir -Recurse

# -- Step 4: Copy runtime resources --
Write-Host "`n[4/6] Copying runtime resources..." -ForegroundColor Yellow

$ResourceDirs = @("Profiles", "MulliganProfiles", "DiscoverCC", "ArenaCC", "Libs", "Data", "Plugins")
foreach ($dir in $ResourceDirs) {
    $src = Join-Path $RepoRoot $dir
    if (Test-Path $src) {
        $dest = Join-Path $PackageDir $dir
        Copy-Item $src $dest -Recurse -Force
        $count = (Get-ChildItem $dest -Recurse -File).Count
        Write-Host "  $dir -> $count files"
    } else {
        Write-Host "  $dir -> (not found, skipped)" -ForegroundColor DarkYellow
    }
}

$cardsJson = Join-Path $RepoRoot "cards.json"
if (Test-Path $cardsJson) {
    Copy-Item $cardsJson $PackageDir -Force
    Write-Host "  cards.json -> OK"
}

# HearthstonePayload.dll (BepInEx plugin, net472 standalone project)
Write-Host "  Building HearthstonePayload..." -ForegroundColor Yellow
dotnet build "$RepoRoot\HearthstonePayload\HearthstonePayload.csproj" -c Release -v q
if ($LASTEXITCODE -ne 0) { throw "HearthstonePayload build failed" }
$payloadDll = Join-Path $RepoRoot "HearthstonePayload\bin\Release\net472\HearthstonePayload.dll"
if (Test-Path $payloadDll) {
    Copy-Item $payloadDll $PackageDir -Force
    Write-Host "  HearthstonePayload.dll -> OK"
} else {
    Write-Host "  HearthstonePayload.dll -> BUILD OUTPUT NOT FOUND" -ForegroundColor Red
}

# -- Step 5: Trim --
Write-Host "`n[5/6] Trimming package..." -ForegroundColor Yellow

# Remove debug symbols
Get-ChildItem $PackageDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# Remove XML doc files in root (keep config files)
Get-ChildItem $PackageDir -Filter "*.xml" -Recurse | Where-Object {
    $_.Name -notmatch "^(cards|obfuscar|app)\." -and $_.Directory.FullName -eq $PackageDir
} | Remove-Item -Force -ErrorAction SilentlyContinue

# Remove locale satellite assemblies
$langDirs = Get-ChildItem $PackageDir -Directory | Where-Object {
    $_.Name -match "^(cs|de|es|fr|it|ja|ko|pl|pt-BR|ru|tr|zh-Hant)$"
}
foreach ($lang in $langDirs) {
    Remove-Item $lang.FullName -Recurse -Force
    Write-Host "  Removed locale: $($lang.Name)"
}

# SQLite native: keep only win-x86, remove all other platforms
$runtimesDir = Join-Path $PackageDir "runtimes"
if (Test-Path $runtimesDir) {
    $kept = 0
    $removed = 0
    Get-ChildItem $runtimesDir -Directory | ForEach-Object {
        if ($_.Name -ne "win-x64" -and $_.Name -ne "win") {
            Remove-Item $_.FullName -Recurse -Force
            $removed++
        } else {
            $kept++
        }
    }
    Write-Host "  SQLite runtimes: kept $kept, removed $removed platforms"
}

# SBAPI.dll exists in both root (runtime assembly loading) and Libs/ (script compiler reference)
# Both copies are needed - do not remove either

$totalSize = (Get-ChildItem $PackageDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMB = [math]::Round($totalSize / 1MB, 1)
$fileCount = (Get-ChildItem $PackageDir -Recurse -File).Count
Write-Host "  Package: $fileCount files, $sizeMB MB" -ForegroundColor Green

# -- Step 6: Create zip --
Write-Host "`n[6/6] Creating zip archive..." -ForegroundColor Yellow
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PackageDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Archive: $ZipPath ($zipSize MB)" -ForegroundColor Green

# -- Verification --
Write-Host "`n=== Verification ===" -ForegroundColor Cyan
$checks = @(
    @{ Name = "BotMain.exe";     Path = Join-Path $PackageDir "BotMain.exe" }
    @{ Name = "BotMain.dll";     Path = Join-Path $PackageDir "BotMain.dll" }
    @{ Name = "BotCore.dll";     Path = Join-Path $PackageDir "BotCore.dll" }
    @{ Name = "cards.json";      Path = Join-Path $PackageDir "cards.json" }
    @{ Name = "Profiles/";       Path = Join-Path $PackageDir "Profiles" }
    @{ Name = "MulliganProfiles/"; Path = Join-Path $PackageDir "MulliganProfiles" }
    @{ Name = "DiscoverCC/";     Path = Join-Path $PackageDir "DiscoverCC" }
    @{ Name = "Libs/SBAPI.dll";  Path = Join-Path $PackageDir "Libs\SBAPI.dll" }
    @{ Name = "Data/teacher.db"; Path = Join-Path $PackageDir "Data\HsBoxTeacher\teacher.db" }
    @{ Name = "HearthstonePayload.dll"; Path = Join-Path $PackageDir "HearthstonePayload.dll" }
)

$allOk = $true
foreach ($check in $checks) {
    if (Test-Path $check.Path) {
        Write-Host "  [OK] $($check.Name)" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $($check.Name)" -ForegroundColor Red
        $allOk = $false
    }
}

$remainingPdb = Get-ChildItem $PackageDir -Filter "*.pdb" -Recurse
if ($remainingPdb) {
    Write-Host "  [WARN] Found $($remainingPdb.Count) .pdb files remaining" -ForegroundColor Red
    $allOk = $false
} else {
    Write-Host "  [OK] No .pdb files" -ForegroundColor Green
}

if ($allOk) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    Write-Host "Release package: $PackageDir"
    Write-Host "Zip archive: $ZipPath"
} else {
    Write-Host "`nBuild completed with warnings." -ForegroundColor Yellow
}
