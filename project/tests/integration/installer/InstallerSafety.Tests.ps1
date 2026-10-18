BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    . (Join-Path $projectRoot 'installer\Install-DesktopSystemMonitor.ps1')
}

Describe 'installer distribution safety' {
    It 'does not require repository metadata for package root resolution' -Tag 'DistributionLayout' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-distribution-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $temp 'payload') -Force | Out-Null
        try {
            $manifest = [pscustomobject]@{
                schemaVersion = 1
                packageChannel = 'local-preview'
                runtimes = [pscustomobject]@{
                    'win-x64' = [pscustomobject]@{
                        runtime = 'win-x64'
                        distributionStatus = 'local-only'
                        archive = 'DesktopSystemMonitor-win-x64.zip'
                        managedFiles = @()
                    }
                }
            }
            $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $temp 'payload\payload-manifest.json')
            (Test-Path -LiteralPath (Join-Path $temp 'payload')) | Should -BeTrue
            (Test-Path -LiteralPath (Join-Path $temp '.git')) | Should -BeFalse
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
        }
    }

    It 'rejects a tampered archive before destination mutation' -Tag 'WindowsOnly' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-archive-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp -Force | Out-Null
        try {
            $archivePath = Join-Path $temp 'payload.zip'
            Set-Content -LiteralPath $archivePath -Value 'tampered archive bytes'
            $entryManifest = [pscustomobject]@{
                runtime = 'win-x64'
                archive = 'payload.zip'
                archiveSha256 = ('0' * 64)
                executable = 'DesktopSystemMonitor.exe'
                executableSha256 = ('0' * 64)
                managedFiles = @()
            }
            { Test-ArchiveAndExtract -ArchivePath $archivePath -ExpectedArchiveSha256 $entryManifest.archiveSha256 -RuntimeEntry $entryManifest -ExtractionRoot (Join-Path $temp 'extract') } | Should -Throw
            (Test-Path -LiteralPath (Join-Path $temp 'extract')) | Should -BeFalse
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
        }
    }
}
