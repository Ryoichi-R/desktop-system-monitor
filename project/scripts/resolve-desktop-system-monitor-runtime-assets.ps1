Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DesktopSystemMonitorJsonProperty {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Required JSON property is missing: $Name"
    }
    return $property.Value
}

function Get-DesktopSystemMonitorClosedVersion {
    param(
        [Parameter(Mandatory)][string]$Range,
        [Parameter(Mandatory)][string]$DependencyName
    )

    if ($Range -notmatch '^\[(?<lower>[^,\]]+),\s*(?<upper>[^\]]+)\]$') {
        throw "Dependency version must be a closed exact range for ${DependencyName}: $Range"
    }
    if ($Matches.lower -ne $Matches.upper -or [string]::IsNullOrWhiteSpace($Matches.lower)) {
        throw "Dependency version range is not an exact version for ${DependencyName}: $Range"
    }
    return $Matches.lower
}

function Resolve-DesktopSystemMonitorRuntimeAssets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectAssetsPath,
        [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime
    )

    $assetsPath = [IO.Path]::GetFullPath($ProjectAssetsPath)
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "project.assets.json is missing: $assetsPath"
    }

    try {
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "project.assets.json is invalid: $assetsPath"
    }

    $frameworks = @((Get-DesktopSystemMonitorJsonProperty -Object $assets.project -Name 'frameworks').PSObject.Properties)
    $windowsFrameworks = @($frameworks | Where-Object { $_.Name -match '^net[^-]+-windows' })
    if ($windowsFrameworks.Count -ne 1) {
        throw "Expected exactly one Windows application framework node in assets; found $($windowsFrameworks.Count): $assetsPath"
    }
    $framework = $windowsFrameworks[0]
    $frameworkValue = $framework.Value
    $downloadDependencies = @((Get-DesktopSystemMonitorJsonProperty -Object $frameworkValue -Name 'downloadDependencies'))

    $runtimeName = "Microsoft.NETCore.App.Runtime.$Runtime"
    $runtimeMatches = @($downloadDependencies | Where-Object {
            [string](Get-DesktopSystemMonitorJsonProperty -Object $_ -Name 'name') -ieq $runtimeName
        })
    if ($runtimeMatches.Count -ne 1) {
        throw "Expected exactly one runtime dependency named $runtimeName; found $($runtimeMatches.Count)."
    }

    $desktopName = "Microsoft.WindowsDesktop.App.Runtime.$Runtime"
    $desktopMatches = @($downloadDependencies | Where-Object {
            [string](Get-DesktopSystemMonitorJsonProperty -Object $_ -Name 'name') -ieq $desktopName
        })
    if ($desktopMatches.Count -ne 1) {
        throw "Expected exactly one runtime dependency named $desktopName; found $($desktopMatches.Count)."
    }

    $runtimeVersion = Get-DesktopSystemMonitorClosedVersion `
        -Range ([string](Get-DesktopSystemMonitorJsonProperty -Object $runtimeMatches[0] -Name 'version')) `
        -DependencyName $runtimeName
    $desktopVersion = Get-DesktopSystemMonitorClosedVersion `
        -Range ([string](Get-DesktopSystemMonitorJsonProperty -Object $desktopMatches[0] -Name 'version')) `
        -DependencyName $desktopName
    if ($runtimeVersion -ne $desktopVersion) {
        throw "Runtime pack versions do not match: $runtimeVersion vs $desktopVersion"
    }

    $ilLinkMatches = @($assets.libraries.PSObject.Properties | Where-Object {
            $_.Name -match '^Microsoft\.NET\.ILLink\.Tasks/'
        })
    if ($ilLinkMatches.Count -ne 1) {
        throw "Expected exactly one Microsoft.NET.ILLink.Tasks library in assets; found $($ilLinkMatches.Count)."
    }
    $ilLinkParts = $ilLinkMatches[0].Name.Split('/', 2)
    if ($ilLinkParts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($ilLinkParts[1])) {
        throw "Invalid Microsoft.NET.ILLink.Tasks library key: $($ilLinkMatches[0].Name)"
    }
    $ilLinkVersion = $ilLinkParts[1]

    [pscustomobject]@{
        runtime = $Runtime
        targetFramework = $framework.Name
        runtimePackName = $runtimeName
        runtimeVersion = $runtimeVersion
        windowsDesktopPackName = $desktopName
        windowsDesktopVersion = $desktopVersion
        ilLinkPackName = 'Microsoft.NET.ILLink.Tasks'
        ilLinkVersion = $ilLinkVersion
        assetsSha256 = (Get-FileHash -LiteralPath $assetsPath -Algorithm SHA256).Hash.ToLowerInvariant()
        assetsPath = $assetsPath
    }
}

function Resolve-DesktopSystemMonitorRuntimeLicenseDirectory {
    <#
    .SYNOPSIS
        Resolves the vendored dotnet-runtime-* license directory for a resolved
        runtime pack version, falling back to the newest snapshot that shares
        the same major.minor when no exact-version snapshot is vendored.

    .DESCRIPTION
        global.json uses rollForward=latestFeature, so the runtime pack patch
        version resolved by dotnet publish drifts with the installed SDK and
        does not always match a snapshot already committed under licenses/.
        The .NET runtime license text is stable within a minor version line,
        so a same-minor fallback is an acceptable legal proxy; a differing
        major.minor always fails closed and requires a new snapshot.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LicensesRoot,
        [Parameter(Mandatory)][string]$RuntimeVersion
    )

    if ($RuntimeVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Runtime version is not in major.minor.patch form: $RuntimeVersion"
    }

    $exact = Join-Path $LicensesRoot "dotnet-runtime-$RuntimeVersion"
    if (Test-Path -LiteralPath $exact -PathType Container) {
        return [pscustomobject]@{ Path = $exact; IsExactMatch = $true }
    }

    $majorMinorPrefix = $RuntimeVersion.Substring(0, $RuntimeVersion.LastIndexOf('.'))
    $candidates = [System.Collections.Generic.List[pscustomobject]]::new()
    foreach ($directory in @(Get-ChildItem -LiteralPath $LicensesRoot -Directory -Filter 'dotnet-runtime-*' -ErrorAction SilentlyContinue)) {
        if ($directory.Name -match '^dotnet-runtime-(?<version>\d+\.\d+\.\d+)$' -and
            $Matches.version.StartsWith("$majorMinorPrefix.", [StringComparison]::Ordinal)) {
            $candidates.Add([pscustomobject]@{ Directory = $directory; Version = [version]$Matches.version })
        }
    }
    if ($candidates.Count -eq 0) {
        throw "Required runtime license directory is missing for published runtime ${RuntimeVersion}: no dotnet-runtime-$majorMinorPrefix.* snapshot exists under $LicensesRoot"
    }

    $fallback = ($candidates | Sort-Object Version -Descending)[0]
    Write-Warning "No exact runtime license directory for $RuntimeVersion; using nearest same-minor snapshot $($fallback.Directory.Name). Add licenses/dotnet-runtime-$RuntimeVersion when convenient (the .NET runtime license text is stable within a minor version line)."
    return [pscustomobject]@{ Path = $fallback.Directory.FullName; IsExactMatch = $false }
}

if ($MyInvocation.InvocationName -ne '.') {
    throw 'This file defines Resolve-DesktopSystemMonitorRuntimeAssets and must be dot-sourced.'
}
