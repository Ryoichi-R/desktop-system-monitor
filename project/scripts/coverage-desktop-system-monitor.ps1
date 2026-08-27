<#
.SYNOPSIS
    Desktop System Monitor 用の Coverlet ベースカバレッジランナー。

.DESCRIPTION
    project 別に閾値を評価する:
      - DesktopSystemMonitor.Core.Tests   : line coverage >= 90%
      - DesktopSystemMonitor.Windows.Tests: line coverage >= 70%
      - DesktopSystemMonitor.App.Avalonia.Tests: line coverage >= 90%
      - DesktopSystemMonitor.Mac.Tests: line coverage >= 90%
      - DesktopSystemMonitor.Mac.SensorHost.Tests: line coverage >= 90%
    P/Invoke 宣言 / WPF は line 閾値から除外する。

.PARAMETER OutputDir
    XML 出力先 (既定: coverage/desktop-system-monitor)
#>
[CmdletBinding()]
param(
    [string]$OutputDir = 'coverage'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$outDir = Join-Path $projectRoot $OutputDir
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$targets = @(
    @{
        Name = 'Core.Tests'
        Csproj = 'tests/unit/Core.Tests/DesktopSystemMonitor.Core.Tests.csproj'
        Threshold = 90
        Include = 'DesktopSystemMonitor.Core'
    },
    @{
        Name = 'Windows.Tests'
        Csproj = 'tests/unit/Windows.Tests/DesktopSystemMonitor.Windows.Tests.csproj'
        Threshold = 70
        Include = 'DesktopSystemMonitor.Windows'
        ExcludeByFile = '**/Cpu/**,**/Gpu/GpuMetricSource.cs,**/Gpu/DxgiAdapterEnumerator.cs,**/Gpu/DxgiAdapterInfo.cs,**/Network/IpHelperInterop.cs,**/Battery/PowerStatusInterop.cs,**/Disk/VolumeDiskInterop.cs,**/Processes/ProcessIoInterop.cs,**/Pdh/**,**/Window/**,**/obj/**'
        WindowsOnly = $true
    },
    @{
        Name = 'App.Avalonia.Tests'
        Csproj = 'tests/unit/App.Avalonia.Tests/DesktopSystemMonitor.App.Avalonia.Tests.csproj'
        Threshold = 90
        Include = 'DesktopSystemMonitor'
    },
    @{
        Name = 'Mac.Tests'
        Csproj = 'tests/unit/Mac.Tests/DesktopSystemMonitor.Mac.Tests.csproj'
        Threshold = 90
        Include = 'DesktopSystemMonitor.Mac'
        ExcludeByFile = '**/obj/**'
        MacOnly = $true
    },
    @{
        Name = 'Mac.SensorHost.Tests'
        Csproj = 'tests/unit/Mac.SensorHost.Tests/DesktopSystemMonitor.Mac.SensorHost.Tests.csproj'
        Threshold = 90
        Include = 'DesktopSystemMonitor.Mac.SensorHost'
        ExcludeByFile = '**/obj/**'
        MacOnly = $true
    }
)

$failed = $false
foreach ($target in $targets) {
    if ($target.ContainsKey('WindowsOnly') -and $target.WindowsOnly -and -not $IsWindows) {
        Write-Host "Skipping $($target.Name): not Windows." -ForegroundColor Yellow
        continue
    }
    if ($target.ContainsKey('MacOnly') -and $target.MacOnly -and -not $IsMacOS) {
        Write-Host "Skipping $($target.Name): macOS-only target; run this gate on Mac Studio." -ForegroundColor Yellow
        continue
    }
    $csproj = Join-Path $projectRoot $target.Csproj
    $projOut = Join-Path $outDir ("{0}-{1}" -f $target.Name, [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $projOut -Force | Out-Null
    Write-Host "==> $($target.Name) coverage"
    $args = @(
        'test', $csproj,
        '--configuration', 'Release',
        '--nologo',
        '--collect', 'XPlat Code Coverage',
        '--results-directory', $projOut,
        '--',
        "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura",
        "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[$($target.Include)]*"
    )
    if ($target.ContainsKey('ExcludeByFile')) {
        $args += "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile=$($target.ExcludeByFile)"
    }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$($target.Name) test failed" -ForegroundColor Red
        $failed = $true
        continue
    }
    $reports = Get-ChildItem -Path $projOut -Recurse -Filter 'coverage.cobertura.xml'
    $reportList = @($reports)
    if ($reportList.Count -ne 1) {
        Write-Host "Expected exactly one coverage report for $($target.Name), found $($reportList.Count)" -ForegroundColor Red
        $failed = $true
        continue
    }
    $report = $reportList[0]
    [xml]$xml = Get-Content $report.FullName
    $lineRate = [double]$xml.coverage.'line-rate'
    $percent = [math]::Round($lineRate * 100, 2)
    Write-Host ("{0} line coverage = {1}% (threshold {2}%)" -f $target.Name, $percent, $target.Threshold)
    if ($percent -lt $target.Threshold) {
        Write-Host "Threshold not met for $($target.Name)" -ForegroundColor Red
        $failed = $true
    }
}
if ($failed) {
    exit 1
}
Write-Host "Coverage targets met." -ForegroundColor Green
