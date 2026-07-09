param(
    [switch]$IncludeInstaller
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root "VERSION") -Raw).Trim()
$releaseDir = Join-Path $root "release\PinShotWin"
$exePath = Join-Path $releaseDir "PinShotWin.exe"
$zipPath = Join-Path $root "release\PinShotWin.zip"
$versionedZipPath = Join-Path $root "release\PinShotWin-$version.zip"
$installerPath = Join-Path $root "release\PinShotWinSetup-$version.exe"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-PowerShellSyntax {
    param([string]$Path)

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    Assert-True ($errors.Count -eq 0) "$Path parse failed: $($errors[0].Message)"
}

Write-Host "Checking PowerShell syntax..."
Get-ChildItem -Path $root -Filter "*.ps1" -Recurse |
    Where-Object { $_.FullName -notmatch "\\(bin|obj|release)\\" } |
    ForEach-Object { Test-PowerShellSyntax $_.FullName }

Write-Host "Building and packaging..."
Get-Process PinShotWin -ErrorAction SilentlyContinue | Stop-Process -Force
& (Join-Path $root "build.ps1")
& (Join-Path $root "package.ps1")

Write-Host "Checking release files..."
Assert-True (Test-Path -LiteralPath $exePath) "Missing release exe"
Assert-True (Test-Path -LiteralPath $zipPath) "Missing PinShotWin.zip"
Assert-True (Test-Path -LiteralPath $versionedZipPath) "Missing versioned zip"
Assert-True (Test-Path -LiteralPath (Join-Path $releaseDir "VERSION.txt")) "Missing VERSION.txt"
Assert-True (Test-Path -LiteralPath "$zipPath.sha256") "Missing zip checksum"
Assert-True (Test-Path -LiteralPath "$versionedZipPath.sha256") "Missing versioned zip checksum"

$releaseVersion = (Get-Content -LiteralPath (Join-Path $releaseDir "VERSION.txt") -Raw).Trim()
Assert-True ($releaseVersion -eq $version) "Release VERSION.txt mismatch"

$exeVersion = (Get-Item -LiteralPath $exePath).VersionInfo.ProductVersion
Assert-True ($exeVersion -eq "$version.0") "Exe version mismatch: $exeVersion"

$recordedHash = (Get-Content -LiteralPath "$zipPath.sha256").Split(" ")[0]
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
Assert-True ($recordedHash -eq $actualHash) "PinShotWin.zip checksum mismatch"

Write-Host "Checking startup and single-instance guard..."
$p1 = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden
Start-Sleep -Milliseconds 600
$p2 = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden
Start-Sleep -Milliseconds 800
$running = @(Get-Process PinShotWin -ErrorAction SilentlyContinue)
$secondExited = $p2.HasExited
$running | Stop-Process -Force

Assert-True ($running.Count -eq 1) "Expected one running PinShotWin process, got $($running.Count)"
Assert-True $secondExited "Second instance did not exit"

if ($IncludeInstaller) {
    Write-Host "Checking single-file setup installer..."
    & (Join-Path $root "make-installer.ps1")
    Assert-True (Test-Path -LiteralPath $installerPath) "Missing setup installer"
    Assert-True (Test-Path -LiteralPath "$installerPath.sha256") "Missing setup installer checksum"

    $installerRecordedHash = (Get-Content -LiteralPath "$installerPath.sha256").Split(" ")[0]
    $installerActualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash
    Assert-True ($installerRecordedHash -eq $installerActualHash) "Setup installer checksum mismatch"
}

Write-Host "Verification passed for PinShotWin $version"
