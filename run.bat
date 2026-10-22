@echo off
title Launch ShieldBlock
echo ===================================================
echo             Starting ShieldBlock
echo ===================================================
echo.

if not exist "%~dp0dist\ShieldBlock.exe" (
    echo [ERROR] Binary not found in dist folder.
    pause
    exit /b 1
)

echo Initializing environment...
start "" "%~dp0dist\ShieldBlock.exe"
echo ShieldBlock launched successfully!
exit /b 0
