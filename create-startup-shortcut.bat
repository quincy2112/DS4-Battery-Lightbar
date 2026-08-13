@echo off
REM Create startup shortcut for DS4 Battery Lightbar Mapper

setlocal enabledelayedexpansion

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"
REM Remove trailing backslash
set "SCRIPT_DIR=!SCRIPT_DIR:~0,-1!"

REM Get the app executable path
set "APP_PATH=!SCRIPT_DIR!\bin\Release\net6.0-windows\DS4BatteryMapper.exe"

REM Check if the app exists
if not exist "!APP_PATH!" (
    echo Error: App not found at !APP_PATH!
    echo Please build the app in Release mode first.
    pause
    exit /b 1
)

REM Get the Startup folder path
set "STARTUP_FOLDER=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

REM Create a shortcut using PowerShell
powershell -NoProfile -Command "^
$WshShell = New-Object -ComObject WScript.Shell; ^
$Shortcut = $WshShell.CreateShortcut('%STARTUP_FOLDER%\DS4 Battery Lightbar.lnk'); ^
$Shortcut.TargetPath = '!APP_PATH!'; ^
$Shortcut.WorkingDirectory = '!SCRIPT_DIR!'; ^
$Shortcut.Description = 'DS4 Battery Lightbar Mapper - Runs on startup'; ^
$Shortcut.Save(); ^
Write-Host 'Shortcut created at %STARTUP_FOLDER%\DS4 Battery Lightbar.lnk'
"

echo.
echo Startup shortcut created successfully!
echo The app will run automatically on the next startup.
echo You can remove it anytime from: %STARTUP_FOLDER%
pause
