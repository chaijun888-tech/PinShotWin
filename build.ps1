$ErrorActionPreference = "Stop"

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found: $csc"
}

$outDir = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$iconPath = Join-Path $PSScriptRoot "assets\app.ico"

& $csc `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /utf8output `
    /win32icon:"$iconPath" `
    /out:"$outDir\PinShotWin.exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "$PSScriptRoot\PinShotWin.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$assetOut = Join-Path $outDir "assets"
New-Item -ItemType Directory -Force -Path $assetOut | Out-Null
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetOut "app.ico") -Force

Write-Host "Built $outDir\PinShotWin.exe"
