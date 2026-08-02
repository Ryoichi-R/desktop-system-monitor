<# Creates an owner-private candidate manifest for DSM-1/DSM-2/DSM-4 runs. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$ArtifactPath,
    [string]$OutputDirectory = 'result/readiness',
    [ValidatePattern('^[a-fA-F0-9]{40,64}$')][string]$SourceCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PathWithinRoot {
    param([string]$Path, [string]$Root)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    if ($pathFull -ne $rootFull -and -not $pathFull.StartsWith(
            $rootFull + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside project root: $pathFull"
    }
    $pathFull
}

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifact = [IO.Path]::GetFullPath($ArtifactPath)
if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) { throw "Artifact not found: $artifact" }
$output = Assert-PathWithinRoot -Path (Join-Path $projectRoot $OutputDirectory) -Root $projectRoot
$releaseDryRun = & (Join-Path $PSScriptRoot 'new-desktop-system-monitor-release.ps1') -Runtime $Runtime -DryRun | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Release contract dry-run failed.' }
$contract = $releaseDryRun | ConvertFrom-Json

$manifest = [ordered]@{
    schemaVersion = 1
    candidateLineageId = $contract.candidateLineageId
    sourceTreeDigest = $contract.sourceTreeDigest
    sourceCommit = if ([string]::IsNullOrWhiteSpace($SourceCommit)) { $null } else { $SourceCommit.ToLowerInvariant() }
    productVersion = $contract.version
    runtime = $Runtime
    dependencyFingerprint = $contract.dependencyFingerprint
    dotnetSdk = $contract.dotnetSdk
    publishContract = $contract.publishContract
    artifact = [ordered]@{
        fileName = [IO.Path]::GetFileName($artifact)
        bytes = (Get-Item -LiteralPath $artifact).Length
        sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    createdAtUtc = [DateTime]::UtcNow.ToString('o')
    evidencePolicy = [ordered]@{
        rawEvidenceIsGitIgnored = $true
        publicSummaryPath = 'docs/validation/readiness-summary.json'
        excludes = @('username', 'absolute path', 'process name', 'network identifier')
    }
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$destination = Join-Path $output "candidate-$Runtime-$($contract.candidateLineageId.Substring(0, 12)).json"
if (Test-Path -LiteralPath $destination) { throw "Refusing to replace existing candidate manifest: $destination" }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $destination -Encoding utf8NoBOM
Write-Output $destination
