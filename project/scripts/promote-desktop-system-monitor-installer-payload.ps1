<#
.SYNOPSIS
    Shared lock and transactional helpers for the canonical installer payload.

    The helper deliberately does not acquire a process-local mutex. The
    FileStream lock below is cross-session and is held by the caller from
    payload validation through portable-output replacement.
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function ConvertTo-DesktopSystemMonitorFullPath {
    param([Parameter(Mandatory)][string]$Path)
    return [IO.Path]::GetFullPath($Path)
}

function Get-DesktopSystemMonitorCanonicalPayloadRoot {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    return ConvertTo-DesktopSystemMonitorFullPath (Join-Path $ProjectRoot 'installer/payload')
}

function Get-DesktopSystemMonitorPayloadLockPath {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    return ConvertTo-DesktopSystemMonitorFullPath (Join-Path $ProjectRoot 'installer/.payload-update.lock')
}

function Get-DesktopSystemMonitorPayloadTransactionPath {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    return ConvertTo-DesktopSystemMonitorFullPath (Join-Path $ProjectRoot 'installer/.payload-transaction.pending.json')
}

function Open-DesktopSystemMonitorPayloadLock {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $path = Get-DesktopSystemMonitorPayloadLockPath -ProjectRoot $ProjectRoot
    $parent = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Payload lock cannot be a reparse point: $path" }
    }
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Payload lock cannot be a reparse point: $path" }
    }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    }
    catch {
        throw "Installer payload is busy in another session: $path"
    }
    return [pscustomobject]@{ Path = $path; Stream = $stream }
}

function Close-DesktopSystemMonitorPayloadLock {
    param([Parameter(Mandatory)]$Lock)
    if ($null -ne $Lock.Stream) { $Lock.Stream.Dispose() }
}

function Write-DesktopSystemMonitorPayloadTransaction {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][hashtable]$State
    )
    $target = Get-DesktopSystemMonitorPayloadTransactionPath -ProjectRoot $ProjectRoot
    $temp = "$target.$([guid]::NewGuid().ToString('N')).tmp"
    $State.updatedAtUtc = [DateTime]::UtcNow.ToString('o')
    ($State | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $target -Force
}

function Remove-DesktopSystemMonitorPayloadTransaction {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $path = Get-DesktopSystemMonitorPayloadTransactionPath -ProjectRoot $ProjectRoot
    if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}

function Get-DesktopSystemMonitorPayloadDirectoryDigest {
    param([Parameter(Mandatory)][string]$Path)
    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName)
    $lines = @($files | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath((ConvertTo-DesktopSystemMonitorFullPath $Path), $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "{0}|{1}|{2}" -f $relative, $_.Length, $hash
        })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Move-DesktopSystemMonitorPayloadIntoPlace {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$StagedPayload,
        [Parameter(Mandatory)][string]$TransactionId
    )
    $target = Get-DesktopSystemMonitorCanonicalPayloadRoot -ProjectRoot $ProjectRoot
    $staged = ConvertTo-DesktopSystemMonitorFullPath $StagedPayload
    if (-not (Test-Path -LiteralPath $staged -PathType Container)) { throw "Staged payload is missing: $staged" }
    foreach ($required in @('payload-manifest.json', 'payload-manifest.schema.json', 'README.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $staged $required) -PathType Leaf)) { throw "Staged payload is incomplete: $required" }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $staged 'payload-manifest.json') -Raw | ConvertFrom-Json
    if ([string]$manifest.packageChannel -ne 'local-preview') {
        throw 'Only local-preview payloads may be promoted into the canonical installer payload.'
    }
    if ([int]$manifest.schemaVersion -ne 2 -or [string]$manifest.layoutContract -ne 'single-file-v1') {
        throw 'Only schema v2 single-file payloads may be promoted into the canonical installer payload.'
    }
    $backup = ConvertTo-DesktopSystemMonitorFullPath (Join-Path $ProjectRoot ("dist/.payload-backup-$TransactionId"))
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup }
    try {
        Move-Item -LiteralPath $staged -Destination $target
    }
    catch {
        if (Test-Path -LiteralPath $backup -PathType Container) { Move-Item -LiteralPath $backup -Destination $target -Force }
        throw
    }
    return [pscustomobject]@{ TargetPath = $target; BackupPath = $backup }
}

function Restore-DesktopSystemMonitorPayloadBackup {
    param([Parameter(Mandatory)][string]$ProjectRoot, [Parameter(Mandatory)][string]$BackupPath)
    $target = Get-DesktopSystemMonitorCanonicalPayloadRoot -ProjectRoot $ProjectRoot
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    if (Test-Path -LiteralPath $BackupPath -PathType Container) { Move-Item -LiteralPath $BackupPath -Destination $target -Force }
}

function Remove-DesktopSystemMonitorPayloadBackup {
    param([Parameter(Mandatory)][string]$BackupPath)
    if (Test-Path -LiteralPath $BackupPath -PathType Container) { Remove-Item -LiteralPath $BackupPath -Recurse -Force }
}

if ($MyInvocation.InvocationName -eq '.') { return }
