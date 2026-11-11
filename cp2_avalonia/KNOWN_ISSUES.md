# Known Issues

## Not Implemented
 - Open physical volumes (Needs General research, platform‑specific APIs, permissions, device enumeration)
 - User cannot drag image file to empty app window to open it.

## Improvements Possibly Needed before release
 - Create a desktop file for Linux (?)
 - Make sure build scripts properly build the Avalonia port
 - Review/Rework avalonia_docs. Possibly add real screenshots.
 
## Research Needed
 - Open physical volumes (General research, platform‑specific APIs, permissions, device enumeration)

## Testing needed
 - Need to test Animated GIF Encoder in the Debug menu
 - Need more testing of opening files by drag and drop to icon

## Limited to Avalonia 11.x at the moment
- Early editions of Avalonia 12 have a broken TextMetrics implementation which breaks the current FancyText viewer
- There is a bug report in the Avalonia GitHub issue tracker for this issue. (https://github.com/AvaloniaUI/Avalonia/issues/21073)
- There are workarounds for this if upgrade to Avalonia 12 is needed, but they are cumbersome and not worth it for now.
 
## macOS: file viewer flickers when the main window is clicked
When a file viewer window is open, clicking the main window brings the main window to the
front for a moment and then puts the viewer back on top.  The viewer is an owned window of
the main window.  The ordering is done by Avalonia's own macOS code, not by CiderPress II:
`windowDidBecomeKey` calls `BringToFront()`, which orders the window forward and then orders
its owned windows forward again.  The code is the same in Avalonia 11.3.x and in the 12.x
`main` branch (checked in the source on 2026-11-10; 12.x was not run), so upgrading is not
expected to fix it.  No Avalonia issue about it was found.  Cosmetic only; accepted for now.
Showing viewers without an owner would remove the flicker, but then the main window could
cover a viewer.

## Linux Drag & Drop Known Limitations

### XWayland cursor glyph (cosmetic only — functionality unaffected)
Under KDE Plasma Wayland (XWayland session), once `XdndSelection` is claimed at drag
start, KWin overrides the X11 cursor with its own "no-drop" glyph while the pointer is
inside the source window.  This is a compositor-side limitation: the real fix is the
Wayland `wl_data_device` drag source in Avalonia 12+ (see item below).  All drag
operations complete correctly despite the incorrect cursor glyph.

### Native Wayland drag-drop deferred to Avalonia 12+
Avalonia 11.x has no Wayland backend.  CP2 runs as an XWayland X11 client on both
Xorg and Wayland+XWayland sessions.  The custom XDND proxy in `Platform/LinuxDrag.cs`
handles drag-and-drop for this configuration.  A native `wl_data_device` implementation
is blocked on Avalonia's official Wayland support, expected in Avalonia 12.
