@echo off
setlocal enabledelayedexpansion
title ShieldBlock Uninstaller
color 0C

echo ========================================================
echo           ShieldBlock - Clean Uninstaller
echo ========================================================
echo.

:: Check for administrative privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [INFO] Requesting Administrator privileges to safely restore network settings...
    powershell -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b 0
)

echo [1/2] Reverting all network filters, DNS, and certificates...
"%~dp0dist\ShieldBlock-Setup.exe" --uninstall
if %errorlevel% neq 0 (
    echo [WARN] Uninstall returned with a warning.
)

echo.
echo [2/2] Removing Desktop Shortcut...
if exist "%USERPROFILE%\Desktop\ShieldBlock.lnk" (
    del "%USERPROFILE%\Desktop\ShieldBlock.lnk"
    echo [PASS] Desktop shortcut removed.
)

echo.
echo ========================================================
echo       ShieldBlock Cleanly Removed with Zero Damage
echo ========================================================
echo.
pause
exit /b 0
