# Code Review Findings: cp2_avalonia/Services

Review date: 2026-11-10. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: all 22 files in `Services/`, plus their call sites in `ViewModels/MainViewModel.cs`,
`Views/MainWindow.axaml.cs`, `Views/LoadingDialog.axaml.cs`, `App.axaml.cs` and
`Common/PlatformUtil.cs`.

**Status of these findings:** they come from reading the code only. Nothing was run, and no
verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

## Summary

| # | Area | File | Finding | Confidence |
|---|------|------|---------|------------|
| 1 | Clipboard | ClipboardService.cs:232 | `ClearIfPendingAsync` wipes a clipboard it does not own | High |
| 2 | Shutdown | WorkspaceService.cs:101 | Exit skips work-file close and clipboard temp cleanup | High |
| 3 | Clipboard | ClipboardService.cs:125 | URI list flattens directory trees | High |
| 4 | Clipboard | ClipboardService.cs:80 | Temp subdirectory `x` collides with entries named `X` | High |
| 5 | Clipboard | ClipboardService.cs:133 | `text/uri-list` string is not read by Explorer or Finder | Medium |
| 6 | Clipboard | ClipboardService.cs:89 | Copy extracts every file twice on the UI thread | High |
| 7 | Settings | SettingsService.cs:102 | Settings saved beside the executable; failure is silent | Medium |
| 8 | Workspace | WorkspaceService.cs:119 | Filesystems flushed parent-first, not child-first | Low |
| 9 | Clipboard | Cp2ClipUtil.cs:103 | File paths from clipboard text are trusted on paste | Medium |
| 10 | Clipboard | ClipboardService.cs:109 | Extraction loops duplicated with the drag path | High |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

Items 1 to 4 and 6 can be confirmed by reading the code alone. Items 5 and 8 depend on
platform or library behaviour that was reasoned about, not tested.

---

## 1. `ClearIfPendingAsync` wipes a clipboard it does not own

- [ ] Fixed
- **Where:** `Services/ClipboardService.cs:227-233`. Callers: `MainViewModel.CloseWorkFile`
  (line 656) and `OptionsPanelViewModel.cs:195`.
- **Category:** logic error, regression from WPF.

**Problem.** Despite its name, the method does not check `HasPendingContent`, and it does not
check that the clipboard still holds data from this process. It always calls
`clipboard.ClearAsync()`. The WPF version (`cp2_wpf/MainController.cs:819-823`) reads the
clipboard and clears it only when `clipInfo.ProcessId` matches the current process.

**How to reproduce.** Copy some text in a browser or editor. In CiderPress II, close the work
file, or open a different one (opening calls `CloseWorkFile` first). Paste in the other
application.

**Result.** The unrelated clipboard contents are gone, although CP2 never copied anything.

**Fix direction.** Return early when `mPendingClipInfo` is null. When it is not null, read the
clipboard text and clear only if `ClipInfo.FromClipString` gives a `ProcessId` equal to
`Environment.ProcessId`. Reset `mXferPaths` here too; it is currently left pointing at deleted
files.

---

## 2. Exit skips work-file close and clipboard temp cleanup

- [ ] Fixed
- **Where:** `Services/WorkspaceService.cs:101` (`Close`) and
  `Services/ClipboardService.cs:74` (`mClipTempDir`). The missing call is in
  `MainViewModel.Shutdown` (line 554), reached from the `Closing` handler at
  `Views/MainWindow.axaml.cs:587`.
- **Category:** missing cleanup, resource leak, possible unsaved changes.

**Problem.** The window's `Closing` handler calls only `vm.Shutdown()`, which disposes the two
trees and saves settings. It never calls `CloseWorkFile`. So on quit with a file open:

- `WorkspaceService.Close` does not run, so `FlushAllFileSystems` and `WorkTree.Dispose` are
  skipped. WPF calls `CloseWorkFile` from its window-closing path.
- `ClipboardService.CleanupClipTemp` does not run, and the clipboard temp directory is not
  registered with `TempDirectoryTracker`, so the `desktop.Exit` cleanup in `App.axaml.cs:90`
  misses it as well.

**How to reproduce.** Open an archive, copy a few files (Ctrl+C), quit the application. Look
in the OS temp directory for `cp2clip_<pid>_<ticks>`.

**Result.** The temp directory, holding full extracted copies of every copied file, stays
forever. Each such session leaves another one. Whether skipping the flush can lose data
depends on whether any filesystem has pending changes at quit; that was not established.

**Fix direction.** Call `CloseWorkFile()` at the start of `MainViewModel.Shutdown`. Consider
also passing the clipboard temp directory to `TempDirectoryTracker.Track` as a second line of
defence. Note that fixing this makes item 1 fire on every quit, so fix item 1 first or
together.

---

## 3. URI list flattens directory trees

- [ ] Fixed
- **Where:** `Services/ClipboardService.cs:109-133` (`SetClipAsync`).
- **Category:** logic error.

**Problem.** The list offered to file managers gets one URI for every extracted file at every
depth (line 125). Directory entries are created on disk but `continue` before a URI is added
(lines 114-118). The drag path in `MainViewModel` (lines 2136-2172) does this correctly: it
collects only the top-level items under the temp directory.

**How to reproduce.** Copy a directory `DIR` that contains `SUB/FILE1` and an empty directory
`EMPTY`. Paste in a Linux file manager.

**Result.** `FILE1` lands directly in the destination with no `DIR/SUB` structure. `EMPTY` is
not created. Files with the same name in different directories collide. In host-preserve mode
the resource forks show up as separate `..namedfork/rsrc` URIs.

**Fix direction.** Build the list from top-level paths only, as the drag path does. Sharing
one helper with the drag path (item 10) fixes this as a side effect.

---

## 4. Temp subdirectory `x` collides with entries named `X`

- [ ] Fixed
- **Where:** `Services/ClipboardService.cs:80` (xfer directory) and line 110 (foreign entry
  path). The same layout is in `MainViewModel.cs:2112`.
- **Category:** name collision, silent data omission.

**Problem.** Transfer files go to `<tempDir>/x/eNNNN`. Foreign entries are extracted into
`<tempDir>` itself under their own names. An entry whose top-level name is `x` shares that
path. On macOS and Windows the default filesystems ignore case, so `X` collides too.
Single-letter file names are common on Apple II disks.

**How to reproduce.** On macOS, copy a file named `X` from a DOS 3.3 disk, then paste in a
file manager.

**Result.** `new FileStream(".../X", FileMode.Create)` throws because `x` is a directory. The
exception is caught and logged, and `X` is silently missing from the paste. If the entry is a
directory named `X` holding a file `E0000`, the foreign extraction overwrites `x/e0000`, and a
paste into another CP2 instance receives the wrong data for entry 0.

**Fix direction.** Keep the two sets apart: put foreign entries in their own subdirectory
(for example `<tempDir>/f/`) and transfer files in another, or use two sibling temp
directories.

---

## 5. `text/uri-list` string is not read by Explorer or Finder

- [ ] Fixed
- **Where:** `Services/ClipboardService.cs:133`.
- **Category:** platform behaviour. Not tested.

**Problem.** The foreign files are advertised with `dataObj.Set("text/uri-list", string)`.
Avalonia treats that as a custom format with that name. Windows Explorer expects `CF_HDROP`
and Finder expects file-URL pasteboard types; both come from `DataFormats.Files` with storage
items, not from a string under a custom name.

**How to reproduce.** On Windows or macOS, copy files in CP2 and paste in Explorer or Finder.

**Expected result (unconfirmed).** Nothing is pasted, although every file was extracted to
temp for this purpose. Linux file managers that read `text/uri-list` work.

**Fix direction.** Confirm on each platform first. If confirmed, set `DataFormats.Files` with
`IStorageItem` objects obtained from the storage provider for the top-level temp paths, and
keep `text/uri-list` for Linux if it is still needed.

---

## 6. Copy extracts every file twice on the UI thread

- [ ] Fixed
- **Where:** `Services/ClipboardService.cs:83-94` and `109-130`. Callers:
  `MainViewModel.CopyToClipboard` (line 1986) and `CutToClipboard` (line 2047).
- **Category:** performance, UI freeze.

**Problem.** `SetClipAsync` has no `await` before its two extraction loops, so both run
synchronously on the UI thread. Each file is written once as `x/eNNNN` and again as a foreign
entry. The callers have already turned the busy cursor off (`SetCursorBusy(false)` in the
`finally` before the call). The work is done at copy time even if the user never pastes.

**How to reproduce.** Open a 32 MB hard-drive image, select all files, press Ctrl+C.

**Result.** The window stops responding, with a normal cursor, until all files have been
decompressed and written twice. Temp space use is double the data size.

**Fix direction.** Move the extraction into `Task.Run` and keep the busy cursor on until it
finishes. Check thread safety against the DiskArc library first: nothing else may touch the
same archive while the worker reads it (see Platform finding 1 for the same hazard). Longer
term, extract lazily at paste time, or share one extracted copy between the two uses where
the formats allow.

---

## 7. Settings saved beside the executable; failure is silent

- [ ] Fixed
- **Where:** `Services/SettingsService.cs:83` and `102`, using
  `Common/PlatformUtil.cs:75` (`GetRuntimeDataDir`).
- **Category:** design, packaging.

**Problem.** `GetRuntimeDataDir` returns `AppContext.BaseDirectory` in production (it walks up
at most six levels looking for an existing settings file or `CiderPress2.sln`). `Save`
catches any exception and only writes it to the debug log.

**Result in a packaged release.**

- macOS: the directory is `CiderPress II.app/Contents/MacOS`. Writing there changes a signed
  bundle, and fails when the app is translocated or on a read-only volume.
- Linux AppImage: the directory is a read-only mount.
- Windows: `Program Files` is not writable without elevation.

In each case recent files, window placement and options are lost on every run with no
message to the user.

**Fix direction.** Use a per-user directory, for example
`Environment.GetFolderPath(SpecialFolder.ApplicationData)/CiderPress2/`, which resolves to
`%APPDATA%` on Windows, `~/Library/Application Support` on macOS and `~/.config` on Linux.
Create the directory on first save. The Avalonia version is a new application, so it does
not need to match the WPF location or migrate a WPF settings file; use whatever storage
suits each platform best. The repository-root lookup can stay for development builds only.
Also consider writing to a temp file and renaming, so a crash during save cannot leave a
truncated settings file.

---

## 8. Filesystems flushed parent-first, not child-first

- [ ] Fixed
- **Where:** `Services/WorkspaceService.cs:118-129` (`FlushAllFileSystems`).
- **Category:** ordering. Not tested; low confidence.

**Problem.** The method calls `fs.Flush()` on a node and then recurses into its children. A
nested filesystem (a disk image stored as a file inside another volume) therefore flushes
after its parent. Its flush writes through an open file in the parent, which may leave the
parent with a pending change again. That pending change is what the method's own comment
says it exists to prevent (`Debug.Assert` in `ProDOS.InvalidateFileEntries`).

**How to check.** In a debug build, open a ProDOS volume that contains a disk image, change a
file inside the inner image, and close the work file. See whether the assert fires.

**Fix direction.** If confirmed, recurse into the children first and flush the node last.

---

## 9. File paths from clipboard text are trusted on paste

- [ ] Fixed
- **Where:** `Services/Cp2ClipUtil.cs:86-111` (`ExtractFileData`). The paths are opened in
  `MainViewModel.cs:2297-2317`.
- **Category:** input validation.

**Problem.** `ExtractFileData` returns the `Cp2FileData` strings exactly as found in the
clipboard text. The paste code opens each with `File.OpenRead`. Nothing checks that a path
lies in a CP2 temp directory.

**Scenario.** Another application, or a web page with clipboard access, places text that
starts with `CiderPressII:clip:v1:`, carries matching version fields, and lists a path such
as `~/.ssh/id_rsa`. When the user pastes into an open archive, that file's contents are
imported under whatever entry name the JSON gives. The user has to paste, and then share the
archive, for this to matter.

**Fix direction.** Accept a path only if its full form lies under `Path.GetTempPath()` in a
directory whose name starts with `cp2clip_` or `cp2drag_`. Drop any other element.

---

## 10. Extraction loops duplicated with the drag path

- [ ] Fixed
- **Where:** `Services/ClipboardService.cs:76-131` and `ViewModels/MainViewModel.cs:2103-2172`.
- **Category:** duplication.

**Problem.** Both places create a temp directory, write transfer files to `x/eNNNN`, extract
foreign entries, and apply timestamps. The code is nearly identical, and the copies have
already diverged: the drag path collects top-level paths, the clipboard path does not
(item 3). Item 4 has to be fixed in both.

**Fix direction.** Move the work into one helper that takes a `ClipFileSet` and a temp
directory and returns the transfer paths and the top-level foreign paths. Call it from both.
Doing this first makes items 3, 4 and 6 single-site fixes.

---

## Smaller observations

These did not make the list above. None was verified.

- `FilePickerService.cs:52, 67, 83, 94` use `Path.LocalPath`. The rest of the code uses
  `TryGetLocalPath()`, which avoids a round trip through URI escaping and returns null for
  non-file items.
- `SettingsService.cs:36-78` logs every settings read at debug level. `MainViewModel` alone has
  58 reads, so the debug log fills with `Settings: read ...` lines.
- `IClipboardService.HasPendingContent` and `GetCachedXferPaths` have no callers.
- `WorkspaceService.OpenAsync` returns `null!` from a non-nullable `Task<WorkTree>` when the
  open fails without an exception, and rethrows with `throw ex`, losing the original stack.
  The interface comment says it closes the current file first; it asserts none is open.
- `MainViewModel` paste reads the clipboard twice for a cross-instance paste (`GetClipAsync`,
  then `GetRawClipTextAsync`). Returning the raw text with the `ClipInfo` would avoid that.
- `Cp2ClipUtil` repeats the `CiderPressII:clip:v1:` prefix twice; `ClipInfo.CLIP_PREFIX` is
  private.

## Suggested order

1. Item 10 (shared helper), then items 3 and 4 in the helper.
2. Item 1, then item 2 (item 2 exposes item 1 on every quit).
3. Item 9.
4. Item 7 (no WPF compatibility constraint), and item 5 after confirming on each platform.
5. Item 6, once thread safety for background extraction is settled.
6. Item 8 only if the assert can be reproduced.
