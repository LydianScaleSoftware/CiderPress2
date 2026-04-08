# Known Issues

## Bugs
 - Highlighting styled text makes it lose formatting (new bug with Avalonia)
 - *Rename Dir* doesn't change FQPN's in details viewer (Legacy bug from WPF version)
 - Change initial app size; preserve previous settings
 - Funky cell highlighting in file details viewer 
   - Should highlight entire Row, not Cell.  
   - Need to lose outline box which is being chopped off and stick with background highlighting
 - The *Metadata* and *Disk Partitions/Utilities* sections of the Disk Image panel are missing
 - The *Show/Hide Settings* button for the Settings panel on the main window is missing
   - Need to make sure settings panel can be hidden, like the WPF version
 - Cannot click header to sort by name or change sort order in file details viewer; other headers work
 - *Conversion Mode* combo box does not populate in *Export Configuration* panel
 - Probably need a smaller font in File Details panel
 
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

 ## New Features
 - Make Debug menu's Debug Log output copyable
 - Theme support (light/dark/Fluent/WPF?)

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
 - Build and test with Windows
 - Build and test with macOS
 - Test Animated GIF Encoder in Debug menu