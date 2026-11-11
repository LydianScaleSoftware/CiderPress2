# Code Review Findings: cp2_avalonia/ViewModels

Review date: 2026-11-09. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: every file in `ViewModels/`, plus the views, models and services a finding depended on
(`Views/MainWindow.axaml(.cs)`, `Views/FileViewer.axaml.cs`, `Views/ReplacePartition.axaml`,
`Views/CreateDiskImage.axaml`, `Models/ArchiveTreeItem.cs`, `Models/FileListItem.cs`,
`Services/SettingsService.cs`, `App.axaml.cs`). The branch's changes against `main` in this
directory (`MainViewModel.cs`, `EditAttributesViewModel.cs`, `DirectoryTreeViewModel.cs`) were
the starting point.

**Status of these findings:** they come from reading the code only. Nothing was built or run,
and no verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

See also `REVIEW_FINDINGS.md` (the `Platform/` review).

## Summary

| # | File | Finding | Confidence |
|---|------|---------|------------|
| 1 | ReplacePartitionViewModel.cs:153 | Copy button is bound to a command that does not exist | High |
| 2 | EditSectorViewModel.cs:703 | Position moves before the "discard changes?" prompt | High |
| 3 | MainViewModel.cs:674 | Open guard is timing-based and can lose `--read-only` | Medium |
| 4 | MainViewModel.cs:3964 | Close File Source never reselects the parent node | Medium |
| 5 | EditAppSettingsViewModel.cs:203 | Settings OK reverts converter option changes | High |
| 6 | FileListViewModel.cs:251 | An active column sort drops the selection on every refresh | Medium |
| 7 | FileViewerViewModel.cs:668 | Viewer reloads on every main-window refresh | High |
| 8 | FileViewerViewModel.cs:1600 | Find Previous stays on the same match | High |
| 9 | MainViewModel.cs:1128 | Activating a viewer resets the main list selection | High |
| 10 | CreateDiskImageViewModel.cs:335 | Create button ignores `IsValid` | High |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

---

## 1. Replace Partition Copy button is bound to a command that does not exist

- [ ] Fixed
- **Where:** `ViewModels/ReplacePartitionViewModel.cs:153` (`OkCommand`), with
  `Views/ReplacePartition.axaml:56`.
- **Category:** broken feature.

**Problem.** The view model exposes the copy action as `OkCommand`. The view binds the Copy
button with `Command="{Binding CopyCommand}"`. `ReplacePartition.axaml` has no `x:DataType`, so
this is a reflection binding that fails silently at run time instead of at build time. The
code-behind has no click handler either.

**How to reproduce.** Actions > Replace Partition Contents. Pick a compatible source image.
Click Copy.

**Result.** Nothing happens. No copy, no error, and the dialog stays open.

**Fix direction.** Rename one side so they match. Adding `x:DataType` to the view would turn
this class of mistake into a build error.

**Related, once the button works.** `DoCopy` runs `SaveAsDiskViewModel.CopyDisk` on the UI
thread with no `try`/`catch`. A large partition freezes the window, and a write failure is an
unhandled exception in an `AsyncRelayCommand`.

---

## 2. Sector editor moves position before the "discard changes?" prompt

- [ ] Fixed
- **Where:** `ViewModels/EditSectorViewModel.cs:683-719` (`ReadButtonClickedAsync`,
  `PrevButtonClickedAsync`, `NextButtonClickedAsync`) and `:822-828`
  (`OnSectorOrderChangedAsync`).
- **Category:** possible data damage.

**Problem.** Prev, Next and Read update `mCurBlockOrTrack` / `mCurSector` and the text fields
first, then call `ReadFromDiskAsync`, which asks "Discard unwritten modifications?". If the
user declines, nothing is rolled back. The buffer still holds the edited data of the old
block, but the current position and the entry fields name the new one. `WriteButtonClickedAsync`
only warns when the current and entered positions differ, and they no longer do.
`OnSectorOrderChangedAsync` has the same shape: it changes `mSectorOrder` before the prompt.
(`OnBlockOrderChangedAsync` does this correctly and reverts the combo box.)

**How to reproduce.** Open a disk for block editing. Edit a byte in block 5. Click Next. Answer
No to the discard prompt. Click Write.

**Result.** Block 5's edited data is written to block 6, with no warning.

**Fix direction.** Ask for confirmation before changing any state, and return early if the
user declines.

**Note.** `cp2_wpf/EditSector.xaml.cs` has the same ordering (`PrevButton_Click` at line 736),
so this is an upstream bug, not a port regression. Worth raising with Andy.

---

## 3. Open guard is timing-based and can lose `--read-only`

- [ ] Fixed
- **Where:** `ViewModels/MainViewModel.cs:667-689` (`OpenWorkFileAsync`), with
  `Views/MainWindow.axaml.cs:603-637` and `App.axaml.cs:149`.
- **Category:** incorrect behaviour; fix is at the wrong depth.

**Problem.** Commit `9fe7762` added `mOpenInProgress` so that a second open request arriving
during a load is ignored. On macOS a file given on the command line is delivered twice: once
from `Program.StartupOptions` (carrying the `-r` flag) and once as a file-activation event
(always `readOnly: false`). The guard drops whichever arrives second while the first is still
loading. It does not compare the path or the read-only flag, and it does nothing once the
first load has finished.

**How to reproduce.** On macOS, run `CiderPress2 -r disk.po`.

**Result.** Depends on timing:

- Activation first: the file opens read-write and the `-r` request is ignored with only a log
  line.
- `-r` first and still loading: correct, the activation is ignored.
- `-r` first and already finished: the file is closed and reopened read-write.

Without `-r`, the third case is still a wasted second load.

**Fix direction.** Remove the duplicate at its source. Either ignore an activation whose path
matches the startup path, or have `OpenWorkFileAsync` treat a request for the file that is
already open or loading as a no-op (keeping the stricter read-only setting).

**Also.** `DropOpenWorkFile` and `OpenRecentFile` call `CloseWorkFile()` before
`OpenWorkFileAsync`, so they close the current file even when the guard then ignores the
request.

---

## 4. Close File Source never reselects the parent node

- [ ] Fixed
- **Where:** `ViewModels/MainViewModel.cs:3951-3965` (`CloseSubTree`).
- **Category:** stale state.

**Problem.** After removing the node, the method sets `parentItem.IsSelected = true`. In WPF
that property is bound to `TreeViewItem.IsSelected`. In `Views/MainWindow.axaml:487-491` the
archive tree's item style binds only `IsExpanded`, so the flag never reaches the control.
The only listener is `ArchiveTreeViewModel.SelectedItem`, which nothing in the view observes.
Commit `9fe7762` fixed the same root cause for the directory tree.

**How to reproduce.** Open a file that contains a sub-archive. Select the sub-archive in the
archive tree. Run Close File Source.

**Result.** The node is disposed and removed, but `CachedArchiveTreeSelection` still points at
it. Command states are computed from the closed node, and follow-up actions operate on a
closed `WorkTree` node.

**Not confirmed.** Whether Avalonia's `TreeView` keeps the removed item as `SelectedItem` or
clears the selection. Either way the parent is not selected.

**Fix direction.** Call `_viewActions.SelectArchiveTreeItem(parentItem)`, as the other callers
do through `ArchiveTreeItem.SelectItem`.

---

## 5. Settings OK reverts converter option changes

- [ ] Fixed
- **Where:** `ViewModels/EditAppSettingsViewModel.cs:203` (`ApplySettingsToGlobal`) and
  `:207-215` (`OpenConvertOpts`).
- **Category:** lost user input.

**Problem.** The Settings dialog edits a snapshot (`mSettings`) taken when it opens. OK and
Apply call `_settingsService.ReplaceSettings(mSettings)`, which replaces the entire global
settings set. The Configure Import/Export sub-dialog is given the global service, and its OK
merges changes straight into the global settings. The snapshot never sees them. The WPF
version passes its local settings object to `EditConvertOpts`.

**How to reproduce.** Edit > Settings. Click Configure export options, change an option, OK.
Then OK the Settings dialog.

**Result.** The converter option change is reverted and the reverted value is saved. The
change survives only if Settings is cancelled, which is the opposite of what the buttons
mean.

**Fix direction.** Have the sub-dialog merge into `mSettings` instead of the global service,
so OK, Apply and Cancel all behave consistently.

---

## 6. An active column sort drops the selection on every refresh

- [ ] Fixed
- **Where:** `ViewModels/FileListViewModel.cs:235-251` (`PopulateFileList`) and `:349-391`
  (`VerifyFileList*`), with `Views/MainWindow.axaml.cs:1540` (`DoReapplyFileListSort`).
- **Category:** selection loss, wasted work.

**Problem.** Two effects of sorting `Items` in place:

- `PopulateFileList` restores the selection with `SelectFileListItemByEntry` and then calls
  `ReapplyFileListSort`. With a sort column active, that clears `Items` and adds everything
  back, which discards the selection that was just set.
- `VerifyForObject` compares `Items` to the archive's natural order. A sorted list never
  matches, so every `RefreshDirAndFileList` repopulates the whole list.

**How to reproduce.** Click a column header to sort. Edit a file's attributes, or delete a
file.

**Result.** The list is rebuilt twice and ends with no row selected. Without a sort, the
edited or neighbouring row stays selected.

**Not confirmed.** That the Avalonia `DataGrid` drops its selection on `Clear()` followed by
re-adding the same instances. That is the expected behaviour for a collection reset.

**Fix direction.** Apply the sort before restoring the selection (or sort the list before
adding it to `Items`). For verification, compare as sets or keep an unsorted copy.

---

## 7. Viewer reloads on every main-window refresh

- [ ] Fixed
- **Where:** `ViewModels/FileViewerViewModel.cs:630-678` (`ReconcileAfterRefresh`), called from
  `MainViewModel.RefreshActiveFileViewer` at `MainViewModel.cs:4277`.
- **Category:** unwanted side effects, wasted work.

**Problem.** `ReconcileAfterRefresh` rebuilds every history entry by re-resolving its whole
path list, then calls `RestoreHistoryEntry`, which reloads and reconverts the current file,
even on the path that returns `NoChange`. `RefreshDirAndFileList` runs on every directory tree
click as well as after add, delete and settings changes.

**How to reproduce.** Open a viewer on an RTF, PDF or Word file. Click a different directory
in the main window's directory tree.

**Result.** The external viewer is launched again, with another temp file, on every click.
For ordinary files the view is reconverted and reset each time. With a multi-file selection,
`PrePopulateForwardHistory` creates one history entry per file, each holding the full path
list, so the rebuild costs (entries × paths) lookups per refresh.

**Fix direction.** Only reload when an entry actually changed (for example, when the resolved
`IFileEntry` differs or is no longer valid). Share one path list between history entries
instead of resolving it once per entry.

---

## 8. Find Previous stays on the same match

- [ ] Fixed
- **Where:** `ViewModels/FileViewerViewModel.cs:1600` (`DoFind`).
- **Category:** logic error.

**Problem.** After a hit at index `i` of length `L`, `mFindCaretOffset` is `i + L`. The
backward search starts `LastIndexOf` at `mFindCaretOffset - 1`, which is the last character of
the current match. `LastIndexOf(value, startIndex)` accepts a match that ends at `startIndex`,
so it returns `i` again.

**How to reproduce.** View a text file with three hits for a search string. Press Find Next
twice, then Find Previous repeatedly.

**Result.** The highlight stays on the second hit.

**Fix direction.** Start the backward search before the current match, for example at
`mFindCaretOffset - SearchString.Length - 1`, wrapping to the end when that is negative. The
`if (startPos < 0)` branch on line 1613 is unreachable because of the `Math.Max(0, …)` above
it.

---

## 9. Activating a viewer resets the main list selection

- [ ] Fixed
- **Where:** `ViewModels/MainViewModel.cs:1109-1133` (`OpenNewViewerWindow`, the
  `FileNavigated` and `Activated` handlers), with `Models/FileListItem.cs:290`
  (`SetSelectionFocusByEntry`).
- **Category:** selection loss; can lead to acting on the wrong entry.

**Problem.** Both handlers call `SelectFileListItemByEntry` for the file the viewer is
showing. When that entry is not in the current file list, `SetSelectionFocusByEntry` falls
back to selecting row 0. Viewers are modeless and stay open while the main window moves to
another directory or archive.

**How to reproduce.** View a file from directory A. In the main window, go to directory B and
select several files. Click the viewer window.

**Result.** The selection in B is replaced by the first row. A following Delete or Extract
acts on that row instead.

**Fix direction.** Have the viewer handlers do nothing when the entry is not in the list,
rather than using the "select the first row" fallback.

---

## 10. Create Disk Image button ignores `IsValid`

- [ ] Fixed
- **Where:** `ViewModels/CreateDiskImageViewModel.cs:335` (`OkCommand`), with
  `Views/CreateDiskImage.axaml:270`.
- **Category:** missing validation; regression from WPF.

**Problem.** `OkCommand` has no `CanExecute`, and the Create button has no `IsEnabled`
binding. `cp2_wpf/CreateDiskImage.xaml:221` binds the button to `IsValid`. The view model
still computes `IsValid`, but nothing uses it.

**How to reproduce.** File > New Disk Image. Enter `abc` as the volume number. Click Create.

**Result.** `GetVolNum` fails, its return value is ignored, and the disk is formatted with
volume 0. An invalid volume name or custom size fails only after the save picker has
truncated the chosen file, which is then deleted.

**Fix direction.** Pass a `CanExecute` that returns `IsValid` and notify it from the `IsValid`
setter, as `CreateDirectoryViewModel` does.

---

## Lower-severity candidates

These did not make the top ten. Same status: read from the code, not confirmed.

- [ ] **Log viewer adds to a bound collection from worker threads.**
  `LogViewerViewModel.cs:170`. `HandleLogEvent` is documented as possibly running on a
  background thread and appends to `mAllEntries` and the bound `LogEntries` directly. With the
  debug log open, a log call from the open or work-progress thread can throw inside that
  worker. Marshal with `Dispatcher.UIThread.Post`.
- [ ] **Worker exceptions are only logged.** `WorkProgressViewModel.cs:227`. When
  `e.Error != null` the dialog closes with `false` and callers show a "Cancelled" toast. WPF
  shows a message box with the error.
- [ ] **Deferred load can run after the viewer is disposed.** `FileViewerViewModel.cs:905`.
  `ShowFileDeferred` posts `ShowFile` at Background priority and `Dispose` sets no flag. If
  the window closes first, `ShowFile` reopens the fork streams and nothing closes them.
- [ ] **Navigation pool rebuilt on every selection change.** `MainViewModel.cs:3685-3693`.
  With a viewer open, each file list selection change walks the whole list, computes
  `FullPathName` for every entry twice, and copies the path list into a new history entry.
- [ ] **CSV export is saved as text.** `FileViewerViewModel.cs:1139`. `FormatFile` replaces a
  `CellGrid` output with `SimpleText`, so the `CellGrid` cases in `DoExportAsync` and
  `DoCopyAsync` are unreachable and spreadsheets export as `.txt`.
- [ ] **Export failure is silent.** `FileViewerViewModel.cs:1506`. The exception is logged and
  the user sees nothing.
- [ ] **Preview bitmaps are never disposed.** `FileViewerViewModel.cs:1296-1308`. Each zoom
  change creates a new Avalonia `Bitmap` and drops the old one for the finalizer.
- [ ] **History-restore flags stick after a failed load.** `FileViewerViewModel.cs:937`. On
  the exception path of `ShowFile`, `OnConverterSelected` never runs, so
  `mIsRestoringHistory` and `mPendingHistoryRestore` stay set until the next good load.
- [ ] **Cut and paste into the same directory can delete the originals.**
  `MainViewModel.cs:2338`. If the user skips every overwrite prompt, the paste still reports
  success and `DeleteCutEntries` removes the source files. `CutCommand` is marked as currently
  unused.

## Cleanup candidates

- [ ] **`ConfigureCenterInfo` exists twice.** `MainViewModel.cs:4096-4147` duplicates
  `CenterInfoViewModel.ConfigureCenterInfo` and `AddInfoItem`; the view-model copy is unused.
- [ ] **Tree `SelectedItem` machinery is unused.** `DirectoryTreeViewModel` and
  `ArchiveTreeViewModel` keep `SelectedItem`, `CachedSelectedItem`, `ResetCache` and
  per-item `IsSelected` subscriptions. Nothing in the view sets or reads them, and
  `MainViewModel` keeps its own cached selections.
- [ ] **Three copies of the clip-building code.** `CopyToClipboard`, `CutToClipboard` and
  `BuildDragData` (`MainViewModel.cs:1933`, `:1996`, `:2065`) repeat the same settings reads,
  base-directory lookup and `ClipFileSet` construction.
- [ ] **Two copies of the disk-creation switches.** `CreateDiskImageViewModel.CreateImageAsync`
  and `SaveAsDiskViewModel.CreateImageAsync` repeat the file-type-to-picker switch and most of
  the image-creation switch.
- [ ] **Small leftovers in `MainViewModel.cs`.** `IsFileOpen` is assigned twice in a row
  (`:3711-3712`); the `if (!isMacZip)` test in `EditAttributesImpl` (`:2745`) is always true;
  the commented-out command block at `:3568-3622` is dead.
