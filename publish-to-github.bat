@echo off
setlocal enabledelayedexpansion
title ShieldBlock - Publish to GitHub
color 0A

echo ========================================================
echo        ShieldBlock - Automatic GitHub Publisher
echo ========================================================
echo.

where gh >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] GitHub CLI (gh) was not found in PATH.
    echo Please install it from https://cli.github.com/ or use Git directly.
    pause
    exit /b 1
)

echo Checking GitHub CLI login status...
gh auth status >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [ACTION REQUIRED] You need to log in to GitHub once.
    echo Opening GitHub login in browser...
    echo.
    gh auth login --hostname github.com --git-protocol https --web
)

echo.
echo [1/3] Creating public GitHub repository 'ShieldBlock'...
gh repo create ShieldBlock --public --source=. --remote=origin --push --description "Safe, high-performance system-wide Windows network ad blocker."
if %errorlevel% neq 0 (
    echo [INFO] Repo might already exist or remote is already added. Pushing master branch...
    git push -u origin master
)

echo.
echo [2/3] Creating GitHub Release v1.0.0 and uploading release binaries...
gh release create v1.0.0 "%~dp0dist\ShieldBlock-Setup.exe" "%~dp0dist\ShieldBlock.exe" "%~dp0dist\ShieldBlockService.exe" "%~dp0dist\ShieldBlockRecovery.exe" "%~dp0dist\SHA256SUMS.txt" --title "ShieldBlock v1.0.0 — Initial Release" --notes "High-performance, safety-first Windows system-wide ad and tracker blocker with zero registry corruption and fail-open internet continuity."

echo.
echo ========================================================
echo        Published Successfully to GitHub!
echo ========================================================
echo.
pause
exit /b 0
