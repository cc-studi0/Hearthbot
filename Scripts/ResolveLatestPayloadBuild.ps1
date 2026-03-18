param(
    [string]$RepoRoot = "",
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$candidatePaths = @(
    (Join-Path $RepoRoot "HearthstonePayload\bin\Debug\net472\HearthstonePayload.dll"),
    (Join-Path $RepoRoot "HearthstonePayload\obj\Debug\net472\HearthstonePayload.dll"),
    (Join-Path $RepoRoot "HearthstonePayload\bin\Release\net472\HearthstonePayload.dll"),
    (Join-Path $RepoRoot "HearthstonePayload\obj\Release\net472\HearthstonePayload.dll")
)

$candidates = foreach ($candidatePath in $candidatePaths) {
    if (Test-Path $candidatePath) {
        Get-Item $candidatePath
    }
}

if (-not $candidates) {
    throw "No compiled HearthstonePayload.dll found in Debug/Release outputs."
}

$selected = $candidates |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

$result = [pscustomobject]@{
    Path = $selected.FullName
    Sha256 = (Get-FileHash $selected.FullName -Algorithm SHA256).Hash
    LastWriteTimeUtc = $selected.LastWriteTimeUtc.ToString("o")
}

if ($AsJson) {
    $result | ConvertTo-Json -Compress
}
else {
    $result.Path
}
