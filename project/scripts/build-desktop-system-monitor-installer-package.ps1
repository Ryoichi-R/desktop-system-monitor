<#!
.SYNOPSIS
    Builds deterministic local, validation, or public installer payloads.

.DESCRIPTION
    This is the owner-local packaging path. It never changes a readiness
    result. A blocked runtime can only become local-only when the caller
    explicitly supplies -AllowReadinessBlocked for a local-preview package.
    Public-release packages require the existing readiness summary to say
    PASS and the artifact hash to match the generated portable payload.

    -StageRoot and -ArtifactsRoot are caller-owned publish outputs. They are
    validated in place to avoid copying a large single-file executable again.
    The canonical installer/payload directory is a promotion target, not a
    direct package-builder output.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Runtime = @('win-x64'),
    [ValidateSet('local-preview', 'validation', 'public-release')]
    [string]$PackageChannel = 'local-preview',
    [string]$OutputRoot = 'installer/payload',
    [string]$StageRoot,
    [string]$ArtifactsRoot,
    [switch]$AllowReadinessBlocked,
    [switch]$CreateReadinessCandidate,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
        throw "Path is outside the project root: $pathFull"
    }
    return $pathFull
}

function Get-RelativePathPortable {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path)
    return [IO.Path]::GetRelativePath((ConvertTo-FullPath $Root), (ConvertTo-FullPath $Path)).Replace('\', '/')
}

function Get-Sha256Text {
    param([Parameter(Mandatory)][string]$Text)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PeMachine {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 0 -or $offset + 6 -gt $stream.Length) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
        return $reader.ReadUInt16()
    }
    finally { $stream.Dispose() }
}

function New-DeterministicZip {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object { (Get-RelativePathPortable -Root $Source -Path $_.FullName) })) {
                $entryName = Get-RelativePathPortable -Root $Source -Path $file.FullName
                $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = $file.OpenRead()
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-ManifestFileRecords {
    param([Parameter(Mandatory)][string]$Stage)
    $records = @(
        Get-ChildItem -LiteralPath $Stage -File -Recurse | Sort-Object { Get-RelativePathPortable -Root $Stage -Path $_.FullName } | ForEach-Object {
            $relative = Get-RelativePathPortable -Root $Stage -Path $_.FullName
            [ordered]@{
                path = $relative
                size = [int64]$_.Length
                sha256 = Get-FileSha256 -Path $_.FullName
            }
        }
    )
    if ($records.Count -eq 0) { throw "Publish stage is empty: $Stage" }
    return $records
}

function Get-ContentDigest {
    param([Parameter(Mandatory)]$Records)
    $lines = @($Records | ForEach-Object { "{0}|{1}|{2}" -f $_['path'], $_['size'], $_['sha256'] })
    return Get-Sha256Text -Text ($lines -join "`n")
}

function Get-StageRuntimeAssets {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][string]$Runtime,
        [string]$ProjectAssetsPath
    )
    if (-not [string]::IsNullOrWhiteSpace($ProjectAssetsPath) -and (Test-Path -LiteralPath $ProjectAssetsPath -PathType Leaf)) {
        return Resolve-DesktopSystemMonitorRuntimeAssets -ProjectAssetsPath $ProjectAssetsPath -Runtime $Runtime
    }
    $licenseDirectories = @(Get-ChildItem -LiteralPath (Join-Path $Stage 'licenses') -Directory -Filter 'dotnet-runtime-*' -ErrorAction SilentlyContinue)
    if ($licenseDirectories.Count -ne 1 -or $licenseDirectories[0].Name -notmatch '^dotnet-runtime-(?<version>\d+\.\d+\.\d+)$') {
        throw "A single resolved runtime license directory or project.assets.json is required for stage: $Stage"
    }
    [pscustomobject]@{
        runtime = $Runtime
        runtimePackName = "Microsoft.NETCore.App.Runtime.$Runtime"
        runtimeVersion = $Matches.version
        windowsDesktopPackName = "Microsoft.WindowsDesktop.App.Runtime.$Runtime"
        windowsDesktopVersion = $Matches.version
        ilLinkPackName = $null
        ilLinkVersion = $null
        assetsSha256 = $null
    }
}

function Assert-StageContract {
    param([Parameter(Mandatory)][string]$Stage, [Parameter(Mandatory)][string]$Rid)
    $exe = Join-Path $Stage 'DesktopSystemMonitor.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable is missing: $exe" }
    $expectedMachine = if ($Rid -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
    if ((Get-PeMachine -Path $exe) -ne $expectedMachine) { throw "PE architecture mismatch for $Rid." }
    foreach ($forbidden in @(Get-ChildItem -LiteralPath $Stage -File -Recurse | Where-Object {
                $_.Extension -in @('.pdb', '.user', '.suo', '.log', '.bak') -or $_.FullName -match '[\\/](src|tests|obj|result|coverage|backup)[\\/]'
            })) {
        throw "Forbidden publish file: $($forbidden.FullName)"
    }
    foreach ($required in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Stage $required) -PathType Leaf)) { throw "Required legal file is missing from stage: $required" }
    }
    $stageFiles = @(Get-ChildItem -LiteralPath $Stage -File -Recurse)
    if ($stageFiles.Count -gt 12) {
        throw "Single-file stage contains too many files: $($stageFiles.Count) (maximum 12)."
    }
    $looseRuntimeFiles = @($stageFiles | Where-Object {
            $_.Name.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
            $_.Name.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or
            $_.Name.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase) -or
            $_.Name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)
        })
    if ($looseRuntimeFiles.Count -gt 0) {
        throw "Single-file stage contains loose runtime files: $($looseRuntimeFiles[0].FullName)"
    }
}

function Get-ReadinessSummary {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $path = Join-Path $ProjectRoot 'docs/validation/readiness-summary.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Readiness summary is missing: $path" }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$projectRoot = ConvertTo-FullPath (Split-Path -Parent $PSScriptRoot)
$runtimeAssetsScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-runtime-assets.ps1'
if (-not (Test-Path -LiteralPath $runtimeAssetsScript -PathType Leaf)) { throw "Runtime assets resolver is missing: $runtimeAssetsScript" }
. $runtimeAssetsScript
$output = Assert-PathWithinRoot -Path (Join-Path $projectRoot $OutputRoot) -Root $projectRoot
$readiness = Get-ReadinessSummary -ProjectRoot $projectRoot
$identityScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-candidate-identity.ps1'
if (-not (Test-Path -LiteralPath $identityScript -PathType Leaf)) { throw "Candidate identity helper is missing: $identityScript" }
. $identityScript
$identity = Resolve-DesktopSystemMonitorCandidateIdentity -ProjectRoot $projectRoot
$publishContract = $identity.publishContractObject

$canonicalPayloadRoot = ConvertTo-FullPath (Join-Path $projectRoot 'installer/payload')
if ($output -ieq $canonicalPayloadRoot) {
    throw "Canonical installer payload cannot be written directly. Build into a staging directory and promote it through rebuild-desktop-system-monitor.ps1."
}

[xml]$props = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Directory.Build.props does not define Version.' }
if ($PackageChannel -eq 'public-release' -and $AllowReadinessBlocked) { throw '-AllowReadinessBlocked is only valid for local-preview.' }
if ($PackageChannel -eq 'validation' -and $AllowReadinessBlocked) { throw '-AllowReadinessBlocked is only valid for local-preview.' }
if ($PackageChannel -eq 'public-release' -and $Runtime.Count -eq 0) { throw 'Public package requires at least one runtime.' }

$work = Assert-PathWithinRoot -Path (Join-Path $projectRoot ('dist\.installer-work\' + [guid]::NewGuid().ToString('N'))) -Root $projectRoot
$stages = @{}
$stageAssets = @{}
$artifacts = [ordered]@{}
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    foreach ($rid in $Runtime) {
        $stage = Join-Path $work $rid
        if ([string]::IsNullOrWhiteSpace($StageRoot)) {
            $publishScript = Join-Path $PSScriptRoot 'publish-desktop-system-monitor.ps1'
            $artifactsRoot = Join-Path $work "artifacts-$rid"
            & $publishScript -Runtime $rid `
                -OutputDir ([IO.Path]::GetRelativePath($projectRoot, $stage)) `
                -ArtifactsPath ([IO.Path]::GetRelativePath($projectRoot, $artifactsRoot))
            if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
        }
        else {
            $provided = ConvertTo-FullPath $StageRoot
            $candidate = Join-Path $provided $rid
            if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { $candidate = $provided }
            if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { throw "StageRoot does not contain a stage for ${rid}: $candidate" }
            if ((Get-Item -LiteralPath $candidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "StageRoot cannot be a symbolic link or junction: $candidate" }
            # A caller-owned stage is already validated in place. Avoid a
            # second copy of the large single-file executable.
            $stage = $candidate
            if (-not [string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
                $providedArtifacts = ConvertTo-FullPath $ArtifactsRoot
                $artifactCandidate = Join-Path $providedArtifacts $rid
                if (-not (Test-Path -LiteralPath $artifactCandidate -PathType Container)) { $artifactCandidate = $providedArtifacts }
                if (-not (Test-Path -LiteralPath $artifactCandidate -PathType Container)) { throw "ArtifactsRoot does not contain artifacts for ${rid}: $artifactCandidate" }
                if ((Get-Item -LiteralPath $artifactCandidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "ArtifactsRoot cannot be a symbolic link or junction: $artifactCandidate" }
                $assetsCandidate = Join-Path $artifactCandidate 'obj/DesktopSystemMonitor.App/project.assets.json'
                if (Test-Path -LiteralPath $assetsCandidate -PathType Leaf) { $stageAssets[$rid] = $assetsCandidate }
            }
        }
        Assert-StageContract -Stage $stage -Rid $rid
        $stages[$rid] = $stage
    }

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $templateRoot = Join-Path $projectRoot 'installer/payload-template'
    foreach ($templateName in @('README.md', 'payload-manifest.schema.json')) {
        $templatePath = Join-Path $templateRoot $templateName
        if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) { throw "Payload template is missing: $templatePath" }
        Copy-Item -LiteralPath $templatePath -Destination (Join-Path $output $templateName) -Force
    }
    foreach ($rid in $Runtime) {
        $stage = $stages[$rid]
        $status = [string]$readiness.architectures.$rid.status
        $distributionStatus = $null
        if ($PackageChannel -eq 'validation') {
            $distributionStatus = 'validation-only'
        }
        elseif ($status -eq 'PASS') {
            $distributionStatus = 'public-release-approved'
        }
        elseif ($PackageChannel -eq 'local-preview' -and $AllowReadinessBlocked -and $status -eq 'BLOCKED') {
            $distributionStatus = 'local-only'
        }
        else {
            throw "$rid readiness status is $status. Use -AllowReadinessBlocked only for an explicitly approved local-preview package."
        }

        $assetsPath = if ([string]::IsNullOrWhiteSpace($StageRoot)) {
            Join-Path $work "artifacts-$rid\obj\DesktopSystemMonitor.App\project.assets.json"
        }
        elseif ($stageAssets.ContainsKey($rid)) { $stageAssets[$rid] }
        else { $null }
        $runtimeAssets = Get-StageRuntimeAssets -Stage $stage -Runtime $rid -ProjectAssetsPath $assetsPath
        $runtimeVersion = $runtimeAssets.runtimeVersion
        $runtimeLicense = (Resolve-DesktopSystemMonitorRuntimeLicenseDirectory -LicensesRoot (Join-Path $projectRoot 'licenses') -RuntimeVersion $runtimeVersion).Path
        $stageRuntimeLicense = Join-Path $stage "licenses/dotnet-runtime-$runtimeVersion"
        if (-not (Test-Path -LiteralPath $stageRuntimeLicense -PathType Container) -or
            -not (Test-Path -LiteralPath (Join-Path $stageRuntimeLicense 'LICENSE.txt') -PathType Leaf) -and
            -not (Test-Path -LiteralPath (Join-Path $stageRuntimeLicense 'LICENSE.TXT') -PathType Leaf) -or
            -not (Test-Path -LiteralPath (Join-Path $stageRuntimeLicense 'THIRD-PARTY-NOTICES.txt') -PathType Leaf) -and
            -not (Test-Path -LiteralPath (Join-Path $stageRuntimeLicense 'THIRD-PARTY-NOTICES.TXT') -PathType Leaf)) {
            throw "Runtime license files are missing from the publish stage for ${runtimeVersion}: $stageRuntimeLicense"
        }
        $records = Get-ManifestFileRecords -Stage $stage
        $contentDigest = Get-ContentDigest -Records $records
        $archiveName = "DesktopSystemMonitor-$rid.zip"
        $archivePath = Join-Path $output $archiveName
        $manifestPath = Join-Path $output 'payload-manifest.json'
        if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $manifestPath)) {
            if (-not $Force) { throw "Refusing to replace existing installer payload. Use -Force after inspecting it: $output" }
            if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
        }
        New-DeterministicZip -Source $stage -Destination $archivePath
        $archiveHash = Get-FileSha256 -Path $archivePath
        $repro = Join-Path $work ($archiveName + '.repro')
        New-DeterministicZip -Source $stage -Destination $repro
        if ((Get-FileSha256 -Path $repro) -ne $archiveHash) { throw "Deterministic archive check failed for $rid." }
        if ($PackageChannel -eq 'public-release') {
            $expectedArtifact = [string]$readiness.architectures.$rid.artifactSha256
            if ([string]::IsNullOrWhiteSpace($expectedArtifact) -or $expectedArtifact.ToLowerInvariant() -ne $archiveHash) {
                throw "Public artifact SHA for $rid does not match readiness summary."
            }
        }
        $exeRecord = $records | Where-Object { $_['path'] -eq 'DesktopSystemMonitor.exe' } | Select-Object -First 1
        if ($null -eq $exeRecord) { throw 'Executable record is missing from the file manifest.' }
        $totalBytes = [int64](($records | ForEach-Object { [int64]$_['size'] } | Measure-Object -Sum).Sum)
        $artifacts[$rid] = [ordered]@{
            runtime = $rid
            artifactRole = 'installer-payload'
            archive = $archiveName
            archiveSha256 = $archiveHash
            contentDigest = $contentDigest
            executable = 'DesktopSystemMonitor.exe'
            executableSha256 = $exeRecord['sha256']
            peMachine = ('0x{0:x4}' -f (Get-PeMachine -Path (Join-Path $stage 'DesktopSystemMonitor.exe')))
            runtimeVersion = $runtimeVersion
            resolvedDependencies = [ordered]@{
                runtimePack = $runtimeAssets.runtimePackName
                runtimeVersion = $runtimeAssets.runtimeVersion
                windowsDesktopPack = $runtimeAssets.windowsDesktopPackName
                windowsDesktopVersion = $runtimeAssets.windowsDesktopVersion
                ilLinkPack = $runtimeAssets.ilLinkPackName
                ilLinkVersion = $runtimeAssets.ilLinkVersion
                assetsSha256 = $runtimeAssets.assetsSha256
            }
            totalUncompressedBytes = $totalBytes
            requiredFreeBytes = [int64]($totalBytes * 3 + 16MB)
            managedFiles = $records
            distributionStatus = $distributionStatus
            readinessStatus = $status
            candidateLineageId = $identity.candidateLineageId
            sourceTreeDigest = $identity.sourceTreeDigest
            artifactSha256 = $archiveHash
        }
        if ($CreateReadinessCandidate) {
            $candidateScript = Join-Path $PSScriptRoot 'new-readiness-candidate.ps1'
            & $candidateScript -Runtime $rid -ArtifactPath $archivePath
            if ($LASTEXITCODE -ne 0) { throw "Readiness candidate generation failed for $rid." }
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 2
        productId = 'desktop-system-monitor'
        productName = 'Desktop System Monitor'
        productVersion = $version
        packageChannel = $PackageChannel
        candidateLineageId = $identity.candidateLineageId
        sourceTreeDigest = $identity.sourceTreeDigest
        dependencyFingerprint = $identity.dependencyFingerprint
        layoutContract = 'single-file-v1'
        publishContract = $publishContract
        createdAtUtc = [DateTime]::UtcNow.ToString('o')
        runtimes = $artifacts
    }
    $json = $manifest | ConvertTo-Json -Depth 20
    $json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Installer payload generated: $output" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
