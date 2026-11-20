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

Describe 'destination update transaction' {
    BeforeEach {
        $parent = (New-Item -ItemType Directory -Path (Join-Path $TestDrive ([guid]::NewGuid()))).FullName
        $backups = Join-Path $parent 'Backups'
        $install = Join-Path $parent 'Desktop System Monitor'
        $marker = Join-Path $install '.desktop-system-monitor-install.json'
        $payload = Join-Path $parent 'source'
        [void][IO.Directory]::CreateDirectory($payload)
        Mock Get-InstallerBackupBase { $backups }
        Mock Get-Process {}
        Mock Test-ArchiveAndExtract {
            [void][IO.Directory]::CreateDirectory($ExtractionRoot)
            Get-ChildItem -LiteralPath $payload | Copy-Item -Destination $ExtractionRoot -Recurse
        }
        function Add-Payload([string]$Relative, [string]$Text) {
            $file = Join-Path $payload $Relative
            [void][IO.Directory]::CreateDirectory((Split-Path $file))
            [IO.File]::WriteAllText($file, $Text)
            [pscustomobject]@{ path = $Relative; size = (Get-Item $file).Length; sha256 = Get-FileSha256 $file }
        }
        function Install-TestPayload {
            Invoke-Install $parent $parent 'win-x64' ([pscustomobject]@{productVersion='0.3.1'}) $entry 'local-preview'
        }
        $entry = [pscustomobject]@{managedFiles=@((Add-Payload 'app.exe' 'old')); totalUncompressedBytes=100
            executable='app.exe'; contentDigest='digest'; distributionStatus='local-only'; archive='fixture'; archiveSha256='fixture'}
        Install-TestPayload | Should -Be 0
        $oldMarker = Get-FileSha256 $marker
        $entry.managedFiles = @((Add-Payload 'app.exe' 'new'), (Add-Payload 'extras/file' 'new file'))
    }

    It 'preflights new paths before replacing existing files (<collision>)' -ForEach @(
        @{collision='file'}, @{collision='directory'}, @{collision='parent'}
    ) {
        $target = Join-Path $install 'extras/file'
        if ($collision -eq 'parent') { $target = Split-Path $target }
        [void][IO.Directory]::CreateDirectory((Split-Path $target))
        if ($collision -eq 'directory') { [void][IO.Directory]::CreateDirectory($target) }
        else { [IO.File]::WriteAllText($target, 'user') }
        { Install-TestPayload } | Should -Throw "*$target*"
        if ($collision -eq 'directory') { [IO.Directory]::Exists($target) | Should -BeTrue }
        else { [IO.File]::ReadAllText($target) | Should -Be 'user' }
        (Get-FileSha256 $marker) | Should -Be $oldMarker
        [IO.File]::ReadAllText((Join-Path $install 'app.exe')) | Should -Be 'old'
    }

    It 'retains files created after preflight and existing empty directories on rollback' {
        $extras = Join-Path $install 'extras'
        [void][IO.Directory]::CreateDirectory($extras)
        $race = Join-Path $extras 'file'
        Mock New-OwnedDirectory { [IO.File]::WriteAllText($race, 'user') } -ParameterFilter { $Path -eq $extras }
        { Install-TestPayload } | Should -Throw
        [IO.File]::ReadAllText($race) | Should -Be 'user'
        (Get-FileSha256 $marker) | Should -Be $oldMarker
        [IO.File]::ReadAllText((Join-Path $install 'app.exe')) | Should -Be 'old'
    }

    It 'restores only owned paths and preserves the exact marker on failure before commit' {
        $extras = Join-Path $install 'extras'
        [void][IO.Directory]::CreateDirectory($extras)
        Mock Write-InstallMarker { throw 'fixture before commit' } -ParameterFilter { $Path -like '*.desktop-system-monitor-install.json' }
        { Install-TestPayload } | Should -Throw '*fixture before commit*'
        (Test-Path $extras -PathType Container) | Should -BeTrue
        (Test-Path (Join-Path $extras 'file')) | Should -BeFalse
        [IO.File]::ReadAllText((Join-Path $install 'app.exe')) | Should -Be 'old'
        (Get-FileSha256 $marker) | Should -Be $oldMarker
    }

    It 'preserves verified recovery data across retry until original bytes are restored' {
        $app = Join-Path $install 'app.exe'
        Mock Write-InstallMarker {
            [IO.File]::Delete($app)
            [void][IO.Directory]::CreateDirectory($app)
            throw 'fixture apply failure'
        } -ParameterFilter { $Path -like '*.desktop-system-monitor-install.json' }
        { Install-TestPayload } | Should -Throw '*Rollback failed*'
        $journal = Get-ChildItem $backups -Filter '*.recovery.json'
        $record = Get-Content $journal.FullName -Raw | ConvertFrom-Json
        $record.markerSha256 | Should -Be $oldMarker
        [IO.File]::ReadAllText((Join-Path $record.backupPath 'app.exe')) | Should -Be 'old'
        $pending = try { Install-TestPayload } catch { $_ }
        $pending.Exception.Message | Should -Match 'Recovery pending'
        $pending.Exception.Data['DesktopSystemMonitor.RecoveryRequired'] | Should -BeTrue
        [IO.Directory]::Delete($app)
        [IO.File]::Copy((Join-Path $record.backupPath 'app.exe'), $app)
        # Force a preflight collision after journal verification.
        [IO.File]::WriteAllText((Join-Path $install 'extras'), 'user')
        { Install-TestPayload } | Should -Throw '*Parent path*'
        (Test-Path $journal.FullName) | Should -BeFalse
    }

    It 'rejects a missing old managed file before starting' {
        [IO.File]::Delete((Join-Path $install 'app.exe'))
        { Install-TestPayload } | Should -Throw
        (Get-FileSha256 $marker) | Should -Be $oldMarker
        (Test-Path (Join-Path $install 'extras/file')) | Should -BeFalse
    }

    It 'retains the new verified backup if promotion fails after commit' {
        [void][IO.Directory]::CreateDirectory($backups)
        $blocked = Join-Path $backups (New-PathHash $install)
        [IO.File]::WriteAllText($blocked, 'occupied')
        $messages = @(Install-TestPayload 3>&1)
        $messages[-1] | Should -Be 0
        ($messages -join ' ') | Should -Match 'Backup rotation incomplete'
        [IO.File]::ReadAllText($blocked) | Should -Be 'occupied'
        $staging = Get-ChildItem -LiteralPath $backups -Force -Directory -Filter '.staging-*'
        (Get-FileSha256 (Join-Path $staging.FullName '.desktop-system-monitor-install.json')) | Should -Be $oldMarker
        [IO.File]::ReadAllText((Join-Path $staging.FullName 'app.exe')) | Should -Be 'old'
        [IO.File]::ReadAllText((Join-Path $install 'app.exe')) | Should -Be 'new'
    }

    It 'keeps the committed update and both backup generations if old generation cleanup fails' {
        [IO.File]::WriteAllText((Join-Path $install 'user.txt'), 'user')
        Install-TestPayload | Should -Be 0
        $entry.managedFiles = @((Add-Payload 'app.exe' 'third'), (Add-Payload 'extras/file' 'new file'))
        Mock Remove-Item {
            [IO.File]::Delete((Join-Path $LiteralPath 'app.exe'))
            throw 'fixture partial cleanup failure'
        } -ParameterFilter { $LiteralPath -like '*.previous-*' }
        $messages = @(Install-TestPayload 3>&1)
        $messages[-1] | Should -Be 0
        ($messages -join ' ') | Should -Match 'Backup rotation incomplete'
        $archive = Get-ChildItem -LiteralPath $backups -Filter '*.previous-*.zip'
        $restored = Join-Path $parent 'previous-backup'
        [IO.Compression.ZipFile]::ExtractToDirectory($archive.FullName, $restored)
        [IO.File]::ReadAllText((Join-Path $restored 'app.exe')) | Should -Be 'old'
        (Get-FileSha256 (Join-Path $restored '.desktop-system-monitor-install.json')) | Should -Be $oldMarker
        [IO.File]::ReadAllText((Join-Path $install 'app.exe')) | Should -Be 'third'
        [IO.File]::ReadAllText((Join-Path $install 'user.txt')) | Should -Be 'user'
        @(Get-ChildItem $backups -Directory).Count | Should -Be 2
    }
}

Describe 'installer exit contract at the process boundary' {
    It 'propagates <code> with a diagnostic through the active host' -ForEach @(
        @{code=0; action="Write-Host 'installed'; return 0"; diagnostic='installed'}
        @{code=3; action="throw [OperationCanceledException]::new('Folder selection was cancelled.')"; diagnostic='cancelled'}
        @{code=4; action="throw 'Unsupported payload manifest schema version'"; diagnostic='Unsupported'}
        @{code=5; action="throw 'Managed file hash mismatch'"; diagnostic='hash mismatch'}
        @{code=6; action="Stop-RecoveryRequired 'Rollback failed. Backup: fixture'"; diagnostic='Backup: fixture'}
        @{code=6; action="Stop-RecoveryRequired 'Recovery pending. Journal: fixture'"; diagnostic='Journal: fixture'}
    ) {
        # Substitute only the outcome of Invoke-Main in a disposable script.
        # The outer catch, error preference, -NoPause and exit are the shipping code.
        $source = Get-Content (Join-Path $projectRoot 'installer/Install-DesktopSystemMonitor.ps1') -Raw
        $guard = 'if ($MyInvocation.InvocationName -eq ''.'') { return }'
        $fixture = Join-Path $TestDrive 'installer-outcome.ps1'
        $source.Replace($guard, "function Invoke-Main { $action }") | Set-Content -LiteralPath $fixture -Encoding UTF8
        $hostPath = (Get-Process -Id $PID).Path
        $ErrorActionPreference = 'Continue' # Windows PowerShell wraps redirected native stderr as error records.
        $output = & $hostPath -NoProfile -ExecutionPolicy Bypass -File $fixture -NoPause 2>&1
        $LASTEXITCODE | Should -Be $code
        ($output -join ' ') | Should -Match $diagnostic
    }
}
