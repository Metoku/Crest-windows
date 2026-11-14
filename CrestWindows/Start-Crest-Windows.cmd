@echo off
cd /d "%~dp0"
echo Crest Windows preview - startup diagnostics
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch-Crest.ps1"
echo.
pause
