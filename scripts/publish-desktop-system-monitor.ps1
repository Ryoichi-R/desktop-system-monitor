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
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputDir,
    [string]$DistRoot
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

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
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
$projectLicense = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'LICENSE') -Root $projectRoot
$notices = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Root $projectRoot
$legalDirectoryNames = @(
    'LibreHardwareMonitorLib-0.9.6'
    'HidSharp-2.6.4'
    'Mono.Posix.NETStandard-1.0.0'
    'dotnet-runtime-10.0.3'
)
$legalSources = @($legalDirectoryNames | ForEach-Object {
        $source = Assert-PathWithinRoot -Path (Join-Path $projectRoot "licenses/$_") -Root $projectRoot
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            throw "Required legal directory is missing: $source"
        }
        $source
    })
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
New-Item -ItemType Directory -Path $destination -Force | Out-Null

& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $destination `
    --nologo `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $readme -Destination (Join-Path $destination 'README.md') -Force
Copy-Item -LiteralPath $projectLicense -Destination (Join-Path $destination 'LICENSE') -Force
Copy-Item -LiteralPath $notices -Destination (Join-Path $destination 'THIRD-PARTY-NOTICES.md') -Force
$licenseDestination = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
foreach ($legalSource in $legalSources) {
    Copy-Item -LiteralPath $legalSource -Destination $licenseDestination -Recurse -Force
}
Write-Host "Published Desktop System Monitor to $destination" -ForegroundColor Green
