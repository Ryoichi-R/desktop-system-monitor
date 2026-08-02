BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    . (Join-Path $projectRoot 'installer\Install-DesktopSystemMonitor.ps1')
}

Describe 'installer manifest helpers' {
    It 'accepts only safe relative ZIP file names' {
        (Assert-ZipEntrySafe -EntryName 'licenses/example.txt') | Should -BeTrue
        { Assert-ZipEntrySafe -EntryName '../outside.txt' } | Should -Throw
        { Assert-ZipEntrySafe -EntryName 'C:/outside.txt' } | Should -Throw
        { Assert-ZipEntrySafe -EntryName 'logs:file.txt' } | Should -Throw
    }

    It 'rejects duplicate managed file paths case-insensitively' {
        $files = @(
            [pscustomobject]@{ path = 'A.txt'; size = 1; sha256 = ('a' * 64) }
            [pscustomobject]@{ path = 'a.txt'; size = 1; sha256 = ('b' * 64) }
        )
        { New-ManagedFileMap -ManagedFiles $files } | Should -Throw
    }

    It 'resolves a manifest runtime by exact RID' {
        $manifest = [pscustomobject]@{
            runtimes = [pscustomobject]@{
                'win-x64' = [pscustomobject]@{ runtime = 'win-x64' }
            }
        }
        (Get-ManifestRuntimeEntry -Manifest $manifest -Rid 'win-x64').runtime | Should -Be 'win-x64'
        { Get-ManifestRuntimeEntry -Manifest $manifest -Rid 'win-arm64' } | Should -Throw
    }

    It 'removes only new managed files while preserving unknown user files' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-rollback-' + [guid]::NewGuid().ToString('N'))
        try {
            $oldDirectory = Join-Path $temp 'old'
            $newDirectory = Join-Path $temp 'licenses\new'
            New-Item -ItemType Directory -Path $oldDirectory,$newDirectory -Force | Out-Null
            $oldPath = Join-Path $oldDirectory 'old.txt'
            $unknownPath = Join-Path $oldDirectory 'user-note.txt'
            $newPath = Join-Path $newDirectory 'runtime.txt'
            Set-Content -LiteralPath $oldPath -Value 'old'
            Set-Content -LiteralPath $unknownPath -Value 'keep'
            Set-Content -LiteralPath $newPath -Value 'new'
            $oldFiles = @([pscustomobject]@{ path = 'old/old.txt'; sha256 = (Get-FileSha256 -Path $oldPath); size = 3 })
            $newFiles = @(
                $oldFiles[0]
                [pscustomobject]@{ path = 'licenses/new/runtime.txt'; sha256 = (Get-FileSha256 -Path $newPath); size = 3 }
            )

            Remove-NewManagedOnlyFiles -InstallDirectory $temp -OldManagedFiles $oldFiles -NewManagedFiles $newFiles
            Remove-EmptyManagedDirectories -InstallDirectory $temp -ManagedFiles @($newFiles[1])

            (Test-Path -LiteralPath $oldPath -PathType Leaf) | Should -BeTrue
            (Test-Path -LiteralPath $unknownPath -PathType Leaf) | Should -BeTrue
            (Test-Path -LiteralPath $newPath -PathType Leaf) | Should -BeFalse
            (Test-Path -LiteralPath $newDirectory) | Should -BeFalse
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
        }
    }
}
