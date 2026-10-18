Describe 'root BAT contract' {
    It 'rebuilds the current architecture before invoking the installer' {
        $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
        $repositoryRoot = Split-Path -Parent $projectRoot
        $batPath = Get-ChildItem -LiteralPath $repositoryRoot -Filter '*.bat' -File | Where-Object { $_.Name -notmatch '^rebuild-' } | Select-Object -First 1 -ExpandProperty FullName
        $bat = Get-Content -LiteralPath $batPath -Raw
        $bat | Should -Match 'DisableDelayedExpansion'
        $bat | Should -Match '%~dp0'
        $bat | Should -Match 'rebuild-desktop-system-monitor\.ps1'
        $bat | Should -Match 'Install-DesktopSystemMonitor\.ps1'
        $bat | Should -Match 'PROCESSOR_ARCHITEW6432'
        $bat | Should -Match 'win-x64'
        $bat | Should -Match 'win-arm64'
        $bat.IndexOf('pwsh.exe -STA', [StringComparison]::OrdinalIgnoreCase) |
            Should -BeLessThan $bat.IndexOf('powershell.exe -STA', [StringComparison]::OrdinalIgnoreCase)
        $bat | Should -Not -Match 'ConvertFrom-Json'
        $bat | Should -Not -Match 'distributionStatus'
        Test-Path -LiteralPath (Join-Path $projectRoot 'rebuild-desktop-system-monitor-x64.bat') |
            Should -BeFalse
    }
}
