@echo off
setlocal
cd /d "%~dp0"

where pwsh.exe >nul 2>&1
if errorlevel 1 (
  echo PowerShell 7 ^(pwsh.exe^) was not found.
  echo Install PowerShell 7 and try again.
  pause
  exit /b 1
)

set "DSM_PORTABLE_ARG="
if /I "%DSM_PORTABLE_ONLY%"=="1" set "DSM_PORTABLE_ARG=-PortableOnly"
if "%~1"=="" (
  pwsh.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\rebuild-desktop-system-monitor.ps1" -Runtime win-arm64 -SelectOutputRoot %DSM_PORTABLE_ARG%
) else (
  pwsh.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\rebuild-desktop-system-monitor.ps1" -Runtime win-arm64 -OutputRoot "%~f1\." %DSM_PORTABLE_ARG%
)
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
  echo ARM64 update failed. Review the error above.
  pause
  exit /b %EXIT_CODE%
)

echo ARM64 update completed successfully.
pause
exit /b 0
