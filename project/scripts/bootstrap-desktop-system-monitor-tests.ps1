[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version = '5.9.0',
    [string]$ModuleRoot = '.test-deps/modules'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$modulePath = if ([IO.Path]::IsPathFullyQualified($ModuleRoot)) { $ModuleRoot } else { Join-Path $projectRoot $ModuleRoot }
New-Item -ItemType Directory -Path $modulePath -Force | Out-Null

$existing = @(Get-ChildItem -LiteralPath $modulePath -Directory -Filter 'Pester' -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Directory -Filter $Version -ErrorAction SilentlyContinue
    })
if ($existing.Count -eq 0) {
    Write-Host "Saving Pester $Version to $modulePath" -ForegroundColor Cyan
    Save-Module -Name Pester -RequiredVersion $Version -Path $modulePath -Force
}
$installed = @(Get-ChildItem -LiteralPath $modulePath -Directory -Filter 'Pester' | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Directory -Filter $Version -ErrorAction SilentlyContinue
    })
if ($installed.Count -ne 1) { throw "Pinned Pester $Version was not installed exactly once under $modulePath." }
$manifest = [ordered]@{
    module = 'Pester'
    version = $Version
    path = [IO.Path]::GetRelativePath($projectRoot, $installed[0].FullName).Replace('\', '/')
    files = @($installed[0].GetFiles('*', [IO.SearchOption]::AllDirectories) | ForEach-Object {
            [ordered]@{ path = [IO.Path]::GetRelativePath($installed[0].FullName, $_.FullName).Replace('\', '/'); bytes = $_.Length }
        })
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $modulePath 'pester-pin.json') -Encoding UTF8
Write-Host "Pester $Version is ready at $($installed[0].FullName)" -ForegroundColor Green
