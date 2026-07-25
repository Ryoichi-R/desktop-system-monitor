[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$failures = [Collections.Generic.List[string]]::new()
$buildPathsScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-build-paths.ps1'
. $buildPathsScript
$identityScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-candidate-identity.ps1'
. $identityScript

$defaultBuildPaths = Resolve-DesktopSystemMonitorBuildPaths -ProjectRoot $projectRoot -OutputRoot $null
$expectedProjectDist = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ($defaultBuildPaths.UsesExternalRoot -or $defaultBuildPaths.ManagedRoot -ine $expectedProjectDist) {
    $failures.Add("Default rebuild output must remain project dist: $($defaultBuildPaths.ManagedRoot)")
}

$explicitProjectPaths = Resolve-DesktopSystemMonitorBuildPaths -ProjectRoot $projectRoot -OutputRoot $projectRoot
if ($explicitProjectPaths.UsesExternalRoot -or $explicitProjectPaths.ManagedRoot -ine $expectedProjectDist) {
    $failures.Add("Explicit project root must use project dist: $($explicitProjectPaths.ManagedRoot)")
}

$filesystemRoot = [IO.Path]::GetPathRoot($projectRoot)
$rootBuildPaths = Resolve-DesktopSystemMonitorBuildPaths -ProjectRoot $projectRoot -OutputRoot $filesystemRoot
$expectedRootBuilds = [IO.Path]::GetFullPath((Join-Path $filesystemRoot 'DesktopSystemMonitorBuilds')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $rootBuildPaths.UsesExternalRoot -or $rootBuildPaths.ManagedRoot -ine $expectedRootBuilds) {
    $failures.Add("Filesystem-root output must use DesktopSystemMonitorBuilds: $($rootBuildPaths.ManagedRoot)")
}

foreach ($json in Get-ChildItem -LiteralPath $projectRoot -Filter '*.json' -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|dist|coverage)[\\/]'
    }) {
    try { Get-Content -LiteralPath $json.FullName -Raw | ConvertFrom-Json | Out-Null }
    catch { $failures.Add("Invalid JSON: $([IO.Path]::GetRelativePath($projectRoot, $json.FullName))") }
}

$summary = Get-Content -LiteralPath (Join-Path $projectRoot 'docs/validation/readiness-summary.json') -Raw
if (-not ($summary | Test-Json -SchemaFile (Join-Path $projectRoot 'docs/validation/readiness-summary.schema.json'))) {
    $failures.Add('readiness-summary.json does not match its schema.')
}

$requiredAcceptanceDocuments = @(
    'docs/validation/DSM-1-metric-acceptance.md'
    'docs/validation/DSM-1-metric-acceptance-checklist.md'
    'docs/validation/DSM-2-endurance-acceptance.md'
    'docs/validation/DSM-2-endurance-acceptance-checklist.md'
    'docs/validation/DSM-4-arm64-acceptance.md'
    'docs/validation/DSM-4-arm64-acceptance-checklist.md'
)
foreach ($relativeAcceptancePath in $requiredAcceptanceDocuments) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relativeAcceptancePath) -PathType Leaf)) {
        $failures.Add("Required acceptance document is missing: $relativeAcceptancePath")
    }
}

$releaseScript = Join-Path $PSScriptRoot 'new-desktop-system-monitor-release.ps1'
try {
    $x64Contract = (& $releaseScript -Runtime win-x64 -DryRun | Out-String) | ConvertFrom-Json
    $arm64Contract = (& $releaseScript -Runtime win-arm64 -DryRun | Out-String) | ConvertFrom-Json
    $dualContract = (& $releaseScript -Runtime win-x64, win-arm64 -DryRun | Out-String) | ConvertFrom-Json
    $contracts = @($x64Contract, $arm64Contract, $dualContract)

    if (@($contracts.candidateLineageId | Sort-Object -Unique).Count -ne 1) {
        $failures.Add('Candidate lineage must be independent of the selected RID set.')
    }
    if (@($contracts.sourceTreeDigest | Sort-Object -Unique).Count -ne 1) {
        $failures.Add('Source tree digest must be independent of the selected RID set.')
    }
    foreach ($property in @('candidateLineageId', 'sourceTreeDigest', 'dependencyFingerprint')) {
        if ($x64Contract.$property -notmatch '^[a-f0-9]{64}$') {
            $failures.Add("Release dry-run returned an invalid $property.")
        }
    }
    foreach ($property in @('dotnetSdk', 'publishContract')) {
        if ([string]::IsNullOrWhiteSpace($x64Contract.$property)) {
            $failures.Add("Release dry-run returned an empty $property.")
        }
    }

    $reconstructedLineage = New-DesktopSystemMonitorCandidateLineageId `
        -SourceTreeDigest $x64Contract.sourceTreeDigest `
        -DependencyFingerprint $x64Contract.dependencyFingerprint `
        -DotNetSdk $x64Contract.dotnetSdk `
        -ProductVersion $x64Contract.version `
        -PublishContract $x64Contract.publishContract
    if ($reconstructedLineage -ne $x64Contract.candidateLineageId) {
        $failures.Add('Release dry-run lineage does not match the documented identity inputs.')
    }

    function Get-DifferentFingerprint {
        param([Parameter(Mandatory)][string]$Value)
        $replacement = if ($Value[0] -eq '0') { '1' } else { '0' }
        $replacement + $Value.Substring(1)
    }

    $negativeCases = @(
        [pscustomobject]@{
            Name = 'source tree digest'
            SourceTreeDigest = Get-DifferentFingerprint -Value $x64Contract.sourceTreeDigest
            DependencyFingerprint = $x64Contract.dependencyFingerprint
            DotNetSdk = $x64Contract.dotnetSdk
            ProductVersion = $x64Contract.version
            PublishContract = $x64Contract.publishContract
        }
        [pscustomobject]@{
            Name = 'dependency fingerprint'
            SourceTreeDigest = $x64Contract.sourceTreeDigest
            DependencyFingerprint = Get-DifferentFingerprint -Value $x64Contract.dependencyFingerprint
            DotNetSdk = $x64Contract.dotnetSdk
            ProductVersion = $x64Contract.version
            PublishContract = $x64Contract.publishContract
        }
        [pscustomobject]@{
            Name = '.NET SDK'
            SourceTreeDigest = $x64Contract.sourceTreeDigest
            DependencyFingerprint = $x64Contract.dependencyFingerprint
            DotNetSdk = "$($x64Contract.dotnetSdk)-contract-test"
            ProductVersion = $x64Contract.version
            PublishContract = $x64Contract.publishContract
        }
        [pscustomobject]@{
            Name = 'product version'
            SourceTreeDigest = $x64Contract.sourceTreeDigest
            DependencyFingerprint = $x64Contract.dependencyFingerprint
            DotNetSdk = $x64Contract.dotnetSdk
            ProductVersion = "$($x64Contract.version)-contract-test"
            PublishContract = $x64Contract.publishContract
        }
        [pscustomobject]@{
            Name = 'publish contract'
            SourceTreeDigest = $x64Contract.sourceTreeDigest
            DependencyFingerprint = $x64Contract.dependencyFingerprint
            DotNetSdk = $x64Contract.dotnetSdk
            ProductVersion = $x64Contract.version
            PublishContract = "$($x64Contract.publishContract)|contract-test"
        }
    )
    foreach ($case in $negativeCases) {
        $changedLineage = New-DesktopSystemMonitorCandidateLineageId `
            -SourceTreeDigest $case.SourceTreeDigest `
            -DependencyFingerprint $case.DependencyFingerprint `
            -DotNetSdk $case.DotNetSdk `
            -ProductVersion $case.ProductVersion `
            -PublishContract $case.PublishContract
        if ($changedLineage -eq $x64Contract.candidateLineageId) {
            $failures.Add("Candidate lineage did not change with changed $($case.Name).")
        }
    }
}
catch {
    $failures.Add("Candidate lineage contract check failed: $($_.Exception.Message)")
}

$repositoryHandoff = Get-Content -LiteralPath (Join-Path $projectRoot 'release/repository-handoff.json') -Raw
if (-not ($repositoryHandoff | Test-Json -SchemaFile (Join-Path $projectRoot 'release/repository-handoff.schema.json'))) {
    $failures.Add('repository-handoff.json does not match its schema.')
}

$requiredLegalFiles = @(
    'LICENSE'
    'THIRD-PARTY-NOTICES.md'
    'licenses/LibreHardwareMonitorLib-0.9.6/LICENSE'
    'licenses/LibreHardwareMonitorLib-0.9.6/THIRD-PARTY-NOTICES.txt'
    'licenses/HidSharp-2.6.4/LICENSE.txt'
    'licenses/Mono.Posix.NETStandard-1.0.0/LICENSE.txt'
    'licenses/dotnet-runtime-10.0.3/LICENSE.txt'
    'licenses/dotnet-runtime-10.0.3/THIRD-PARTY-NOTICES.txt'
)
foreach ($relativeLegalPath in $requiredLegalFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relativeLegalPath) -PathType Leaf)) {
        $failures.Add("Required legal file is missing: $relativeLegalPath")
    }
}
$thirdPartyNoticeText = Get-Content -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Raw
foreach ($component in @(
        'LibreHardwareMonitorLib',
        'BlackSharp.Core',
        'DiskInfoToolkit',
        'RAMSPDToolkit-NDD',
        'HidSharp',
        'Mono.Posix.NETStandard',
        'Microsoft .NET runtime'
    )) {
    if ($thirdPartyNoticeText -notmatch [regex]::Escape($component)) {
        $failures.Add("Third-party notice is missing component: $component")
    }
}

foreach ($markdown in Get-ChildItem -LiteralPath $projectRoot -Filter '*.md' -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|dist|coverage)[\\/]'
    }) {
    $text = Get-Content -LiteralPath $markdown.FullName -Raw
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)')) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        if ($target -match '^(https?://|mailto:|#)' -or $target.Contains('__')) { continue }
        $pathPart = $target.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $markdown.DirectoryName $pathPart))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $failures.Add("Broken relative link: $([IO.Path]::GetRelativePath($projectRoot, $markdown.FullName)) -> $target")
        }
    }
}

try {
    $searchPaths = @(Get-ChildItem -LiteralPath $projectRoot -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](\.git|bin|obj|dist|coverage)[\\/]'
        } | ForEach-Object FullName)
    $forbiddenMatches = @(
        if ($searchPaths.Count -gt 0) {
            Select-String -LiteralPath $searchPaths -Pattern 'C:\\coding|[A-Za-z]:\\Users\\' -AllMatches -ErrorAction Stop
        }
    )

    if ($forbiddenMatches.Count -gt 0) {
        $forbiddenText = $forbiddenMatches | ForEach-Object {
            '{0}:{1}' -f [IO.Path]::GetRelativePath($projectRoot, $_.Path), $_.LineNumber
        }
        $failures.Add("PC-specific absolute path found:`n$($forbiddenText -join "`n")")
    }
}
catch {
    $failures.Add("Absolute-path scan failed: $($_.Exception.Message)")
}

$workflowText = (Get-Content -LiteralPath (Join-Path $projectRoot '.github/workflows/desktop-system-monitor-ci.yml'), (Join-Path $projectRoot '.github/workflows/release-candidate.yml') -Raw) -join "`n"
foreach ($requiredAction in @('actions/checkout@v7', 'actions/setup-dotnet@v6')) {
    if ($workflowText -notmatch [regex]::Escape($requiredAction)) { $failures.Add("Workflow action missing: $requiredAction") }
}
if ((Get-Content -LiteralPath (Join-Path $projectRoot '.github/workflows/release-candidate.yml') -Raw) -match 'upload-artifact|dist/release|SigningScript|ExpectedPublisher') {
    $failures.Add('Source-only preview workflow must not build, sign, or upload binary release assets.')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host 'Publication contract checks passed.' -ForegroundColor Green
exit 0
