$ErrorActionPreference = "Stop"

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found: $csc"
}

$outDir = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$iconPath = Join-Path $PSScriptRoot "assets\app.ico"
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "VERSION") -Raw).Trim()
$assemblyVersion = "$version.0"
$objDir = Join-Path $PSScriptRoot "obj"
$generatedAssemblyInfo = Join-Path $objDir "GeneratedAssemblyInfo.cs"
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

@"
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("PinShotWin")]
[assembly: AssemblyDescription("Windows tray screenshot tool")]
[assembly: AssemblyCompany("PinShotWin")]
[assembly: AssemblyProduct("PinShotWin")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: ComVisible(false)]
"@ | Set-Content -LiteralPath $generatedAssemblyInfo -Encoding UTF8

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
    "$PSScriptRoot\PinShotWin.cs" `
    "$generatedAssemblyInfo"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$assetOut = Join-Path $outDir "assets"
New-Item -ItemType Directory -Force -Path $assetOut | Out-Null
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetOut "app.ico") -Force

Write-Host "Built $outDir\PinShotWin.exe"
