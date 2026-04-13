# Known Issues

## Bugs
 - Highlighting styled text makes it lose formatting (new bug with Avalonia)

## Persistent and annoying bugs that are resisting fixes
  - Funky cell highlighting in file details viewer 
   - Should highlight entire Row, not Cell.  
   - Need to lose outline box which is being chopped off and stick with background highlighting


## Test further
 - All obvious features
 - Settings (set/clear, persistence)

 ## Necessary Improvements Needed before release
 - Styling improvements (spacing, padding, layout consistency)
 - Rework Settings menu (organization, clarity, Avalonia‑native patterns)
 - Side panel/toolbar for File Viewer
    - Zoom in viewer with Ctrl-Mouse, Ctrl+-/Ctrl+=
 - Create desktop file for Linux
 - Make sure build/deploy script works for Avalonia code
 - System should retain Show/Hide Settings status and potentially add a default preference to the System Settings dialog.

 ## New Features
 - ~~Theme support (light/dark/Fluent/WPF?)~~

## Future Major Rework
 - Refactor UI logic out of code‑behind into a consistent, well‑structured MVVM architecture
 - Allow multiple viewers/editors at once (multi‑document workflow)
 - Implement dynamic windowing/paneling API for user‑preferred layouts (VS/Code‑style docking, splits, tabs)
   - Avalonia Dock maybe? (MIT Licensed) https://github.com/wieslawsoltes/Dock
 - Move to a single‑process, multi‑window architecture to support:
   - safe shared access to disk images
   - consistent undo/redo across windows
   - reliable drag/drop and clipboard behavior
   - "File -> New Window" (Chromium‑style multi‑instance UX)
   - future docking/paneling system 
   - elimination of cross‑process race conditions
 - Add unit testing with xUnit or something similar for MVVM code

## Research Needed
 - Drag to/from desktop/file manager in X11 (Wayland/X11 differences, MIME negotiation, payload limits)
 - Open physical volumes (General research, platform‑specific APIs, permissions, device enumeration)

## Other
 - Consider using Avalonia 12.x.  It probably has some more features and bug fixes, but it is fairly new and relatively untested.
     - Ver 12.0.0 is the latest in NuGet.
 - Consider using ProDataGrid (https://github.com/wieslawsoltes/ProDataGrid) as a replacement for the DataGrid in the File Details viewer. It has a lot of features and is also MIT licensed, but it is a much larger dependency and may be overkill for our needs.
    -  Ver 11.3.11 is the latest in NuGet. 
 - Make sure third party notices, licensing, etc. are ok between Apache & Avalonia's MIT license
 - Build and test with Windows
 - Build and test with macOS
 - Test Animated GIF Encoder in Debug menu