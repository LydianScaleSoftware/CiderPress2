# Code Review Findings: cp2_avalonia/Models

Review date: 2026-11-10. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: every file in `Models/` (14 files), plus the callers a finding depended on
(`Views/MainWindow.axaml(.cs)`, `ViewModels/LogViewerViewModel.cs`,
`ViewModels/ArchiveTreeViewModel.cs`, `ViewModels/MainViewModel.cs`, `App.axaml.cs`,
`DiskArc/Arc/Zip_FileEntry.cs`). The directory has no changes against `main` on this branch,
so the files were reviewed as they stand and compared against their `cp2_wpf` originals to
spot port regressions.

**Status of these findings:** they come from reading the code only. Nothing was built or run,
and no verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

**WPF parity and improvement.** The port aims for parity with the WPF application as far as
possible, and also to improve on it. `cp2_wpf` is deprecated and will be discontinued, so a
fix here does not need to be mirrored in the WPF code. Items 2, 3 and 5 also exist in the WPF
originals; fixing them is an improvement, not a reason to leave them. Item 6 is the reverse
case: behaviour that changed from WPF with no recorded reason, so parity is the default
answer unless the change was intended.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

See also `REVIEW_FINDINGS.md` (the `Platform/` review), `REVIEW_FINDINGS_ViewModels.md` and
`REVIEW_FINDINGS_Views.md`.

## Summary

| # | File | Finding | Confidence |
|---|------|---------|------------|
| 1 | MetadataItem.cs:38 | Metadata text is hard-coded black, unreadable in the dark theme | High |
| 2 | FileListItem.cs:404 | Rsrc Len sort ignores MacZip resource fork lengths | High |
| 3 | FileListItem.cs:395 | Size-column sorts overflow for differences of 2 GB or more | High |
| 4 | ArchiveTreeItem.cs:317 | `BringItemIntoView` does nothing for tree items not yet realized | Low |
| 5 | DebugMessageLog.cs:59 | `Clear()` leaves a stale ring-buffer cursor | High (latent) |
| 6 | FileListItem.cs:367 | Text-column sorts became case-sensitive ordinal | High (may be intended) |
| 7 | DebugMessageLog.cs:87 | Log events can arrive out of order or be missed | Medium |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first. Design and cleanup items follow the
numbered findings.

---

## 1. Metadata text is hard-coded black, unreadable in the dark theme

- [ ] Fixed
- **Where:** `Models/MetadataItem.cs:38` (`TextForeground`), bound at
  `Views/MainWindow.axaml:781` and `:788`.
- **Category:** theming.

**Problem.** `TextForeground` returns `Brushes.Black` for editable entries and `Brushes.Gray`
for read-only ones. The app supports a dark theme (`App.axaml.cs:184-188` maps
`ThemeMode.Dark` to `ThemeVariant.Dark`), and the metadata grid binds `Foreground` straight to
this property. The property also has no change notification, so it would not follow a live
theme switch even if it picked a theme-aware brush.

**How to reproduce.** Set the theme to Dark. Open an image that has metadata (WOZ, 2IMG).
Show the Information panel.

**Result.** Editable Key and Value cells are black on a dark background.

**Fix direction.** Move the colour choice out of the model. Expose only `CanEdit` and let the
view pick the brush with a style class or `DynamicResource` brushes, the way the archive tree
already uses `ArchiveTypeTextBrush`.

## 2. Rsrc Len sort ignores MacZip resource fork lengths

- [ ] Fixed
- **Where:** `Models/FileListItem.cs:404` (`ItemComparer.Compare`, `ColumnId.RsrcLen`); the
  displayed value is set at `:222`.
- **Category:** wrong sort.

**Problem.** For a MacZip pair the displayed resource length comes from
`adfAttrs.RsrcLength`. The comparer sorts on `FileEntry.RsrcLength`, and
`Zip_FileEntry.RsrcLength` always returns 0 (`DiskArc/Arc/Zip_FileEntry.cs:171`).

**How to reproduce.** Open a ZIP archive whose entries have `__MACOSX` AppleDouble headers
with resource forks. Click the Rsrc Len column header.

**Result.** Every comparison is 0 against 0, so the list falls through to the pathname
tie-break. The column shows non-zero lengths in pathname order.

**Fix direction.** Keep the numeric resource length in a field, as `mRawDataLen` and
`mTotalSize` already do, and compare that. Doing the same for the data length would stop the
comparer reading `FileEntry` properties during a sort.

## 3. Size-column sorts overflow for differences of 2 GB or more

- [ ] Fixed
- **Where:** `Models/FileListItem.cs:395`, `:398`, `:404`, `:410` (DataLen, RawDataLen,
  RsrcLen, TotalSize).
- **Category:** wrong sort.

**Problem.** Each case computes `(int)(a - b)` on `long` values. A difference of 2 GB or more
wraps when cast to `int`.

**How to reproduce.** Sort by Data Len or Total Size in a list that holds both a very large
entry (for example a 3 GB disk image inside an archive) and a small one.

**Result.** 3,000,000,000 minus 100 casts to a negative number, so the large entry sorts
before the small one. A difference of exactly 4 GB casts to 0 and is treated as equal. The
comparer is no longer transitive, so the overall order is arbitrary.

**Fix direction.** Use `a.CompareTo(b)`.

## 4. `BringItemIntoView` does nothing for tree items not yet realized

- [ ] Fixed
- **Where:** `Models/ArchiveTreeItem.cs:317` and `Models/DirectoryTreeItem.cs:160`; callers
  include `MainViewModel.cs:1215`, `:2588`, `:2597` via `SelectArchiveTreeItem`, and
  `SelectBestArchiveTreeItem` after a file is opened.
- **Category:** scrolling. **Unconfirmed: rests on the reviewer's understanding of when
  Avalonia creates `TreeViewItem` containers. Test in the app first.**

**Problem.** The walk calls `ContainerFromItem` at each level and returns silently when it
gets `null`. The comment says that should not happen without virtualization. It is expected
to happen when the item was added to the tree in the same UI tick, because containers are
created on the next layout pass. Setting `tvi.IsExpanded = true` on a collapsed ancestor has
the same limitation: it does not create the child containers synchronously, so the next
`ContainerFromItem` call still returns `null`.

**How to reproduce.** Open a file whose archive tree is taller than the panel and whose
"best" node is below the fold, or open a nested archive deep in a long tree.

**Result.** The node is selected, because `SelectedItem` is set on the model item, but the
tree is not scrolled to show it.

**Fix direction.** Defer the walk with `Dispatcher.UIThread.Post` at a priority after layout,
or force layout on the just-expanded container before descending. Fix it once in a shared
helper; see the duplication item under Cleanup candidates.

## 5. `Clear()` leaves a stale ring-buffer cursor

- [ ] Fixed
- **Where:** `Models/DebugMessageLog.cs:59` (`Clear`), read back at `:103-108` (`GetLogs`).
- **Category:** crash, latent. Nothing in `cp2_avalonia` calls `Clear()` on the debug log
  today.

**Problem.** `Clear()` empties `mEntries` but does not reset `mTopEntry`. Once the log has
wrapped (more than 100,000 lines), `mTopEntry` is non-zero and points past the end of the
emptied list.

**How to reproduce.** Log more than 100,000 lines, call `Clear()`, then open the log viewer.

**Result.** `GetLogs()` runs `for (i = 0; i < mTopEntry; i++) list.Add(mEntries[i])` against
a shorter list and throws `ArgumentOutOfRangeException`. Without the throw, the next wrap
starts overwriting at the stale index and `GetLogs()` returns entries out of order.

**Fix direction.** Set `mTopEntry = 0` inside the lock in `Clear()`.

## 6. Text-column sorts became case-sensitive ordinal

- [ ] Fixed (or confirmed as intended)
- **Where:** `Models/FileListItem.cs:367-413` (every `string.CompareOrdinal`), and the
  tie-break at `:423`, which is still culture-aware `string.Compare`.
- **Category:** behaviour change in the port. May be deliberate (trimming, invariance); the
  reason is not recorded in the code.

**Problem.** The WPF comparer used `string.Compare` (current culture) for Filename, Pathname,
Type, Auxtype, formats and Access. The port switched them all to `CompareOrdinal`, which
orders by code point, so every uppercase letter sorts before every lowercase one. The
pathname tie-break was left culture-aware, so the primary key and the tie-break can order the
same pair of names differently.

**How to reproduce.** On an HFS or ProDOS volume with files `apple`, `Banana` and `ZEBRA`,
click the Filename header.

**Result.** Banana, ZEBRA, apple. A case-insensitive sort gives apple, Banana, ZEBRA.

**Fix direction.** Decide the intended order. If ordinal was chosen for trimming, use
`StringComparison.OrdinalIgnoreCase` (with an ordinal tie-break for names that differ only by
case) and apply the same comparison at `:423`.

## 7. Log events can arrive out of order or be missed

- [ ] Fixed
- **Where:** `Models/DebugMessageLog.cs:84-95` (`OnRaiseLogEvent`), with
  `ViewModels/LogViewerViewModel.cs:109-116` and `:163-166`.
- **Category:** display glitch in the debug log viewer.

**Problem.** A log call on the UI thread invokes subscribers immediately. A log call on a
worker thread queues the invocation with `Dispatcher.UIThread.Post`, using the delegate list
captured at log time. Three consequences:

- A worker entry followed closely by a UI-thread entry reaches the viewer in the opposite
  order.
- `LogViewerViewModel` takes its `GetLogs()` snapshot and then subscribes. A worker entry
  logged between those two steps is in neither the snapshot nor the event stream.
- An entry posted before `OnWindowClosed` unsubscribes still invokes the handler afterwards.

**Fix direction.** Always post, so delivery order matches log order, or have the viewer
insert by `Index`. In the viewer, subscribe before taking the snapshot and drop duplicates by
`Index`. The comment on `HandleLogEvent` ("may be called on a background thread") is no
longer true and should go.

---

## Design issue

- [ ] **Sort column is resolved from the header's display text.** `FileListItem.cs:330-350`.
  `ItemComparer` maps `col.Header?.ToString()` to a `ColumnId` and silently falls back to
  `FileName` for anything it does not recognise. Renaming a header in `MainWindow.axaml`,
  localizing headers, or using non-string header content makes that column sort by filename
  with no error. `SetFileListSortIndicator` (`MainWindow.axaml.cs:778`) relies on the same
  unique-header-string assumption. Key the mapping on something stable, such as
  `DataGridColumn.SortMemberPath` or `Tag`, and assert on the default arm.

## Cleanup candidates

- [ ] **Tree item helpers exist in three places.** `BringItemIntoView`,
  `PurgeSelectionsExcept`/`PurgeSelections` and the `IsExpanded`/`IsSelected` plumbing are
  copy-pasted between `ArchiveTreeItem.cs` and `DirectoryTreeItem.cs`, and
  `MainWindow.FocusSelectedDirectoryTreeItem` (`MainWindow.axaml.cs:950-976`) has a third copy
  of the root-to-target container walk. A fix for item 4 has to be made in each. A generic
  base class (`Parent`, `Items`, `IsExpanded`, `IsSelected`) plus one container-walk helper in
  the view layer would remove the copies.
- [ ] **`PurgeSelections` is wasteful and skips the root.** `ArchiveTreeItem.cs:330-346`,
  `DirectoryTreeItem.cs:173-189`. It walks the whole tree on every `IsSelected = true`, even
  when the value did not change, and starts at `top.Items`, so the root item's own flag is
  never cleared. `IsSelected` is not bound in `MainWindow.axaml` (only `IsExpanded` is), so
  this code compensates for a WPF virtualization problem that does not exist here. See the
  related "Tree `SelectedItem` machinery is unused" item in `REVIEW_FINDINGS_ViewModels.md`
  and decide both together.
- [ ] **Dead and pass-through members left from the port.** `FileListItem.SelectAndView`
  (`FileListItem.cs:281`) has no callers. `ArchiveTreeItem.SelectItem`
  (`ArchiveTreeItem.cs:247`) only forwards to `viewActions.SelectArchiveTreeItem` and is the
  reason `Models` imports `cp2_avalonia.Views`. `DebugMessageLog.GetEntryCount` is unused.
  `[Serializable]` on `ClipInfo` does nothing for `System.Text.Json`.
- [ ] **Models reference Avalonia controls.** `SelectBestFrom` and `BringItemIntoView` take a
  `TreeView`; `SetSelectionFocusByEntry` takes a `DataGrid`. Moving them into the view would
  leave `Models` free of control types. `SetSelectionFocusByEntry` also carries a TODO at
  `FileListItem.cs:299` for the row-focus step that WPF's `SelectRowByIndex` performed.
