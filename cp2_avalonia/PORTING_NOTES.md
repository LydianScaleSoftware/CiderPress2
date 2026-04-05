# Avalonia Port — Design Divergences from WPF

This document tracks intentional design or behavioral changes made during the
WPF-to-Avalonia port that go beyond a straight 1:1 conversion. Each entry describes
what changed, how it differs from the WPF version, and what supporting code was
modified.

---

## 1. Block Editor: Consolidated CP/M Block Ordering into the Editor Dialog

**Iteration:** 11 (Sector Editor)

### WPF Behavior

The WPF version provides three separate menu items under the Actions menu:

- **Edit Sectors...** — opens the hex editor in sector mode (track + sector addressing,
  DOS 3.3 sector order)
- **Edit Blocks...** — opens the hex editor in block mode (ProDOS block addressing)
- **Edit Blocks (CP/M)...** — opens the hex editor in block mode with CP/M skewed
  block addressing (`SectorOrder.CPM_KBlock`)

Each menu item launches the same `EditSector` dialog with a different `SectorEditMode`
enum value (`Sectors`, `Blocks`, or `CPMBlocks`). The CP/M menu item has its own
`CanExecute` guard (`CanEditBlocksCPM`) that checks `CPM.IsSizeAllowed()` on the disk's
formatted length. Once the dialog is open, there is no way to switch between ProDOS and
CP/M block ordering without closing and reopening via a different menu item.

### Avalonia Behavior

The "Edit Blocks (CP/M)..." menu item has been **removed**. The Actions menu now has
only two items:

- **Edit Sectors...**
- **Edit Blocks...**

When the editor opens in block mode, a **Block Order combo box** appears in the Advanced
configuration panel (alongside the existing Sector Skew and Sector Format controls). The
combo is always visible in block mode. When the disk supports meaningful CP/M remapping
(both `CPM.IsSizeAllowed()` and `NumSectorsPerTrack == 16`), the combo is enabled and
presents "ProDOS" and "CP/M" options. Otherwise the combo shows "ProDOS" but is
**disabled**, matching the pattern used by the Sector Skew combo. In sector mode, the
combo and its label are hidden entirely since block ordering is not relevant.

The CP/M option requires 16-sector floppy images because `ReadBlock` only applies
sector-order remapping when `NumSectorsPerTrack == 16`. Block-only images (e.g., 800K
`.po` files) ignore the `SectorOrder` parameter entirely, so offering CP/M ordering on
those images would be misleading.

Selecting a different block order in the combo immediately re-reads the current block
using the new `SectorOrder`, just as changing the existing Sector Skew combo does. If
there are unsaved modifications (`IsDirty`), the user is prompted to confirm abandoning
changes before the switch proceeds; declining reverts the combo to the previous selection.

### Rationale

Having the ordering choice inside the editor is more discoverable and convenient — users
can compare ProDOS and CP/M views of the same block without closing the dialog. It also
simplifies the menu structure by eliminating a specialized item that most users would
rarely need.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/EditSector.axaml` | Added Block Order combo box (label + `ComboBox`) in the Advanced `GroupBox`, with `IsVisible` bound to `IsBlockOrderVisible` and `IsEnabled` bound to `IsBlockOrderEnabled`. `ItemsSource` set in code-behind (not AXAML binding — see Note below). |
| `cp2_avalonia/EditSector.axaml.cs` | Added `BlockOrderItem` class (Label + SectorOrder + `ToString()`), `BlockOrderList` property, `IsBlockOrderVisible`/`IsBlockOrderEnabled` properties, `PrepareBlockOrder()` method, and `BlockOrderCombo_SelectionChanged` handler with dirty-change guard. Constructor's `CPMBlocks` case now maps to `SectorEditMode.Blocks` with `SectorOrder.CPM_KBlock`. CP/M guard checks both `CPM.IsSizeAllowed()` and `NumSectorsPerTrack == 16`. Label display (`SetSectorDataLabel`) checks `mSectorOrder == CPM_KBlock` (not `mEditMode`). Added `using DiskArc.FS` for `CPM.IsSizeAllowed()`. |
| `cp2_avalonia/MainWindow.axaml` | Removed the "Edit Blocks (CP/M)..." `MenuItem`. |
| `cp2_avalonia/MainWindow.axaml.cs` | Removed `EditBlocksCPMCommand` property and its `RelayCommand` initialization. |
| `cp2_avalonia/MainController_Panels.cs` | Removed the `EditBlocksCPMCommand.RaiseCanExecuteChanged()` call. The `CanEditBlocksCPM` property remains (unused, harmless). |

### Backward Compatibility

The `SectorEditMode.CPMBlocks` enum value is retained. If passed to the constructor (e.g.
from future code or tests), it is silently remapped to `SectorEditMode.Blocks` with
`mSectorOrder = SectorOrder.CPM_KBlock`, so the dialog opens in block mode with CP/M
ordering pre-selected in the combo. This ensures no call-site breakage.

### Note: Avalonia ComboBox Gotchas

Two Avalonia-specific issues were discovered during this work:

1. **`DisplayMemberBinding` does not exist on Avalonia `ComboBox`** — it is a WPF/DataGrid
   concept. Avalonia silently ignores the attribute. Items must render via `ToString()`
   override or an `ItemTemplate`.
2. **AXAML `ItemsSource` binding to a plain `List<T>`** — if items are added after
   `DataContext` is set, Avalonia doesn't see the changes because `List<T>` does not
   implement `INotifyCollectionChanged`. Fix: set `combo.ItemsSource = list` directly in
   code-behind after populating the list.
---

## Iteration 12: Library Tests & Bulk Compress

### TestManager — Output select area

**WPF behavior:** The ComboBox and detail TextBox below the progress area are simply blank
until tests have been run and failures have occurred.

**Avalonia change:** The ComboBox is disabled and the TextBox shows explanatory placeholder
text in both the "not yet run" and "all passed" states:
- Before any run: `"(No test results yet. Run the tests first. ...)"` 
- After a passing run: `"All tests passed. No failures to report."`
- After a run with failures: ComboBox enabled; selecting an entry shows the exception
  details in the TextBox.

---

## Iteration 13: File List Drag-Drop — Empty-Space Drop Targets the Volume Directory

**Iteration:** 13 (Clipboard & Advanced Drag-and-Drop)

### WPF Behavior

When dragging files within the file list DataGrid, dropping on empty space below the last
row is silently ignored. The drop target resolves to `NO_ENTRY`, which fails the
`IsDirectory` check. To move files to the volume root directory, the user must either
drop onto a visible directory entry for the root or drag to the directory tree panel.

### Avalonia Behavior

Dropping on empty space below the file list rows (or on a non-directory file entry) now
targets the **volume directory** of the currently selected filesystem in the Archive
Contents tree. This applies to both internal drag-move operations and external file drops
from the OS file manager.

Implementation: a parent `Grid` (`fileListPanel`) wraps the DataGrid with
`DragDrop.AllowDrop="True"` and `Background="Transparent"`. The `Transparent` background
is required because Avalonia does not hit-test controls with no background — drag events
would pass through without firing. The panel's `DragOver`/`Drop` handlers catch events
that the DataGrid did not handle (i.e. pointer was over empty space), and route them to
`IFileSystem.GetVolDirEntry()`.

### Rationale

The WPF behavior made it impossible to drag files back to the volume root from a
subdirectory view without using the directory tree panel. Since the empty space below the
file list has no other interactive purpose, treating it as a drop zone for the volume
directory is a natural and discoverable improvement.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainWindow.axaml` | Added `Name="fileListPanel"`, `DragDrop.AllowDrop="True"`, and `Background="Transparent"` to the parent Grid wrapping the DataGrid. |
| `cp2_avalonia/MainWindow.axaml.cs` | Added `FileListPanel_DragOver`, `FileListPanel_Drop`, and `GetCurrentVolumeDirEntry()` helper. Updated `FileListDataGrid_Drop` internal-move fallback to also use `GetCurrentVolumeDirEntry()` when the drop target is not a directory. |

---

## Clipboard Copy/Paste — Temp File Extraction and External App Support

**Iteration:** 13 (Clipboard & Advanced Drag-and-Drop)

### WPF Behavior

The WPF version uses `VirtualFileDataObject` (VFDO), a Windows COM mechanism based on
`FILEGROUPDESCRIPTOR`/`FILEDESCRIPTOR` structures. File contents are virtualized on the
clipboard and streamed on demand only when the receiving application requests them (lazy
materialization). No temp files are created.

For same-process paste (CP2→CP2), the WPF version serializes `ClipInfo` JSON plus stream
accessors via `ClipHelper` into the VFDO. The receiving CP2 instance reads
`ClipInfo.XFER_METADATA` and `ClipInfo.XFER_STREAMS` from the data object.

Paste from external applications (e.g. Windows Explorer) into CP2 is **not supported** —
if the clipboard does not contain a `ClipInfo` structure, the paste is silently ignored.

Changing the Drag & Copy mode radio button (Add/Extract ↔ Import/Export) has **no effect**
on data already placed on the clipboard, since VFDO evaluates the stream callbacks lazily
at drop time. However the callbacks capture settings at copy time, so this is arguably a
latent inconsistency the WPF version never addressed.

### Avalonia Behavior

`VirtualFileDataObject` is not available outside Windows. On Linux (X11/XWayland), the
clipboard cannot lazily provide file streams — data must be fully materialized before being
placed on the clipboard.

**CP2→Desktop copy:** When the user copies files, the selected entries are **eagerly
extracted** to a temp directory under the system temp path. The clipboard receives:

1. `DataFormats.Text` — serialized JSON (for same-process paste, same as WPF).
2. `"text/uri-list"` — RFC 2483 URI list (`file:///path\r\n` per file) using the extracted
   temp file paths. This is the standard freedesktop MIME type that X11-based file managers
   (KDE Dolphin, GNOME Nautilus, Thunar, etc.) recognize for file clipboard operations.

The format string `"text/uri-list"` is set directly on Avalonia's `DataObject` because the
`X11Clipboard` backend maps `DataObject.Set(formatString, value)` format strings directly
to X11 atom names. Avalonia's built-in `DataFormats.Files` constant does not produce atoms
that desktop file managers recognize.

The temp directory is cleaned up when: (a) a new copy operation replaces the old one,
(b) the work file is closed, or (c) the application exits.

**Desktop→CP2 paste:** The Avalonia version **adds support** for pasting files from
external applications, which the WPF version did not have. When the clipboard does not
contain CP2 JSON, the paste handler tries the following formats in order:

1. `text/uri-list` — standard freedesktop format.
2. `x-special/gnome-copied-files` — used by KDE Dolphin and GNOME (`copy\n<uri>\n...`).
3. Plain text fallback — scans for lines beginning with `file://`.

Parsed `file://` URIs are converted to local paths and routed through the existing
`AddFileDrop()` path, which handles both single files and directories.

**Mode change clears clipboard:** Because temp files are extracted eagerly at copy time
using the current mode's settings (NAPS suffixes for extract, converted extensions for
export), changing the Drag & Copy mode radio button **clears the clipboard** if there is
pending copied data (`mCachedClipEntries != null`). This prevents stale temp files from
being pasted with the wrong format assumptions. The WPF version did not need this because
VFDO callbacks were (in theory) evaluated at paste time.

### Rationale

The eager extraction approach is the only viable strategy on X11, where clipboard data must
be fully materialized. Supporting `text/uri-list` enables clipboard interop with Linux
desktop file managers — a capability the WPF version never had with Windows Explorer.
Clearing the clipboard on mode change is a necessary consequence of eager extraction: the
temp files are already named and formatted according to the old mode, so they cannot be
retroactively re-extracted.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainController.cs` | Added `mCachedClipEntries` and `mClipTempDir` fields. `CopyToClipboard()` now extracts `ForeignEntries` to a temp dir, builds `text/uri-list` from `file://` URIs, and sets both text and URI data on the clipboard. Added `ClearClipboardIfPending()`, `CleanupClipTemp()`, `PasteExternalFiles()`, `GetClipboardUriList()`, and `TryGetClipFormat()` helpers. `PasteOrDrop()` falls back to `text/uri-list` when CP2 JSON is not found. `CloseWorkFile()` calls `ClearClipboardIfPending()`. `WindowClosing()` calls `CleanupClipTemp()`. |
| `cp2_avalonia/MainWindow.axaml.cs` | `IsChecked_AddExtract` and `IsChecked_ImportExport` setters now call `mMainCtrl.ClearClipboardIfPending()` when toggled on. |

This makes the purpose of the controls unambiguous without requiring a test failure.