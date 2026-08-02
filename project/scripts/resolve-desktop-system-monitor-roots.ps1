<#!
    Repository/developer root resolver.

    This helper is never used by the distributed installer. Distribution
    scripts resolve from their own $PSScriptRoot and require no repository
    marker, .git directory, solution, or .github directory.
#>
Set-StrictMode -Version 2.0

function ConvertTo-DesktopSystemMonitorFullPath {
    param([Parameter(Mandatory)][string]$Path)
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
}

function Test-DesktopSystemMonitorSourceRoot {
    param([Parameter(Mandatory)][string]$Path)
    return (Test-Path -LiteralPath (Join-Path $Path 'src') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $Path 'scripts') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $Path 'Directory.Build.props') -PathType Leaf)
}

function Resolve-DesktopSystemMonitorRoots {
    [CmdletBinding()]
    param(
        [string]$RepositoryRoot,
        [string]$ProjectRoot,
        [string]$StartPath = $PSScriptRoot
    )
    if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        $repo = ConvertTo-DesktopSystemMonitorFullPath -Path $RepositoryRoot
    }
    else {
        $start = ConvertTo-DesktopSystemMonitorFullPath -Path $StartPath
        $repo = $null
        $candidate = $start
        for ($i = 0; $i -lt 8 -and $null -eq $repo; $i++) {
            if ((Test-Path -LiteralPath (Join-Path $candidate 'project') -PathType Container) -and
                (Test-Path -LiteralPath (Join-Path $candidate '.github') -PathType Container)) {
                $repo = $candidate
                break
            }
            if (Test-DesktopSystemMonitorSourceRoot -Path $candidate) {
                $parent = Split-Path -Parent $candidate
                $candidateLeaf = Split-Path -Leaf $candidate
                if ($candidateLeaf -ieq 'project' -and
                    (Test-Path -LiteralPath (Join-Path $parent '.github') -PathType Container)) {
                    $repo = $parent
                }
                else {
                    $repo = $candidate
                }
                break
            }
            $parent = Split-Path -Parent $candidate
            if ($parent -eq $candidate) { break }
            $candidate = $parent
        }
        if ($null -eq $repo) { throw "Unable to resolve repository root from $StartPath." }
    }
    if (-not (Test-Path -LiteralPath $repo -PathType Container)) { throw "Repository root does not exist: $repo" }

    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $project = ConvertTo-DesktopSystemMonitorFullPath -Path $ProjectRoot
    }
    elseif (Test-DesktopSystemMonitorSourceRoot -Path (Join-Path $repo 'project')) {
        $project = ConvertTo-DesktopSystemMonitorFullPath -Path (Join-Path $repo 'project')
    }
    else {
        $project = $repo
    }
    if (-not (Test-DesktopSystemMonitorSourceRoot -Path $project)) { throw "Project root does not contain the expected source contract: $project" }
    $repoBoundary = $repo.TrimEnd('\') + '\'
    if ($project -ne $repo -and -not $project.StartsWith($repoBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Project root must be the repository root or a child of it: $project"
    }
    [pscustomobject]@{
        RepositoryRoot = $repo
        ProjectRoot = $project
        Layout = if ($project -eq $repo) { 'pre-move' } else { 'migrated' }
    }
}

function Resolve-DesktopSystemMonitorDistributionRoot {
    [CmdletBinding()]
    param([string]$PackageRoot = $PSScriptRoot)
    $root = ConvertTo-DesktopSystemMonitorFullPath -Path $PackageRoot
    if (-not (Test-Path -LiteralPath (Join-Path $root 'payload') -PathType Container)) {
        throw "Distribution package root must contain payload: $root"
    }
    [pscustomobject]@{ PackageRoot = $root; PayloadRoot = ConvertTo-DesktopSystemMonitorFullPath -Path (Join-Path $root 'payload') }
}
