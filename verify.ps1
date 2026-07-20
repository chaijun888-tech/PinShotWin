param(
    [switch]$IncludeInstaller,
    [switch]$IncludeUi
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

if ($IncludeUi) {
    Write-Host "Checking UI rendering self-test..."
    $uiOut = Join-Path $root "obj\ui-test"
    if (Test-Path -LiteralPath $uiOut) {
        Remove-Item -LiteralPath $uiOut -Recurse -Force
    }
    New-Item -ItemType Directory -Path $uiOut | Out-Null

    $uiProcess = Start-Process -FilePath $exePath -ArgumentList @("--self-test-ui", $uiOut) -PassThru -Wait -WindowStyle Hidden
    Assert-True ($uiProcess.ExitCode -eq 0) "UI self-test exited with $($uiProcess.ExitCode)"

    $dragResultPath = Join-Path $uiOut "drag-performance.txt"
    $dragProcess = Start-Process -FilePath $exePath -ArgumentList @("--self-test-drag", $dragResultPath) -PassThru -Wait -WindowStyle Hidden
    Assert-True ($dragProcess.ExitCode -eq 0) "Drag performance self-test exited with $($dragProcess.ExitCode)"
    Assert-True (Test-Path -LiteralPath $dragResultPath) "Missing drag performance result"
    $dragValues = @{}
    Get-Content -LiteralPath $dragResultPath | ForEach-Object {
        $parts = $_.Split('=', 2)
        if ($parts.Count -eq 2) { $dragValues[$parts[0]] = $parts[1] }
    }
    $dragFps = [double]$dragValues['fps']
    Assert-True ($dragFps -ge 50) "Preview drag performance too low: $dragFps FPS"
    Assert-True ([int]$dragValues['selection_x'] -eq 600) "Preview drag did not reach the expected location"
    Assert-True ($dragValues['toolbar_hidden_during_drag'] -eq 'True') "Toolbar remained visible during selection adjustment"
    Assert-True ($dragValues['toolbar_visible_after_drag'] -eq 'True') "Toolbar did not return after selection adjustment"

    $annotationPath = Join-Path $uiOut "annotation.png"
    $scrollPath = Join-Path $uiOut "scroll.png"
    $editorScrollPath = Join-Path $uiOut "scroll-editor.png"
    $streamedEditorScrollPath = Join-Path $uiOut "scroll-editor-streamed.png"
    $streamedScrollPath = Join-Path $uiOut "scroll-streamed.png"
    $movedSelectionPath = Join-Path $uiOut "moved-selection.png"
    $annotationOrderPath = Join-Path $uiOut "annotation-order.png"
    $diagonalArrowPath = Join-Path $uiOut "arrow-diagonal.png"
    Assert-True (Test-Path -LiteralPath $annotationPath) "Missing annotation self-test image"
    Assert-True (Test-Path -LiteralPath $scrollPath) "Missing scroll self-test image"
    Assert-True (Test-Path -LiteralPath $editorScrollPath) "Missing editor scroll self-test image"
    Assert-True (Test-Path -LiteralPath $streamedEditorScrollPath) "Missing streamed editor scroll self-test image"
    Assert-True (Test-Path -LiteralPath $streamedScrollPath) "Missing streamed scroll self-test image"
    Assert-True (Test-Path -LiteralPath $movedSelectionPath) "Missing moved selection self-test image"
    Assert-True (Test-Path -LiteralPath $annotationOrderPath) "Missing annotation order self-test image"
    Assert-True (Test-Path -LiteralPath $diagonalArrowPath) "Missing diagonal arrow self-test image"
    Assert-True ((Get-Content -LiteralPath (Join-Path $uiOut "text-escape.txt") -Raw).Trim() -eq "pass") "Escape did not cancel text editing cleanly"
    Assert-True ((Get-Content -LiteralPath (Join-Path $uiOut "long-preview-lock.txt") -Raw).Trim() -eq "pass") "Long screenshot preview could be distorted or displayed the wrong size"
    $dpiAwareness = [int](Get-Content -LiteralPath (Join-Path $uiOut "dpi-awareness.txt") -Raw)
    Assert-True ($dpiAwareness -eq 2) "Process is not Per-Monitor DPI aware: $dpiAwareness"
    Assert-True ((Get-Content -LiteralPath (Join-Path $uiOut "hotkey-rollback.txt") -Raw).Trim() -eq "pass") "Hotkey registration failure did not preserve the previous hotkey"
    Assert-True ((Get-Content -LiteralPath (Join-Path $uiOut "clipboard-retry.txt") -Raw).Trim() -eq "pass") "Clipboard retry did not recover from transient contention"
    Assert-True ((Get-Content -LiteralPath (Join-Path $uiOut "history-limit.txt") -Raw).Trim() -eq "pass") "Screenshot history did not enforce its total pixel limit"
    $previewLayout = (Get-Content -LiteralPath (Join-Path $uiOut "scroll_preview_layout.txt") -Raw).Trim().Split(',')
    $previewWidth = [int]$previewLayout[2]
    $previewHeight = [int]$previewLayout[3]
    Assert-True ($previewWidth -eq 412 -and $previewHeight -eq 824) "Scrolling preview was not scaled proportionally: ${previewWidth}x${previewHeight}"

    Add-Type -AssemblyName System.Drawing
    $annotation = [System.Drawing.Bitmap]::FromFile($annotationPath)
    $scroll = [System.Drawing.Bitmap]::FromFile($scrollPath)
    $editorScroll = [System.Drawing.Bitmap]::FromFile($editorScrollPath)
    $streamedEditorScroll = [System.Drawing.Bitmap]::FromFile($streamedEditorScrollPath)
    $streamedScroll = [System.Drawing.Bitmap]::FromFile($streamedScrollPath)
    $movedSelection = [System.Drawing.Bitmap]::FromFile($movedSelectionPath)
    $annotationOrder = [System.Drawing.Bitmap]::FromFile($annotationOrderPath)
    $diagonalArrow = [System.Drawing.Bitmap]::FromFile($diagonalArrowPath)
    try {
        Assert-True ($annotation.Width -eq 180 -and $annotation.Height -eq 180) "Unexpected annotation image size"
        Assert-True ($scroll.Width -lt 300 -and $scroll.Width -ge 220) "Scroll stitch did not remove the fixed sidebar"
        Assert-True ($scroll.Height -ge 650 -and $scroll.Height -le 665) "Unexpected scroll image height: $($scroll.Height)"
        Assert-True ($editorScroll.Width -ge 260 -and $editorScroll.Width -le 285) "Editor scroll retained side UI: $($editorScroll.Width) px"
        Assert-True ($editorScroll.Height -eq 660) "Editor scroll retained fixed rows or appended a settled frame: $($editorScroll.Height) px"
        $editorUiPixels = [int](Get-Content -LiteralPath (Join-Path $uiOut "scroll_editor_ui_pixels.txt") -Raw)
        Assert-True ($editorUiPixels -eq 0) "Editor scroll retained $editorUiPixels side-UI pixels"
        Assert-True ($streamedEditorScroll.Width -ge 260 -and $streamedEditorScroll.Width -le 285) "Streamed editor scroll retained side UI: $($streamedEditorScroll.Width) px"
        Assert-True ($streamedEditorScroll.Height -eq 660) "Streamed editor scroll height is incorrect: $($streamedEditorScroll.Height) px"
        $streamedEditorUiPixels = [int](Get-Content -LiteralPath (Join-Path $uiOut "scroll_editor_streamed_ui_pixels.txt") -Raw)
        Assert-True ($streamedEditorUiPixels -eq 0) "Streamed editor scroll retained $streamedEditorUiPixels side-UI pixels"
        Assert-True ($streamedScroll.Width -ge 220 -and $streamedScroll.Width -lt 300) "Streamed scroll width is incorrect: $($streamedScroll.Width)"
        Assert-True ($streamedScroll.Height -ge 2090 -and $streamedScroll.Height -le 2110) "Streamed scroll height is incorrect: $($streamedScroll.Height)"
        $compressedBytes = [long](Get-Content -LiteralPath (Join-Path $uiOut "scroll_streamed_compressed_bytes.txt") -Raw)
        $rawBytes = [long]$streamedScroll.Width * $streamedScroll.Height * 4
        Assert-True ($compressedBytes -lt $rawBytes) "Streamed strips were not kept in compressed form"
        $scrollLimits = (Get-Content -LiteralPath (Join-Path $uiOut "scroll_limits.txt") -Raw).Trim().Split(',')
        Assert-True ([long]$scrollLimits[0] -eq 67108864) "Unexpected scrolling pixel limit"
        Assert-True ([int]$scrollLimits[1] -eq 32760) "Unexpected scrolling dimension limit"
        Assert-True ([long]$scrollLimits[2] -eq 67108864) "Unexpected screenshot history pixel limit"
        Assert-True ($scrollLimits[3] -eq 'True') "A safe long image was rejected"
        Assert-True ($scrollLimits[4] -eq 'False') "An oversized long image was accepted"
        $redProbe = $annotation.GetPixel(20, 20)
        Assert-True ($redProbe.R -gt 150 -and $redProbe.G -lt 100) "Annotation rectangle probe did not render red"
        $movedProbe = $movedSelection.GetPixel(20, 20)
        Assert-True ($movedProbe.G -gt 200 -and $movedProbe.R -lt 100) "Moved selection did not use its new screen region"
        $annotationOrderRed = [int](Get-Content -LiteralPath (Join-Path $uiOut "annotation-order-red.txt") -Raw)
        Assert-True ($annotationOrderRed -eq 0) "Mosaic did not cover the earlier annotation"
        $arrowSolidPixels = [int](Get-Content -LiteralPath (Join-Path $uiOut "arrow-diagonal-solid.txt") -Raw)
        $arrowEdgePixels = [int](Get-Content -LiteralPath (Join-Path $uiOut "arrow-diagonal-edge.txt") -Raw)
        Assert-True ($arrowSolidPixels -ge 250) "Diagonal arrow body is incomplete: $arrowSolidPixels solid pixels"
        Assert-True ($arrowEdgePixels -ge 120) "Diagonal arrow antialiasing is too weak: $arrowEdgePixels edge pixels"
    }
    finally {
        $annotation.Dispose()
        $scroll.Dispose()
        $editorScroll.Dispose()
        $streamedEditorScroll.Dispose()
        $streamedScroll.Dispose()
        $movedSelection.Dispose()
        $annotationOrder.Dispose()
        $diagonalArrow.Dispose()
    }
}

if ($IncludeInstaller) {
    Write-Host "Checking single-file setup installer..."
    & (Join-Path $root "make-installer.ps1")
    Assert-True (Test-Path -LiteralPath $installerPath) "Missing setup installer"
    Assert-True (Test-Path -LiteralPath "$installerPath.sha256") "Missing setup installer checksum"

    $installerRecordedHash = (Get-Content -LiteralPath "$installerPath.sha256").Split(" ")[0]
    $installerActualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash
    Assert-True ($installerRecordedHash -eq $installerActualHash) "Setup installer checksum mismatch"

    Start-Sleep -Seconds 2
    $installerDelayedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath).Hash
    Assert-True ($installerRecordedHash -eq $installerDelayedHash) "Setup installer changed after checksum generation"
}

Write-Host "Verification passed for PinShotWin $version"
