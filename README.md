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

## Shortcuts

- Default screenshot hotkey: `F1`
- Cancel capture: `Esc`
- Copy current capture: `Ctrl+C`
- Save current capture: `Ctrl+S`
- Pin current capture: `Ctrl+P`
- Copy current capture: `Enter`

## Features

- Tray-only app
- Configurable screenshot hotkey
- Start with Windows toggle
- JPG/PNG save format setting
- JPG quality setting
- Window hover selection
- Manual rectangle selection
- Copy, save, pin, cancel toolbar
- Adjustable preview selection with size display
- Multiple pinned images
- Drag pinned images
- Mouse-wheel scale while pointer is over pinned image
- Double-click pinned image to close
- Right-click pinned image menu: copy, save, close
