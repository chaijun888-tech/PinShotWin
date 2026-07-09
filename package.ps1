$ErrorActionPreference = "Stop"

$releaseRoot = Join-Path $PSScriptRoot "release"
$releaseDir = Join-Path $releaseRoot "PinShotWin"
$zipPath = Join-Path $releaseRoot "PinShotWin.zip"
$exePath = Join-Path $PSScriptRoot "bin\PinShotWin.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    & (Join-Path $PSScriptRoot "build.ps1")
}

Remove-Item -LiteralPath $releaseDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Copy-Item -LiteralPath $exePath -Destination (Join-Path $releaseDir "PinShotWin.exe") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "installer\install.ps1") -Destination (Join-Path $releaseDir "install.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "installer\uninstall.ps1") -Destination (Join-Path $releaseDir "uninstall.ps1") -Force

$readme = @'
PinShotWin

Quick start:
1. Double-click PinShotWin.exe. The app runs in the system tray.
2. Press F1 to capture. Press Esc to cancel.

Install for current Windows user:
1. Open PowerShell in this folder.
2. Run: powershell -ExecutionPolicy Bypass -File .\install.ps1
3. The installer creates Desktop and Start Menu shortcuts and enables startup.

Uninstall:
1. Run "Uninstall PinShotWin" from Start Menu, or run:
   powershell -ExecutionPolicy Bypass -File .\uninstall.ps1

Usage:
1. Drag to select, then adjust the blue frame or handles.
2. Toolbar actions: copy, save, pin, cancel.
3. Preview shortcuts: Enter/Ctrl+C copy, Ctrl+S save, Ctrl+P pin.
4. Pinned images support right-click: copy, save, close.
5. Tray menu supports settings, recent screenshots, and exit.
6. Recent screenshots are in-memory only, keep latest 10, and clear on exit.

If Windows shows an unknown publisher warning, choose "More info" and continue.
'@

Set-Content -LiteralPath (Join-Path $releaseDir "README.txt") -Value $readme -Encoding UTF8

Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -LiteralPath $releaseDir -DestinationPath $zipPath -Force

Write-Host "Packaged $zipPath"
