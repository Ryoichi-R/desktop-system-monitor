<#
.SYNOPSIS
    Desktop System Monitor 用の .NET テストランナー。

.DESCRIPTION
    dotnet test を通じて Core / Windows / Mac / Avalonia のユニットテストと
    Windows 統合テストを実行する。既存 scripts/test.ps1 (Pester) とは独立している。

.PARAMETER Core
    Ubuntu / Windows 双方で通る Core.Tests のみ実行する。

.PARAMETER Windows
    Windows.Tests + App.Tests + IntegrationTests を実行する (windows-latest 専用)。

.PARAMETER Mac
    Mac.Tests + Mac.SensorHost.Tests を実行する。

.PARAMETER Avalonia
    Phase 0W PoCとAvalonia AppのHeadlessテストを実行する。

.PARAMETER All
    Core / Windows / Mac / Avalonia すべてを実行する。既定はこれ。

.EXAMPLE
    pwsh scripts/test-desktop-system-monitor.ps1 -Core
    pwsh scripts/test-desktop-system-monitor.ps1 -Windows
    pwsh scripts/test-desktop-system-monitor.ps1 -Mac
    pwsh scripts/test-desktop-system-monitor.ps1 -Avalonia
    pwsh scripts/test-desktop-system-monitor.ps1 -All
#>
[CmdletBinding()]
param(
    [switch]$Core,
    [switch]$Windows,
    [switch]$Mac,
    [switch]$Avalonia,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

if (-not ($Core -or $Windows -or $Mac -or $Avalonia -or $All)) {
    $All = $true
}

$coreCsproj = Join-Path $projectRoot 'tests/unit/Core.Tests/DesktopSystemMonitor.Core.Tests.csproj'
$winCsproj = Join-Path $projectRoot 'tests/unit/Windows.Tests/DesktopSystemMonitor.Windows.Tests.csproj'
$appCsproj = Join-Path $projectRoot 'tests/unit/App.Tests/DesktopSystemMonitor.App.Tests.csproj'
$intCsproj = Join-Path $projectRoot 'tests/integration/DesktopSystemMonitor.IntegrationTests.csproj'
$macCsproj = Join-Path $projectRoot 'tests/unit/Mac.Tests/DesktopSystemMonitor.Mac.Tests.csproj'
$sensorHostCsproj = Join-Path $projectRoot 'tests/unit/Mac.SensorHost.Tests/DesktopSystemMonitor.Mac.SensorHost.Tests.csproj'
$avaloniaAppCsproj = Join-Path $projectRoot 'tests/unit/App.Avalonia.Tests/DesktopSystemMonitor.App.Avalonia.Tests.csproj'
$avaloniaPocCsproj = Join-Path $projectRoot 'tests/phase0w/DesktopSystemMonitor.Avalonia.PoC.csproj'

$failed = $false

function Invoke-Test {
    param([string]$Csproj)
    Write-Host ""
    Write-Host "==> dotnet test $Csproj"
    & dotnet test $Csproj --configuration Release --nologo --logger "console;verbosity=minimal"
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

if ($Mac -or $All) {
    Invoke-Test -Csproj $macCsproj
    Invoke-Test -Csproj $sensorHostCsproj
}

if ($Avalonia -or $All) {
    Invoke-Test -Csproj $avaloniaAppCsproj
    Invoke-Test -Csproj $avaloniaPocCsproj
}

if ($failed) {
    exit 1
}
Write-Host "All requested test targets passed." -ForegroundColor Green
