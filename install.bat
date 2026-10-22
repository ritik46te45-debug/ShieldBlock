@echo off
setlocal enabledelayedexpansion
title ShieldBlock Installer
color 0B

echo ========================================================
echo           ShieldBlock - One-Click Installer
echo ========================================================
echo.

:: Check for administrative privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [INFO] Requesting Administrator privileges to set up network filters...
    powershell -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b 0
)

echo [1/3] Running pre-flight setup and initializing filter lists...
"%~dp0dist\ShieldBlock-Setup.exe"
if %errorlevel% neq 0 (
    echo [ERROR] Setup encountered an issue.
    pause
    exit /b 1
)

echo.
echo [2/3] Creating Desktop Shortcut...
powershell -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'ShieldBlock.lnk')); $s.TargetPath = '%~dp0dist\ShieldBlock.exe'; $s.WorkingDirectory = '%~dp0dist'; $s.Description = 'ShieldBlock System-Wide Ad Blocker'; $s.Save()"
echo [PASS] Desktop Shortcut created!

echo.
echo [3/3] Launching ShieldBlock...
start "" "%~dp0dist\ShieldBlock.exe"

echo.
echo ========================================================
echo       ShieldBlock Installed and Started Successfully!
echo ========================================================
echo.
timeout /t 3 >nul
exit /b 0
