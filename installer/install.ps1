$ErrorActionPreference = "Stop"

$appName = "PinShotWin"
$sourceExe = Join-Path $PSScriptRoot "PinShotWin.exe"
$sourceVersion = Join-Path $PSScriptRoot "VERSION.txt"
$installDir = Join-Path $env:LOCALAPPDATA $appName
$installExe = Join-Path $installDir "PinShotWin.exe"
$installVersion = Join-Path $installDir "VERSION.txt"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "$appName.lnk"
$startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) $appName
$startMenuShortcut = Join-Path $startMenuDir "$appName.lnk"
$uninstallShortcut = Join-Path $startMenuDir "Uninstall $appName.lnk"
$uninstallScript = Join-Path $installDir "uninstall.ps1"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "PinShotWin.exe was not found. Run install.ps1 from the release package folder."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installExe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination $uninstallScript -Force
if (Test-Path -LiteralPath $sourceVersion) {
    Copy-Item -LiteralPath $sourceVersion -Destination $installVersion -Force
}

$shell = New-Object -ComObject WScript.Shell

function New-AppShortcut {
    param(
        [string]$Path,
        [string]$Target,
        [string]$Arguments = ""
    )

    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = Split-Path -Parent $Target
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Save()
}

New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
New-AppShortcut -Path $desktopShortcut -Target $installExe
New-AppShortcut -Path $startMenuShortcut -Target $installExe
New-AppShortcut -Path $uninstallShortcut -Target "powershell.exe" -Arguments "-ExecutionPolicy Bypass -File `"$uninstallScript`""

New-Item -Path $runKeyPath -Force | Out-Null
Set-ItemProperty -Path $runKeyPath -Name $appName -Value "`"$installExe`""

Write-Host "$appName installed to $installDir"
if (Test-Path -LiteralPath $installVersion) {
    $version = (Get-Content -LiteralPath $installVersion -Raw).Trim()
    Write-Host "Version: $version"
}
Write-Host "Desktop and Start Menu shortcuts were created. Startup is enabled."
