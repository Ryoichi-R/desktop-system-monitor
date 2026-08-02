<#
.SYNOPSIS
    Desktop System MonitorをRID別にclean、build、publishする。

.PARAMETER Runtime
    出力対象のWindows RID。win-arm64またはwin-x64。

.PARAMETER OutputRoot
    外部buildディレクトリを作成する親ディレクトリ。相対pathはproject root基準。
    省略時またはproject rootを指定した場合はproject rootのdistを使用する。

.PARAMETER SelectOutputRoot
    OutputRootが省略された場合、Windowsのfolder選択dialogを表示する。

.PARAMETER PortableOnly
    Portable出力だけを更新し、canonical installer payloadは変更しない。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,
    [string]$OutputRoot,
    [Alias('PromptForOutputRoot')]
    [switch]$SelectOutputRoot,
    [switch]$PortableOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    if ($pathFull -ne $rootFull -and
        -not $pathFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the allowed root: $pathFull"
    }
    return $pathFull
}

function Assert-ManagedDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ExpectedLeaf
    )

    $pathFull = Assert-PathWithinRoot -Path $Path -Root $Root
    if ((Split-Path -Leaf $pathFull) -cne $ExpectedLeaf) {
        throw "Refusing to manage an unexpected directory: $pathFull"
    }
    if (Test-Path -LiteralPath $pathFull) {
        if (-not (Test-Path -LiteralPath $pathFull -PathType Container)) {
            throw "Managed path exists but is not a directory: $pathFull"
        }
        if ((Get-Item -LiteralPath $pathFull -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Managed directory cannot be a symbolic link or junction: $pathFull"
        }
    }
    return $pathFull
}

function Select-OutputRootFolder {
    param([Parameter(Mandatory)][string]$InitialDirectory)

    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
        throw 'フォルダー選択画面を表示するには、PowerShellを-STAオプション付きで起動してください。'
    }

    Add-Type -AssemblyName System.Windows.Forms
    $dialog = [Windows.Forms.FolderBrowserDialog]::new()
    try {
        $dialog.Description = 'Desktop System Monitorを保存する親フォルダーを選択してください。project root以外を選択すると、その中に DesktopSystemMonitorBuilds フォルダーを作成します。'
        $dialog.UseDescriptionForTitle = $false
        $dialog.AutoUpgradeEnabled = $true
        $dialog.ShowNewFolderButton = $true
        $dialog.SelectedPath = $InitialDirectory
        $result = $dialog.ShowDialog()
        if ($result -ne [Windows.Forms.DialogResult]::OK -or
            [string]::IsNullOrWhiteSpace($dialog.SelectedPath)) {
            throw '保存先の選択がキャンセルされました。ファイルは生成していません。'
        }
        return $dialog.SelectedPath
    }
    finally {
        $dialog.Dispose()
    }
}

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) {
            throw "Invalid PE header offset: $Path"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "Invalid PE signature: $Path"
        }
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
$buildPathsScript = Assert-PathWithinRoot -Path (Join-Path $PSScriptRoot 'resolve-desktop-system-monitor-build-paths.ps1') -Root $projectRoot
. $buildPathsScript
$payloadHelper = Assert-PathWithinRoot -Path (Join-Path $PSScriptRoot 'promote-desktop-system-monitor-installer-payload.ps1') -Root $projectRoot
. $payloadHelper
if ($SelectOutputRoot -and [string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Select-OutputRootFolder -InitialDirectory $projectRoot
}

$buildPaths = Resolve-DesktopSystemMonitorBuildPaths -ProjectRoot $projectRoot -OutputRoot $OutputRoot
$resolvedOutputRoot = $buildPaths.OutputParent
if ($buildPaths.UsesExternalRoot -and (Split-Path -Leaf $resolvedOutputRoot) -ieq 'DesktopSystemMonitorBuilds') {
    throw "Specify the parent folder in which DesktopSystemMonitorBuilds will be created, not DesktopSystemMonitorBuilds itself: $resolvedOutputRoot"
}
if (Test-Path -LiteralPath $resolvedOutputRoot) {
    if (-not (Test-Path -LiteralPath $resolvedOutputRoot -PathType Container)) {
        throw "The selected output parent exists but is not a directory: $resolvedOutputRoot"
    }
    if ((Get-Item -LiteralPath $resolvedOutputRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "The selected output parent cannot be a symbolic link or junction: $resolvedOutputRoot"
    }
}

$appProject = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'src/DesktopSystemMonitor.App/DesktopSystemMonitor.App.csproj') -Root $projectRoot
$projectDistRoot = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'dist') -Root $projectRoot
if ($resolvedOutputRoot -ine $projectRoot -and
    ($resolvedOutputRoot -ieq $projectDistRoot -or
        $resolvedOutputRoot.StartsWith($projectDistRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
    throw "The selected parent folder cannot be inside the project dist directory: $resolvedOutputRoot"
}
$managedRootLeaf = if ($buildPaths.UsesExternalRoot) { 'DesktopSystemMonitorBuilds' } else { 'dist' }
$managedRoot = Assert-ManagedDirectory -Path $buildPaths.ManagedRoot -Root $resolvedOutputRoot -ExpectedLeaf $managedRootLeaf
$outputDir = Assert-ManagedDirectory -Path (Join-Path $managedRoot "desktop-system-monitor-$Runtime") -Root $managedRoot -ExpectedLeaf "desktop-system-monitor-$Runtime"
$stagingName = ".staging-desktop-system-monitor-$Runtime-$([guid]::NewGuid().ToString('N'))"
$stagingDir = Assert-ManagedDirectory -Path (Join-Path $managedRoot $stagingName) -Root $managedRoot -ExpectedLeaf $stagingName
$artifactsName = ".artifacts-desktop-system-monitor-$Runtime-$([guid]::NewGuid().ToString('N'))"
$artifactsDir = Assert-ManagedDirectory -Path (Join-Path $managedRoot $artifactsName) -Root $managedRoot -ExpectedLeaf $artifactsName
$backupDir = Assert-ManagedDirectory -Path (Join-Path $managedRoot "_backup-desktop-system-monitor-$Runtime") -Root $managedRoot -ExpectedLeaf "_backup-desktop-system-monitor-$Runtime"
$payloadStagingName = ".payload-staging-$Runtime-$([guid]::NewGuid().ToString('N'))"
$payloadStagingDir = Assert-ManagedDirectory -Path (Join-Path $projectDistRoot $payloadStagingName) -Root $projectDistRoot -ExpectedLeaf $payloadStagingName
$packageScript = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'scripts/build-desktop-system-monitor-installer-package.ps1') -Root $projectRoot
$publishScript = Assert-PathWithinRoot -Path (Join-Path $projectRoot 'scripts/publish-desktop-system-monitor.ps1') -Root $projectRoot
$mutexHash = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($projectRoot.ToUpperInvariant()))).Substring(0, 16)
$updateMutex = [Threading.Mutex]::new($false, "Local\DesktopSystemMonitor-Rebuild-$mutexHash")
$updateLockTaken = $false
$published = $false
$payloadLock = $null
$payloadPromotion = $null
$payloadPromotionAttempted = $false
$payloadPromoted = $false

try {
    try {
        $updateLockTaken = $updateMutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $updateLockTaken = $true
    }
    if (-not $updateLockTaken) {
        throw 'Another Desktop System Monitor update is already running. Wait for it to finish, then try again.'
    }

    New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $managedRoot -Force | Out-Null

    Write-Host "Output parent: $resolvedOutputRoot" -ForegroundColor Cyan
    Write-Host "Managed output: $managedRoot" -ForegroundColor Cyan
    Write-Host "The managed $Runtime directory will be replaced after a successful publish." -ForegroundColor Yellow

    $runningTarget = @(Get-Process -Name 'DesktopSystemMonitor' -ErrorAction SilentlyContinue | Where-Object {
            $processPath = $_.Path
            $processPath -and [IO.Path]::GetFullPath($processPath).StartsWith(
                $outputDir + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase
            )
        })
    if ($runningTarget.Count -gt 0) {
        throw "$Runtime build is running from the update target. Exit it from the task tray, then run this updater again."
    }

    Invoke-DotNetStep -Name "Restore ($Runtime)" -Arguments @(
        'restore', $appProject,
        '--runtime', $Runtime,
        '--nologo'
    )
    Invoke-DotNetStep -Name "Clean Release ($Runtime)" -Arguments @(
        'clean', $appProject,
        '--configuration', 'Release',
        '--runtime', $Runtime,
        '--verbosity', 'normal',
        '--nologo'
    )
    Invoke-DotNetStep -Name "Build Release ($Runtime)" -Arguments @(
        'build', $appProject,
        '--configuration', 'Release',
        '--runtime', $Runtime,
        '--no-restore',
        '--nologo'
    )

    Write-Host "==> Publish $Runtime" -ForegroundColor Cyan
    Push-Location -LiteralPath $projectRoot
    try {
        & $publishScript `
            -Runtime $Runtime `
            -OutputDir $stagingDir `
            -DistRoot $managedRoot `
            -ArtifactsPath $artifactsDir
        if ($LASTEXITCODE -ne 0) {
            throw "Publish $Runtime failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    $stagedExe = Assert-PathWithinRoot -Path (Join-Path $stagingDir 'DesktopSystemMonitor.exe') -Root $stagingDir
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
        throw "Published executable was not found: $stagedExe"
    }
    $expectedMachine = if ($Runtime -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
    $actualMachine = Get-PeMachine -Path $stagedExe
    if ($actualMachine -ne $expectedMachine) {
        throw ('Published executable architecture mismatch. Expected 0x{0:X4}, actual 0x{1:X4}.' -f $expectedMachine, $actualMachine)
    }

    if (-not $PortableOnly) {
        Write-Host "==> Build installer payload staging ($Runtime)" -ForegroundColor Cyan
        Push-Location -LiteralPath $projectRoot
        try {
            & $packageScript `
                -Runtime $Runtime `
                -PackageChannel 'local-preview' `
                -OutputRoot ([IO.Path]::GetRelativePath($projectRoot, $payloadStagingDir)) `
                -StageRoot $stagingDir `
                -ArtifactsRoot $artifactsDir `
                -AllowReadinessBlocked `
                -Force
            if ($LASTEXITCODE -ne 0) { throw "Installer payload staging failed with exit code $LASTEXITCODE" }
        }
        finally {
            Pop-Location
        }
        $payloadManifestPath = Join-Path $payloadStagingDir 'payload-manifest.json'
        if (-not (Test-Path -LiteralPath $payloadManifestPath -PathType Leaf)) { throw "Payload manifest was not generated: $payloadManifestPath" }
        $payloadManifest = Get-Content -LiteralPath $payloadManifestPath -Raw | ConvertFrom-Json
        if ([int]$payloadManifest.schemaVersion -ne 2 -or [string]$payloadManifest.layoutContract -ne 'single-file-v1') {
            throw 'Generated payload manifest does not satisfy schema v2 single-file contract.'
        }
        $payloadLock = Open-DesktopSystemMonitorPayloadLock -ProjectRoot $projectRoot
        $transactionId = [guid]::NewGuid().ToString('N')
        Write-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot -State ([ordered]@{
                transactionId = $transactionId
                phase = 'prepared'
                runtime = $Runtime
                candidateLineageId = [string]$payloadManifest.candidateLineageId
                payloadDigest = Get-DesktopSystemMonitorPayloadDirectoryDigest -Path $payloadStagingDir
                portableOutput = [IO.Path]::GetRelativePath($projectRoot, $outputDir).Replace('\\', '/')
            })
    }

    if (-not $PortableOnly) {
        try {
            $payloadPromotionAttempted = $true
            $payloadPromotion = Move-DesktopSystemMonitorPayloadIntoPlace -ProjectRoot $projectRoot -StagedPayload $payloadStagingDir -TransactionId $transactionId
            $payloadPromoted = $true
            Write-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot -State ([ordered]@{
                    transactionId = $transactionId
                    phase = 'payload-promoted'
                    runtime = $Runtime
                    candidateLineageId = [string]$payloadManifest.candidateLineageId
                    payloadDigest = Get-DesktopSystemMonitorPayloadDirectoryDigest -Path $payloadPromotion.TargetPath
                    portableOutput = [IO.Path]::GetRelativePath($projectRoot, $outputDir).Replace('\\', '/')
                })
        }
        catch {
            throw "Installer payload promotion failed; portable output was not replaced: $($_.Exception.Message)"
        }
    }

    try {
        if (Test-Path -LiteralPath $outputDir) {
            if (Test-Path -LiteralPath $backupDir) {
                $null = Assert-ManagedDirectory -Path $backupDir -Root $managedRoot -ExpectedLeaf "_backup-desktop-system-monitor-$Runtime"
                Remove-Item -LiteralPath $backupDir -Recurse -Force
            }
            Move-Item -LiteralPath $outputDir -Destination $backupDir
        }
        Move-Item -LiteralPath $stagingDir -Destination $outputDir
        $published = $true
    }
    catch {
        if (-not (Test-Path -LiteralPath $outputDir) -and (Test-Path -LiteralPath $backupDir)) {
            Move-Item -LiteralPath $backupDir -Destination $outputDir
        }
        if ($payloadPromoted -and $null -ne $payloadPromotion) {
            Restore-DesktopSystemMonitorPayloadBackup -ProjectRoot $projectRoot -BackupPath $payloadPromotion.BackupPath
            Remove-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot
        }
        throw
    }

    if (-not $PortableOnly) {
        Write-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot -State ([ordered]@{
                transactionId = $transactionId
                phase = 'committed'
                runtime = $Runtime
                candidateLineageId = [string]$payloadManifest.candidateLineageId
                payloadDigest = Get-DesktopSystemMonitorPayloadDirectoryDigest -Path $payloadPromotion.TargetPath
                portableOutput = [IO.Path]::GetRelativePath($projectRoot, $outputDir).Replace('\\', '/')
            })
        Remove-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot
    }

    Write-Host "Updated $Runtime build: $outputDir" -ForegroundColor Green
    Write-Host "Executable: $(Join-Path $outputDir 'DesktopSystemMonitor.exe')" -ForegroundColor Green
    if (Test-Path -LiteralPath $backupDir) {
        Write-Host "Previous build retained for rollback: $backupDir" -ForegroundColor Yellow
    }
}
finally {
    if ($null -ne $payloadLock) {
        if (-not $published -and $payloadPromoted -and $null -ne $payloadPromotion) {
            Restore-DesktopSystemMonitorPayloadBackup -ProjectRoot $projectRoot -BackupPath $payloadPromotion.BackupPath
            Remove-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot
        }
        elseif (-not $published -and -not $payloadPromotionAttempted) {
            Remove-DesktopSystemMonitorPayloadTransaction -ProjectRoot $projectRoot
        }
        Close-DesktopSystemMonitorPayloadLock -Lock $payloadLock
    }
    if (Test-Path -LiteralPath $payloadStagingDir) {
        $safePayloadStaging = Assert-ManagedDirectory -Path $payloadStagingDir -Root $projectDistRoot -ExpectedLeaf $payloadStagingName
        Remove-Item -LiteralPath $safePayloadStaging -Recurse -Force
    }
    if (-not $published -and (Test-Path -LiteralPath $stagingDir)) {
        $safeStaging = Assert-ManagedDirectory -Path $stagingDir -Root $managedRoot -ExpectedLeaf $stagingName
        Remove-Item -LiteralPath $safeStaging -Recurse -Force
    }
    if (Test-Path -LiteralPath $artifactsDir) {
        $safeArtifacts = Assert-ManagedDirectory -Path $artifactsDir -Root $managedRoot -ExpectedLeaf $artifactsName
        Remove-Item -LiteralPath $safeArtifacts -Recurse -Force
    }
    if ($updateLockTaken) {
        $updateMutex.ReleaseMutex()
    }
    $updateMutex.Dispose()
}
