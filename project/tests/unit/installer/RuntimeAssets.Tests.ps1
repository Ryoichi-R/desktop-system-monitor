BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    . (Join-Path $projectRoot 'scripts\resolve-desktop-system-monitor-runtime-assets.ps1')
}

BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    . (Join-Path $projectRoot 'scripts\resolve-desktop-system-monitor-runtime-assets.ps1')
function New-TestAssetsFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$MultipleRuntimeEntries,
        [switch]$OpenRange,
        [switch]$MultipleFrameworks
    )

    $runtimeVersion = if ($OpenRange) { '[10.0.8, 10.0.9)' } else { '[10.0.8, 10.0.8]' }
    $runtimeDependencies = @(
        @{ name = 'Microsoft.NETCore.App.Runtime.win-x64'; version = $runtimeVersion }
        @{ name = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; version = '[10.0.8, 10.0.8]' }
        @{ name = 'Microsoft.AspNetCore.App.Runtime.win-x64'; version = '[10.0.8, 10.0.8]' }
        @{ name = 'Microsoft.Windows.SDK.NET.Ref'; version = '[10.0.19041.57, 10.0.19041.57]' }
    )
    if ($MultipleRuntimeEntries) {
        $runtimeDependencies += @{ name = 'Microsoft.NETCore.App.Runtime.win-x64'; version = '[10.0.8, 10.0.8]' }
    }
    $frameworks = [ordered]@{
        'net10.0-windows10.0.19041' = @{ downloadDependencies = $runtimeDependencies }
    }
    if ($MultipleFrameworks) {
        $frameworks['net10.0-windows10.0.19041-alt'] = @{ downloadDependencies = $runtimeDependencies }
    }
    $assets = [ordered]@{
        project = @{ frameworks = $frameworks }
        libraries = [ordered]@{ 'Microsoft.NET.ILLink.Tasks/10.0.8' = @{} }
    }
    $assets | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
}
}

Describe 'runtime assets resolver' {
    It 'selects exact runtime names from the complete dependency array' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-assets-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            New-TestAssetsFile -Path $temp
            $resolved = Resolve-DesktopSystemMonitorRuntimeAssets -ProjectAssetsPath $temp -Runtime win-x64
            $resolved.runtimeVersion | Should -Be '10.0.8'
            $resolved.windowsDesktopVersion | Should -Be '10.0.8'
            $resolved.ilLinkVersion | Should -Be '10.0.8'
            $resolved.targetFramework | Should -Be 'net10.0-windows10.0.19041'
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
        }
    }

    It 'rejects an open or mismatched runtime range' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-assets-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            New-TestAssetsFile -Path $temp -OpenRange
            { Resolve-DesktopSystemMonitorRuntimeAssets -ProjectAssetsPath $temp -Runtime win-x64 } | Should -Throw
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
        }
    }

    It 'rejects duplicate exact runtime entries and ambiguous framework nodes' {
        foreach ($mode in @('duplicate', 'framework')) {
            $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-assets-' + [guid]::NewGuid().ToString('N') + '.json')
            try {
                if ($mode -eq 'duplicate') { New-TestAssetsFile -Path $temp -MultipleRuntimeEntries }
                else { New-TestAssetsFile -Path $temp -MultipleFrameworks }
                { Resolve-DesktopSystemMonitorRuntimeAssets -ProjectAssetsPath $temp -Runtime win-x64 } | Should -Throw
            }
            finally {
                if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
            }
        }
    }
}

Describe 'runtime license directory resolver' {
    BeforeEach {
        $script:licensesRoot = Join-Path ([IO.Path]::GetTempPath()) ('dsm-licenses-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $licensesRoot -Force | Out-Null
    }

    AfterEach {
        if (Test-Path -LiteralPath $licensesRoot) { Remove-Item -LiteralPath $licensesRoot -Recurse -Force }
    }

    It 'returns the exact-version directory when it exists' {
        New-Item -ItemType Directory -Path (Join-Path $licensesRoot 'dotnet-runtime-10.0.8') -Force | Out-Null
        $resolved = Resolve-DesktopSystemMonitorRuntimeLicenseDirectory -LicensesRoot $licensesRoot -RuntimeVersion '10.0.8'
        $resolved.IsExactMatch | Should -Be $true
        (Split-Path -Leaf $resolved.Path) | Should -Be 'dotnet-runtime-10.0.8'
    }

    It 'falls back to the newest same-minor snapshot and warns' {
        New-Item -ItemType Directory -Path (Join-Path $licensesRoot 'dotnet-runtime-10.0.3') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $licensesRoot 'dotnet-runtime-10.0.8') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $licensesRoot 'dotnet-runtime-9.0.5') -Force | Out-Null
        $warnings = @()
        $resolved = Resolve-DesktopSystemMonitorRuntimeLicenseDirectory `
            -LicensesRoot $licensesRoot -RuntimeVersion '10.0.9' -WarningVariable +warnings -WarningAction SilentlyContinue
        $resolved.IsExactMatch | Should -Be $false
        (Split-Path -Leaf $resolved.Path) | Should -Be 'dotnet-runtime-10.0.8'
        $warnings.Count | Should -Be 1
    }

    It 'throws when no snapshot shares the requested major.minor' {
        New-Item -ItemType Directory -Path (Join-Path $licensesRoot 'dotnet-runtime-9.0.5') -Force | Out-Null
        { Resolve-DesktopSystemMonitorRuntimeLicenseDirectory -LicensesRoot $licensesRoot -RuntimeVersion '10.0.8' } | Should -Throw
    }
}
