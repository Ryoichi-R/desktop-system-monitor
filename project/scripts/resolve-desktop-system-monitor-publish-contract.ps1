Set-StrictMode -Version Latest

function Get-DesktopSystemMonitorPublishContract {
    [CmdletBinding()]
    param()

    $properties = [ordered]@{
        publishSingleFile = $true
        includeNativeLibrariesForSelfExtract = $true
        publishTrimmed = $false
        enableCompressionInSingleFile = $false
        includeAllContentForSelfExtract = $false
        continuousIntegrationBuild = $true
        debugType = 'None'
        debugSymbols = $false
    }

    $canonicalParts = @(
        'Release'
        'self-contained=true'
        "single-file=$(([bool]$properties.publishSingleFile).ToString().ToLowerInvariant())"
        "native-self-extract=$(([bool]$properties.includeNativeLibrariesForSelfExtract).ToString().ToLowerInvariant())"
        "trimmed=$(([bool]$properties.publishTrimmed).ToString().ToLowerInvariant())"
        "compression=$(([bool]$properties.enableCompressionInSingleFile).ToString().ToLowerInvariant())"
        "include-all-content=$(([bool]$properties.includeAllContentForSelfExtract).ToString().ToLowerInvariant())"
        "ci-build=$(([bool]$properties.continuousIntegrationBuild).ToString().ToLowerInvariant())"
        "debug=$($properties.debugType)"
        "debug-symbols=$(([bool]$properties.debugSymbols).ToString().ToLowerInvariant())"
    )

    [pscustomobject]@{
        Configuration = 'Release'
        SelfContained = $true
        Properties = $properties
        Manifest = [ordered]@{
            configuration = 'Release'
            selfContained = $true
            publishSingleFile = $properties.publishSingleFile
            includeNativeLibrariesForSelfExtract = $properties.includeNativeLibrariesForSelfExtract
            publishTrimmed = $properties.publishTrimmed
            enableCompressionInSingleFile = $properties.enableCompressionInSingleFile
            includeAllContentForSelfExtract = $properties.includeAllContentForSelfExtract
            continuousIntegrationBuild = $properties.continuousIntegrationBuild
            debugType = $properties.debugType
            debugSymbols = $properties.debugSymbols
        }
        CandidateString = ($canonicalParts -join '|')
    }
}

function Get-DesktopSystemMonitorPublishArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Runtime,
        [Parameter(Mandatory)][string]$OutputDir,
        [string]$ArtifactsPath,
        [string]$Framework = 'net10.0-windows10.0.19041.0'
    )

    $contract = Get-DesktopSystemMonitorPublishContract
    $args = @(
        $Project
        '--configuration', $contract.Configuration
        '--framework', $Framework
        '--runtime', $Runtime
        '--self-contained', $contract.SelfContained.ToString().ToLowerInvariant()
        '--output', $OutputDir
        '--nologo'
    )
    $args += @(
        "-p:PublishSingleFile=$($contract.Properties.publishSingleFile.ToString().ToLowerInvariant())"
        "-p:IncludeNativeLibrariesForSelfExtract=$($contract.Properties.includeNativeLibrariesForSelfExtract.ToString().ToLowerInvariant())"
        "-p:PublishTrimmed=$($contract.Properties.publishTrimmed.ToString().ToLowerInvariant())"
        "-p:EnableCompressionInSingleFile=$($contract.Properties.enableCompressionInSingleFile.ToString().ToLowerInvariant())"
        "-p:IncludeAllContentForSelfExtract=$($contract.Properties.includeAllContentForSelfExtract.ToString().ToLowerInvariant())"
        "-p:ContinuousIntegrationBuild=$($contract.Properties.continuousIntegrationBuild.ToString().ToLowerInvariant())"
        "-p:DebugType=$($contract.Properties.debugType)"
        "-p:DebugSymbols=$($contract.Properties.debugSymbols.ToString().ToLowerInvariant())"
    )
    if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
        $args += @('--artifacts-path', $ArtifactsPath)
    }
    return $args
}

if ($MyInvocation.InvocationName -eq '.') { return }
Get-DesktopSystemMonitorPublishContract | ConvertTo-Json -Depth 10
