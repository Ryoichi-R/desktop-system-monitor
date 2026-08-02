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
