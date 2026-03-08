$ErrorActionPreference = "Stop"

param(
    [string]$Source = "",
    [string]$Destination = "H:\Hearthstone\BepInEx\plugins\HearthstonePayload.dll",
    [int]$PollSeconds = 2
)

if ([string]::IsNullOrWhiteSpace($Source)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $binRelease = Join-Path $repoRoot "HearthstonePayload\bin\Release\net472\HearthstonePayload.dll"
    $objRelease = Join-Path $repoRoot "HearthstonePayload\obj\Release\net472\HearthstonePayload.dll"
    if (Test-Path $binRelease) {
        $Source = $binRelease
    } elseif (Test-Path $objRelease) {
        $Source = $objRelease
    } else {
        throw "No compiled HearthstonePayload.dll found."
    }
}

while (Get-Process Hearthstone -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds $PollSeconds
}

Copy-Item -Force $Source $Destination
