@echo off
rem Builds dist\PitWall.exe. Double-click it, or run:  build.cmd            (build only)
rem                                                     build.cmd -Install   (build + Start menu shortcut)
rem ExecutionPolicy Bypass applies to this one PowerShell process only; no system setting changes.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" %*
if errorlevel 1 (
  echo.
  echo Build failed. See the messages above.
)
if "%~1"=="" pause
