<#
.SYNOPSIS
    Builds deterministic, versioned Desktop System Monitor release archives.

.DESCRIPTION
    Fails closed when readiness, first-party license, architecture, legal files,
    or package contents are incomplete. DryRun validates the contract without
    publishing binaries. Signing is an optional project-local hook executed
    after publish and before archive creation.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Runtime = @('win-x64', 'win-arm64'),
    [string]$Tag,
    [string]$OutputDir = 'dist/release',
    [string]$SigningScript,
    [string]$ExpectedPublisher,
    [switch]$AllowUnsignedPreview,
    [string]$UnsignedWaiverOwner,
    [string]$UnsignedWaiverReason,
    [string]$UnsignedWaiverExpiresAt,
    [switch]$AllowConditional,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PathWithinRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Root)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    if ($pathFull -ne $rootFull -and -not $pathFull.StartsWith(
            $rootFull + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside project root: $pathFull"
    }
    $pathFull
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
        $reader.ReadUInt16()
    }
    finally { $stream.Dispose() }
}

function New-DeterministicZip {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object FullName) {
                $entryName = [IO.Path]::GetRelativePath($Source, $file.FullName).Replace('\', '/')
                if ($entryName.StartsWith('/') -or $entryName.Contains('../')) { throw "Unsafe ZIP entry: $entryName" }
                $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = $file.OpenRead()
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-PackageContents {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ExpectedRuntimeVersion
    )
    $required = @(
        'DesktopSystemMonitor.exe'
        'README.md'
        'LICENSE'
        'THIRD-PARTY-NOTICES.md'
        'licenses/LibreHardwareMonitorLib-0.9.6/LICENSE'
        'licenses/LibreHardwareMonitorLib-0.9.6/THIRD-PARTY-NOTICES.txt'
        'licenses/HidSharp-2.6.4/LICENSE.txt'
        'licenses/Mono.Posix.NETStandard-1.0.0/LICENSE.txt'
    )
    $relativeFiles = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | ForEach-Object {
            [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/')
        })
    $runtimeVersion = $ExpectedRuntimeVersion
    $required += @(
        "licenses/dotnet-runtime-$runtimeVersion/LICENSE.txt"
        "licenses/dotnet-runtime-$runtimeVersion/THIRD-PARTY-NOTICES.txt"
    )
    foreach ($requiredFile in $required) {
        if ($relativeFiles -notcontains $requiredFile) { throw "Required release file is missing: $requiredFile" }
    }
    $forbidden = @('.pdb', '.user', '.suo', '.log', '.bak', '.csv')
    foreach ($file in $relativeFiles) {
        if ($forbidden -contains [IO.Path]::GetExtension($file).ToLowerInvariant() -or
            $file -match '(^|/)(settings\.json|result|backup|obj|src|tests)(/|$)') {
            throw "Forbidden release entry: $file"
        }
    }
}

function Test-ReleaseZip {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$ExpectedRid,
        [Parameter(Mandatory)][string]$ExpectedExecutableHash,
        [Parameter(Mandatory)][string]$ExpectedRuntimeVersion,
        [Parameter(Mandatory)][string]$ExtractionRoot
    )
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if ([IO.Path]::IsPathRooted($entry.FullName) -or
                ($entry.FullName -split '/') -contains '..') {
                throw "Unsafe ZIP entry: $($entry.FullName)"
            }
        }
    }
    finally { $archive.Dispose() }

    [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $ExtractionRoot)
    Assert-PackageContents -Directory $ExtractionRoot -ExpectedRuntimeVersion $ExpectedRuntimeVersion
    $extractedExe = Join-Path $ExtractionRoot 'DesktopSystemMonitor.exe'
    $actualHash = (Get-FileHash -LiteralPath $extractedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedExecutableHash) { throw "Extracted executable hash mismatch for $ExpectedRid." }
    $actualMachine = Get-PeMachine -Path $extractedExe
    $expectedMachine = if ($ExpectedRid -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
    if ($actualMachine -ne $expectedMachine) { throw "Extracted PE architecture mismatch for $ExpectedRid." }
}

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
$rootResolver = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-roots.ps1'
if (-not (Test-Path -LiteralPath $rootResolver -PathType Leaf)) { throw "Root resolver is missing: $rootResolver" }
. $rootResolver
$roots = Resolve-DesktopSystemMonitorRoots -StartPath $PSScriptRoot
$repositoryRoot = $roots.RepositoryRoot
$runtimeAssetsScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-runtime-assets.ps1'
if (-not (Test-Path -LiteralPath $runtimeAssetsScript -PathType Leaf)) { throw "Runtime assets resolver is missing: $runtimeAssetsScript" }
. $runtimeAssetsScript
$identityScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-candidate-identity.ps1'
if (-not (Test-Path -LiteralPath $identityScript -PathType Leaf)) {
    throw "Candidate identity helper is missing: $identityScript"
}
. $identityScript
$identity = Resolve-DesktopSystemMonitorCandidateIdentity -ProjectRoot $projectRoot
$version = $identity.productVersion
if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = "v$version" }
if ($Tag -ne "v$version") { throw "Tag $Tag does not match product version $version." }

$readinessPath = Join-Path $projectRoot 'docs/validation/readiness-summary.json'
$readinessSchemaPath = Join-Path $projectRoot 'docs/validation/readiness-summary.schema.json'
$manifestSchemaPath = Join-Path $projectRoot 'release/release-manifest.schema.json'
foreach ($path in @($readinessPath, $readinessSchemaPath, $manifestSchemaPath, (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release contract file is missing: $path" }
}
$readiness = Get-Content -LiteralPath $readinessPath -Raw | ConvertFrom-Json
if ($readiness.productVersion -ne $version) { throw 'Readiness summary version does not match Directory.Build.props.' }

$sourceDigest = $identity.sourceTreeDigest
$dependencyDigest = $identity.dependencyFingerprint
$sdkVersion = $identity.dotnetSdk
$publishContract = $identity.publishContract
$lineage = $identity.candidateLineageId

if ($DryRun) {
    [pscustomobject]@{
        status = 'DRY_RUN_PASS'
        version = $version
        tag = $Tag
        runtimes = $Runtime
        sourceTreeDigest = $sourceDigest
        dependencyFingerprint = $dependencyDigest
        dotnetSdk = $sdkVersion
        publishContract = $publishContract
        candidateLineageId = $lineage
        readinessStatus = @($Runtime | ForEach-Object { $rid = $_; $readiness.architectures.$rid.status })
    } | ConvertTo-Json -Depth 5
    exit 0
}

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -PathType Leaf)) {
    throw 'First-party LICENSE is required before release packaging.'
}
if ($readiness.candidateLineageId -ne $lineage -or $readiness.sourceTreeDigest -ne $sourceDigest) {
    throw 'Readiness summary does not match the current candidate lineage. Regenerate and rerun affected acceptance tests.'
}
foreach ($rid in $Runtime) {
    $status = $readiness.architectures.$rid.status
    if ($status -ne 'PASS' -and -not ($AllowConditional -and $status -eq 'CONDITIONAL')) {
        throw "$rid readiness status is $status; release packaging is blocked."
    }
}

$unsignedWaiver = $null
if ($SigningScript) {
    if ($AllowUnsignedPreview -or $UnsignedWaiverOwner -or $UnsignedWaiverReason -or $UnsignedWaiverExpiresAt) {
        throw 'Unsigned-preview waiver parameters cannot be combined with SigningScript.'
    }
}
else {
    if (-not $AllowUnsignedPreview) {
        throw 'SigningScript is required unless an explicit unsigned-preview waiver is supplied.'
    }
    if ([string]::IsNullOrWhiteSpace($UnsignedWaiverOwner) -or
        [string]::IsNullOrWhiteSpace($UnsignedWaiverReason) -or
        [string]::IsNullOrWhiteSpace($UnsignedWaiverExpiresAt)) {
        throw 'Unsigned preview requires waiver owner, reason, and expiry.'
    }
    $waiverExpiry = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            $UnsignedWaiverExpiresAt,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$waiverExpiry)) {
        throw 'UnsignedWaiverExpiresAt must be an ISO-8601 date/time.'
    }
    if ($waiverExpiry -le [DateTimeOffset]::UtcNow) {
        throw 'Unsigned-preview waiver has expired.'
    }
    $unsignedWaiver = [ordered]@{
        owner = $UnsignedWaiverOwner
        reason = $UnsignedWaiverReason
        expiresAt = $waiverExpiry.ToUniversalTime().ToString('o')
    }
}

& (Join-Path $projectRoot 'scripts/test-desktop-system-monitor.ps1') -All
if ($LASTEXITCODE -ne 0) { throw 'Quality test gate failed.' }
& (Join-Path $projectRoot 'scripts/coverage-desktop-system-monitor.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Coverage gate failed.' }

$output = Assert-PathWithinRoot -Path (Join-Path $projectRoot $OutputDir) -Root $projectRoot
$work = Assert-PathWithinRoot -Path (Join-Path $projectRoot "dist/.release-work/$([guid]::NewGuid().ToString('N'))") -Root $projectRoot
New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $work -Force | Out-Null
$artifacts = @()
try {
    foreach ($rid in $Runtime) {
        $stage = Join-Path $work $rid
        $artifactsRoot = Join-Path $work "artifacts-$rid"
        & (Join-Path $projectRoot 'scripts/publish-desktop-system-monitor.ps1') `
            -Runtime $rid `
            -OutputDir ([IO.Path]::GetRelativePath($projectRoot, $stage)) `
            -ArtifactsPath ([IO.Path]::GetRelativePath($projectRoot, $artifactsRoot))
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE')
        $runtimeAssets = Resolve-DesktopSystemMonitorRuntimeAssets `
            -ProjectAssetsPath (Join-Path $artifactsRoot 'obj/DesktopSystemMonitor.App/project.assets.json') `
            -Runtime $rid
        $runtimeVersion = $runtimeAssets.runtimeVersion
        $executablePath = Join-Path $stage 'DesktopSystemMonitor.exe'
        $preSignExecutableHash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()

        $signatureStatus = 'unsigned'
        $signatureSubject = $null
        if ($SigningScript) {
            $hook = Assert-PathWithinRoot -Path (Join-Path $projectRoot $SigningScript) -Root $projectRoot
            if ([string]::IsNullOrWhiteSpace($ExpectedPublisher)) { throw 'ExpectedPublisher is required with SigningScript.' }
            & $hook -InputDirectory $stage -ExpectedPublisher $ExpectedPublisher
            if ($LASTEXITCODE -ne 0) { throw "Signing hook failed for $rid." }
            $signature = Get-AuthenticodeSignature -LiteralPath $executablePath
            if ($signature.Status -ne 'Valid' -or
                -not [StringComparer]::OrdinalIgnoreCase.Equals($signature.SignerCertificate.Subject, $ExpectedPublisher)) {
                throw "Signature verification failed for $rid."
            }
            $signatureStatus = 'valid'
            $signatureSubject = $signature.SignerCertificate.Subject
        }

        $packagedExecutableHash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()

        Assert-PackageContents -Directory $stage -ExpectedRuntimeVersion $runtimeVersion
        $machine = Get-PeMachine -Path (Join-Path $stage 'DesktopSystemMonitor.exe')
        $expectedMachine = if ($rid -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
        if ($machine -ne $expectedMachine) { throw "PE architecture mismatch for $rid." }
        $archiveName = "desktop-system-monitor-$version-$rid.zip"
        $archivePath = Join-Path $output $archiveName
        if (Test-Path -LiteralPath $archivePath) { throw "Refusing to replace existing release asset: $archivePath" }
        New-DeterministicZip -Source $stage -Destination $archivePath
        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $reproArchive = Join-Path $work "$archiveName.repro"
        New-DeterministicZip -Source $stage -Destination $reproArchive
        $reproHash = (Get-FileHash -LiteralPath $reproArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($reproHash -ne $archiveHash) { throw "Deterministic repackage check failed for $rid." }
        $extractRoot = Join-Path $work "extract-$rid"
        New-Item -ItemType Directory -Path $extractRoot | Out-Null
        Test-ReleaseZip -ArchivePath $archivePath -ExpectedRid $rid -ExpectedExecutableHash $packagedExecutableHash `
            -ExpectedRuntimeVersion $runtimeVersion -ExtractionRoot $extractRoot
        $artifacts += [ordered]@{
            rid = $rid
            fileName = $archiveName
            bytes = (Get-Item -LiteralPath $archivePath).Length
            sha256 = $archiveHash
            peMachine = ('0x{0:x4}' -f $machine)
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
            preSignExecutableSha256 = $preSignExecutableHash
            packagedExecutableSha256 = $packagedExecutableHash
            signatureStatus = $signatureStatus
            signatureSubject = $signatureSubject
            unsignedWaiver = $unsignedWaiver
            legalFiles = @(
                'LICENSE'
                'THIRD-PARTY-NOTICES.md'
                'licenses/LibreHardwareMonitorLib-0.9.6/LICENSE'
                'licenses/LibreHardwareMonitorLib-0.9.6/THIRD-PARTY-NOTICES.txt'
                'licenses/HidSharp-2.6.4/LICENSE.txt'
                'licenses/Mono.Posix.NETStandard-1.0.0/LICENSE.txt'
                "licenses/dotnet-runtime-$runtimeVersion/LICENSE.txt"
                "licenses/dotnet-runtime-$runtimeVersion/THIRD-PARTY-NOTICES.txt"
            )
        }
    }

    $resolvedRuntimeVersions = @($artifacts | ForEach-Object { $_.resolvedDependencies.runtimeVersion } | Sort-Object -Unique)
    $resolvedIlLinkVersions = @($artifacts | ForEach-Object { $_.resolvedDependencies.ilLinkVersion } | Sort-Object -Unique)
    if ($resolvedRuntimeVersions.Count -ne 1 -or $resolvedIlLinkVersions.Count -ne 1) {
        throw "Selected RID publish results do not share one runtime and ILLink version: runtime=$($resolvedRuntimeVersions -join ',') ilLink=$($resolvedIlLinkVersions -join ',')"
    }

    # This workspace is validated as a filesystem workspace; Git metadata is
    # retained as a root exception but is not queried by the release script.
    $commit = $null
    $manifest = [ordered]@{
        schemaVersion = 1
        product = [ordered]@{ name = 'Desktop System Monitor'; version = $version; tag = $Tag }
        source = [ordered]@{ commit = $commit; treeDigest = $sourceDigest; candidateLineageId = $lineage }
        toolchain = [ordered]@{
            dotnetSdk = $sdkVersion
            dependencyFingerprint = $dependencyDigest
            publishContract = $publishContract
            createdAtUtc = [DateTime]::UtcNow.ToString('o')
        }
        resolvedDependencySummary = [ordered]@{
            runtimeVersions = $resolvedRuntimeVersions
            ilLinkVersions = $resolvedIlLinkVersions
        }
        readiness = [ordered]@{ path = 'docs/validation/readiness-summary.json'; sha256 = (Get-FileHash -LiteralPath $readinessPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        artifacts = $artifacts
    }
    $manifestName = "desktop-system-monitor-$version-release-manifest.json"
    $manifestJson = $manifest | ConvertTo-Json -Depth 10
    if (-not ($manifestJson | Test-Json -SchemaFile $manifestSchemaPath)) {
        throw 'Generated release manifest does not match release-manifest.schema.json.'
    }
    $manifestJson | Set-Content -LiteralPath (Join-Path $output $manifestName) -Encoding utf8NoBOM
    $checksumLines = @($artifacts | ForEach-Object { "$($_.sha256)  $($_.fileName)" })
    $checksumLines += "$((Get-FileHash -LiteralPath (Join-Path $output $manifestName) -Algorithm SHA256).Hash.ToLowerInvariant())  $manifestName"
    $checksumLines | Set-Content -LiteralPath (Join-Path $output "desktop-system-monitor-$version-checksums.txt") -Encoding ascii
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

Write-Host "Release candidate generated in $output" -ForegroundColor Green
