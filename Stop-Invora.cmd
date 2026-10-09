@echo off
cd /d "%~dp0"
docker compose stop
set "invoraStopExit=%errorlevel%"
if not "%invoraStopExit%"=="0" pause
exit /b %invoraStopExit%
