# Known Issues

## Bugs
 - Highlighting styled text makes it lose formatting (new bug with Avalonia)

 - ~~Change initial app size; preserve previous~~
 - ~~The block and sector editors should only hightlight one cell, not an entire row~~
 - ~~Read-only metadata should be shown in grey.  There is no difference currently in the axaml version.~~
 - ~~*Conversion Mode* combo box does not populate in *Export Configuration* panel~~
 - ~~When you move a directory into another directory by dragging, it does not update the FQPN of the first directory's contents~~
 - ~~Probably need a smaller font in File Details panel~~
 - ~~Cannot click header to sort by name or change sort order in file details viewer; other headers work~~
 - ~~*Rename Dir* doesn't change FQPN's in details viewer (Legacy bug from WPF version)~~
    - ~~When a rename occurs, the details viewer should just refresh itself. That would be the easy path.~~
 - ~~The *Metadata* and *Disk Partitions/Utilities* sections of the Disk Image panel are missing~~
 - ~~The *Show/Hide Settings* button for the Settings panel on the main window is missing~~
   - ~~Need to make sure settings panel can be hidden, like the WPF version~~
 - ~~When trying to resize columns in the File Details viewer, it perceives the drag as the start of a drag-drop event.~~
    - ~~Drag/drop should only occur when selecting and dragging a file or directory row.~~
 - ~~Double clicking on resize arrows in File Details viewer header should auto-size column to fit the data to the left of it.~~
 - ~~Metadata panel columns are not resizable~~
 
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
 - ~~Make Debug menu's Debug Log output copyable~~
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
 - Consider using Avalonia 12.x.  It probably has some more features and bug fixes, but it is fairly new and relatively untested.
     - Ver 12.0.0 is the latest in NuGet.
 - Consider using ProDataGrid (https://github.com/wieslawsoltes/ProDataGrid) as a replacement for the DataGrid in the File Details viewer. It has a lot of features and is also MIT licensed, but it is a much larger dependency and may be overkill for our needs.
    -  Ver 11.3.11 is the latest in NuGet. 
 - Make sure third party notices, licensing, etc. are ok between Apache & Avalonia's MIT license
 - Build and test with Windows
 - Build and test with macOS
 - Test Animated GIF Encoder in Debug menu