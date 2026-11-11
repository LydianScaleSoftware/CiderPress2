# Code Review Findings: cp2_avalonia/Actions

Review date: 2026-11-10. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: every file in `Actions/` in full, plus `ViewModels/WorkProgressViewModel.cs`,
`Common/WorkProgress.axaml.cs`, the call sites in `ViewModels/MainViewModel.cs` and
`Services/WorkspaceService.cs`, and the matching files in `cp2_wpf/Actions` for comparison.

**Status of these findings:** they come from reading the code only. Nothing was run, and no
verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

## Summary

| # | Area | File | Finding | Confidence |
|---|------|------|---------|------------|
| 1 | Extract | ExtractProgress.cs:71 | Failures are reported as "Cancelled" with no error text | High |
| 2 | Edit attributes | EditAttributesProgress.cs:80 | A failed edit leaves the entry half-modified in memory | Medium |
| 3 | Bad-block scan | ScanBlocksProgress.cs:64 | An aborted scan is reported as a clean disk | Medium |
| 4 | Edit attributes | EditAttributesProgress.cs:87 | Error dialog duplicates `PlatformUtil.ShowMessageAsync` and is not awaited | High |
| 5 | Add / paste | ClipPasteProgress.cs:73 | `DoWork` body is a copy of `AddProgress.DoWork` | High |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

---

## 1. Extract failures are reported as "Cancelled"

- [ ] Fixed
- **Where:** `Actions/ExtractProgress.cs:55-90` (`DoWork`). The root cause is shared with
  `ViewModels/WorkProgressViewModel.cs` (`RunWorkerCompleted`), and the toast is in
  `ViewModels/MainViewModel.cs:1518-1523`.
- **Category:** error swallowed; regression from WPF.

**Problem.** `DoWork` has a `finally` but no `catch`. Any exception goes to the
`BackgroundWorker` as `e.Error`. The WPF `WorkProgress` showed a message box with the
exception. The Avalonia `WorkProgressViewModel` only writes it to the log and returns false.
`MainViewModel` then treats false as a cancel.

The most likely trigger is line 71, `Environment.CurrentDirectory = outputDir`, which throws
if the folder is missing or not accessible. That line also changes the current directory for
the whole process while the extraction runs, and modeless windows stay usable during that
time.

**How to reproduce.** Start an extract, pick an output folder, and make that folder
unavailable before the work starts (remove it, unmount the volume, or pick one without write
access).

**Result.** The progress dialog closes, and a "Cancelled" toast appears. No error is shown.

**Fix direction.** Fix it once in `WorkProgressViewModel.RunWorkerCompleted`: when `e.Error`
is set, raise `MessageBoxRequested` (or an equivalent) with the message before closing, as WPF
did. That covers every action. In `MainViewModel`, say "Failed" instead of "Cancelled" when
the worker was not cancelled. Passing a base path into `ExtractFileWorker` instead of changing
the current directory would remove the process-wide side effect; `basePath` is already a
constructor argument.

Other places that depend on the same fix:

- `ClipPasteProgress.cs:65` throws `NullReferenceException` on the worker thread when
  `ClipEntries` is null.
- `DeleteProgress.cs:74` and `MoveProgress.cs:72`: `DeleteFromDisk` catches only
  `IOException`. Any other exception skips `SaveUpdates`, so deletions already made are not
  pushed up to a parent archive.
- `TestProgress.cs:82`: the `(IFileSystem)` cast.

---

## 2. A failed attribute edit leaves the entry half-modified

- [ ] Fixed
- **Where:** `Actions/EditAttributesProgress.cs:80-82` (filesystem branch of `DoUpdate`). The
  archive branches at lines 59-78 are protected by `CancelTransaction`.
- **Category:** inconsistent state after error. Same code as WPF.

**Problem.** `newAttribs.CopyAttrsTo(fileEntry, false)` changes the live `IFileEntry` one
attribute at a time, and `fileEntry.SaveChanges()` writes them. If either call throws, the
`catch` shows the error and returns false. Nothing restores the attributes that were already
copied. The caller then runs `RefreshDirAndFileList`, which reads the same entry.

**How to reproduce.** On a disk image, edit a file's attributes: change the file type and
also rename it to a name that already exists in that directory.

**Result.** Expected: the error appears, and the list then shows the new type or dates even
though they were not written. A later save on the same filesystem may write them. The order
in which `CopyAttrsTo` applies attributes was not checked, so which fields are affected is not
confirmed.

**Fix direction.** Capture `new FileAttribs(fileEntry)` before the copy. In the `catch`, copy
the saved attributes back and call `SaveChanges` inside its own `try`.

---

## 3. An aborted bad-block scan is reported as a clean disk

- [ ] Fixed
- **Where:** `Actions/ScanBlocksProgress.cs:61-66` (`DoWork`) and the early returns in
  `ScanDisk` at lines 73-76, 84-88 and 125-129. Same code as WPF.
- **Category:** wrong result on error path.

**Problem.** `ScanDisk` returns false when it cannot scan. `DoWork` ignores the return value
and returns the empty failure list. `RunWorkerCompleted` sees a non-null list and returns
true, and `MainViewModel.ScanForBadBlocks` reports no bad blocks. The guards are
`Debug.Assert(false, ...)`, which do nothing in a release build. The cancel case is fine,
because the view model handles it before the result is used.

The scan also never calls `ReportProgress`, but the dialog is created as determinate
(`new WorkProgressViewModel(prog, false)`), so the bar stays at zero with no message.

**Result.** If any of the "shouldn't happen" paths is hit, the user is told the disk is clean
when nothing was tested. The command is limited to nibble images, where these paths are
unlikely, so this is low severity.

**Fix direction.** Return `null` from `DoWork` when `ScanDisk` returns false and the worker
was not cancelled, and have the caller show an error for a null result. Report progress once
per track, or create the dialog as indeterminate.

---

## 4. Error dialog duplicates `PlatformUtil.ShowMessageAsync` and is not awaited

- [ ] Fixed
- **Where:** `Actions/EditAttributesProgress.cs:87-113`.
- **Category:** duplicated code.

**Problem.** The `catch` block builds a window with a `TextBlock` and an OK button. It is the
same window as `Common/PlatformUtil.cs:103` (`ShowMessageAsync`). The dialog task is discarded
(`_ = errWin.ShowDialog(parent)`), so `DoUpdate` returns while the dialog is still open.

**Result.** About 25 lines to keep in step by hand. `MainViewModel` refreshes the file list
underneath the open error window, and an exception from `ShowDialog` would go unobserved.

**Fix direction.** Make `DoUpdate` return `Task<bool>` and await
`PlatformUtil.ShowMessageAsync`, or return the error message and let `MainViewModel` show it
through `mDialogService`. Both call sites are already in an async method. The second option
also removes the `Window` parameter and the Avalonia dependency from this class.

---

## 5. Paste and add `DoWork` bodies are copy-pasted

- [ ] Fixed
- **Where:** `Actions/ClipPasteProgress.cs:73-140` and `Actions/AddProgress.cs:63-132`.
- **Category:** duplicated code. Same structure as WPF.

**Problem.** The archive branch (transaction, cancel check, `SaveUpdates`, three `catch`
clauses) and the filesystem branch (deferred failure message, `SaveUpdates`, report) are the
same in both files. Only the worker type differs.

**Result.** About 70 lines maintained twice. The comment that explains why the failure
message is deferred exists in `AddProgress` only.

**Fix direction.** Move the body into a shared static helper in `ProgressUtil` that takes two
delegates, one for `AddFilesToArchive` and one for `AddFilesToDisk`.

---

## Smaller observations

- `ScanBlocksProgress.cs:34` and `TestProgress.cs:48`: `mAppHook` is assigned and never read.
- `ProgressUtil.cs:132`: `ShowCancelled` has no callers.
- `OpenProgress.cs:70`: the comment says `RunWorkerCompleted` runs on the GUI thread.
  `WorkspaceService.OpenAsync` calls it inside `Task.Run`. It only stores a reference, so
  this is a comment fix.
- `ProgressUtil.cs:98-100`: `MessageBoxQuery` carries a button set and an icon, but the
  handler in `Common/WorkProgress.axaml.cs` ignores both and always shows a plain OK box.
- `ProgressUtil.cs:67`: `FlushStreams` runs for every existing file, including when the
  "overwrite all" or "skip all" choice is already made and no dialog will be shown.
- `TestProgress.cs:150` and `:225`: a new `PersistentChoices` is allocated for every progress
  callback. A direct `bkWorker.ReportProgress` call would be simpler.

## Suggested order

1. Item 1, in `WorkProgressViewModel`. It restores error reporting for every action.
2. Item 4, then item 2. They touch the same method, and item 4 makes the `catch` block small
   enough to add the rollback cleanly.
3. Item 3.
4. Item 5 when the add or paste path is next changed.
