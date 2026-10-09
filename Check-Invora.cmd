@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Test-Invora.ps1" -SaveReport
set "invoraCheckExit=%errorlevel%"
pause
exit /b %invoraCheckExit%
