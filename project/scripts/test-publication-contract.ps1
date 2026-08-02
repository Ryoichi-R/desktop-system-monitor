[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rootResolver = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-roots.ps1'
if (-not (Test-Path -LiteralPath $rootResolver -PathType Leaf)) { throw "Root resolver is missing: $rootResolver" }
. $rootResolver
$roots = Resolve-DesktopSystemMonitorRoots -StartPath $PSScriptRoot
$repositoryRoot = $roots.RepositoryRoot
$projectRoot = $roots.ProjectRoot
$failures = [Collections.Generic.List[string]]::new()
$buildPathsScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-build-paths.ps1'
. $buildPathsScript
$identityScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-candidate-identity.ps1'
. $identityScript
$projectFile = Join-Path $projectRoot 'src/DesktopSystemMonitor.App/DesktopSystemMonitor.App.csproj'
$publishScript = Join-Path $PSScriptRoot 'publish-desktop-system-monitor.ps1'
$publishContractScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-publish-contract.ps1'
$releaseScript = Join-Path $PSScriptRoot 'new-desktop-system-monitor-release.ps1'
$runtimeAssetsScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-runtime-assets.ps1'
foreach ($requiredScript in @($projectFile, $publishScript, $publishContractScript, $releaseScript, $runtimeAssetsScript)) {
    if (-not (Test-Path -LiteralPath $requiredScript -PathType Leaf)) {
        $failures.Add("Single-file contract input is missing: $requiredScript")
    }
}
if (Test-Path -LiteralPath $projectFile -PathType Leaf) {
    $projectText = Get-Content -LiteralPath $projectFile -Raw
    foreach ($property in @(
            '<PublishSingleFile>true</PublishSingleFile>'
            '<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>'
            '<PublishTrimmed>false</PublishTrimmed>'
            '<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>'
            '<IncludeAllContentForSelfExtract>false</IncludeAllContentForSelfExtract>'
        )) {
        if ($projectText -notmatch [regex]::Escape($property)) {
            $failures.Add("App project is missing single-file contract property: $property")
        }
    }
}
if (Test-Path -LiteralPath $publishScript -PathType Leaf) {
    $publishText = Get-Content -LiteralPath $publishScript -Raw
    $publishContractText = if (Test-Path -LiteralPath $publishContractScript -PathType Leaf) { Get-Content -LiteralPath $publishContractScript -Raw } else { '' }
    foreach ($property in @(
            '-p:PublishSingleFile=true'
            '-p:IncludeNativeLibrariesForSelfExtract=true'
            '-p:PublishTrimmed=false'
            '-p:EnableCompressionInSingleFile=false'
            '-p:IncludeAllContentForSelfExtract=false'
            '-p:ContinuousIntegrationBuild=true'
            '--artifacts-path'
        )) {
        if ($publishText -notmatch [regex]::Escape($property) -and $publishContractText -notmatch [regex]::Escape($property)) {
            $failures.Add("Publish script is missing single-file contract argument: $property")
        }
    }
    if ($publishText -match 'hostfxr\.dll') { $failures.Add('Publish script must not resolve runtime version from hostfxr.dll.') }
}
$expectedPublishContract = 'Release|self-contained=true|single-file=true|native-self-extract=true|trimmed=false|compression=false|include-all-content=false|ci-build=true|debug=None|debug-symbols=false'

$allowedRootEntries = @(
    '.git'
    '.github'
    '.gitignore'
    'LICENSE'
    'README.md'
    'project'
    'ここから開始 - Desktop System Monitorを導入・更新.bat'
)
$actualRootEntries = @(Get-ChildItem -LiteralPath $repositoryRoot -Force | ForEach-Object Name)
foreach ($requiredRootEntry in $allowedRootEntries) {
    if ($actualRootEntries -notcontains $requiredRootEntry) {
        $failures.Add("Required root entry is missing: $requiredRootEntry")
    }
}
foreach ($unexpectedRootEntry in @($actualRootEntries | Where-Object { $allowedRootEntries -notcontains $_ })) {
    $failures.Add("Unexpected root entry outside the approved exceptions: $unexpectedRootEntry")
}

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
        $_.FullName -notmatch '[\\/](bin|obj|dist|coverage|installer[\\/]payload|\.test-deps|_internal[\\/]migration)[\\/]'
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
    if ($x64Contract.publishContract -ne $expectedPublishContract) {
        $failures.Add("Candidate publish contract is not the single-file contract: $($x64Contract.publishContract)")
    }
    if (Test-Path -LiteralPath $releaseScript -PathType Leaf) {
        $releaseText = Get-Content -LiteralPath $releaseScript -Raw
        $dryRunIndex = $releaseText.IndexOf('if ($DryRun)', [StringComparison]::Ordinal)
        $publishIndex = $releaseText.IndexOf('publish-desktop-system-monitor.ps1', [StringComparison]::Ordinal)
        if ($dryRunIndex -lt 0 -or $publishIndex -lt 0 -or $dryRunIndex -gt $publishIndex) {
            $failures.Add('Release DryRun must complete before any publish invocation.')
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
    [pscustomobject]@{ Root = $repositoryRoot; RelativePath = 'LICENSE' }
    [pscustomobject]@{ Root = $projectRoot; RelativePath = 'THIRD-PARTY-NOTICES.md' }
    [pscustomobject]@{ Root = $projectRoot; RelativePath = 'licenses/LibreHardwareMonitorLib-0.9.6/LICENSE' }
    [pscustomobject]@{ Root = $projectRoot; RelativePath = 'licenses/LibreHardwareMonitorLib-0.9.6/THIRD-PARTY-NOTICES.txt' }
    [pscustomobject]@{ Root = $projectRoot; RelativePath = 'licenses/HidSharp-2.6.4/LICENSE.txt' }
    [pscustomobject]@{ Root = $projectRoot; RelativePath = 'licenses/Mono.Posix.NETStandard-1.0.0/LICENSE.txt' }
)
foreach ($legalFile in $requiredLegalFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $legalFile.Root $legalFile.RelativePath) -PathType Leaf)) {
        $failures.Add("Required legal file is missing: $($legalFile.Root)\$($legalFile.RelativePath)")
    }
}
$runtimeLicenseDirectories = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'licenses') -Directory -Filter 'dotnet-runtime-*' -ErrorAction SilentlyContinue)
if ($runtimeLicenseDirectories.Count -eq 0) {
    $failures.Add('At least one dotnet-runtime-* license directory is required.')
}
else {
    foreach ($runtimeDirectory in $runtimeLicenseDirectories) {
        $runtimeFiles = @(Get-ChildItem -LiteralPath $runtimeDirectory.FullName -File -ErrorAction SilentlyContinue | ForEach-Object Name)
        if (-not ($runtimeFiles -contains 'LICENSE.txt' -or $runtimeFiles -contains 'LICENSE.TXT')) {
            $failures.Add("Runtime license file is missing: $($runtimeDirectory.Name)/LICENSE.txt")
        }
        if (-not ($runtimeFiles -contains 'THIRD-PARTY-NOTICES.txt' -or $runtimeFiles -contains 'THIRD-PARTY-NOTICES.TXT')) {
            $failures.Add("Runtime notice file is missing: $($runtimeDirectory.Name)/THIRD-PARTY-NOTICES.txt")
        }
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

foreach ($markdown in Get-ChildItem -LiteralPath $repositoryRoot -Filter '*.md' -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](\.git|bin|obj|dist|coverage|\.test-deps|_internal[\\/]migration)[\\/]'
    }) {
    $text = Get-Content -LiteralPath $markdown.FullName -Raw
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)')) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        if ($target -match '^(https?://|mailto:|#)' -or $target.Contains('__')) { continue }
        $pathPart = $target.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $markdown.DirectoryName $pathPart))
        if (-not (Test-Path -LiteralPath $resolved)) {
            $failures.Add("Broken relative link: $([IO.Path]::GetRelativePath($repositoryRoot, $markdown.FullName)) -> $target")
        }
    }
}

try {
    $textExtensions = @('.md', '.markdown', '.txt', '.ps1', '.psm1', '.psd1', '.bat', '.cmd', '.json', '.yml', '.yaml', '.xml', '.props', '.targets', '.slnx', '.cs', '.csproj')
    $searchPaths = @(Get-ChildItem -LiteralPath $repositoryRoot -File -Recurse | Where-Object {
            $_.Extension.ToLowerInvariant() -in $textExtensions -and
            $_.FullName -notmatch '[\\/](\.git|bin|obj|dist|coverage|installer[\\/]payload|\.test-deps|_internal[\\/]migration)[\\/]'
        } | ForEach-Object FullName)
    $forbiddenMatches = @(
        if ($searchPaths.Count -gt 0) {
            Select-String -LiteralPath $searchPaths -Pattern 'C:\\coding|[A-Za-z]:\\Users\\' -AllMatches -ErrorAction Stop
        }
    )

    if ($forbiddenMatches.Count -gt 0) {
        $forbiddenText = $forbiddenMatches | ForEach-Object {
            '{0}:{1}' -f [IO.Path]::GetRelativePath($repositoryRoot, $_.Path), $_.LineNumber
        }
        $failures.Add("PC-specific absolute path found:`n$($forbiddenText -join "`n")")
    }
}
catch {
    $failures.Add("Absolute-path scan failed: $($_.Exception.Message)")
}

$workflowText = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/desktop-system-monitor-ci.yml'), (Join-Path $repositoryRoot '.github/workflows/release-candidate.yml') -Raw) -join "`n"
foreach ($requiredAction in @('actions/checkout@v7', 'actions/setup-dotnet@v6')) {
    if ($workflowText -notmatch [regex]::Escape($requiredAction)) { $failures.Add("Workflow action missing: $requiredAction") }
}
if ($workflowText -match '(?m)global-json-file:\s*global\.json\s*$' -or
    $workflowText -notmatch '(?m)global-json-file:\s*project/global\.json\s*$') {
    $failures.Add('Workflow setup-dotnet must resolve project/global.json from the repository root.')
}
if ($workflowText -match '(?m)run:\s*pwsh\s+scripts/' -or
    $workflowText -match '(?m)-File\s+scripts/') {
    $failures.Add('Workflow run steps must use project/scripts after migration.')
}
if ((Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release-candidate.yml') -Raw) -match 'upload-artifact|dist/release|SigningScript|ExpectedPublisher') {
    $failures.Add('Source-only preview workflow must not build, sign, or upload binary release assets.')
}

$launcherPath = Join-Path $repositoryRoot 'ここから開始 - Desktop System Monitorを導入・更新.bat'
$installerPath = Join-Path $projectRoot 'installer/Install-DesktopSystemMonitor.ps1'
$installerSchemaPath = Join-Path $projectRoot 'installer/payload/payload-manifest.schema.json'
$payloadPromotionPath = Join-Path $projectRoot 'scripts/promote-desktop-system-monitor-installer-payload.ps1'
$rebuildPath = Join-Path $projectRoot 'scripts/rebuild-desktop-system-monitor.ps1'
foreach ($requiredInstallerPath in @($launcherPath, $installerPath, $installerSchemaPath)) {
    if (-not (Test-Path -LiteralPath $requiredInstallerPath -PathType Leaf)) {
        $failures.Add("Installer contract file is missing: $requiredInstallerPath")
    }
}
foreach ($requiredPayloadPath in @($payloadPromotionPath, $rebuildPath)) {
    if (-not (Test-Path -LiteralPath $requiredPayloadPath -PathType Leaf)) {
        $failures.Add("Payload handoff contract file is missing: $requiredPayloadPath")
    }
}
if (Test-Path -LiteralPath $launcherPath -PathType Leaf) {
    $launcherText = Get-Content -LiteralPath $launcherPath -Raw
    if ($launcherText -notmatch 'DisableDelayedExpansion' -or $launcherText -notmatch '%~dp0') { $failures.Add('Root launcher must be anchored to %~dp0 with delayed expansion disabled.') }
    foreach ($requiredLauncherToken in @('rebuild-desktop-system-monitor.ps1', 'Install-DesktopSystemMonitor.ps1', 'PROCESSOR_ARCHITEW6432', 'win-x64', 'win-arm64')) {
        if ($launcherText -notmatch [regex]::Escape($requiredLauncherToken)) { $failures.Add("Root launcher rebuild/install handoff is missing: $requiredLauncherToken") }
    }
    if ($launcherText -match 'ConvertFrom-Json|distributionStatus') { $failures.Add('Root launcher must not parse manifest JSON or distribution status.') }
    if ($launcherText -match 'DSM_PAYLOAD|payload.*\.zip') { $failures.Add('Root launcher must not perform an unprotected payload existence check.') }
}
if (Test-Path -LiteralPath (Join-Path $projectRoot 'rebuild-desktop-system-monitor-x64.bat')) {
    $failures.Add('Legacy x64 rebuild BAT must not remain under project; use the root launcher.')
}
if (Test-Path -LiteralPath $installerPath -PathType Leaf) {
    $installerText = Get-Content -LiteralPath $installerPath -Raw
    foreach ($requiredInstallerToken in @('Open-InstallerPayloadLock', 'schemaVersion -ne 2', 'Copy-Item -LiteralPath $archive', 'single-file-v1')) {
        if ($installerText -notmatch [regex]::Escape($requiredInstallerToken)) { $failures.Add("Installer handoff guard is missing: $requiredInstallerToken") }
    }
}
if (Test-Path -LiteralPath $rebuildPath -PathType Leaf) {
    $rebuildText = Get-Content -LiteralPath $rebuildPath -Raw
    foreach ($requiredRebuildToken in @('PortableOnly', 'Build installer payload staging', 'payload-promoted', 'Move-DesktopSystemMonitorPayloadIntoPlace')) {
        if ($rebuildText -notmatch [regex]::Escape($requiredRebuildToken)) { $failures.Add("Rebuild payload handoff step is missing: $requiredRebuildToken") }
    }
}
if (Test-Path -LiteralPath $installerSchemaPath -PathType Leaf) {
    try { Get-Content -LiteralPath $installerSchemaPath -Raw | ConvertFrom-Json | Out-Null }
    catch { $failures.Add("Installer payload schema is invalid: $installerSchemaPath") }
    $schemaText = Get-Content -LiteralPath $installerSchemaPath -Raw
    if ($schemaText -notmatch '"schemaVersion"\s*:\s*\{\s*"const"\s*:\s*2') { $failures.Add('Installer payload schema must be v2.') }
    if ($schemaText -notmatch 'single-file-v1') { $failures.Add('Installer payload schema must declare single-file-v1 layout.') }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host 'Publication contract checks passed.' -ForegroundColor Green
exit 0
