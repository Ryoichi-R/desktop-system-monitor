BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    . (Join-Path $projectRoot 'scripts\resolve-desktop-system-monitor-roots.ps1')
}

Describe 'repository root resolver' {
    It 'resolves the current pre-move layout without repository markers' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-pre-move-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $temp 'src') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $temp 'scripts') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $temp 'Directory.Build.props') -Value '<Project />'
        try {
            $roots = Resolve-DesktopSystemMonitorRoots -StartPath (Join-Path $temp 'scripts')
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
        }
        $roots.RepositoryRoot | Should -Be ([IO.Path]::GetFullPath($temp))
        $roots.ProjectRoot | Should -Be ([IO.Path]::GetFullPath($temp))
        $roots.Layout | Should -Be 'pre-move'
    }

    It 'resolves the migrated repository from a project script path' {
        $repositoryRoot = Split-Path -Parent $projectRoot
        $roots = Resolve-DesktopSystemMonitorRoots -StartPath (Join-Path $projectRoot 'scripts')
        $roots.RepositoryRoot | Should -Be ([IO.Path]::GetFullPath($repositoryRoot))
        $roots.ProjectRoot | Should -Be $projectRoot
        $roots.Layout | Should -Be 'migrated'
    }
}

Describe 'repository boundary validation' {
    It 'rejects a sibling whose name starts with the repository name' {
        $repo = Join-Path $TestDrive 'repo'
        $outside = Join-Path $TestDrive 'repo-other'
        New-Item -ItemType Directory -Path $repo -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $outside 'src') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $outside 'scripts') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $outside 'Directory.Build.props') -Value '<Project />'
        { Resolve-DesktopSystemMonitorRoots -RepositoryRoot $repo -ProjectRoot $outside } | Should -Throw '*child of it*'
    }

    It 'preserves the filesystem root during normalization' {
        $filesystemRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($TestDrive))
        $normalized = ConvertTo-DesktopSystemMonitorFullPath -Path $filesystemRoot
        [IO.Path]::IsPathFullyQualified($normalized) | Should -BeTrue
        if ([IO.Path]::DirectorySeparatorChar -eq '/') { $normalized | Should -Be '/' }
    }
}
