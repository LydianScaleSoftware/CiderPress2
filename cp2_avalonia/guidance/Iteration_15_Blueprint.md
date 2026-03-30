# Iteration 15 Blueprint: Polish & Parity

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Final iteration: remove all "Not Implemented" stubs, port any remaining WPF utilities,
verify feature parity, fix outstanding issues, and test on all three platforms. This is the
cleanup and QA iteration.

---

## Prerequisites

- Iterations 0–14 are complete: all major features ported.
- Have the WPF project available for side-by-side comparison.
- Access to Windows, Linux, and macOS for testing (or CI builds for all three).

---

## Step-by-Step Instructions

### Step 1: Audit All "Not Implemented" Stubs

Search the entire `cp2_avalonia/` directory for the stub pattern used throughout the port:

```bash
grep -rn "NotImplemented\|Not Implemented\|throw new NotImplementedException" cp2_avalonia/
```

For each stub found:
1. Determine what the WPF equivalent does (read the corresponding WPF source)
2. Implement the functionality or determine it's intentionally omitted (with a comment)
3. Remove the stub dialog/message

Common stubs that may remain:
- Import/Export files (if deferred from Iteration 6)
- Some file viewer export formats
- Virtual file drag-out (intentionally omitted — document limitation)
- Any DEBUG-only features that are low priority

**Risk (G-01):** This audit may uncover stubs in core paths (e.g., `ExtractFiles`,
`AddFiles`, `CopyToClipboard`) that were deferred from earlier iterations. If T2/T3 items
from prior concerns were deferred as stubs rather than fully implemented, the scope of this
step expands. Treat this step as discovery + remediation — track each stub found and
prioritize by criticality.

### Step 2: Port Remaining `WPFCommon/` Utilities

Check all files in `cp2_wpf/WPFCommon/` and ensure everything needed has been ported:

| WPF File | Status / Notes |
|---|---|
| `AnimatedGifEncoder.cs` | **Port using pure .NET GIF LZW encoding** (option b). The class already manually assembles GIF binary from `UnpackedGif` structures — the only WPF dependency is `GifBitmapEncoder.Save()` for per-frame LZW compression. Replace with a self-contained GIF LZW encoder that writes the GIF image data blocks directly from the indexed pixel data in `UnpackedGif`. This avoids adding a SkiaSharp explicit dependency (SkiaSharp is pulled in transitively by Avalonia for rendering, but its GIF encoding API surface varies across versions and is not guaranteed stable for this use). Also replace `BitmapFrame` type with `byte[]` (raw indexed pixel data per frame). The `WinUtil.ConvertToBitmapSource()` call from `DoConvertANI()` must also be updated — see note below. |
| `BindingProxy.cs` | **Skip.** `Freezable` + `DependencyProperty` are WPF-only and cannot compile. In Avalonia, DataGrid columns inherit the row's DataContext directly, so the proxy pattern is unnecessary. If any column binding requires access to the window-level DataContext, use `{Binding RelativeSource={RelativeSource AncestorType=Window}}` instead. |
| `BrowseForFolder.cs` | Replaced by `StorageProvider.OpenFolderPickerAsync` |
| `ClipHelper.cs` | **Not ported** — Windows COM clipboard (Iteration 13 decision) |
| `CreateFolder.xaml/.cs` | Should be ported in earlier iteration (for mkdir) |
| `FileSelector.xaml/.cs` | **Skip.** Replaced entirely by `StorageProvider.OpenFilePickerAsync()` / `OpenFolderPickerAsync()`. This makes `WinMagic.GetKnownFolderPath()` and `WinMagic.GetIcon()` unused — see below. |
| `InverseBooleanConverter.cs` | **Replace with Avalonia built-in.** The WPF version implements `System.Windows.Data.IValueConverter` with `[ValueConversion]` attribute — neither compiles in Avalonia. Avalonia provides `BoolConverters.Not` as a built-in static resource. Replace all `{StaticResource InvertBool}` bindings with `{x:Static BoolConverters.Not}`, or reimplement using `Avalonia.Data.Converters.IValueConverter` if a keyed resource is needed. |
| `SelectTextOnFocus.cs` | **Skip.** Uses WPF Blend SDK `Interactivity.Behavior<TextBox>` (not available in Avalonia). Confirmed no call sites in the WPF project. Omit from port. |
| `VirtualFileDataObject.cs` | **Not ported** — Windows COM (Iteration 13 decision) |
| `WPFExtensions.cs` | **Rewrite needed.** Contains two DataGrid extension methods that use WPF-only types and must be reimplemented for Avalonia: (1) `GetClickRowColItem(DataGrid, MouseButtonEventArgs)` — identifies the row, column, and item under the mouse click. Called 4 times: `MainWindow.axaml.cs` file list double-click, right-click context menu, drag initiation, and `SelectPhysicalDrive.axaml.cs` double-click. **Avalonia replacement:** Accept `PointerPressedEventArgs`, use `e.GetPosition(dataGrid)` to get coordinates, then walk the visual tree upward from `e.Source as Visual` using `visual.FindAncestorOfType<DataGridRow>()` (Avalonia `VisualExtensions`) to find the row. Get the item from `DataGridRow.DataContext`. For column index, compare `e.GetPosition(dataGrid).X` against column offset widths. (2) `SelectRowColAndFocus(DataGrid, int row, int col)` — programmatically selects a cell and focuses the grid. Used by the sector editor (Iteration 11). **Avalonia replacement:** Set `dataGrid.SelectedIndex = row`, then call `dataGrid.ScrollIntoView(dataGrid.SelectedItem, dataGrid.Columns[col])` and `dataGrid.Focus()`. Cell-level selection requires `SelectionUnit=Cell` mode. |
| `WinMagic.cs` | **Skip entirely.** Both methods (`GetKnownFolderPath` via `SHGetKnownFolderPath` P/Invoke, `GetIcon` via `SHGetFileInfo` P/Invoke) are Windows-only and only used by `FileSelector.xaml.cs`. Since `FileSelector` is replaced by `StorageProvider`, both methods are unused. Verify no other callers exist via `grep -rn WinMagic cp2_avalonia/`. |
| `WindowPlacement.cs` | Should already be ported (Iteration 3) |
| `WorkProgress.xaml/.cs` | Should already be ported (early iteration) |

For each file not yet ported:
1. Check if its functionality is used by any ported code
2. Port methods that are still referenced
3. Skip Windows-only code that has no Avalonia equivalent

**Additional porting notes for Step 2:**

- **`WinUtil.ConvertToBitmapSource(IBitmap bitmap)`**: returns a WPF
  `System.Windows.Media.Imaging.BitmapSource`. Called by `DoConvertANI()` to convert
  frames before passing to `AnimatedGifEncoder.AddFrame`. The Avalonia equivalent type is
  `Avalonia.Media.Imaging.Bitmap` (from stream) or `WriteableBitmap`. If
  `AnimatedGifEncoder.AddFrame` is changed to accept raw `byte[]` data, this conversion
  step can be eliminated entirely.

- **`WinUtil.GetRuntimeDataDir()`**: strips 4 path levels for development builds
  (`cp2_wpf\bin\Debug\net6.0-windows` — four levels due to the `-windows` suffix). The
  Avalonia output path is `cp2_avalonia/bin/Debug/net8.0` — only **3 levels** (no
  `-windows` suffix). The equivalent Avalonia utility must strip 3 levels, not 4.
  Previously flagged in Iteration 12 T2-02 for the related `GetTestRoot()` function.

- **`SaveFileDialog` / `OpenFileDialog`**: All remaining uses of
  `Microsoft.Win32.SaveFileDialog` and `Microsoft.Win32.OpenFileDialog` must be replaced
  with `StorageProvider.SaveFilePickerAsync()` and `StorageProvider.OpenFilePickerAsync()`
  (async). Known sites: `DoConvertANI` (line 2820), any other `SaveFileDialog` / 
  `OpenFileDialog` calls. Callers become `async Task`.

### Step 3: Verify Recent-Files List

Ensure the File menu's recent-files list works:
1. When opening a file, it's added to the recent-files list in `AppSettings`
2. The File menu shows recent files
3. Clicking a recent file opens it
4. Invalid/missing recent files are handled gracefully (removed from list or shown grayed)

Check `cp2_wpf/MainController.cs` for `UpdateRecentFileList()` and similar methods.

**RecentFileCmd wiring (6 commands):** The WPF source declares six separate
`RoutedUICommand` instances (`RecentFileCmd1` through `RecentFileCmd6`) with keyboard
gestures `Ctrl+Shift+1` through `Ctrl+Shift+6`, no CanExecute (always enabled). Each
handler calls `RecentFileCmd_Executed(index)` which delegates to
`mMainCtrl.OpenRecentFile(index)`. In Avalonia, declare six `RelayCommand` properties:
```csharp
RecentFileCommand1 = new RelayCommand(() => mMainCtrl.OpenRecentFile(0));
RecentFileCommand2 = new RelayCommand(() => mMainCtrl.OpenRecentFile(1));
RecentFileCommand3 = new RelayCommand(() => mMainCtrl.OpenRecentFile(2));
RecentFileCommand4 = new RelayCommand(() => mMainCtrl.OpenRecentFile(3));
RecentFileCommand5 = new RelayCommand(() => mMainCtrl.OpenRecentFile(4));
RecentFileCommand6 = new RelayCommand(() => mMainCtrl.OpenRecentFile(5));
```
With corresponding `KeyBinding` entries:
```xml
<KeyBinding Gesture="Ctrl+Shift+D1" Command="{Binding RecentFileCommand1}"/>
<KeyBinding Gesture="Ctrl+Shift+D2" Command="{Binding RecentFileCommand2}"/>
<!-- ... through D6 -->
```
**Menu population:** The WPF `FileMenu_SubmenuOpened` event dynamically updates recent-file
menu items. In Avalonia, bind the File menu's `SubmenuOpened` event to a handler that
reads the recent-files list from `AppSettings` and updates menu item `Header` text and
`IsVisible` properties. Each of the 6 menu items is statically declared in AXAML with
`Command="{Binding RecentFileCommandN}"` and dynamically shown/hidden based on the
recent-files count. `MAX_RECENT_FILES` = 6 (from `MainController`).

### Step 4: Verify All Keyboard Shortcuts

**WPF `RoutedUICommand` / `CommandBinding` → Avalonia `RelayCommand` + `KeyBinding`:**

WPF uses `RoutedUICommand` (with `InputGestures`) and `CommandBinding` (with `CanExecute`
handlers) declared in XAML resources and the Window's `CommandBindings` collection. These
are entirely WPF infrastructure (`System.Windows.Input`). Avalonia has no `RoutedUICommand`,
`CommandBinding`, or `InputBinding`. All ~25 commands must be converted to:
- `RelayCommand` properties (or equivalent `ICommand` implementations) on the code-behind
  or controller
- `<KeyBinding Gesture="..." Command="{Binding ...}"/>` entries under `Window.KeyBindings`
  in the AXAML

**WPF `ApplicationCommands` (`Copy`, `Paste`, `Find`, `Help`, `Open`, `SelectAll`):**

Six built-in WPF `ApplicationCommands` are used via `CommandBinding` without a custom
`RoutedUICommand`. These route automatically with system-defined shortcuts in WPF. Avalonia
has no `ApplicationCommands`. Each must become an explicit `RelayCommand` with an explicit
`KeyBinding`:
- `Copy` → Ctrl+C
- `Paste` → Ctrl+V
- `Find` → Ctrl+F (see note below)
- `Help` → F1 (WPF default gesture for `ApplicationCommands.Help` — must be explicitly
  declared since Avalonia has no automatic system gesture)
- `Open` → Ctrl+O
- `SelectAll` → Ctrl+A

**Full shortcut table** (from XAML, including ApplicationCommands):

| Shortcut | WPF Command | Action |
|---|---|---|
| Ctrl+Shift+A | AddFilesCmd | Add Files |
| Ctrl+W | CloseCmd | Close |
| Ctrl+Shift+W | CloseSubTreeCmd | Close File Source |
| Ctrl+C | ApplicationCommands.Copy | Copy |
| Ctrl+Shift+N | CreateDirectoryCmd | Create Directory |
| Delete | DeleteFilesCmd | Delete Files |
| Alt+Enter | EditAttributesCmd | Rename / Edit Attributes |
| Ctrl+E | ExtractFilesCmd | Extract Files |
| Ctrl+F | ApplicationCommands.Find | Find in Archive |
| F1 | ApplicationCommands.Help | Help |
| Alt+Up | NavToParentCmd | Go To Parent |
| Ctrl+N | NewDiskImageCmd | New Disk Image |
| Ctrl+O | ApplicationCommands.Open | Open File |
| Ctrl+V | ApplicationCommands.Paste | Paste |
| Ctrl+Shift+1..6 | RecentFileCmd1..6 | Recent Files 1-6 |
| Ctrl+A | ApplicationCommands.SelectAll | Select All |
| Ctrl+I | ToggleInfoCmd | Toggle Information |
| Enter | ViewFilesCmd | View Files |
| Ctrl+Shift+T | Debug_DiskArcLibTestCmd | DiskArc Library Tests (DEBUG) |

**Paste `CanExecute` (T1-06):** WPF `CanPasteFiles` checks clipboard synchronously. An
async delegate cannot be used for `ICommand.CanExecute`. Per Iteration 13 decision: always
enable Paste and check clipboard content on invocation. Verify this is wired correctly.

**`ScanForBadBlocksCmd` CanExecute guard (design improvement):** WPF wires `CanExecute="IsNibbleImageSelected"`,
but `ScanForBadBlocks` works on any `IDiskImage` (block and sector modes). The guard may
be too restrictive — consider broadening to `IsDiskImageSelected` as a **deliberate
improvement over WPF behavior**. This is NOT a porting error — the WPF behavior is
intentional; changing it is a design decision that should be explicitly approved.

**`Find` (Ctrl+F) CanExecute guard (design improvement):** WPF wires `CanExecute="AreFileEntriesSelected"`,
meaning Ctrl+F only works when items are selected. A reasonable case can be made that Find
should be available whenever a file is open (`IsFileOpen` guard). However, this is a
**deliberate change from WPF behavior**, not a correction of a porting error. The WPF
behavior may be intentional (searching requires files to search through). If changing,
document it as an intentional improvement.

**Cross-platform keyboard conflict risks:**
- **Alt+Enter** on Linux: GNOME terminal and some WMs use this for fullscreen toggle.
  Consider testing on target desktop environments.
- **Delete**: standard file manager semantics but may conflict with in-place editing focus.
- **Enter** on Linux/macOS: fires in DataGrid after confirming inline edits — could
  accidentally launch View if focus is in the grid after editing.
- Avalonia's `KeyBinding` at the `Window` level competes with control-local key handling.
  Verify shortcuts are not silently swallowed by the OS/WM on Linux.

**Not present in WPF XAML** (no KeyGesture): Open Physical Drive, Settings.
These are menu-only commands in the WPF version.

### Step 5: Verify Window State Persistence

Ensure `WindowPlacement.cs` (from Iteration 3) correctly:
1. Saves window position, size, and state (maximized/normal) on close
2. Restores them on next launch
3. Handles multi-monitor scenarios (window on disconnected monitor → reset to default)
4. Handles first-launch (no saved position) gracefully

### Step 6: Theme & Visual Review

**`BooleanToVisibilityConverter` → direct `IsVisible` binding (T3-01, T3-02):**

`App.xaml` declares `<BooleanToVisibilityConverter x:Key="BoolToVis"/>` — this is
`System.Windows.Controls.BooleanToVisibilityConverter` (WPF-only). In Avalonia, `IsVisible`
is a `bool` property (not a `Visibility` enum), so no converter is needed at all. Replace
all bindings of the form:
```
Visibility="{Binding ..., Converter={StaticResource BoolToVis}}"
```
with:
```
IsVisible="{Binding ...}"
```
This also eliminates the need for the `BindingProxy` (`{StaticResource proxy}`) in
DataGrid column visibility bindings (lines 725–754 in WPF `MainWindow.xaml`). In Avalonia,
DataGrid columns inherit the row's DataContext, and the `IsVisible` property is a direct
`bool` — so `IsVisible="{Binding ShowCol_*}"` works directly if the column's DataContext
has access to the settings. If not, use `{Binding RelativeSource={RelativeSource AncestorType=Window}}`.

Remove the `BoolToVis` resource declaration from `App.axaml`.

Run through the entire application checking for visual issues:

1. **Fluent theme consistency:** All controls should match the Fluent theme. Look for:
   - Buttons, checkboxes, radio buttons rendering correctly
   - ComboBoxes showing styled dropdowns
   - DataGrids with proper headers and alternating rows
   - TreeViews with correct expand/collapse indicators

2. **Icon rendering:** All toolbar and tree icons render correctly (not stretched, not
   pixelated, not missing)

3. **Font rendering:** Mono fonts (Cascadia Mono → Consolas → Menlo → monospace) display
   correctly on each platform

4. **Dark mode:** If Avalonia Fluent supports system dark mode detection, verify the app
   looks reasonable in both light and dark modes. Minimum: no hard-coded colors that break
   in dark mode.

5. **High DPI:** Test on high-DPI displays. Avalonia handles scaling natively, but verify
   icons and layouts aren't blurry or misaligned.

### Step 7: Cross-Platform Testing

Test on all three platforms:

**Linux (primary dev platform):**
- All features should work
- Physical drive access may require elevated privileges
- Verify file dialogs (`StorageProvider`) work with various desktop environments
  (GNOME, KDE, etc.)

**macOS:**
- Verify standard macOS behaviors (close button on left, application menu)
- Test Retina display rendering
- Physical drive access may require Full Disk Access permission
- Avalonia's `KeyBinding` automatically maps `Ctrl` to `Cmd` on macOS (platform
  normalization). No code changes or platform-conditional `KeyBinding` entries are needed.
  Simply verify that `Ctrl+C` in AXAML fires on `Cmd+C` on macOS.

**Windows:**
- Verify feature parity with the original `cp2_wpf`
- Test that both `cp2_wpf` and `cp2_avalonia` can coexist (same AssemblyName but different
  .exes)
- Physical drive access should use existing Windows code path

### Step 8: Update MakeDist

Read `MakeDist/Build.cs`. Key facts:
- `sTargets` = `{ "cp2" }` — cross-platform CLI (always built)
- `sWinTargets` = `{ "cp2_wpf" }` — Windows-only GUI (currently WPF)
- `sDistFiles` lists: README.md, LegalStuff.txt, sample.cp2rc, docs/Manual-cp2.md
- Uses `dotnet publish` with runtime identifiers (RIDs)
- `SetExec` (line 170) marks binaries executable in non-Windows ZIPs via `sTargets`

**Decision required — three options for `cp2_avalonia` placement:**
1. Add `cp2_avalonia` to `sTargets` (cross-platform) — all RIDs (Windows, Linux, macOS)
   get a GUI binary. `cp2_wpf` goes away for non-Windows builds. This is correct since
   Avalonia is cross-platform. The `SetExec` call will mark the Avalonia binary executable
   in non-Windows ZIPs (correct behavior — `chmod +x` needed).
2. Replace `cp2_wpf` with `cp2_avalonia` in `sWinTargets` — Windows distributions get
   `cp2_avalonia` instead of `cp2_wpf`, but Linux/macOS distributions get no GUI.
3. Add `cp2_avalonia` to `sTargets` AND remove `cp2_wpf` from `sWinTargets` entirely —
   cleanest option: all platforms get the Avalonia GUI, WPF is retired.

Option 3 is recommended. Select the appropriate option and implement.

**`mkcp2.sh` (T3-05):** Currently hardcoded to `cp2/cp2.csproj`:
```bash
tver=$(awk -F "[><]" '/TargetFramework/{ print $3 }' cp2/cp2.csproj)
```
and the output path `cp2/bin/$config/$tver` is also hardcoded to `cp2`. To support
`cp2_avalonia` builds, either create a separate `mkcp2gui.sh` or extend `mkcp2.sh` with
an `--app` argument that overrides the project directory. Blueprint does not specify
content for `mkcp2gui.sh` — determine at implementation time.

### Step 9: Update Documentation

Update project documentation to reflect the Avalonia port:
1. `README.md` — update build instructions, platform support
2. `Install.md` — update installation instructions for all platforms
3. `SourceNotes.md` — document Avalonia-specific technical details
4. `WineNotes.md` — retain for historical reference but add a prominent note at the top
   marking it as obsolete: the document covers running the WPF binary under Wine for Linux
   testing, which is no longer necessary since Avalonia runs natively on Linux. Do not
   delete — the WPF-specific workaround information may still be useful for anyone
   maintaining the `cp2_wpf` project.

### Step 10: Final Cleanup

1. Remove any TODO/HACK/FIXME comments that reference temporary workarounds
2. Ensure all files have the correct Apache 2.0 license header
3. Verify all namespace references are `cp2_avalonia` (no lingering `cp2_wpf`)
4. Run `dotnet build` in Release configuration
5. Run the command-line tests: `dotnet test` on DiskArcTests and FileConvTests
6. Do a final side-by-side walkthrough comparing WPF and Avalonia functionality

---

## Feature Parity Checklist

Core operations:
- [ ] Open file archives (ZIP, NuFX, Binary II, etc.)
- [ ] Open disk images (ProDOS, DOS, HFS, etc.)
- [ ] Open nested archives/filesystems (multi-level)
- [ ] Navigate archive tree
- [ ] Navigate directory tree
- [ ] View file list with all columns
- [ ] Sort file list by any column
- [ ] View files (text, hex dump, images)
- [ ] Extract files to disk
- [ ] Add files from disk
- [ ] Import/Export with converters
- [ ] Delete files
- [ ] Move files (filesystem only)
- [ ] Edit file attributes
- [ ] Create new disk images (all types)
- [ ] Create new file archives (Binary II, NuFX, ZIP)
- [ ] Save disk image in new format
- [ ] Replace partition contents
- [ ] Edit sectors/blocks
- [ ] Scan blocks for errors
- [ ] Open physical drives
- [ ] Copy/Paste file entries
- [ ] Drag-drop from file manager to open
- [ ] Drag-drop from file manager to add
- [ ] Find file by name
- [ ] Recent files list
- [ ] Application settings persistence
- [ ] Window position persistence

Visual elements:
- [ ] Menu bar with all menus
- [ ] Toolbar with all buttons
- [ ] Status bar with file counts
- [ ] Archive tree with typed icons
- [ ] Directory tree
- [ ] File list DataGrid
- [ ] About box
- [ ] All dialogs present and functional
- [ ] Debug menu (conditional)
- [ ] Log viewer

---

## Files Modified in This Iteration

This iteration primarily modifies existing files rather than creating new ones:

| Action | File |
|---|---|
| **Modify** | Various `*.axaml.cs` files (remove stubs) |
| **Modify** | `cp2_avalonia/MainController.cs` (fill remaining TODOs) |
| **Create** | Any remaining utility files from `WPFCommon/` |
| **Modify** | `MakeDist/` files (build system updates) |
| **Modify** | `README.md`, `Install.md`, `SourceNotes.md` |
| **Modify** | `mkcp2.sh` (if applicable) |

---

## Verification: Final Sign-Off

- [ ] `dotnet build` succeeds in Debug and Release for all platforms
- [ ] Zero "Not Implemented" stubs remain (or documented as intentional omissions)
- [ ] All keyboard shortcuts functional
- [ ] Feature parity checklist above fully checked
- [ ] Tested on Linux
- [ ] Tested on macOS (or CI verified)
- [ ] Tested on Windows (or CI verified)
- [ ] Distribution packages build successfully
- [ ] Documentation updated
- [ ] Code review: no WPF references remain in cp2_avalonia
- [ ] License headers present on all new files
