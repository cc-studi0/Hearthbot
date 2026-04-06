[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$asm = [System.Reflection.Assembly]::LoadFile("H:\Hearthstone\Hearthstone_Data\Managed\Assembly-CSharp.dll")
try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }

# Find actual full names
Write-Output "=== Finding DraftManager ==="
$types | Where-Object { $_.Name -eq "DraftManager" } | ForEach-Object { Write-Output "  FOUND: $($_.FullName)  Namespace: $($_.Namespace)" }

Write-Output ""
Write-Output "=== Finding DraftDisplay ==="
$types | Where-Object { $_.Name -eq "DraftDisplay" } | ForEach-Object { Write-Output "  FOUND: $($_.FullName)  Namespace: $($_.Namespace)" }

Write-Output ""
Write-Output "=== Finding NetCache ==="
$types | Where-Object { $_.Name -eq "NetCache" } | ForEach-Object { Write-Output "  FOUND: $($_.FullName)  Namespace: $($_.Namespace)" }

Write-Output ""
Write-Output "=== Finding StoreManager ==="
$types | Where-Object { $_.Name -like "*StoreManager*" } | ForEach-Object { Write-Output "  FOUND: $($_.FullName)  Namespace: $($_.Namespace)" }

Write-Output ""
Write-Output "=== Finding ArenaLandingPage ==="
$types | Where-Object { $_.Name -like "*ArenaLanding*" } | ForEach-Object { Write-Output "  FOUND: $($_.FullName)  Namespace: $($_.Namespace)" }

# Check if nested types are what we're seeing
Write-Output ""
Write-Output "=== DraftManager as nested type? ==="
$types | Where-Object { $_.FullName -like "*+DraftManager" } | ForEach-Object { Write-Output "  FOUND: $($_.FullName)" }

Write-Output ""
Write-Output "=== Check NetCache nesting ==="
$types | Where-Object { $_.FullName -like "*+NetCache" -or ($_.FullName -like "NetCache+*" -and -not $_.FullName.Contains("+NetCache+")) } | Select-Object -First 5 | ForEach-Object { Write-Output "  $($_.FullName) DeclaringType: $($_.DeclaringType)" }

# Total types count
Write-Output ""
Write-Output "Total types loaded: $($types.Count)"
