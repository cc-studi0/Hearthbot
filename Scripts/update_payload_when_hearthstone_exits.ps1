param(
    [string]$Source = "",
    [string]$Destination = "H:\Hearthstone\BepInEx\plugins\HearthstonePayload.dll",
    [int]$PollSeconds = 2
)

$ErrorActionPreference = "Stop"

$selectedHash = ""

if ([string]::IsNullOrWhiteSpace($Source)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $resolveScript = Join-Path $PSScriptRoot "ResolveLatestPayloadBuild.ps1"
    if (-not (Test-Path $resolveScript)) {
        throw "ResolveLatestPayloadBuild.ps1 not found."
    }

    $selected = & $resolveScript -RepoRoot $repoRoot -AsJson | ConvertFrom-Json
    $Source = $selected.Path
    $selectedHash = $selected.Sha256
}

if ([string]::IsNullOrWhiteSpace($selectedHash)) {
    if (-not (Test-Path $Source)) {
        throw "Source payload not found: $Source"
    }
    $selectedHash = (Get-FileHash $Source -Algorithm SHA256).Hash
}

Write-Host "[payload] Source: $Source"
Write-Host "[payload] Source SHA256: $selectedHash"

while (Get-Process Hearthstone -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds $PollSeconds
}

Copy-Item -Force $Source $Destination
$destinationHash = (Get-FileHash $Destination -Algorithm SHA256).Hash
Write-Host "[payload] Destination: $Destination"
Write-Host "[payload] Destination SHA256: $destinationHash"
