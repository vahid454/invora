@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Invora.ps1"
set "invoraStartExit=%errorlevel%"
if not "%invoraStartExit%"=="0" pause
exit /b %invoraStartExit%
