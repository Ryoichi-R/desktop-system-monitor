function Get-DesktopSystemMonitorSha256Text {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-DesktopSystemMonitorFileSetDigest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Files,
        [Parameter(Mandatory)][string]$Root
    )

    $lines = foreach ($file in $Files | Sort-Object) {
        $relative = [IO.Path]::GetRelativePath($Root, $file).Replace('\', '/')
        "$relative $((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())"
    }

    Get-DesktopSystemMonitorSha256Text -Text ($lines -join "`n")
}

function New-DesktopSystemMonitorCandidateLineageId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$SourceTreeDigest,
        [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$DependencyFingerprint,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$DotNetSdk,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ProductVersion,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PublishContract
    )

    $inputs = @(
        $SourceTreeDigest
        $DependencyFingerprint
        $DotNetSdk
        $ProductVersion
        $PublishContract
    )
    Get-DesktopSystemMonitorSha256Text -Text ($inputs -join "`n")
}

function Resolve-DesktopSystemMonitorCandidateIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot)

    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $propsPath = Join-Path $root 'Directory.Build.props'
    $globalJsonPath = Join-Path $root 'global.json'
    foreach ($requiredPath in @($propsPath, $globalJsonPath, (Join-Path $root 'src'))) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Candidate identity input is missing: $requiredPath"
        }
    }

    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $version = [string]$props.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'Directory.Build.props does not define Version.'
    }

    $sourceFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $root 'src') -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|dist|coverage)[\\/]'
        }
        Get-Item -LiteralPath $propsPath, $globalJsonPath
    ) | ForEach-Object FullName
    $dependencyFiles = @(
        Get-ChildItem -LiteralPath $root -Filter '*.csproj' -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|dist|coverage)[\\/]'
        }
    ) | ForEach-Object FullName
    if ($sourceFiles.Count -eq 0 -or $dependencyFiles.Count -eq 0) {
        throw 'Candidate identity requires non-empty source and dependency file sets.'
    }

    $sdkOutput = & dotnet --version
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the .NET SDK version for candidate identity.'
    }
    $sdkVersion = ($sdkOutput | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw 'The .NET SDK version for candidate identity is empty.'
    }

    $sourceDigest = Get-DesktopSystemMonitorFileSetDigest -Files $sourceFiles -Root $root
    $dependencyDigest = Get-DesktopSystemMonitorFileSetDigest -Files $dependencyFiles -Root $root
    $publishContract = 'Release|self-contained=true|single-file=false|debug=None|debug-symbols=false'
    $lineage = New-DesktopSystemMonitorCandidateLineageId `
        -SourceTreeDigest $sourceDigest `
        -DependencyFingerprint $dependencyDigest `
        -DotNetSdk $sdkVersion `
        -ProductVersion $version `
        -PublishContract $publishContract

    [pscustomobject]@{
        productVersion = $version
        sourceTreeDigest = $sourceDigest
        dependencyFingerprint = $dependencyDigest
        dotnetSdk = $sdkVersion
        publishContract = $publishContract
        candidateLineageId = $lineage
    }
}
