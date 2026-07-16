# Changelog

## 0.2.0 - 2026-07-13

- Added annotation output rendering for copy, save, pin, and recent history.
- Added rectangle, arrow, text, mosaic, and undo tools in the capture preview toolbar.
- Improved mosaic preview and output to use source-color pixelation without a colored overlay.
- Added a first scrolling capture workflow that stitches repeated captures into a longer image.
- Added hidden UI self-test mode for annotation rendering and scroll stitching.
- Added `verify.ps1 -IncludeUi` for automated UI output checks.
- Fixed installer checksum generation waiting for IExpress output to become stable.
- Improved preview dragging with dirty-region rendering and a performance regression gate.
- Replaced fixed-overlap scrolling stitches with scrolling-region detection and image overlap matching.
- Preserved annotation order between preview and final output.
- Made Escape cancel active text editing before closing the capture overlay.

## 0.1.0 - 2026-07-09

- Added tray-only screenshot workflow with configurable hotkey.
- Added manual selection, window hover selection, adjustable preview bounds, and size display.
- Added copy, save, pin, and cancel actions.
- Added preview shortcuts: `Enter`/`Ctrl+C`, `Ctrl+S`, `Ctrl+P`.
- Added keyboard nudge and resize for preview selection.
- Added pinned images with drag, mouse-wheel scale, double-click close, right-click copy/save/lock/opacity/close, and `Ctrl+C`.
- Added in-memory recent screenshot history from tray menu.
- Added tray About dialog with version and configuration details.
- Added settings for hotkey, startup, save format, and JPG quality.
- Added user install/uninstall scripts and release packaging.
- Installer now stops a running app before replacing the executable.
- Added optional IExpress single-file setup exe generation.
- Added optional setup exe verification via `verify.ps1 -IncludeInstaller`.
- Added installed version metadata via `VERSION.txt`.
- Added SHA256 checksum generation for release packages.
- Added app icon and release zip generation.
