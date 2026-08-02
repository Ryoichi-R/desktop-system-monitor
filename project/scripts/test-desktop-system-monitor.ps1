<#
.SYNOPSIS
    Desktop System Monitor 用の .NET テストランナー。

.DESCRIPTION
    dotnet test を通じて Core / Windows のユニットテストと Windows 統合テストを
    実行する。既存 scripts/test.ps1 (Pester) とは独立している。

.PARAMETER Core
    Ubuntu / Windows 双方で通る Core.Tests のみ実行する。

.PARAMETER Windows
    Windows.Tests + App.Tests + IntegrationTests を実行する (windows-latest 専用)。

.PARAMETER All
    Core / Windows すべてを実行する。既定はこれ。

.EXAMPLE
    pwsh scripts/test-desktop-system-monitor.ps1 -Core
    pwsh scripts/test-desktop-system-monitor.ps1 -Windows
    pwsh scripts/test-desktop-system-monitor.ps1 -All
#>
[CmdletBinding()]
param(
    [switch]$Core,
    [switch]$Windows,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

if (-not ($Core -or $Windows -or $All)) {
    $All = $true
}

$coreCsproj = Join-Path $projectRoot 'tests/unit/Core.Tests/DesktopSystemMonitor.Core.Tests.csproj'
$winCsproj = Join-Path $projectRoot 'tests/unit/Windows.Tests/DesktopSystemMonitor.Windows.Tests.csproj'
$appCsproj = Join-Path $projectRoot 'tests/unit/App.Tests/DesktopSystemMonitor.App.Tests.csproj'
$intCsproj = Join-Path $projectRoot 'tests/integration/DesktopSystemMonitor.IntegrationTests.csproj'

$failed = $false

function Invoke-Test {
    param([string]$Csproj)
    Write-Host ""
    Write-Host "==> dotnet test $Csproj"
    & dotnet test $Csproj --nologo --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Csproj" -ForegroundColor Red
        $script:failed = $true
    }
}

if ($Core -or $All) {
    Invoke-Test -Csproj $coreCsproj
}

if (($Windows -or $All) -and $IsWindows) {
    Invoke-Test -Csproj $winCsproj
    Invoke-Test -Csproj $appCsproj
    if (Test-Path $intCsproj) {
        Invoke-Test -Csproj $intCsproj
    }
} elseif ($Windows -and -not $IsWindows) {
    Write-Host "-Windows was requested but this runner is not Windows; skipping." -ForegroundColor Yellow
}

if ($failed) {
    exit 1
}
Write-Host "All requested test targets passed." -ForegroundColor Green
