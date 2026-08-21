@echo off
setlocal
cd /d "%~dp0"
echo Audio Limits 1.0.0-rc.2 GitHub release-candidate build
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1"
if errorlevel 1 (
  echo.
  echo The build did not complete successfully.
  echo Leave this window open and keep the complete output.
  echo.
  pause
  exit /b 1
)
echo.
echo Build completed successfully.
echo User-facing release assets are in release\.
echo Detailed build information is in artifacts\RELEASE_REPORT.txt.
echo.
pause
