param(
    [string]$AppPath,
    [string]$WorkingDir
)

$StartupFolder = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
$ShortcutPath = "$StartupFolder\DS4 Battery Lightbar.lnk"

try {
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $AppPath
    $Shortcut.WorkingDirectory = $WorkingDir
    $Shortcut.Description = "DS4 Battery Lightbar Mapper - Runs on startup"
    $Shortcut.Save()
    Write-Host "Shortcut created at $ShortcutPath"
    exit 0
}
catch {
    Write-Host "Error creating shortcut: $_"
    exit 1
}
