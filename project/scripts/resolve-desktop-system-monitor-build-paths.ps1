function ConvertTo-CanonicalDirectoryPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $filesystemRoot = [IO.Path]::GetPathRoot($fullPath)
    if ($fullPath -ieq $filesystemRoot) {
        return $filesystemRoot
    }

    return $fullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
}

function Resolve-DesktopSystemMonitorBuildPaths {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [AllowNull()][AllowEmptyString()][string]$OutputRoot
    )

    $resolvedProjectRoot = ConvertTo-CanonicalDirectoryPath -Path $ProjectRoot
    $requestedOutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $resolvedProjectRoot
    }
    elseif ([IO.Path]::IsPathFullyQualified($OutputRoot.Trim())) {
        $OutputRoot.Trim()
    }
    else {
        Join-Path $resolvedProjectRoot $OutputRoot.Trim()
    }
    $resolvedOutputRoot = ConvertTo-CanonicalDirectoryPath -Path $requestedOutputRoot
    $usesExternalRoot = $resolvedOutputRoot -ine $resolvedProjectRoot
    $managedRoot = if ($usesExternalRoot) {
        Join-Path $resolvedOutputRoot 'DesktopSystemMonitorBuilds'
    }
    else {
        Join-Path $resolvedProjectRoot 'dist'
    }

    [pscustomobject]@{
        OutputParent     = $resolvedOutputRoot
        ManagedRoot      = ConvertTo-CanonicalDirectoryPath -Path $managedRoot
        UsesExternalRoot = $usesExternalRoot
    }
}
