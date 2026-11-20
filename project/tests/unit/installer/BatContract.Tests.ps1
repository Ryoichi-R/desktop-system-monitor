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

Describe 'root BAT process exit' -Tag 'WindowsOnly' {
    It 'passes child exit <code> through exit /b without pausing' -ForEach @(0,3,4,5,6 | ForEach-Object { @{code=$_} }) {
        $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
        $bat = Get-ChildItem -LiteralPath $root -Filter '*.bat' -File | Select-Object -First 1
        $probe = Join-Path $TestDrive 'probe.ps1'
        @('param([string]$Runtime)', "Write-Output 'probe $code'", "exit $code") | Set-Content -LiteralPath $probe
        $savedProbe = $env:DSM_INSTALLER_PROBE
        $savedPause = $env:DSM_NO_PAUSE
        try {
            $env:DSM_INSTALLER_PROBE = $probe
            $env:DSM_NO_PAUSE = '1'
            $output = & $bat.FullName
            $LASTEXITCODE | Should -Be $code
            ($output -join ' ') | Should -Match "probe $code"
        }
        finally {
            $env:DSM_INSTALLER_PROBE = $savedProbe
            $env:DSM_NO_PAUSE = $savedPause
        }
    }
}
