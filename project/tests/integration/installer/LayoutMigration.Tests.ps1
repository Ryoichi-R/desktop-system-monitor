BeforeAll {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    . (Join-Path $projectRoot 'scripts\resolve-desktop-system-monitor-roots.ps1')
}

Describe 'migrated layout contract' -Tag 'RequiresMigratedLayout' {
    It 'resolves a repository with project as a child' {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ('dsm-layout-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $temp '.github') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $temp 'project\src') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $temp 'project\scripts') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $temp 'project\Directory.Build.props') -Value '<Project />'
        try {
            $roots = Resolve-DesktopSystemMonitorRoots -RepositoryRoot $temp
            $roots.Layout | Should -Be 'migrated'
            $roots.ProjectRoot | Should -Be ([IO.Path]::GetFullPath((Join-Path $temp 'project')))
        }
        finally {
            if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
        }
    }
}
