Set-StrictMode -Version Latest

# Reads the Win32 VERSIONINFO of a published Windows executable without
# FileVersionInfo, which cannot read native resources on macOS/Linux hosts.
# Only fixed headers and the RT_VERSION resource are read; the file is never
# loaded, executed, or modified.

function Get-DesktopSystemMonitorProductVersion {
    param([Parameter(Mandatory)][string]$ProjectRoot)

    [xml]$props = Get-Content -LiteralPath (Join-Path $ProjectRoot 'Directory.Build.props') -Raw
    $version = [string]$props.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Directory.Build.props must define a numeric major.minor.patch Version.'
    }
    return $version
}

function Read-DesktopSystemMonitorVersionBlock {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][int]$Offset,
        [Parameter(Mandatory)][int]$Limit
    )

    if ($Offset + 6 -gt $Limit) { throw 'VERSIONINFO block header is truncated.' }
    $length = [BitConverter]::ToUInt16($Bytes, $Offset)
    $valueLength = [BitConverter]::ToUInt16($Bytes, $Offset + 2)
    $type = [BitConverter]::ToUInt16($Bytes, $Offset + 4)
    $end = $Offset + $length
    if ($length -lt 6 -or $end -gt $Limit) { throw 'VERSIONINFO block length is invalid.' }
    $keyStart = $Offset + 6
    $keyEnd = $keyStart
    while ($keyEnd + 1 -lt $end -and ($Bytes[$keyEnd] -ne 0 -or $Bytes[$keyEnd + 1] -ne 0)) { $keyEnd += 2 }
    $valueStart = ($keyEnd + 2 + 3) -band -bnot 3
    # String values are counted in UTF-16 units, binary values in bytes.
    $valueBytes = if ($type -eq 1) { $valueLength * 2 } else { $valueLength }
    $text = $null
    if ($type -eq 1 -and $valueStart -lt $end) {
        $text = [Text.Encoding]::Unicode.GetString($Bytes, $valueStart, [Math]::Min($valueBytes, $end - $valueStart)).Split([char]0)[0]
    }
    [pscustomobject]@{
        Key = [Text.Encoding]::Unicode.GetString($Bytes, $keyStart, $keyEnd - $keyStart)
        ValueStart = $valueStart
        ValueBytes = $valueBytes
        Text = $text
        ChildrenStart = ($valueStart + $valueBytes + 3) -band -bnot 3
        End = $end
    }
}

function Get-DesktopSystemMonitorVersionBlockChild {
    param([Parameter(Mandatory)][byte[]]$Bytes, [Parameter(Mandatory)]$Block)

    $offset = $Block.ChildrenStart
    while ($offset + 6 -le $Block.End) {
        $child = Read-DesktopSystemMonitorVersionBlock -Bytes $Bytes -Offset $offset -Limit $Block.End
        $child
        $offset = ($child.End + 3) -band -bnot 3
    }
}

function ConvertFrom-DesktopSystemMonitorVersionInfo {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    $root = Read-DesktopSystemMonitorVersionBlock -Bytes $Bytes -Offset 0 -Limit $Bytes.Length
    if ($root.Key -ne 'VS_VERSION_INFO' -or $root.ValueBytes -lt 52) { throw 'VS_VERSION_INFO is missing.' }
    $fixed = $root.ValueStart
    if ([BitConverter]::ToUInt32($Bytes, $fixed) -ne [uint32]4277077181) { throw 'VS_FIXEDFILEINFO signature is invalid.' }
    $fixedVersion = {
        param([int]$At)
        $high = [BitConverter]::ToUInt32($Bytes, $At)
        $low = [BitConverter]::ToUInt32($Bytes, $At + 4)
        '{0}.{1}.{2}.{3}' -f ($high -shr 16), ($high -band 0xFFFF), ($low -shr 16), ($low -band 0xFFFF)
    }
    $strings = @{}
    foreach ($section in @(Get-DesktopSystemMonitorVersionBlockChild -Bytes $Bytes -Block $root)) {
        if ($section.Key -ne 'StringFileInfo') { continue }
        foreach ($table in @(Get-DesktopSystemMonitorVersionBlockChild -Bytes $Bytes -Block $section)) {
            foreach ($entry in @(Get-DesktopSystemMonitorVersionBlockChild -Bytes $Bytes -Block $table)) {
                if (-not $strings.ContainsKey($entry.Key)) { $strings[$entry.Key] = $entry.Text }
            }
        }
    }
    [pscustomobject]@{
        ProductVersion = $strings['ProductVersion']
        FileVersion = $strings['FileVersion']
        FixedFileVersion = & $fixedVersion ($fixed + 8)
        FixedProductVersion = & $fixedVersion ($fixed + 16)
    }
}

function Get-DesktopSystemMonitorExecutableVersionInfo {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        $read32 = { param([long]$At) $stream.Position = $At; $reader.ReadUInt32() }
        $read16 = { param([long]$At) $stream.Position = $At; $reader.ReadUInt16() }
        if ($stream.Length -lt 0x40 -or (& $read16 0) -ne 0x5A4D) { throw "Not a PE executable: $Path" }
        $pe = & $read32 0x3C
        if ($pe + 24 -gt $stream.Length) { throw "Not a PE executable: $Path" }
        if ((& $read32 $pe) -ne 0x00004550) { throw "Not a PE executable: $Path" }
        $machine = & $read16 ($pe + 4)
        $sectionCount = & $read16 ($pe + 6)
        $optional = $pe + 24
        $directories = switch (& $read16 $optional) {
            0x10B { $optional + 96 }
            0x20B { $optional + 112 }
            default { throw "Unknown PE optional header: $Path" }
        }
        if ((& $read32 ($directories - 4)) -lt 3) { throw "PE has no resource directory: $Path" }
        $resourceRva = & $read32 ($directories + 16)
        $sectionTable = $optional + (& $read16 ($pe + 20))
        $toOffset = {
            param([uint32]$Rva)
            for ($index = 0; $index -lt $sectionCount; $index++) {
                $section = $sectionTable + 40 * $index
                $virtualSize = & $read32 ($section + 8)
                $virtualAddress = & $read32 ($section + 12)
                $rawSize = & $read32 ($section + 16)
                if ($Rva -ge $virtualAddress -and $Rva -lt $virtualAddress + [Math]::Max($virtualSize, $rawSize)) {
                    return [long](& $read32 ($section + 20)) + $Rva - $virtualAddress
                }
            }
            throw "RVA is outside all sections: $Rva"
        }
        if ($resourceRva -eq 0) { throw "PE has no resources: $Path" }
        $resourceBase = & $toOffset $resourceRva
        # Directory levels: type RT_VERSION (16), then the first name and language.
        $entries = {
            param([long]$Directory)
            $count = [int](& $read16 ($Directory + 12)) + [int](& $read16 ($Directory + 14))
            for ($index = 0; $index -lt $count; $index++) {
                [pscustomobject]@{ Name = & $read32 ($Directory + 16 + 8 * $index); Target = & $read32 ($Directory + 20 + 8 * $index) }
            }
        }
        $node = @(& $entries $resourceBase | Where-Object { $_.Name -eq 16 })
        foreach ($level in 1..3) {
            if ($node.Count -eq 0) { throw "PE has no VERSIONINFO resource: $Path" }
            $target = $node[0].Target
            $isDirectory = ($target -band [uint32]2147483648) -ne 0
            if ($level -lt 3) {
                if (-not $isDirectory) { throw "VERSIONINFO resource tree is malformed: $Path" }
                $node = @(& $entries ($resourceBase + ($target -band 0x7FFFFFFF)))
            }
            elseif ($isDirectory) { throw "VERSIONINFO resource tree is malformed: $Path" }
        }
        $dataEntry = $resourceBase + $target
        $dataRva = & $read32 $dataEntry
        $dataSize = & $read32 ($dataEntry + 4)
        $stream.Position = & $toOffset $dataRva
        $bytes = $reader.ReadBytes([int]$dataSize)
        if ($bytes.Length -ne $dataSize) { throw "VERSIONINFO resource is truncated: $Path" }
        $information = ConvertFrom-DesktopSystemMonitorVersionInfo -Bytes $bytes
        $information | Add-Member -NotePropertyName Machine -NotePropertyValue ([int]$machine)
        return $information
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-DesktopSystemMonitorExecutableVersion {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ProductVersion
    )

    $information = Get-DesktopSystemMonitorExecutableVersionInfo -Path $Path
    $numericVersion = "$ProductVersion.0"
    $expected = [ordered]@{
        ProductVersion = $ProductVersion
        FileVersion = $numericVersion
        FixedProductVersion = $numericVersion
        FixedFileVersion = $numericVersion
    }
    $mismatches = @(foreach ($name in $expected.Keys) {
            if ($information.$name -cne $expected[$name]) { "$name=$($information.$name) (expected $($expected[$name]))" }
        })
    if ($mismatches.Count -gt 0) {
        throw "Executable version resource does not match Directory.Build.props: $($mismatches -join '; ')"
    }
    return $information
}
