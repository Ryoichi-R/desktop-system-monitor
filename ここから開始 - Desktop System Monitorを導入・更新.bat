@echo off
setlocal DisableDelayedExpansion

set "DSM_EXIT=1"
set "DSM_ROOT=%~dp0"
set "DSM_PROJECT=%~dp0project"
set "DSM_REBUILD=%~dp0project\scripts\rebuild-desktop-system-monitor.ps1"
set "DSM_INSTALLER=%~dp0project\installer\Install-DesktopSystemMonitor.ps1"

if "%DSM_ROOT:~0,2%"=="\\" (
  echo UNC paths are not supported. Move the entire folder to a drive-letter path.
  set "DSM_EXIT=4"
  goto :finish
)

set "DSM_FIRST=%~1"
set "DSM_SECOND=%~2"
if defined DSM_SECOND (
  echo Only one install parent directory argument is accepted.
  set "DSM_EXIT=2"
  goto :finish
)

if not exist "%DSM_REBUILD%" (
  echo Rebuild script is missing: "%DSM_REBUILD%"
  set "DSM_EXIT=5"
  goto :finish
)
if not exist "%DSM_INSTALLER%" (
  echo Installer is missing: "%DSM_INSTALLER%"
  set "DSM_EXIT=5"
  goto :finish
)

set "DSM_ARCH=%PROCESSOR_ARCHITEW6432%"
if not defined DSM_ARCH set "DSM_ARCH=%PROCESSOR_ARCHITECTURE%"
if /I "%DSM_ARCH%"=="AMD64" set "DSM_RUNTIME=win-x64"
if /I "%DSM_ARCH%"=="x86_64" set "DSM_RUNTIME=win-x64"
if /I "%DSM_ARCH%"=="ARM64" set "DSM_RUNTIME=win-arm64"
if not defined DSM_RUNTIME (
  echo Unsupported processor architecture: "%DSM_ARCH%"
  set "DSM_EXIT=4"
  goto :finish
)

if defined DSM_INSTALLER_PROBE goto :probe

where pwsh.exe >nul 2>&1
if errorlevel 1 (
  echo PowerShell 7 ^(pwsh.exe^) was not found.
  echo Install PowerShell 7 and try again.
  set "DSM_EXIT=1"
  goto :finish
)
where dotnet.exe >nul 2>&1
if errorlevel 1 (
  echo .NET SDK ^(dotnet.exe^) was not found.
  echo Install the .NET SDK required by project\global.json and try again.
  set "DSM_EXIT=1"
  goto :finish
)
set "DSM_SDK_OK="
for /f "tokens=1 delims=." %%M in ('dotnet --list-sdks 2^>nul') do (
  if %%M GEQ 10 set "DSM_SDK_OK=1"
)
if not defined DSM_SDK_OK (
  echo .NET SDK 10.x or later was not found ^(required by project\global.json^).
  echo Installed SDKs:
  dotnet --list-sdks 2>nul
  echo Install a .NET 10 SDK and try again.
  set "DSM_EXIT=1"
  goto :finish
)

echo [1/2] Rebuilding Desktop System Monitor from the current source...
pwsh.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DSM_REBUILD%" -Runtime "%DSM_RUNTIME%" -OutputRoot "%DSM_PROJECT%"
set "DSM_EXIT=%ERRORLEVEL%"
if not "%DSM_EXIT%"=="0" (
  echo Rebuild failed. Installation was not started.
  goto :finish
)

echo.
echo [2/2] Installing or updating Desktop System Monitor...
if defined DSM_FIRST goto :install_with_parent
powershell.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DSM_INSTALLER%" -PackageRoot "%DSM_PROJECT%\installer" -Runtime "%DSM_RUNTIME%"
goto :install_finished

:install_with_parent
powershell.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DSM_INSTALLER%" -PackageRoot "%DSM_PROJECT%\installer" -Runtime "%DSM_RUNTIME%" -ParentDirectory "%DSM_FIRST%"

:install_finished
set "DSM_EXIT=%ERRORLEVEL%"
if "%DSM_EXIT%"=="0" echo Desktop System Monitor rebuild and installation completed.
goto :finish

:probe
powershell.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DSM_INSTALLER_PROBE%" -Runtime "%DSM_RUNTIME%"
set "DSM_EXIT=%ERRORLEVEL%"

:finish
if not "%DSM_EXIT%"=="0" echo Desktop System Monitor update exited with code %DSM_EXIT%.
if not "%DSM_NO_PAUSE%"=="1" pause
endlocal & exit /b %DSM_EXIT%
