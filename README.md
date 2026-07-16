# PinShotWin

Windows tray screenshot tool.

Current version: `0.2.0`

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output:

```text
bin\PinShotWin.exe
```

## Verify

```powershell
powershell -ExecutionPolicy Bypass -File .\verify.ps1
```

Verify release package, UI self-test, and setup exe:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify.ps1 -IncludeInstaller -IncludeUi
```

## Release

The release package can be used directly or installed for the current Windows user.

Direct run:

```powershell
.\PinShotWin.exe
```

Install:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer copies `VERSION.txt` to `%LOCALAPPDATA%\PinShotWin`.

Release packages include SHA256 checksum files for integrity checks.

The installer stops a running PinShotWin process before upgrading files.

Single-file setup exe:

```powershell
powershell -ExecutionPolicy Bypass -File .\make-installer.ps1
```

Uninstall:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

## Shortcuts

- Default screenshot hotkey: `F1`
- Cancel capture: `Esc`
- Copy current capture: `Ctrl+C`
- Save current capture: `Ctrl+S`
- Pin current capture: `Ctrl+P`
- Undo latest annotation: `Ctrl+Z`
- Copy current capture: `Enter`
- Nudge preview selection: arrow keys
- Resize preview selection: `Ctrl` + arrow keys
- Use 10px steps: hold `Shift`

## Features

- Tray-only app
- Recent screenshot history and About dialog from tray menu
- Configurable screenshot hotkey
- Start with Windows toggle
- JPG/PNG save format setting
- JPG quality setting
- Window hover selection
- Manual rectangle selection
- Copy, save, pin, cancel toolbar
- Rectangle, arrow, text, mosaic, undo, and scrolling capture toolbar actions
- Final copy/save/pin/history output includes annotations and mosaic
- Adjustable preview selection with size display
- Keyboard nudge and resize for preview selection
- Multiple pinned images
- Drag pinned images
- Mouse-wheel scale while pointer is over pinned image
- Double-click pinned image to close
- Right-click pinned image menu: copy, save, lock position/锁定位置, opacity/透明度, close
- In-memory recent history keeps the latest 10 captures
