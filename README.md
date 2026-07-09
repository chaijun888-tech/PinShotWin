# PinShotWin

Windows tray screenshot tool.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output:

```text
bin\PinShotWin.exe
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
- Copy current capture: `Enter`
- Nudge preview selection: arrow keys
- Resize preview selection: `Ctrl` + arrow keys
- Use 10px steps: hold `Shift`

## Features

- Tray-only app
- Recent screenshot history from tray menu
- Configurable screenshot hotkey
- Start with Windows toggle
- JPG/PNG save format setting
- JPG quality setting
- Window hover selection
- Manual rectangle selection
- Copy, save, pin, cancel toolbar
- Adjustable preview selection with size display
- Keyboard nudge and resize for preview selection
- Multiple pinned images
- Drag pinned images
- Mouse-wheel scale while pointer is over pinned image
- Double-click pinned image to close
- Right-click pinned image menu: copy, save, lock position, opacity, close
- In-memory recent history keeps the latest 10 captures
