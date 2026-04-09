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
| `cp2_avalonia/MainController.cs` | Added `mCachedClipEntries` and `mClipTempDir` fields. `CopyToClipboard()` now extracts `ForeignEntries` to a temp dir, builds `text/uri-list` from `file://` URIs, and sets both text and URI data on the clipboard. Also buffers each `XferEntry`'s file data as base64 into `ClipFileEntry.DataBase64` so the JSON is self-contained for cross-instance paste. Added `ClearClipboardIfPending()`, `CleanupClipTemp()`, `PasteExternalFiles()`, `GetClipboardUriList()`, and `TryGetClipFormat()` helpers. `PasteOrDrop()` falls back to `text/uri-list` when CP2 JSON is not found; for cross-instance paste, uses `DataBase64` from the deserialized entries. `CloseWorkFile()` calls `ClearClipboardIfPending()`. `WindowClosing()` calls `CleanupClipTemp()`. |
| `cp2_avalonia/MainWindow.axaml.cs` | `IsChecked_AddExtract` and `IsChecked_ImportExport` setters now call `mMainCtrl.ClearClipboardIfPending()` when toggled on. |
| `AppCommon/ClipFileEntry.cs` | **(Outside `cp2_avalonia` tree.)** Added `DataBase64` property — base64-encoded file contents populated at copy time, enabling cross-instance paste without a live `StreamGenerator`. |

---

## Drag-and-Drop to/from Desktop and Between Instances — Not Available on X11

**Iteration:** 13 (Clipboard & Advanced Drag-and-Drop)

### WPF Behavior

The WPF version uses `VirtualFileDataObject` (VFDO), a Windows COM mechanism that
implements the OLE drag-and-drop protocol. Dragging files from the CP2 file list to
Windows Explorer (or another application) works seamlessly — file contents are streamed on
demand through the VFDO callbacks. Dragging between two instances of CP2 also works: the
receiving instance reads `ClipInfo`/stream data from the VFDO.

### Avalonia Behavior (Linux / X11)

Dragging files **to the desktop, to a file manager, or between separate CP2 instances** is
**not available**.

Avalonia 11.2.x has no implementation of the XDND protocol (the X11 standard for
drag-and-drop between windows). The only drag source on X11 is `InProcessDragSource`, a
pure-Avalonia fallback that tracks pointer events through Avalonia's own input manager and
raises `RawDragEvent` to Avalonia visual tree nodes within the same process. When the
pointer leaves the Avalonia window, the `InProcessDragSource` receives a `LeaveWindow`
event and cancels the drag — it has no mechanism to negotiate with external X11 windows.

This means:

- **CP2 → Desktop / file manager:** Not possible. The desktop shows a "not allowed" cursor
  because it never receives an XDND enter message.
- **Desktop / file manager → CP2:** Works (Avalonia does handle *inbound* XDND drops from
  external sources).
- **CP2 instance → CP2 instance:** Not possible for the same reason as the desktop case.
- **Within a single CP2 instance:** Works. Internal drag-move between directories uses the
  `InProcessDragSource` with a custom `INTERNAL_DRAG_FORMAT` data format, which is purely
  in-process.

Users should use **clipboard copy / paste** (Ctrl+C / Ctrl+V) or the **menu commands**
(Actions → Extract Files, Actions → Export Files, Edit → Copy / Paste, etc.) to transfer
files between CP2 and the desktop.

### Rationale

There is no practical workaround for the missing XDND support short of implementing the
protocol from scratch via X11 P/Invoke calls, which is far beyond the scope of the port.
Future Windows and macOS builds may regain full drag-to-desktop support using their native
DnD APIs (OLE on Windows, `NSPasteboardItem` on macOS).

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainWindow.axaml.cs` | `StartFileListDragAsync` sets only `INTERNAL_DRAG_FORMAT` for in-process drag-move. Comment documents the XDND limitation. |

This makes the purpose of the controls unambiguous without requiring a test failure.

---

## Full File List Syncs Directory Tree Selection

**Iteration:** 13 (UI Polish)

### WPF Behavior

The WPF version does not sync the directory tree when a file is selected in the full
(flattened) file list.

### Avalonia Behavior

When viewing a hierarchical filesystem in the full file list mode, selecting a file now
updates the directory tree to highlight the containing directory of the selected file.
Selecting a directory entry highlights that directory itself in the tree.  If multiple
files are selected across different directories, the last selection wins.

A `mSyncingSelection` guard in `MainController` prevents infinite recursion between the
directory tree and file list selection handlers (directory tree change → file list select →
directory tree change → ...).

This only applies in full-list mode on filesystems.  In single-directory mode or for
archives, the behavior is unchanged.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainController_Panels.cs` | Added `mSyncingSelection` guard and `SyncDirectoryTreeToFileSelection()` method. Also added early return in `DirectoryTree_SelectionChanged` when guard is set. |

---

## Archive Tree Auto-Refresh After Add/Paste

### Problem

When a disk image or file archive is added (via Add Files or Paste) into a file archive
such as a ZIP, the new entry appeared in the file list but was not automatically opened as
a sub-volume in the Archive Contents tree.  The user had to double-click the entry to force
it open.  Both the WPF and Avalonia versions had this limitation — the initial WorkTree
scan discovers sub-volumes, but entries added later were not re-scanned.

### Solution

Added `TryOpenNewSubVolumes()` in `MainController_Panels.cs`.  After a successful add or
paste into a file archive, the method iterates the archive's entries and attempts
`WorkTree.TryCreateSub()` on any entry not already present in the archive tree.
`TryCreateSub` handles the "is this a recognizable format?" check internally, so only
genuine sub-volumes are opened.  New tree items are added via
`ArchiveTreeItem.ConstructTree()`.

This is called after `RefreshDirAndFileList()` in both the add-files and paste completion
paths.  It only applies to `IArchive` objects (file archives like ZIP, NuFX, etc.).

### Behavioral Difference from WPF

This is new behavior — the WPF version also requires a manual double-click to open
newly added sub-volumes.  The Avalonia version now handles this automatically.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainController_Panels.cs` | Added `TryOpenNewSubVolumes()` method |
| `cp2_avalonia/MainController.cs` | Added `TryOpenNewSubVolumes()` call after add-files and paste completion |
| `cp2_avalonia/MainWindow.axaml.cs` | `FileListDataGrid_SelectionChanged` now calls `SyncDirectoryTreeToFileSelection()`. |

---

## File List Sort Order Preserved Across Repopulation

### WPF Behavior

When the user renames a directory (via Edit Attributes), the file list does not update
the FQPN (Fully Qualified Path Name) column for child entries — they continue to show the
old directory name until the list is manually refreshed.  Additionally, any user-applied
column sort is lost whenever the file list is repopulated (e.g., after an edit, add, or
delete operation).

### Avalonia Behavior

Two improvements were made:

1. **Directory rename refreshes all paths.**  When a directory entry is renamed,
   `FinishEditAttributes` now rebuilds every `FileListItem` in the file list in-place.
   Each item is reconstructed from its `IFileEntry` (which the filesystem has already
   updated with the new `FullPathName`), so all child entries immediately show the
   correct path.  Because items are replaced at the same indices, the display order is
   preserved.

2. **Sort order is remembered and reapplied.**  `MainWindow` now stores the last-sorted
   `DataGridColumn` reference and sort direction (`mSortColumn`, `mSortAscending`).
   These are set in `FileListDataGrid_Sorting` whenever the user clicks a column header,
   and cleared in the `ResetSortCommand`.  A new `ReapplyFileListSort()` method re-sorts
   the `FileList` using the stored state; it is called at the end of `PopulateFileList`
   so that any repopulation (after rename, add, delete, paste, etc.) automatically
   restores the user's chosen sort order.

### Rationale

The stale-FQPN bug existed in the WPF version as well and was a known issue.  Preserving
sort order across repopulation is a quality-of-life improvement — users should not have to
re-click the column header every time the list refreshes.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainController.cs` | `FinishEditAttributes` rebuilds all `FileListItem` objects in-place when the edited entry is a directory. |
| `cp2_avalonia/MainWindow.axaml.cs` | Added `mSortColumn` / `mSortAscending` fields, updated `FileListDataGrid_Sorting` to store sort state, updated `ResetSortCommand` to clear it, added `ReapplyFileListSort()` method. |
| `cp2_avalonia/MainController_Panels.cs` | `PopulateFileList` calls `mMainWin.ReapplyFileListSort()` after repopulation. |

---

## Window Placement Restore on Startup

### WPF Behavior

The WPF version saves and restores window position, size, and state using Win32
`GetWindowPlacement` / `SetWindowPlacement` interop.  The placement is serialized as a
Base64 string and stored in the `MAIN_WINDOW_PLACEMENT` setting.  Both save and restore
are implemented, so the window geometry is fully persisted across sessions.

### Avalonia Behavior

The Avalonia version uses a cross-platform JSON-based `WindowPlacement` utility class
(`cp2_avalonia/Common/WindowPlacement.cs`) that saves X, Y, Width, Height, and
WindowState.  The `Save` side was already wired into `SaveAppSettings()`, but the
`Restore` call was missing from `ApplyAppSettings()` — so the placement was saved but
never applied on startup.  Additionally, `WindowClosing()` did not force
`IsDirty = true` on the settings, which meant that if the window was only moved or
resized (without changing any other setting), the placement would not be persisted at all.

Both issues have been corrected:
- `ApplyAppSettings()` now calls `WindowPlacement.Restore(mMainWin, placement)`.
- `WindowClosing()` now sets `AppSettings.Global.IsDirty = true` before calling
  `SaveAppSettings()`, matching the WPF pattern.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/MainController.cs` | Added `WindowPlacement.Restore()` call in `ApplyAppSettings()`. Added `AppSettings.Global.IsDirty = true` in `WindowClosing()`. |

---

## Debug Log — Copy to Clipboard

### WPF Behavior

The WPF debug log viewer (`Tools/LogViewer`) provides only a **Save to File** button.
There is no built-in way to copy log output to the system clipboard.

### Avalonia Behavior

A **Copy to Clipboard** button has been added alongside the existing Save to File button.
Clicking it formats all log entries (timestamp, priority, message) using the same format
as the file save and places the text on the system clipboard via
`TopLevel.Clipboard.SetTextAsync()`.  This is new functionality that does not exist in the
WPF version.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/Tools/LogViewer.axaml` | Replaced single Save button with a `StackPanel` containing "Copy to Clipboard" and "Save to File" buttons. |
| `cp2_avalonia/Tools/LogViewer.axaml.cs` | Added `CopyLog_Click` handler. Added `using Avalonia.Input` for clipboard access. |