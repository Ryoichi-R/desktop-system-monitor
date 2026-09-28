[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath
)

# Read-only validation for a locally produced macOS bundle. This script does
# not install, launch, sign, or modify the bundle.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsMacOS) { throw 'The macOS bundle contract must be checked on macOS.' }

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    $output = @(& $FilePath @ArgumentList 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage ExitCode=$LASTEXITCODE Output=$($output -join ' ')"
    }
    return $output
}

function Get-PlistRawValue {
    param(
        [Parameter(Mandatory)][string]$PlistPath,
        [Parameter(Mandatory)][string]$Key
    )

    $output = Invoke-Checked -FilePath '/usr/bin/plutil' `
        -ArgumentList @('-extract', $Key, 'raw', '-o', '-', '--', $PlistPath) `
        -FailureMessage "Info.plist key is missing: $Key."
    return (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
}

$bundle = [IO.Path]::GetFullPath($BundlePath)
if (-not $bundle.EndsWith('.app', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Bundle path must end in .app: $bundle"
}
if (-not (Test-Path -LiteralPath $bundle -PathType Container)) {
    throw "Bundle directory does not exist: $bundle"
}

$contents = Join-Path $bundle 'Contents'
$macos = Join-Path $contents 'MacOS'
$plist = Join-Path $contents 'Info.plist'
$appExecutable = Join-Path $macos 'DesktopSystemMonitor'
$hostExecutable = Join-Path $macos 'DesktopSystemMonitor.Mac.SensorHost'

foreach ($requiredPath in @($contents, $macos, $plist, $appExecutable, $hostExecutable)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -and
        -not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
        throw "Required bundle path is missing: $requiredPath"
    }
}

foreach ($executable in @($appExecutable, $hostExecutable)) {
    $item = Get-Item -LiteralPath $executable -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Bundle executable must be a regular file, not a symlink: $executable"
    }
    Invoke-Checked -FilePath '/bin/test' -ArgumentList @('-x', $executable) `
        -FailureMessage "Bundle executable is not executable: $executable" | Out-Null
    $fileDescription = (Invoke-Checked -FilePath '/usr/bin/file' -ArgumentList @('-b', $executable) `
        -FailureMessage "Unable to inspect executable: $executable" | ForEach-Object { [string]$_ }) -join ' '
    if ($fileDescription -notmatch '\barm64(?:e)?\b') {
        throw "Bundle executable is not arm64: $executable ($fileDescription)"
    }
}

Invoke-Checked -FilePath '/usr/bin/plutil' -ArgumentList @('-lint', '--', $plist) `
    -FailureMessage 'Info.plist is invalid.' | Out-Null
$bundleExecutable = Get-PlistRawValue -PlistPath $plist -Key 'CFBundleExecutable'
if ($bundleExecutable -ne 'DesktopSystemMonitor') {
    throw "Unexpected CFBundleExecutable: $bundleExecutable"
}
$bundleIdentifier = Get-PlistRawValue -PlistPath $plist -Key 'CFBundleIdentifier'
if ($bundleIdentifier -ne 'local.desktop-system-monitor') {
    throw "Unexpected CFBundleIdentifier: $bundleIdentifier"
}
$loginAgent = Get-PlistRawValue -PlistPath $plist -Key 'LSUIElement'
if ($loginAgent -notin @('true', '1')) {
    throw "LSUIElement must be true: $loginAgent"
}

$resources = Join-Path $contents 'Resources'
foreach ($relativePath in @('THIRD-PARTY-NOTICES.md', 'dependency-inventory.json', 'licenses/Avalonia-12.1.3/LICENSE.txt', 'licenses/MicroCom.Runtime-0.11.6/LICENSE.txt', 'licenses/Tmds.DBus.Protocol-0.94.1/LICENSE.txt', 'licenses/skiasharp.nativeassets.macos-3.119.4/LICENSE.txt', 'licenses/harfbuzzsharp.nativeassets.macos-8.3.1.3/LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $resources $relativePath) -PathType Leaf)) { throw "Missing bundle notice: $relativePath" }
}
$inventory = @(Get-Content -LiteralPath (Join-Path $resources 'dependency-inventory.json') -Raw | ConvertFrom-Json)
if ($inventory -match '^(DesktopSystemMonitor.Windows/|Microsoft.WindowsDesktop.App)') { throw 'Windows project/runtime leaked into macOS dependency graph.' }
$runtime = @($inventory | Where-Object { $_ -like 'Microsoft.NETCore.App.Runtime.osx-arm64/*' })
if ($runtime.Count -ne 1) { throw 'Runtime inventory must contain exactly one macOS runtime pack.' }
$version = $runtime[0].Split('/')[1]
foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    if (-not (Test-Path -LiteralPath (Join-Path $resources "licenses/dotnet-runtime-$version/$notice") -PathType Leaf)) { throw 'Runtime notice is missing.' }
}

foreach ($signedPath in @($appExecutable, $hostExecutable, $bundle)) {
    Invoke-Checked -FilePath '/usr/bin/codesign' -ArgumentList @('--verify', '--strict', '--', $signedPath) `
        -FailureMessage "Code-signature verification failed: $signedPath" | Out-Null
}

Write-Host "macOS publication contract passed: $bundle"
