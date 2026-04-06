[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$asm = [System.Reflection.Assembly]::LoadFile("H:\Hearthstone\Hearthstone_Data\Managed\Assembly-CSharp.dll")
try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
$bf = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static

function Dump-Type($typeName) {
    $type = $types | Where-Object { $_.FullName -eq $typeName } | Select-Object -First 1
    if ($type -eq $null) {
        Write-Output "!!! TYPE NOT FOUND: $typeName"
        return
    }

    Write-Output "=========================================="
    Write-Output "=== $typeName FIELDS ==="
    Write-Output "=========================================="
    $type.GetFields($bf) | ForEach-Object {
        $access = if ($_.IsPublic) { "public" } elseif ($_.IsPrivate) { "private" } else { "protected/internal" }
        $static = if ($_.IsStatic) { " static" } else { "" }
        Write-Output "  $access$static $($_.FieldType.Name) $($_.Name)"
    }

    Write-Output ""
    Write-Output "=== $typeName PROPERTIES ==="
    $type.GetProperties($bf) | ForEach-Object {
        Write-Output "  $($_.PropertyType.Name) $($_.Name) { get; set; }"
    }

    Write-Output ""
    Write-Output "=== $typeName METHODS ==="
    $type.GetMethods($bf) | Where-Object { $_.DeclaringType.FullName -eq $typeName } | ForEach-Object {
        $access = if ($_.IsPublic) { "public" } elseif ($_.IsPrivate) { "private" } else { "protected/internal" }
        $static = if ($_.IsStatic) { " static" } else { "" }
        $params = ($_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Output "  $access$static $($_.ReturnType.Name) $($_.Name)($params)"
    }

    Write-Output ""
    Write-Output "=== $typeName NESTED TYPES ==="
    $type.GetNestedTypes($bf) | ForEach-Object {
        Write-Output "  $($_.FullName)"
        if ($_.IsEnum) {
            $vals = [System.Enum]::GetNames($_) -join ", "
            Write-Output "    Enum values: $vals"
        }
    }
    Write-Output ""
}

# DraftManager
Dump-Type "DraftManager"

# DraftDisplay
Dump-Type "DraftDisplay"

# NetCache
Dump-Type "NetCache"

# NetPlayerArenaTickets
Dump-Type "NetCache+NetPlayerArenaTickets"

# NetCacheGoldBalance
Dump-Type "NetCache+NetCacheGoldBalance"

# ArenaClientStateType (enum)
$enumType = $types | Where-Object { $_.FullName -eq "ArenaClientStateType" } | Select-Object -First 1
if ($enumType) {
    Write-Output "=== ArenaClientStateType ENUM ==="
    [System.Enum]::GetNames($enumType) | ForEach-Object { Write-Output "  $_" }
    Write-Output ""
}

# Search for StoreManager or similar
Write-Output "=== Searching for Store-related manager classes ==="
$types | Where-Object { $_.Name -like "*Store*Manager*" -or $_.Name -like "*Shop*Manager*" } | ForEach-Object { Write-Output "  $($_.FullName)" }
Write-Output ""

# IStore interface
$istore = $types | Where-Object { $_.FullName -eq "IStore" } | Select-Object -First 1
if ($istore) {
    Write-Output "=== IStore METHODS ==="
    $istore.GetMethods() | ForEach-Object {
        $params = ($_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Output "  $($_.ReturnType.Name) $($_.Name)($params)"
    }
    Write-Output ""
}

# ArenaTrayDisplay
$tray = $types | Where-Object { $_.Name -eq "ArenaTrayDisplay" } | Select-Object -First 1
if ($tray) {
    Dump-Type $tray.FullName
}

# DraftDeckSet
Dump-Type "DraftManager+DraftDeckSet"

# DraftDisplay nested enums/classes
$dd = $types | Where-Object { $_.FullName -eq "DraftDisplay" } | Select-Object -First 1
if ($dd) {
    Write-Output "=== DraftDisplay+DraftMode ENUM ==="
    $dm = $dd.GetNestedTypes($bf) | Where-Object { $_.Name -eq "DraftMode" }
    if ($dm -and $dm.IsEnum) {
        [System.Enum]::GetNames($dm) | ForEach-Object { Write-Output "  $_" }
    }
    Write-Output ""

    Write-Output "=== DraftDisplay+DraftChoice FIELDS ==="
    $dc = $dd.GetNestedTypes($bf) | Where-Object { $_.Name -eq "DraftChoice" }
    if ($dc) {
        $dc.GetFields($bf) | ForEach-Object { Write-Output "  $($_.FieldType.Name) $($_.Name)" }
    }
    Write-Output ""
}
