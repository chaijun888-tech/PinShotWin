$ErrorActionPreference = "Stop"

$appName = "PinShotWin"
$installDir = Join-Path $env:LOCALAPPDATA $appName
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "$appName.lnk"
$startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) $appName
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process $appName -ErrorAction SilentlyContinue | Stop-Process -Force

Remove-ItemProperty -Path $runKeyPath -Name $appName -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "$appName uninstalled."
