@echo off
setlocal
chcp 65001 >nul
set "demo_args=%*"
if "%~1"=="--check" set "demo_args=-Check"
if "%~1"=="--auto" set "demo_args=-Auto"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "D:\otus_project\demo-importer.ps1" %demo_args%
set "demo_result=%errorlevel%"
if "%~1"=="" pause
exit /b %demo_result%
