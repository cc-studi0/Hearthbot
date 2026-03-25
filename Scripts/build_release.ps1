<#
.SYNOPSIS
    Build, obfuscate and package Hearthbot for release.
.NOTES
    Requires: dotnet tool install --global Obfuscar.GlobalTool
#>

param(
    [switch]$SkipObfuscation,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

$PublishDir    = Join-Path $RepoRoot "publish\app"
$PackageDir    = if ($OutputDir) { $OutputDir } else { Join-Path $RepoRoot "publish\Hearthbot" }
$ObfuscateTmp  = "C:\temp\hb_obfuscate"
$ObfuscatedTmp = "C:\temp\obfuscated"
$ZipPath       = Join-Path $RepoRoot "publish\Hearthbot.zip"

Write-Host "=== Hearthbot Release Build ===" -ForegroundColor Cyan
Write-Host "Repo root: $RepoRoot"

# -- Step 1: Clean & Publish --
Write-Host "`n[1/6] Publishing (self-contained, win-x86)..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$RepoRoot\BotMain\BotMain.csproj" `
    -c Release -r win-x86 --self-contained `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Publish OK" -ForegroundColor Green

# -- Step 2: Obfuscate --
if (-not $SkipObfuscation) {
    Write-Host "`n[2/6] Obfuscating BotMain.dll + BotCore.dll..." -ForegroundColor Yellow

    # Obfuscar does not support Unicode paths, use temp dir
    if (Test-Path $ObfuscateTmp)  { Remove-Item $ObfuscateTmp  -Recurse -Force }
    if (Test-Path $ObfuscatedTmp) { Remove-Item $ObfuscatedTmp -Recurse -Force }

    New-Item -ItemType Directory -Path $ObfuscateTmp  -Force | Out-Null
    New-Item -ItemType Directory -Path $ObfuscatedTmp -Force | Out-Null

    # Copy all DLLs (Obfuscar needs to resolve references)
    Copy-Item "$PublishDir\*.dll" $ObfuscateTmp
    Copy-Item "$RepoRoot\obfuscar.xml" $ObfuscateTmp

    Push-Location $ObfuscateTmp
    try {
        obfuscar.console obfuscar.xml
        if ($LASTEXITCODE -ne 0) { throw "Obfuscar failed" }
    } finally {
        Pop-Location
    }

    # Replace with obfuscated DLLs
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

$ResourceDirs = @("Profiles", "MulliganProfiles", "DiscoverCC", "ArenaCC", "Libs", "Data")
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

# -- Step 5: Trim --
Write-Host "`n[5/6] Trimming package..." -ForegroundColor Yellow

Get-ChildItem $PackageDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

$langDirs = Get-ChildItem $PackageDir -Directory | Where-Object {
    $_.Name -match "^(cs|de|es|fr|it|ja|ko|pl|pt-BR|ru|tr|zh-Hant)$"
}
foreach ($lang in $langDirs) {
    Remove-Item $lang.FullName -Recurse -Force
    Write-Host "  Removed locale: $($lang.Name)"
}

$totalSize = (Get-ChildItem $PackageDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMB = [math]::Round($totalSize / 1MB, 1)
$fileCount = (Get-ChildItem $PackageDir -Recurse -File).Count
Write-Host "  Package: $fileCount files, ${sizeMB} MB" -ForegroundColor Green

# -- Step 6: Create zip --
Write-Host "`n[6/6] Creating zip archive..." -ForegroundColor Yellow
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PackageDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Archive: $ZipPath (${zipSize} MB)" -ForegroundColor Green

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
