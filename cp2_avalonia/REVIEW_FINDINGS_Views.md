# Code Review Findings: cp2_avalonia/Views

Review date: 2026-11-10. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: every file in `Views/`, plus the view-model, service and platform code a finding
depended on (`ViewModels/MainViewModel.cs`, `ViewModels/FileListViewModel.cs`,
`ViewModels/EditSectorViewModel.cs`, `Services/DialogService.cs`, `Platform/LinuxDrag.cs`,
`Platform/MacOSDrag.cs`, `App.axaml.cs`) and the WPF original (`cp2_wpf/MainWindow.xaml.cs`)
for comparison.

**Status of these findings:** nothing was built or run. Findings 1 and 4 rest on Avalonia
behaviour that was confirmed by decompiling the installed packages (`Avalonia.Base` 11.3.22,
`Avalonia.Controls.DataGrid` 11.3.13); the rest come from reading the code only. Confirm each
one before fixing it. Line numbers refer to the commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

See also `REVIEW_FINDINGS.md` (the `Platform/` review) and `REVIEW_FINDINGS_ViewModels.md`.

## Summary

| # | Area | File | Finding | Confidence |
|---|------|------|---------|------------|
| 1 | All | MainWindow.axaml:77 | Window key bindings steal Delete / Ctrl+C / Ctrl+V / Ctrl+A from the find box | High |
| 2 | All | MainWindow.axaml.cs:1111 | File-list double-tap is not limited to data rows | High |
| 3 | All | MainWindow.axaml.cs:1889 | A file drag can start from the scrollbar track or blank grid space | High |
| 4 | All | MainWindow.axaml.cs:1846 | Resize-grip detection looks for a `Thumb` that never exists | High |
| 5 | Linux | MainWindow.axaml.cs:2019 | Internal drop can move files while extraction still runs | High |
| 6 | Linux | MainWindow.axaml.cs:1698 | Internal drop ignores window z-order | Medium |
| 7 | All | MainWindow.axaml.cs:1599 | Blank-grid drop uses the tree's directory even in full-list mode | Medium |
| 8 | macOS | FileViewer.axaml.cs:468 | Drag-to-viewer cannot work with the native drag | Medium |
| 9 | macOS | MainWindow.axaml.cs:621 | File activation can close the work file under a modal dialog | Medium |
| 10 | All | MainWindow.axaml.cs:1515 | Sort rebuilds the list with one notification per item | High |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

---

## 1. Window key bindings steal editing keys from the find box

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml:71-77` (`Window.KeyBindings`), with
  `Views/MainWindow.axaml.cs:1047` (`DirectoryTree_PreviewKeyDown`).
- **Category:** correctness.

**Problem.** Avalonia's `KeyboardDevice.ProcessRawEvent` walks `KeyBindings` from the focused
element up to the window *before* it raises `KeyDown`. `KeyBinding.TryHandle` marks the event
handled whenever the command's `CanExecute` is true. So the window-level bindings for `Delete`,
`Ctrl+C`, `Ctrl+V` and `Ctrl+A` run before `findFileTextBox` ever sees the key. None of the
commands' `CanExecute` methods (`CanDeleteFiles`, `CanCopy`, `CanPaste`) check where focus is.

The same ordering means a tunnel `KeyDown` handler cannot pre-empt a window key binding. The
comment on `DirectoryTree_PreviewKeyDown` says it stops Enter from reaching the window's
`Enter` -> `ViewFilesCommand` binding; it can only do that when `CanViewFiles` is already false.

**How to reproduce.** On Windows or Linux, open an image, press Ctrl+F, search for a file (it
becomes selected in the list while focus stays in the find box), then press Delete to edit the
search text.

**Result.** The "Delete 1 file entry?" confirmation appears instead of a character being
removed. Likewise Ctrl+V pastes clipboard files into the archive, Ctrl+A selects all rows and
Ctrl+C copies file entries. Enter in the directory tree opens the viewer whenever a file-list
row is selected.

**Fix direction.** Either move these bindings off the window onto the controls they belong to
(file list, trees), or have the commands (or a small wrapper) decline when a `TextBox` has
focus. The directory-tree Enter case needs the same treatment rather than a tunnel handler.
The macOS native-menu gestures (Cmd+C etc.) were not examined and may need the same check.

---

## 2. File-list double-tap is not limited to data rows

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:1099` (`FileListDataGrid_DoubleTapped`) and `:1117`
  (`PartitionLayout_DoubleTapped`).
- **Category:** correctness.

**Problem.** Both handlers only exclude column headers. `DoubleTapped` bubbles from anything
inside the DataGrid, including the scrollbar's arrow and track buttons and the blank area below
the rows, and the handlers then act on the current selection. `MetadataList_DoubleTapped`
already has the `DataGridRow` ancestor check these two lack.

**How to reproduce.** Select a file, then click the vertical scrollbar's down arrow twice
quickly.

**Result.** The file viewer opens (or the selected directory is entered, or the selected
partition is opened) for a row that was never double-clicked.

**Fix direction.** Return unless `e.Source` has a `DataGridRow` ancestor, as the metadata
handler does.

---

## 3. A file drag can start from the scrollbar track or blank grid space

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:1881-1889` (`FileListDataGrid_PointerPressed`).
- **Category:** correctness.

**Problem.** The handler arms a drag (`mDragStartPosn`) for any left press that is not on a
column header or a `Thumb`. The scrollbar's track and arrow buttons are `RepeatButton`s, not
thumbs, and blank grid space is neither. The WPF original
(`cp2_wpf/MainWindow.xaml.cs:1453`) arms a drag only when the press is on a `DataGridRow`, with
the comment "this allows the scroll bar to work".

**How to reproduce.** With files selected, press and hold on the scrollbar track or an arrow
button (or on blank space below the rows) and move the pointer more than 4 pixels.

**Result.** `StartFileListDragAsync` runs: on Windows and macOS it extracts the whole selection
to a temp directory on the UI thread, then starts an OS drag the user did not intend.

**Fix direction.** Require a `DataGridRow` ancestor before setting `mDragStartPosn`; this also
makes the separate scrollbar-thumb check unnecessary.

---

## 4. Resize-grip detection looks for a `Thumb` that never exists

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:1846` (`FileListDataGrid_PointerPressed`), `:1426`
  (`InfoDataGrid_PointerPressed`), `:1259` (`ResolveResizeTargetColumn`), `:1944`
  (`FileListDataGrid_PointerReleased`).
- **Category:** correctness (dead code ported from the WPF template).

**Problem.** The code decides "the press is on a column resize grip" by looking for a `Thumb`
under the `DataGridColumnHeader`. WPF's header template has left/right gripper thumbs;
Avalonia's does not. Avalonia resizes by pointer position inside the header
(`OnMouseLeftButtonDown` / `TrySetResizeColumn`), and the 11.3.13 DataGrid assembly has no
reference to the `Thumb` type at all. The app defines no custom header template. So `isOnGrip`
is always false, and everything hanging off it is dead: `mSuppressSort`, the `ClickCount == 2`
grip auto-size, `mPendingResizeCol`, and the "left grip resizes the column to the left" branch.

**How to reproduce.**
1. Drag the Filename (or Pathname) divider to a custom width, then navigate to another
   directory.
2. Double-click the divider at the left edge of a column.

**Result.**
1. `MarkNameColumnUserSized` was never called, so `PreSizeNameColumn` (run on every repopulate
   from `FileListViewModel.cs:256`) overwrites the user's width.
2. The header `DoubleTapped` path auto-sizes the column whose header was hit, not the column to
   its left that the divider actually resizes.

**Fix direction.** Detect a resize the way Avalonia does: compare the pointer's X against the
header's edges (the resize zone is a few pixels wide), or simply snapshot the name columns'
`ActualWidth` on every header press and compare on release. Then delete the `Thumb` checks.
Avalonia already skips the sort after a resize drag, so `mSuppressSort` may not be needed at
all.

---

## 5. Linux: internal drop can move files while extraction still runs

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:2007-2020` (`StartFileListDragAsync`) and `:1692`
  (`HandleLinuxInternalDrop`), with `Platform/LinuxDrag.cs:1109`.
- **Category:** correctness (thread safety).

This is the same defect as item 1 in `REVIEW_FINDINGS.md`; it is repeated here because the fix
is in the view. Track it in one place.

**Problem.** On Linux the native drag is armed first, then `BuildDragData` runs on a thread-pool
thread while the UI stays live. `LinuxDrag` posts the `onInternalDrop` callback as soon as the
button is released, without waiting for extraction. `HandleLinuxInternalDrop` can therefore
start `MoveFiles` on the UI thread while the pool thread is still reading the same
`IFileSystem`. `BuildDragData` also reads view-model selection state off the UI thread.

A second, smaller problem in the same function: if `BuildDragData` throws, the `catch` logs and
the `finally` resets state, but `SetNativeDragPaths` is never called, so the armed drag is left
to its 10-second spin-wait.

**How to reproduce.** On Linux, drag several large files from the file list onto a directory in
the same window and release quickly.

**Result.** Concurrent access to a DiskArc filesystem that is not thread-safe: open-file
errors, a failed move, or a damaged image.

**Fix direction.** Make the internal-drop callback wait for the extraction task (or cancel it)
before calling `MoveFiles`, and call `SetNativeDragPaths` with empty data from the `catch`
path.

---

## 6. Linux: internal drop ignores window z-order

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:1698` (`HandleLinuxInternalDrop`).
- **Category:** correctness.

**Problem.** The method tests the main window's bounds first and returns. A file viewer (or the
DropTarget debug window) that overlaps the main window is never reached; the drop is treated as
a drop on whatever main-window control lies underneath.

**How to reproduce.** On Linux, open a file viewer so it sits over the main window, then drag an
entry from the file list onto the viewer.

**Result.** `HitTestScreenDropTarget` picks the directory-tree node or directory row hidden
beneath the viewer and `MoveFiles` moves the entry there, instead of loading it in the viewer.

**Fix direction.** Check the secondary windows (viewers, DropTarget) before the main window, or
order the candidates by actual stacking order.

---

## 7. Blank-grid drop uses the tree's directory even in full-list mode

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:1599` (`FileListDataGrid_Drop`).
- **Category:** correctness (inconsistent target).

**Problem.** For an internal drag dropped on blank space inside the DataGrid, the handler
targets `GetCurrentDirEntry()` (the directory tree's selection) regardless of mode.
`FileListPanel_Drop` and `HandleLinuxInternalDrop` use `GetFileListEmptySpaceTarget()`, which is
the volume root in full-list mode. The comment at `:1715` explicitly warns against
`GetCurrentDirEntry` for this case.

**How to reproduce.** In full-list mode, double-click a subdirectory (this points the directory
tree at it). Drag a root-level file and drop it on blank space in the list.

**Result.** If the blank area is inside the DataGrid's bounds, the file moves into that
subdirectory; a few pixels lower, on the panel, the same gesture targets the volume root.
Dragging the tree-selected directory itself to blank grid space gives "Cannot move a directory
into itself or a descendant."

**Fix direction.** Use `GetFileListEmptySpaceTarget()` here too. Check that the "file dropped on
itself" case the existing comment describes is still filtered by the `ContainingDir` test.

**Related.** Dropping a directory row onto itself (a few pixels of pointer jitter while
clicking) also reaches `MoveFiles` and shows the same "Bad Move" dialog, because only
`ContainingDir != moveTarget` is filtered. WPF behaves the same way, so this is parity rather
than a regression.

---

## 8. macOS: drag-to-viewer cannot work with the native drag

- [ ] Fixed
- **Where:** `Views/FileViewer.axaml.cs:468` (`FileViewer_DragOver`), with
  `Views/MainWindow.axaml.cs:2055` (`StartFileListDragAsync`).
- **Category:** correctness (broken feature on one platform).

**Problem.** The viewer accepts a drop only when the data object carries the in-process
`cp2_avalonia/FileListDrag` and `FileListDragSource` formats. On macOS,
`StartFileListDragAsync` returns right after `StartNativeDrag` succeeds and never passes that
`DataObject` to `DoDragDrop`, so the pasteboard holds only file promises and the CP2 JSON.
Linux has an explicit fallback that hit-tests viewer windows; macOS has none.

**How to reproduce.** On macOS, drag an entry from the file list onto an open File Viewer
window.

**Result.** `FileViewer_DragOver` sets `DragDropEffects.None` and the drop is rejected.

**Fix direction.** While `mIsDraggingFileList` is set, let the viewer take the entry from the
main window's drag state (for example through the viewer service) instead of from the data
object. This needs a run on macOS to confirm; it was not tested.

---

## 9. macOS: file activation can close the work file under a modal dialog

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:619` (`OpenFileFromActivation`), with
  `ViewModels/MainViewModel.cs` (`OpenWorkFileCoreAsync`, `CloseWorkFile`).
- **Category:** correctness.

See also item 3 in `REVIEW_FINDINGS_ViewModels.md`, which covers the open guard itself.

**Problem.** `OpenFileFromActivation` posts `OpenWorkFileAsync` unconditionally.
`OpenWorkFileCoreAsync` calls `CloseWorkFile()`, which closes viewers and disposes the work
tree. The only guard is `mOpenInProgress`, which covers a concurrent *open*. Normal UI paths
are protected by modality; an OS "open document" event is not.

**How to reproduce.** On macOS, start an Add or Extract (the progress dialog runs a background
worker on the archive), or leave Edit Sector or Edit Attributes open. Double-click another disk
image in Finder.

**Result.** The work tree is disposed mid-operation. The worker, or the dialog's OK handler,
then acts on disposed archive objects.

**Fix direction.** Ignore or defer the activation while a modal dialog or work operation is
active (the dialog service knows its current owner), and tell the user why.

---

## 10. Sort rebuilds the list with one notification per item

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:1515` (`FileListDataGrid_Sorting`), `:1546`
  (`DoReapplyFileListSort`), `:849` (`SetFileListSelection`).
- **Category:** efficiency, with duplicated code.

**Problem.** Both sort paths clear the `ObservableCollection` and re-add the items one at a
time, so a live DataGrid processes N+1 collection changes instead of one reset. The two
functions are copies of each other. `SetFileListSelection` has the same shape: each `Add` fires
`SelectionChanged`, and the handler copies the whole selection each time, which is O(n²).

**Result.** In full-list mode on a volume with many thousands of entries, clicking a column
header stalls the UI, and so does every repopulate once a sort is active.

**Fix direction.** One shared helper that sorts into a list and replaces the collection's
contents in a single reset. For selection, suppress the per-item handler while the batch is
applied and snapshot once at the end.

---

## Smaller cleanups (not ranked)

These did not make the list above. None is a bug.

- [ ] `Views/FindFile.axaml(.cs)` is an unused window; its own comment says the inline find
  panel replaced it.
- [ ] Unused members in `MainWindow.axaml.cs`: `NotImplemented`, `GetCrossInstanceClipText`,
  `SelectedFileListItem`, `SelectedDirectoryTreeItem`, `FileList_SetSelectionFocus`, and the
  `if (false)` block around `openPhysicalDriveMenuItem`.
- [ ] `AsciiChartWindow.axaml.cs`: `GetCodeName` is unused and `EscapeDisplayChar` returns its
  input unchanged.
- [ ] The three drag-format strings are declared in both `MainWindow.axaml.cs:57` and
  `FileViewer.axaml.cs:462`; a typo in one silently breaks drag-to-viewer.
- [ ] The "files -> local paths" extraction is repeated in `FileListDataGrid_Drop`,
  `FileListPanel_Drop` and `DirectoryTree_Drop`.
- [ ] `EditSector.Window_KeyDown` handles Ctrl+C only, so Cmd+C does not copy the sector on
  macOS.
