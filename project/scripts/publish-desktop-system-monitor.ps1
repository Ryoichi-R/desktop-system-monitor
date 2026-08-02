<#
.SYNOPSIS
    Desktop System Monitor の portable self-contained 配布物を生成する。

.PARAMETER Runtime
    Windows RID。既定は win-x64。win-arm64 は実機検証後に明示指定する。

.PARAMETER OutputDir
    プロジェクトルートからの出力先。省略時は
    dist/desktop-system-monitor-<Runtime>。

.PARAMETER DistRoot
    OutputDirに指定した出力先を格納できる管理ディレクトリ。省略時は
    プロジェクトルートのdist。外部出力時はrebuild scriptから
    DesktopSystemMonitorBuildsを指定する。

.PARAMETER ArtifactsPath
    dotnet SDKのbin/obj生成先。指定時はdistまたはDesktopSystemMonitorBuilds配下の
    新規空ディレクトリを使用する。省略時はSDK既定のproject objを使用する。
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputDir,
    [string]$DistRoot,
    [string]$ArtifactsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    if ($pathFull -ne $rootFull -and
        -not $pathFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to publish outside the allowed output root: $pathFull"
    }
    return $pathFull
}

function Assert-DirectoryEmpty {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Purpose)

    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
            throw "$Purpose exists but is not a directory: $Path"
        }
        if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "$Purpose cannot be a symbolic link or junction: $Path"
        }
        $items = @(Get-ChildItem -LiteralPath $Path -Force)
        if ($items.Count -ne 0) {
            throw "$Purpose must be empty; refusing to delete or overwrite existing contents: $Path"
        }
    }
    else {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Assert-ManagedArtifactsPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ProjectRoot
    )

    $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $projectDist = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'dist')).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $underProjectDist = $full.StartsWith($projectDist + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    $externalRootPattern = [IO.Path]::DirectorySeparatorChar + 'DesktopSystemMonitorBuilds' + [IO.Path]::DirectorySeparatorChar
    $underExternalBuilds = $full.IndexOf($externalRootPattern, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $full.EndsWith([IO.Path]::DirectorySeparatorChar + 'DesktopSystemMonitorBuilds', [StringComparison]::OrdinalIgnoreCase)
    if (-not $underProjectDist -and -not $underExternalBuilds) {
        throw "ArtifactsPath must be under project dist or DesktopSystemMonitorBuilds: $full"
    }
    Assert-DirectoryEmpty -Path $full -Purpose 'ArtifactsPath'
    return $full
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
$publishContractScript = Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-publish-contract.ps1'
if (-not (Test-Path -LiteralPath $publishContractScript -PathType Leaf)) { throw "Publish contract helper is missing: $publishContractScript" }
. $publishContractScript
# The shared helper emits these exact contract arguments:
# -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=false
# -p:IncludeAllContentForSelfExtract=false -p:ContinuousIntegrationBuild=true
# --artifacts-path <managed-artifacts-root>
$projectDistRoot = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'dist') -Root $projectRoot
$distRoot = if ([string]::IsNullOrWhiteSpace($DistRoot)) {
    $projectDistRoot
}
else {
    $requestedDistRoot = if ([IO.Path]::IsPathFullyQualified($DistRoot)) {
        $DistRoot
    }
    else {
        Join-Path $projectRoot $DistRoot
    }
    [IO.Path]::GetFullPath($requestedDistRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
}
$distRootLeaf = Split-Path -Leaf $distRoot
if ($distRootLeaf -ine 'dist' -and $distRootLeaf -ine 'DesktopSystemMonitorBuilds') {
    throw "DistRoot must be a directory named 'dist' or 'DesktopSystemMonitorBuilds': $distRoot"
}
if (Test-Path -LiteralPath $distRoot) {
    if (-not (Test-Path -LiteralPath $distRoot -PathType Container)) {
        throw "DistRoot exists but is not a directory: $distRoot"
    }
    if ((Get-Item -LiteralPath $distRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "DistRoot cannot be a symbolic link or junction: $distRoot"
    }
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $requestedDestination = Join-Path $distRoot "desktop-system-monitor-$Runtime"
}
else {
    $requestedDestination = if ([IO.Path]::IsPathFullyQualified($OutputDir)) {
        $OutputDir
    }
    else {
        Join-Path $projectRoot $OutputDir
    }
}
$destination = Assert-PathWithinRoot -Path $requestedDestination -Root $distRoot
if ($destination -eq $distRoot) {
    throw 'Specify an architecture-specific subdirectory under the allowed dist directory.'
}
if (Test-Path -LiteralPath $destination) {
    if (-not (Test-Path -LiteralPath $destination -PathType Container)) {
        throw "OutputDir exists but is not a directory: $destination"
    }
    if ((Get-Item -LiteralPath $destination -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "OutputDir cannot be a symbolic link or junction: $destination"
    }
}

$project = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'src/DesktopSystemMonitor.App/DesktopSystemMonitor.App.csproj') -Root $projectRoot
$readme = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'README.md') -Root $projectRoot
$projectLicense = Assert-PathWithinRoot -Path (Join-Path $repositoryRoot 'LICENSE') -Root $repositoryRoot
$notices = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Root $projectRoot
$legalDirectoryNames = @(
    'LibreHardwareMonitorLib-0.9.6'
    'HidSharp-2.6.4'
    'Mono.Posix.NETStandard-1.0.0'
)
$legalSources = @($legalDirectoryNames | ForEach-Object {
        $source = Assert-PathWithinRoot -Path (Join-Path $projectRoot "licenses/$_") -Root $projectRoot
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            throw "Required legal directory is missing: $source"
        }
        $source
    })
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
Assert-DirectoryEmpty -Path $destination -Purpose 'OutputDir'
$artifactsFull = $null
if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $requestedArtifacts = if ([IO.Path]::IsPathFullyQualified($ArtifactsPath)) {
        $ArtifactsPath
    }
    else {
        Join-Path $projectRoot $ArtifactsPath
    }
    $artifactsFull = Assert-ManagedArtifactsPath -Path $requestedArtifacts -ProjectRoot $projectRoot
}

$publishArgs = Get-DesktopSystemMonitorPublishArguments `
    -Project $project `
    -Runtime $Runtime `
    -OutputDir $destination `
    -ArtifactsPath $artifactsFull
& dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$assetsPath = if ($null -ne $artifactsFull) {
    Join-Path $artifactsFull 'obj/DesktopSystemMonitor.App/project.assets.json'
}
else {
    Join-Path (Split-Path -Parent $project) 'obj/project.assets.json'
}
$runtimeAssets = Resolve-DesktopSystemMonitorRuntimeAssets -ProjectAssetsPath $assetsPath -Runtime $Runtime
$runtimeVersion = $runtimeAssets.runtimeVersion
$runtimeLicenseResolution = Resolve-DesktopSystemMonitorRuntimeLicenseDirectory -LicensesRoot (Join-Path $projectRoot 'licenses') -RuntimeVersion $runtimeVersion
$runtimeLicenseSource = Assert-PathWithinRoot -Path $runtimeLicenseResolution.Path -Root $projectRoot

Copy-Item -LiteralPath $readme -Destination (Join-Path $destination 'README.md') -Force
Copy-Item -LiteralPath $projectLicense -Destination (Join-Path $destination 'LICENSE') -Force
Copy-Item -LiteralPath $notices -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.md') -Force
$licenseDestination = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
foreach ($legalSource in $legalSources) {
    Copy-Item -LiteralPath $legalSource -Destination $licenseDestination -Recurse -Force
}
# The vendored snapshot's own folder name may be a same-minor fallback rather
# than an exact match (see Resolve-DesktopSystemMonitorRuntimeLicenseDirectory),
# so copy its contents into a destination named after the true resolved
# version rather than Copy-Item -Recurse'ing the source directory as-is.
$runtimeLicenseDestination = Join-Path $licenseDestination "dotnet-runtime-$runtimeVersion"
New-Item -ItemType Directory -Path $runtimeLicenseDestination -Force | Out-Null
Copy-Item -Path (Join-Path $runtimeLicenseSource '*') -Destination $runtimeLicenseDestination -Recurse -Force
Write-Host "Published Desktop System Monitor to $destination" -ForegroundColor Green
