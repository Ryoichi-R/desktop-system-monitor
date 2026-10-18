[CmdletBinding()]
param(
    [ValidateSet('WindowsPowerShell51', 'PowerShell7')]
    [string]$HostContract,
    [switch]$IncludeMigratedLayout,
    [string]$DistributionPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$version = '5.9.0'
$isWindowsHost = $true
if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) { $isWindowsHost = [bool]$IsWindows }
$major = [int]$PSVersionTable.PSVersion.Major
if ($HostContract -eq 'WindowsPowerShell51' -and ($major -ne 5 -or -not $isWindowsHost)) { throw 'WindowsPowerShell51 contract requires Windows PowerShell 5.1.' }
if ($HostContract -eq 'PowerShell7' -and $major -lt 7) { throw 'PowerShell7 contract requires PowerShell 7 or newer.' }

$bootstrap = Join-Path $PSScriptRoot 'bootstrap-desktop-system-monitor-tests.ps1'
$moduleRoot = Join-Path $projectRoot '.test-deps/modules'
$pesterModule = Join-Path $moduleRoot "Pester\$version"
if (-not (Test-Path -LiteralPath $pesterModule -PathType Container)) {
    & $bootstrap -Version $version
    if ($LASTEXITCODE -ne 0) { throw 'Pester bootstrap failed.' }
}
$env:PSModulePath = $moduleRoot + [IO.Path]::PathSeparator + $env:PSModulePath
Import-Module Pester -RequiredVersion $version -Force

$paths = @(
    (Join-Path $projectRoot 'tests/unit/installer'),
    (Join-Path $projectRoot 'tests/integration/installer')
)
$configuration = New-PesterConfiguration
$configuration.Run.Path = $paths
$configuration.Run.PassThru = $true
$configuration.Output.Verbosity = 'Detailed'
$excluded = @()
if (-not $isWindowsHost) { $excluded += 'WindowsOnly' }
if (-not $IncludeMigratedLayout) { $excluded += 'RequiresMigratedLayout' }
if ([string]::IsNullOrWhiteSpace($DistributionPath)) { $excluded += 'DistributionLayout' }
if ($excluded.Count -gt 0) { $configuration.Filter.ExcludeTag = $excluded }
if (-not [string]::IsNullOrWhiteSpace($DistributionPath)) {
    $env:DSM_DISTRIBUTION_PATH = [IO.Path]::GetFullPath($DistributionPath)
}
$result = Invoke-Pester -Configuration $configuration
if ($result.FailedCount -gt 0) { exit 1 }
exit 0
