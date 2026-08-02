<#!
.SYNOPSIS
    Installs or updates a prebuilt Desktop System Monitor payload.

.DESCRIPTION
    This script is deliberately compatible with Windows PowerShell 5.1. The
    batch facade only supplies a runtime and an optional parent directory; all
    manifest, status, hash, ZIP, backup, and rollback decisions happen here.
#>
[CmdletBinding()]
param(
    [string]$PackageRoot,
    [string]$ParentDirectory,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,
    [ValidateSet('local-preview', 'public-release')]
    [string]$ExpectedPackageChannel = 'local-preview',
    [switch]$PublicDistribution,
    [switch]$NoPause
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Open-InstallerPayloadLock {
    param([Parameter(Mandatory)][string]$PackageRoot)
    $path = Join-Path $PackageRoot '.payload-update.lock'
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Payload lock cannot be a reparse point: $path" }
    }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    }
    catch {
        throw "Installer payload is being replaced by another session: $path"
    }
    return [pscustomobject]@{ Path = $path; Stream = $stream }
}

function Close-InstallerPayloadLock {
    param([Parameter(Mandatory)]$Lock)
    if ($null -ne $Lock.Stream) { $Lock.Stream.Dispose() }
}

function Test-IsWindowsHost {
    if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
        return [bool]$IsWindows
    }
    return $true
}

function ConvertTo-FullPath {
    param([Parameter(Mandatory)][string]$Path)
    return [IO.Path]::GetFullPath($Path)
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )
    $rootFull = (ConvertTo-FullPath -Path $Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = ConvertTo-FullPath -Path $Path
    if ($pathFull -ne $rootFull -and -not $pathFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the package root: $pathFull"
    }
    return $pathFull
}

function Assert-NotReparsePoint {
    param([Parameter(Mandatory)][string]$Path)
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse points are not accepted: $Path"
        }
    }
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name
    )
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name
    )
    $value = Get-PropertyValue -Object $Object -Name $Name
    if ($null -eq $value -or ([string]$value).Length -eq 0) {
        throw "Manifest property is missing: $Name"
    }
    return $value
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    $sha = New-Object Security.Cryptography.SHA256Managed
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Get-RelativePathPortable {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )
    $rootFull = (ConvertTo-FullPath -Path $Root).TrimEnd('\') + '\'
    $pathFull = ConvertTo-FullPath -Path $Path
    $rootUri = New-Object Uri($rootFull)
    $pathUri = New-Object Uri($pathFull)
    return $rootUri.MakeRelativeUri($pathUri).ToString().Replace('/', '\')
}

function Get-PeMachine {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset + 6 -gt $stream.Length) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ZipEntrySafe {
    param([Parameter(Mandatory)][string]$EntryName)
    $normalized = $EntryName.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or $normalized.EndsWith('/')) { return $false }
    if ([IO.Path]::IsPathRooted($normalized) -or $normalized.StartsWith('/') -or $normalized -match '^[A-Za-z]:') {
        throw "Rooted ZIP entry is not allowed: $EntryName"
    }
    if ($normalized -match '(^|/)\.\.(/|$)' -or $normalized -match '(^|/)\.(/|$)') {
        throw "Traversal ZIP entry is not allowed: $EntryName"
    }
    if ($normalized -match ':') { throw "ADS-like ZIP entry is not allowed: $EntryName" }
    return $true
}

function Get-ManifestRuntimeEntry {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$Rid
    )
    $runtimes = Get-RequiredProperty -Object $Manifest -Name 'runtimes'
    $entry = $runtimes.PSObject.Properties[$Rid]
    if ($null -eq $entry) { throw "The package does not contain runtime $Rid." }
    return $entry.Value
}

function Resolve-ParentDirectory {
    param([string]$Requested)
    $candidate = $Requested
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = $env:DSM_PARENT_DIRECTORY
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = 'Select an existing parent directory for Desktop System Monitor.'
        $dialog.ShowNewFolderButton = $false
        try {
            if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
                throw [System.OperationCanceledException]::new('Folder selection was cancelled.')
            }
            $candidate = $dialog.SelectedPath
        }
        finally { $dialog.Dispose() }
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw [System.OperationCanceledException]::new('Parent directory is empty.')
    }
    $full = ConvertTo-FullPath -Path $candidate
    if ($full.StartsWith('\\')) { throw "UNC destinations are not supported: $full" }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Parent directory does not exist: $full" }
    Assert-NotReparsePoint -Path $full
    if ([IO.Path]::GetFileName($full).Equals('Desktop System Monitor', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Select the parent directory, not the Desktop System Monitor directory itself.'
    }
    return $full
}

function Get-DirectoryFreeBytes {
    param([Parameter(Mandatory)][string]$Path)
    $root = [IO.Path]::GetPathRoot((ConvertTo-FullPath -Path $Path))
    return (New-Object IO.DriveInfo($root)).AvailableFreeSpace
}

function New-PathHash {
    param([Parameter(Mandatory)][string]$Path)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Path.ToLowerInvariant())
    $sha = New-Object Security.Cryptography.SHA256Managed
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function New-ManagedFileMap {
    param([Parameter(Mandatory)]$ManagedFiles)
    $map = @{}
    foreach ($file in @($ManagedFiles)) {
        $path = [string](Get-RequiredProperty -Object $file -Name 'path')
        $key = $path.Replace('\', '/').ToLowerInvariant()
        if ($map.ContainsKey($key)) { throw "Duplicate managed file in manifest: $path" }
        $map[$key] = $file
    }
    return $map
}

function Remove-NewManagedOnlyFiles {
    param(
        [Parameter(Mandatory)][string]$InstallDirectory,
        [Parameter(Mandatory)]$OldManagedFiles,
        [Parameter(Mandatory)]$NewManagedFiles
    )

    $oldMap = New-ManagedFileMap -ManagedFiles $OldManagedFiles
    foreach ($newFile in @($NewManagedFiles)) {
        $relative = ([string](Get-RequiredProperty -Object $newFile -Name 'path')).Replace('/', '\')
        $key = $relative.Replace('\', '/').ToLowerInvariant()
        if ($oldMap.ContainsKey($key)) { continue }
        $path = Assert-PathWithinRoot -Path (Join-Path $InstallDirectory $relative) -Root $InstallDirectory
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to remove a reparse point during rollback: $relative"
        }
        if (-not $item.PSIsContainer) { Remove-Item -LiteralPath $path -Force }
    }
}

function Remove-EmptyManagedDirectories {
    param(
        [Parameter(Mandatory)][string]$InstallDirectory,
        [Parameter(Mandatory)]$ManagedFiles
    )

    $root = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $directories = @{}
    foreach ($file in @($ManagedFiles)) {
        $relative = ([string](Get-RequiredProperty -Object $file -Name 'path')).Replace('/', '\')
        $parent = Split-Path -Parent $relative
        while (-not [string]::IsNullOrWhiteSpace($parent) -and $parent -ne '.') {
            $full = Assert-PathWithinRoot -Path (Join-Path $root $parent) -Root $root
            $directories[$full.ToLowerInvariant()] = $full
            $parent = Split-Path -Parent $parent
        }
    }
    foreach ($directory in @($directories.Values | Sort-Object Length -Descending)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        $item = Get-Item -LiteralPath $directory -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if (@(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $directory -Force
        }
    }
}

function Assert-ManagedFilesMatchMarker {
    param(
        [Parameter(Mandatory)][string]$InstallDirectory,
        [Parameter(Mandatory)]$ManagedFiles
    )

    foreach ($file in @($ManagedFiles)) {
        $relative = ([string](Get-RequiredProperty -Object $file -Name 'path')).Replace('/', '\')
        $path = Assert-PathWithinRoot -Path (Join-Path $InstallDirectory $relative) -Root $InstallDirectory
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Rollback did not restore managed file: $relative" }
        $expected = ([string](Get-RequiredProperty -Object $file -Name 'sha256')).ToLowerInvariant()
        if ((Get-FileSha256 -Path $path) -ne $expected) { throw "Rollback hash mismatch: $relative" }
    }
}

function Test-ArchiveAndExtract {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$ExpectedArchiveSha256,
        [Parameter(Mandatory)]$RuntimeEntry,
        [Parameter(Mandatory)][string]$ExtractionRoot
    )
    if ((Get-FileSha256 -Path $ArchivePath) -ne $ExpectedArchiveSha256.ToLowerInvariant()) {
        throw 'Payload archive SHA-256 does not match the package manifest.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    $seen = @{}
    try {
        foreach ($entry in $archive.Entries) {
            if (-not (Assert-ZipEntrySafe -EntryName $entry.FullName)) { continue }
            $key = $entry.FullName.Replace('\', '/').ToLowerInvariant()
            if ($seen.ContainsKey($key)) { throw "Duplicate ZIP entry: $($entry.FullName)" }
            $seen[$key] = $true
        }
    }
    finally { $archive.Dispose() }
    New-Item -ItemType Directory -Path $ExtractionRoot -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $ExtractionRoot)

    $managed = New-ManagedFileMap -ManagedFiles (Get-RequiredProperty -Object $RuntimeEntry -Name 'managedFiles')
    $actualFiles = @(Get-ChildItem -LiteralPath $ExtractionRoot -File -Recurse | ForEach-Object {
            (Get-RelativePathPortable -Root $ExtractionRoot -Path $_.FullName).Replace('\', '/')
        })
    foreach ($path in $actualFiles) {
        if (-not $managed.ContainsKey($path.ToLowerInvariant())) { throw "Unexpected payload file: $path" }
    }
    foreach ($key in $managed.Keys) {
        $file = $managed[$key]
        $relative = ([string](Get-RequiredProperty -Object $file -Name 'path')).Replace('/', '\')
        $full = Assert-PathWithinRoot -Path (Join-Path $ExtractionRoot $relative) -Root $ExtractionRoot
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Managed payload file is missing: $relative" }
        $expectedHash = ([string](Get-RequiredProperty -Object $file -Name 'sha256')).ToLowerInvariant()
        if ((Get-FileSha256 -Path $full) -ne $expectedHash) { throw "Managed file hash mismatch: $relative" }
    }
    $executable = [string](Get-RequiredProperty -Object $RuntimeEntry -Name 'executable')
    $exePath = Assert-PathWithinRoot -Path (Join-Path $ExtractionRoot $executable) -Root $ExtractionRoot
    $expectedExeHash = ([string](Get-RequiredProperty -Object $RuntimeEntry -Name 'executableSha256')).ToLowerInvariant()
    if ((Get-FileSha256 -Path $exePath) -ne $expectedExeHash) { throw 'Executable hash does not match the manifest.' }
    $expectedMachine = if ($RuntimeEntry.runtime -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
    if ((Get-PeMachine -Path $exePath) -ne $expectedMachine) { throw 'Executable PE architecture does not match the runtime.' }
    return $actualFiles
}

function Write-InstallMarker {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Marker
    )
    $temp = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    $backup = "$Path.$([guid]::NewGuid().ToString('N')).bak"
    try {
        $Marker | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temp -Encoding UTF8
        $null = Get-Content -LiteralPath $temp -Raw | ConvertFrom-Json
        if (Test-Path -LiteralPath $Path) {
            [IO.File]::Replace($temp, $Path, $backup, $true)
        }
        else {
            Move-Item -LiteralPath $temp -Destination $Path
        }
    }
    finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
    }
}

function Get-ExistingMarker {
    param([Parameter(Mandatory)][string]$InstallDirectory)
    $path = Join-Path $InstallDirectory '.desktop-system-monitor-install.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json) }
    catch { throw "Existing install marker is invalid: $path" }
}

function Invoke-Install {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Parent,
        [Parameter(Mandatory)][string]$Rid,
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$RuntimeEntry,
        [Parameter(Mandatory)][string]$PackageChannel
    )
    $installDirectory = Join-Path $Parent 'Desktop System Monitor'
    Assert-NotReparsePoint -Path $Parent
    if (Test-Path -LiteralPath $installDirectory) { Assert-NotReparsePoint -Path $installDirectory }
    $markerPath = Join-Path $installDirectory '.desktop-system-monitor-install.json'
    $existingMarker = if (Test-Path -LiteralPath $installDirectory -PathType Container) { Get-ExistingMarker -InstallDirectory $installDirectory } else { $null }
    if ((Test-Path -LiteralPath $installDirectory -PathType Container) -and $null -eq $existingMarker) {
        $existingItems = @(Get-ChildItem -LiteralPath $installDirectory -Force)
        if ($existingItems.Count -gt 0) { throw 'The destination exists without a trusted installer marker.' }
    }
    foreach ($process in @(Get-Process -Name DesktopSystemMonitor -ErrorAction SilentlyContinue)) {
        $processPath = $null
        try { $processPath = $process.MainModule.FileName } catch { $processPath = $null }
        $targetExecutable = ConvertTo-FullPath -Path (Join-Path $installDirectory 'DesktopSystemMonitor.exe')
        if (-not [string]::IsNullOrWhiteSpace($processPath) -and (ConvertTo-FullPath -Path $processPath) -ieq $targetExecutable) {
            throw 'DesktopSystemMonitor is running from the selected install directory. Close it before updating.'
        }
        if ([string]::IsNullOrWhiteSpace($processPath) -and (Test-Path -LiteralPath $installDirectory -PathType Container)) {
            throw 'A DesktopSystemMonitor process is running and its executable path could not be inspected.'
        }
        if (-not [string]::IsNullOrWhiteSpace($processPath)) {
            Write-Warning "A DesktopSystemMonitor process is running from another path and will not be modified: $processPath"
        }
    }

    $managed = New-ManagedFileMap -ManagedFiles (Get-RequiredProperty -Object $RuntimeEntry -Name 'managedFiles')
    $payloadBytes = [int64](Get-RequiredProperty -Object $RuntimeEntry -Name 'totalUncompressedBytes')
    $backupBytes = if ($null -ne $existingMarker) { $payloadBytes } else { 0 }
    $requiredBytes = $payloadBytes + $backupBytes + 16MB
    if ((Get-DirectoryFreeBytes -Path $Parent) -lt $requiredBytes) { throw "Insufficient free space. Required approximately $requiredBytes bytes." }
    $tempPath = [IO.Path]::GetTempPath()
    $tempRequiredBytes = ($payloadBytes * 2) + 16MB
    if ((Get-DirectoryFreeBytes -Path $tempPath) -lt $tempRequiredBytes) {
        throw "Insufficient free space in the temporary directory ($tempPath). Required approximately $tempRequiredBytes bytes."
    }

    $tempRoot = Join-Path $tempPath ('DesktopSystemMonitorInstaller-' + [guid]::NewGuid().ToString('N'))
    $extractionRoot = Join-Path $tempRoot 'payload'
    $stagedInstall = Join-Path $tempRoot 'install'
    $backupBase = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopSystemMonitorInstaller\Backups'
    $installHash = New-PathHash -Path $installDirectory
    $backupPath = Join-Path $backupBase $installHash
    $backupTemp = Join-Path $backupBase ('.staging-' + [guid]::NewGuid().ToString('N'))
    $mutexName = 'Local\DesktopSystemMonitor-Installer-' + $installHash
    $mutex = New-Object Threading.Mutex($false, $mutexName)
    $haveMutex = $false
    $applyStarted = $false
    try {
        $haveMutex = $mutex.WaitOne(0)
        if (-not $haveMutex) { throw 'Another installer operation is already running for this destination.' }
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        Test-ArchiveAndExtract -ArchivePath (Join-Path $Root ([string](Get-RequiredProperty -Object $RuntimeEntry -Name 'archive'))) `
            -ExpectedArchiveSha256 ([string](Get-RequiredProperty -Object $RuntimeEntry -Name 'archiveSha256')) `
            -RuntimeEntry $RuntimeEntry -ExtractionRoot $extractionRoot | Out-Null
        New-Item -ItemType Directory -Path $stagedInstall -Force | Out-Null
        foreach ($payloadItem in @(Get-ChildItem -LiteralPath $extractionRoot -Force)) {
            Copy-Item -LiteralPath $payloadItem.FullName -Destination $stagedInstall -Recurse -Force
        }

        if ($null -ne $existingMarker) {
            New-Item -ItemType Directory -Path $backupTemp -Force | Out-Null
            foreach ($old in @($existingMarker.managedFiles)) {
                $oldRelative = ([string](Get-RequiredProperty -Object $old -Name 'path')).Replace('/', '\')
                $oldPath = Assert-PathWithinRoot -Path (Join-Path $installDirectory $oldRelative) -Root $installDirectory
                if (Test-Path -LiteralPath $oldPath -PathType Leaf) {
                    $destination = Join-Path $backupTemp $oldRelative
                    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                    Copy-Item -LiteralPath $oldPath -Destination $destination -Force
                    if ((Get-FileSha256 -Path $oldPath) -ne ([string](Get-RequiredProperty -Object $old -Name 'sha256')).ToLowerInvariant()) {
                        throw "Managed file was changed outside the installer: $oldRelative"
                    }
                }
            }
            Copy-Item -LiteralPath $markerPath -Destination (Join-Path $backupTemp '.desktop-system-monitor-install.json') -Force
        }

        $applyStarted = $true
        if (-not (Test-Path -LiteralPath $installDirectory)) { New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null }
        foreach ($file in @(Get-ChildItem -LiteralPath $stagedInstall -File -Recurse)) {
            $relative = Get-RelativePathPortable -Root $stagedInstall -Path $file.FullName
            $destination = Assert-PathWithinRoot -Path (Join-Path $installDirectory $relative) -Root $installDirectory
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            if ((Get-FileSha256 -Path $destination) -ne (Get-FileSha256 -Path $file.FullName)) { throw "Post-copy hash mismatch: $relative" }
        }
        if ($null -ne $existingMarker) {
            foreach ($old in @($existingMarker.managedFiles)) {
                $oldRelative = ([string](Get-RequiredProperty -Object $old -Name 'path')).Replace('/', '\')
                $oldPath = Assert-PathWithinRoot -Path (Join-Path $installDirectory $oldRelative) -Root $installDirectory
                if (-not $managed.ContainsKey($oldRelative.Replace('\', '/').ToLowerInvariant()) -and (Test-Path -LiteralPath $oldPath -PathType Leaf)) {
                    Remove-Item -LiteralPath $oldPath -Force
                }
            }
            Remove-EmptyManagedDirectories -InstallDirectory $installDirectory -ManagedFiles $existingMarker.managedFiles
        }
        $marker = [ordered]@{
            schemaVersion = 1
            productId = 'desktop-system-monitor'
            productVersion = [string](Get-RequiredProperty -Object $Manifest -Name 'productVersion')
            runtime = $Rid
            packageChannel = $PackageChannel
            distributionStatus = [string](Get-RequiredProperty -Object $RuntimeEntry -Name 'distributionStatus')
            contentDigest = [string](Get-RequiredProperty -Object $RuntimeEntry -Name 'contentDigest')
            installedAt = if ($null -eq $existingMarker) { [DateTime]::UtcNow.ToString('o') } else { $existingMarker.installedAt }
            updatedAt = [DateTime]::UtcNow.ToString('o')
            executable = [string](Get-RequiredProperty -Object $RuntimeEntry -Name 'executable')
            managedFiles = @($managed.Values | ForEach-Object {
                    [ordered]@{ path = [string]$_.path; sha256 = ([string]$_.sha256).ToLowerInvariant(); size = [int64]$_.size }
                })
        }
        Write-InstallMarker -Path $markerPath -Marker $marker
        if ($null -ne $existingMarker) {
            if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Recurse -Force }
            New-Item -ItemType Directory -Path $backupBase -Force | Out-Null
            Move-Item -LiteralPath $backupTemp -Destination $backupPath
        }
        Write-Host "Installed Desktop System Monitor ($Rid) to $installDirectory" -ForegroundColor Green
        if ([string]$RuntimeEntry.distributionStatus -eq 'local-only') {
            Write-Warning 'UNVERIFIED PREVIEW - DO NOT PUBLISH: this local-only payload has not passed readiness.'
        }
        return 0
    }
    catch {
        $primary = $_.Exception.Message
        if ($applyStarted) {
            try {
                if ($null -ne $existingMarker) {
                    if (-not (Test-Path -LiteralPath $backupTemp -PathType Container)) {
                        throw 'Rollback backup staging directory is missing.'
                    }
                    $oldManagedMap = New-ManagedFileMap -ManagedFiles $existingMarker.managedFiles
                    Remove-NewManagedOnlyFiles -InstallDirectory $installDirectory `
                        -OldManagedFiles $existingMarker.managedFiles -NewManagedFiles $managed.Values
                    $newOnly = @($managed.Values | Where-Object {
                            -not $oldManagedMap.ContainsKey(
                                ([string](Get-RequiredProperty -Object $_ -Name 'path')).Replace('/', '\').Replace('\', '/').ToLowerInvariant())
                        })
                    Remove-EmptyManagedDirectories -InstallDirectory $installDirectory -ManagedFiles $newOnly
                    foreach ($old in @($existingMarker.managedFiles)) {
                        $relative = ([string](Get-RequiredProperty -Object $old -Name 'path')).Replace('/', '\')
                        $source = Join-Path $backupTemp $relative
                        $destination = Assert-PathWithinRoot -Path (Join-Path $installDirectory $relative) -Root $installDirectory
                        if (Test-Path -LiteralPath $source -PathType Leaf) {
                            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                            Copy-Item -LiteralPath $source -Destination $destination -Force
                        }
                    }
                    Write-InstallMarker -Path $markerPath -Marker $existingMarker
                    Assert-ManagedFilesMatchMarker -InstallDirectory $installDirectory -ManagedFiles $existingMarker.managedFiles
                }
                else {
                    foreach ($file in @($managed.Values)) {
                        $relative = ([string](Get-RequiredProperty -Object $file -Name 'path')).Replace('/', '\')
                        $path = Join-Path $installDirectory $relative
                        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
                    }
                    Remove-EmptyManagedDirectories -InstallDirectory $installDirectory -ManagedFiles $managed.Values
                    if ($null -eq $existingMarker -and (Test-Path -LiteralPath $markerPath)) { Remove-Item -LiteralPath $markerPath -Force }
                }
            }
            catch { throw "Install failed and rollback failed. Primary: $primary. Rollback: $($_.Exception.Message)" }
        }
        throw $primary
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
        if (Test-Path -LiteralPath $backupTemp) { Remove-Item -LiteralPath $backupTemp -Recurse -Force }
        if ($haveMutex) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Invoke-Main {
    if (-not (Test-IsWindowsHost)) { throw 'The installer requires Windows.' }
    $root = if ([string]::IsNullOrWhiteSpace($PackageRoot)) { $PSScriptRoot } else { ConvertTo-FullPath -Path $PackageRoot }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Package root does not exist: $root" }
    Assert-NotReparsePoint -Path $root
    $payloadRoot = Join-Path $root 'payload'
    $payloadLock = Open-InstallerPayloadLock -PackageRoot $root
    $temporaryPayloadRoot = $null
    try {
        $pendingPath = Join-Path $root '.payload-transaction.pending.json'
        if (Test-Path -LiteralPath $pendingPath -PathType Leaf) {
            throw "Installer payload promotion is incomplete; refusing to install until the pending transaction is recovered: $pendingPath"
        }
        $manifestPath = Join-Path $payloadRoot 'payload-manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Payload manifest is missing: $manifestPath" }
        try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } catch { throw "Payload manifest is invalid: $manifestPath" }
        $schemaVersion = [int](Get-RequiredProperty -Object $manifest -Name 'schemaVersion')
        if ($schemaVersion -ne 2) { throw "Unsupported payload manifest schema version: $schemaVersion" }
        if ([string](Get-RequiredProperty -Object $manifest -Name 'layoutContract') -ne 'single-file-v1') { throw 'Unsupported installer payload layout contract.' }
        $publish = Get-RequiredProperty -Object $manifest -Name 'publishContract'
        $expectedPublish = [ordered]@{
            configuration = 'Release'; selfContained = $true; publishSingleFile = $true
            includeNativeLibrariesForSelfExtract = $true; publishTrimmed = $false
            enableCompressionInSingleFile = $false; includeAllContentForSelfExtract = $false
            continuousIntegrationBuild = $true; debugType = 'None'; debugSymbols = $false
        }
        foreach ($name in $expectedPublish.Keys) {
            if ([string](Get-RequiredProperty -Object $publish -Name $name) -ne [string]$expectedPublish[$name]) { throw "Publish contract mismatch: $name" }
        }
        $channel = [string](Get-RequiredProperty -Object $manifest -Name 'packageChannel')
        if ($PublicDistribution) { $ExpectedPackageChannel = 'public-release' }
        if ($channel -ne $ExpectedPackageChannel) { throw "Package channel $channel does not match expected channel $ExpectedPackageChannel." }
        $rid = if ([string]::IsNullOrWhiteSpace($Runtime)) { $env:DSM_RUNTIME } else { $Runtime }
        if ([string]::IsNullOrWhiteSpace($rid)) {
            $arch = $env:PROCESSOR_ARCHITEW6432
            if ([string]::IsNullOrWhiteSpace($arch)) { $arch = $env:PROCESSOR_ARCHITECTURE }
            if ($arch -match '^ARM64$') { $rid = 'win-arm64' } elseif ($arch -match '^(AMD64|x86_64)$') { $rid = 'win-x64' } else { throw "Unsupported processor architecture: $arch" }
        }
        $entry = Get-ManifestRuntimeEntry -Manifest $manifest -Rid $rid
        $entryRuntime = [string](Get-RequiredProperty -Object $entry -Name 'runtime')
        if ($entryRuntime -ne $rid) { throw 'Runtime manifest key does not match runtime field.' }
        $null = Get-RequiredProperty -Object $entry -Name 'resolvedDependencies'
        $null = Get-RequiredProperty -Object $entry -Name 'artifactSha256'
        $managedFiles = @(Get-RequiredProperty -Object $entry -Name 'managedFiles')
        if ($managedFiles.Count -gt 12) { throw "Single-file payload contains too many managed files: $($managedFiles.Count)" }
        foreach ($managedFile in $managedFiles) {
            $managedPath = [string](Get-RequiredProperty -Object $managedFile -Name 'path')
            if ($managedPath -match '(?i)(\.dll|\.deps\.json|\.runtimeconfig\.json|\.pdb)$') { throw "Single-file payload contains loose runtime file: $managedPath" }
        }
        $status = [string](Get-RequiredProperty -Object $entry -Name 'distributionStatus')
        if (@('local-only', 'validation-only', 'public-release-approved') -notcontains $status) { throw "Unknown distribution status: $status" }
        if ($PublicDistribution -and $status -ne 'public-release-approved') { throw "Public distribution requires public-release-approved; got $status." }
        if (-not $PublicDistribution -and $status -eq 'validation-only') { throw 'validation-only artifacts require the explicit readiness workflow, not the root installer.' }
        $archive = Assert-PathWithinRoot -Path (Join-Path $payloadRoot ([string](Get-RequiredProperty -Object $entry -Name 'archive'))) -Root $payloadRoot
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw "Runtime archive is missing: $archive" }
        $temporaryPayloadRoot = Join-Path ([IO.Path]::GetTempPath()) ('DesktopSystemMonitorPayload-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporaryPayloadRoot -Force | Out-Null
        Copy-Item -LiteralPath $archive -Destination (Join-Path $temporaryPayloadRoot ([IO.Path]::GetFileName($archive))) -Force
        $parent = Resolve-ParentDirectory -Requested $ParentDirectory
        $result = Invoke-Install -Root $temporaryPayloadRoot -Parent $parent -Rid $rid -Manifest $manifest -RuntimeEntry $entry -PackageChannel $channel
        return $result
    }
    finally {
        if ($null -ne $temporaryPayloadRoot -and (Test-Path -LiteralPath $temporaryPayloadRoot)) { Remove-Item -LiteralPath $temporaryPayloadRoot -Recurse -Force }
        Close-InstallerPayloadLock -Lock $payloadLock
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }

$exitCode = 0
try { $exitCode = Invoke-Main }
catch [System.OperationCanceledException] { Write-Warning $_.Exception.Message; $exitCode = 3 }
catch {
    Write-Error $_.Exception.Message
    $message = $_.Exception.Message
    if ($message -match 'Unsupported processor|validation-only|distribution status|channel|Public distribution|archive is missing') { $exitCode = 4 } else { $exitCode = 5 }
}
if (-not $NoPause -and $exitCode -ne 0 -and $env:DSM_NO_PAUSE -ne '1') { Write-Host 'Press any key to continue...'; $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') }
exit $exitCode
