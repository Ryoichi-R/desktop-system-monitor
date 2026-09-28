[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'The macOS bundle must be built on macOS.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputRoot)
$bundle = Join-Path $destination 'DesktopSystemMonitor.app'
if (Test-Path -LiteralPath $bundle) { throw 'Choose a new output directory; an existing bundle will not be overwritten.' }
$macos = Join-Path $bundle 'Contents/MacOS'
New-Item -ItemType Directory -Path $macos -Force | Out-Null
foreach ($name in @('DesktopSystemMonitor.Mac.SensorHost', 'DesktopSystemMonitor.App')) {
    $project = Join-Path $projectRoot "src/$name/$name.csproj"
    & dotnet publish $project -c Release -f net10.0 -r osx-arm64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' '-p:MacAvaloniaOnly=true' '-p:EnableCompressionInSingleFile=false' '-p:IncludeAllContentForSelfExtract=false' '-p:UsedAvaloniaProducts=' '-p:DebugType=None' '-p:DebugSymbols=false' -o $macos
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name" }
}
$resources = Join-Path $bundle 'Contents/Resources'
New-Item -ItemType Directory -Path $resources -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $resources
$licenseNames = @('Avalonia-12.1.3', 'MicroCom.Runtime-0.11.6', 'Tmds.DBus.Protocol-0.94.1', 'skiasharp.nativeassets.macos-3.119.4', 'harfbuzzsharp.nativeassets.macos-8.3.1.3', 'avalonia.angle.windows.natives-2.1.27548.20260419')
$licenseRoot = Join-Path $resources 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
foreach ($licenseName in $licenseNames) {
    $source = Join-Path $projectRoot "licenses/$licenseName"
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Missing dependency license: $licenseName" }
    Copy-Item -LiteralPath $source -Destination $licenseRoot -Recurse
}
# Bind the runtime notices to the actually resolved osx-arm64 runtime pack.
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src/DesktopSystemMonitor.App/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$runtime = @($assets.project.frameworks['net10.0'].downloadDependencies | Where-Object { $_.name -eq 'Microsoft.NETCore.App.Runtime.osx-arm64' })
if ($runtime.Count -ne 1) { throw 'Expected exactly one resolved macOS runtime pack.' }
if ($runtime[0].version -notmatch '^\[(?<version>[^,]+),\s*\k<version>\]$') { throw 'Runtime pack version must be exact.' }
$runtimeVersion = $Matches['version']
$runtimeSource = $null
foreach ($folder in $assets.packageFolders.Keys) {
    $candidate = Join-Path $folder "microsoft.netcore.app.runtime.osx-arm64/$runtimeVersion"
    if (Test-Path -LiteralPath (Join-Path $candidate 'LICENSE.TXT')) { $runtimeSource = $candidate; break }
}
if (-not $runtimeSource) { throw 'Resolved runtime license is missing.' }
$runtimeDestination = Join-Path $licenseRoot "dotnet-runtime-$runtimeVersion"
New-Item -ItemType Directory -Path $runtimeDestination -Force | Out-Null
foreach ($file in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimeSource $file) -Destination $runtimeDestination
}
$inventory = @(@($assets.libraries.Keys) + @("Microsoft.NETCore.App.Runtime.osx-arm64/$runtimeVersion") | Sort-Object)
[IO.File]::WriteAllText((Join-Path $resources 'dependency-inventory.json'), (ConvertTo-Json -InputObject $inventory))
$plist = @'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>DesktopSystemMonitor</string>
<key>CFBundleIdentifier</key><string>local.desktop-system-monitor</string>
<key>CFBundleName</key><string>Desktop System Monitor</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
'@
[IO.File]::WriteAllText((Join-Path $bundle 'Contents/Info.plist'), $plist)
& /usr/bin/plutil -lint (Join-Path $bundle 'Contents/Info.plist')
if ($LASTEXITCODE -ne 0) { throw 'Invalid Info.plist.' }
foreach ($executable in @('DesktopSystemMonitor.Mac.SensorHost', 'DesktopSystemMonitor')) {
    & /usr/bin/codesign --force --sign - (Join-Path $macos $executable)
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $executable" }
}
& /usr/bin/codesign --force --sign - $bundle
if ($LASTEXITCODE -ne 0) { throw 'Bundle signing failed.' }
& /usr/bin/codesign --verify --deep --strict $bundle
if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed.' }
$contractScript = Join-Path $PSScriptRoot 'test-publication-contract-macos.ps1'
if (-not (Test-Path -LiteralPath $contractScript -PathType Leaf)) { throw "macOS publication contract script is missing: $contractScript" }
& $contractScript -BundlePath $bundle
if ($LASTEXITCODE -ne 0) { throw 'macOS publication contract failed.' }
Write-Host "Local ad-hoc signed bundle: $bundle"
