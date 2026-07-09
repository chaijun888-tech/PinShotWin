$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root "VERSION") -Raw).Trim()
$releaseRoot = Join-Path $root "release"
$releaseDir = Join-Path $releaseRoot "PinShotWin"
$installerPath = Join-Path $releaseRoot "PinShotWinSetup-$version.exe"
$sedPath = Join-Path $releaseRoot "PinShotWinSetup.sed"

if (-not (Test-Path -LiteralPath (Join-Path $releaseDir "PinShotWin.exe"))) {
    & (Join-Path $root "package.ps1")
}

$iexpress = Join-Path $env:WINDIR "System32\iexpress.exe"
if (-not (Test-Path -LiteralPath $iexpress)) {
    throw "IExpress was not found at $iexpress"
}

$installCommand = 'powershell.exe -ExecutionPolicy Bypass -File install.ps1'
$files = @(
    "PinShotWin.exe",
    "install.ps1",
    "uninstall.ps1",
    "README.txt",
    "VERSION.txt"
)

foreach ($file in $files) {
    $path = Join-Path $releaseDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing release file: $path"
    }
}

$fileList = for ($i = 0; $i -lt $files.Count; $i++) {
    "FILE$($i)=`"$($files[$i])`""
}

$sourceFiles = for ($i = 0; $i -lt $files.Count; $i++) {
    "%FILE$($i)%="
}

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=PinShotWin installed.
TargetName=$installerPath
FriendlyName=PinShotWin Setup
AppLaunched=$installCommand
PostInstallCmd=<None>
AdminQuietInstCmd=$installCommand
UserQuietInstCmd=$installCommand
SourceFiles=SourceFiles

[Strings]
FILE_COUNT=$($files.Count)
$($fileList -join "`r`n")

[SourceFiles]
SourceFiles0=$releaseDir

[SourceFiles0]
$($sourceFiles -join "`r`n")
"@

Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
& $iexpress /N /Q $sedPath

for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $installerPath); $i++) {
    Start-Sleep -Milliseconds 250
}

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not created: $installerPath"
}

Set-Content -LiteralPath "$installerPath.sha256" -Value ((Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash + "  " + [System.IO.Path]::GetFileName($installerPath)) -Encoding ASCII
Remove-Item -LiteralPath $sedPath -Force -ErrorAction SilentlyContinue

Write-Host "Created $installerPath"
