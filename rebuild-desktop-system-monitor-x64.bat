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

if "%~1"=="" (
  pwsh.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\rebuild-desktop-system-monitor.ps1" -Runtime win-x64 -SelectOutputRoot
) else (
  pwsh.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\rebuild-desktop-system-monitor.ps1" -Runtime win-x64 -OutputRoot "%~f1\."
)
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
  echo x64 update failed. Review the error above.
  pause
  exit /b %EXIT_CODE%
)

echo x64 update completed successfully.
pause
exit /b 0
