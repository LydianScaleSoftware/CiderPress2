# Code Review Findings: cp2_avalonia/Platform

Review date: 2026-11-09. Reviewed at commit `9fe7762` (branch `staging`).
Scope: `Platform/LinuxDrag.cs` and `Platform/MacOSDrag.cs` in full, plus their call sites in
`Views/MainWindow.axaml.cs`, `Services/ClipboardService.cs` and `Tools/DropTarget.axaml.cs`.

**Status of these findings:** they come from reading the code only. Nothing was run, and no
verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

## Summary

| # | Area | File | Finding | Confidence |
|---|------|------|---------|------------|
| 1 | Linux | MainWindow.axaml.cs:2019 | Internal drop can move files while extraction still runs | High |
| 2 | Linux | LinuxDrag.cs:1109 | Refused drop on a foreign window becomes an internal move | High |
| 3 | Linux | LinuxDrag.cs:1348 | `ReadAtomList` reads 8-byte atoms as 4-byte values | High |
| 4 | Linux | LinuxDrag.cs:409 | `Dispose` closes the X display while the event thread still uses it | High |
| 5 | Linux | LinuxDrag.cs:1096 | Export drag never completes if the target ignores the drop | High |
| 6 | Linux | LinuxDrag.cs:316 | Clipboard read timeout throws `TimeoutException`, not caught | High |
| 7 | macOS | MacOSDrag.cs:368 | File promise reports success even when nothing was copied | Medium |
| 8 | macOS | MacOSDrag.cs:81 | Objective-C `BOOL` returns marshalled as 4-byte `bool` | Medium |
| 9 | Linux | LinuxDrag.cs:1323 | `ReadStringProperty` truncates at 256 KB and ignores INCR | High |
| 10 | Linux | LinuxDrag.cs:1281 | Drop target search stops at the root's direct child | Medium |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

---

## 1. Internal drop can move files while extraction still runs

- [ ] Fixed
- **Where:** `Views/MainWindow.axaml.cs:2019` (`StartFileListDragAsync`), with
  `HandleLinuxInternalDrop` at line 1692.
- **Category:** race condition, possible data damage.

**Problem.** On Linux the drag is armed with `BeginNativeDrag` before extraction, and
`vm.BuildDragData()` then runs on a thread-pool thread via `Task.Run` so the UI stays
responsive. If the user releases the button over the CP2 window before extraction finishes,
LinuxDrag's poll thread posts `onInternalDrop`, and `HandleLinuxInternalDrop` calls
`vm.MoveFiles` on the UI thread. The move rewrites directory structures on the same
`IFileSystem` that the worker thread is still reading. The DiskArc library is not thread-safe.

**How to reproduce.** Open a ProDOS or HFS volume. Select a large file, or many files. Drag
onto a folder in the same window and release quickly, while extraction is still in progress.

**Result.** Exceptions, wrong extracted data, or a damaged disk image.

**Fix direction.** Do not start the move until the `BuildDragData` task has completed. For
example, have the internal-drop handler await the extraction task (or cancel it) before
calling `MoveFiles`. A cheaper alternative is to skip extraction entirely when the drop turns
out to be internal, which also removes wasted work on every internal move.

---

## 2. Refused drop on a foreign window becomes an internal move

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:1109` (`ExportOnButtonRelease`), with
  `IsScreenPointInWindow` at `Views/MainWindow.axaml.cs:1765`.
- **Category:** logic error, unintended modification of the archive.

**Problem.** When no external target accepted the drop, `ExportOnButtonRelease` always calls
the internal-drop callback. But `FindXdndAwareWindow` returns zero in three cases, not one:
the pointer is over our own window, over a foreign window that is not XDND-aware, or over a
foreign window that refused the drop (`_exportTargetAccepts` false). The view then decides
with `IsScreenPointInWindow`, which is purely geometric and ignores which window is on top.

**How to reproduce.** Place a terminal (or any window that refuses file drops) so it partly
overlaps the CP2 window. Drag a file from the file list and release over the overlapping
window, at a point that lies inside CP2's bounds.

**Result.** The files are moved into whichever directory row sits underneath the other
window, although the user dropped on a different application.

**Fix direction.** Record whether the top-level window under the pointer at release is one of
our own windows, and only call the internal-drop callback in that case. Invariant 6 in the
file header should be updated to match.

---

## 3. `ReadAtomList` reads 8-byte atoms as 4-byte values

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:1348`.
- **Category:** size / off-by-factor error.

**Problem.** The element size is derived from the property format: `sz = fmt / 8`, which is 4
for format 32. Xlib returns format-32 data as an array of C `long`, which is 8 bytes on
64-bit Linux. The `sz == 8` branch can never run because the format is never 64.

**Example.** With 8 atoms, the loop reads 8 `Int32` values at 4-byte steps:
`atom0, 0, atom1, 0, atom2, 0, atom3, 0`. Zeros are skipped, and atoms 4 to 7 are never seen.
Only the first half of the list survives.

**Result.** This path is used for `XdndTypeList`, which a drag source sends when it offers
more than three types. If `text/uri-list` is in the second half of the list,
`ImportOnXdndPosition` answers "not accepted" and the file drop is refused. It works today
only when the source lists `text/uri-list` early.

**Fix direction.** For format 32, always read `nItems` values of `IntPtr.Size` bytes with
`Marshal.ReadIntPtr(propReturn, i * IntPtr.Size)`. `ReadSingleWindowProperty` already does
this correctly.

---

## 4. `Dispose` closes the X display while the event thread still uses it

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:409` (`Dispose`), event loop at line 805.
- **Category:** race condition, use-after-free in native code.

**Problem.** `Dispose` sets `_disposed`, then immediately calls `XDestroyWindow` and
`XCloseDisplay`. It does not wait for `_eventThread` to exit. The thread only checks
`_disposed` at the top of its loop, so it can still be inside an Xlib call on the freed
`Display`.

**When it happens.** Closing the main window or the DropTarget debug window while the thread
is in `XPending` / `XNextEvent` / `XQueryPointer`, in `PollPointerForExport` during a drag, or
in the 10-second spin-wait in `ExportOnSelectionRequest`.

**Result.** A native crash in libX11 on window close. The window is narrow when idle and wide
during an export drag.

**Fix direction.** After setting `_disposed`, join the event thread (with a timeout) before
destroying the window and closing the display. The spin-wait in `ExportOnSelectionRequest`
should also check `_disposed` so the join is not held up for 10 seconds.

---

## 5. Export drag never completes if the target ignores the drop

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:1096` (`ExportOnButtonRelease`) and line 1122
  (`ExportOnXdndStatus`).
- **Category:** logic error, stuck state.

**Problem.** Two defects combine here.

1. After `XdndDrop` is sent, `_exportTcs` completes only on `XdndFinished` or on a
   `text/uri-list` `SelectionRequest`. There is no timeout.
2. `ExportOnXdndStatus` accepts a status message from any window. It does not check that
   `data0` (the sender) is `_exportCurrentTarget`.

**How to reproduce.** Move the pointer from an accepting window A onto a non-accepting window
B and release straight away. A's late `XdndStatus(accept)` arrives after the switch and sets
`_exportTargetAccepts` for B. `XdndDrop` goes to B, which ignores it. The same end state
occurs if a target crashes after accepting.

**Result.** The task never completes. `StartFileListDragAsync` never reaches its `finally`
block, so `mIsDraggingFileList` stays true and the DragMove cursor stays set. No further
file-list drags work until the app is restarted.

**Fix direction.** Ignore `XdndStatus` unless `ev.data0 == _exportCurrentTarget`. Add a
timeout after `XdndDrop` (a few seconds) that completes the task with
`DragDropEffects.None` and clears `_exportDropSent`.

---

## 6. Clipboard read timeout throws `TimeoutException`, not caught

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:316` (`ReadClipboardTextAsync`).
- **Category:** wrong exception type.

**Problem.** `Task.WaitAsync(TimeSpan)` throws `TimeoutException` on timeout. The code
catches only `OperationCanceledException`. The method's documentation says it returns null on
timeout, which never happens.

**Result.** If the clipboard owner does not answer within 2 seconds (a busy or hung
application), the exception propagates into `ClipboardService.GetRawClipTextAsync`. Paste
fails with an exception instead of falling back to Avalonia's `clipboard.GetTextAsync()`.

**Fix direction.** Catch `TimeoutException` as well and return null.

**Related.** After a timeout, a late `SelectionNotify` can complete the *next* read's
`TaskCompletionSource` with stale data, because `ClipboardOnSelectionNotify` takes whatever
is in `_clipReadTcs`. Low likelihood, but worth handling in the same change.

---

## 7. File promise reports success even when nothing was copied

- [ ] Fixed
- **Where:** `Platform/MacOSDrag.cs:368` (`WritePromiseImpl`), with `DragEndedImpl` at
  line 404.
- **Category:** error swallowed.
- **Needs checking:** the ordering scenario below depends on the order in which AppKit
  delivers callbacks. The failed-copy case does not.

**Problem.** `WritePromiseImpl` calls the completion block with a nil error in every case:

- when `CopyItem` throws (disk full, permission denied), the error is only logged;
- when the provider is missing from `sProviderPaths`, nothing is copied.

The second case can arise because `DragEndedImpl` clears `sProviderPaths` as soon as the drag
session ends. If `draggingSession:endedAtPoint:operation:` is delivered before a remaining
`writePromiseToURL:` call, the path lookup fails.

**Result.** Finder is told the write succeeded. The user sees a missing or partial file with
no error.

**Fix direction.** Pass an `NSError` to the completion block on failure. Remove each
provider's entry from `sProviderPaths` when its own promise has been written (or when the
provider is deallocated), not when the drag session ends.

---

## 8. Objective-C `BOOL` returns marshalled as 4-byte `bool`

- [ ] Fixed
- **Where:** `Platform/MacOSDrag.cs:81` (`SendBool1`), also `SendBool2` (line 78) and
  `class_addMethod` (line 53).
- **Category:** interop.
- **Affects:** Intel Macs. Apple Silicon is not affected, because its calling convention
  requires the callee to extend small return values.

**Problem.** These P/Invoke declarations return a plain C# `bool`, which the marshaller reads
as a 4-byte value. Objective-C `BOOL` is one byte. On x86_64 the upper bytes of the return
register are undefined.

**Result.** `isEqualToString:` can return NO in `AL` with non-zero garbage in the rest of
`EAX`, and `SendBool1` reports true. `PropertyListForTypeImpl` then returns the temp-file URL
string (or the CP2 JSON) for the file-promise pasteboard type, and `WritingOptionsImpl`
returns 0 for it. Finder drops fail or deliver the wrong data.

**Fix direction.** Add `[return: MarshalAs(UnmanagedType.I1)]` to all three declarations.

---

## 9. `ReadStringProperty` truncates at 256 KB and ignores INCR

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:1323`.
- **Category:** size limit, silent data loss.

**Problem.** The call requests at most 65536 32-bit units (262,144 bytes) in a single
`XGetWindowProperty`, discards `bytesAfter`, and does not recognise an INCR transfer. It
still returns a non-null string. For a format-32 property it also copies `nItems` bytes
instead of `nItems * 8`.

**How to reproduce.** Copy several hundred entries in one CP2 instance so the clipboard JSON
exceeds 256 KB. Paste in another instance on Linux.

**Result.** The direct read returns truncated JSON, or a one-character string made from the
INCR length word. Because the result is non-null, `GetRawClipTextAsync` does not fall back to
Avalonia. `ClipInfo.FromClipString` fails and the paste silently does nothing. The same limit
applies to dropped URI lists and CP2 drag payloads.

**Fix direction.** Loop on `bytesAfter` until the whole property is read. If the returned
type is `INCR`, either implement the incremental protocol or return null so the caller falls
back to Avalonia's clipboard.

---

## 10. Drop target search stops at the root's direct child

- [ ] Fixed
- **Where:** `Platform/LinuxDrag.cs:1281` (`FindXdndAwareWindow`).
- **Category:** protocol logic.
- **Needs checking:** depends on how the window manager nests windows. Test on a plain Xorg
  session before changing anything.

**Problem.** The function tests `XdndAware` only on the root window's direct child under the
pointer. The XDND protocol requires descending the window tree until an aware window is
found.

**Result.** With a reparenting window manager on a plain Xorg session, the root's child is
the window manager's frame, not the application's client window. The frame has no `XdndAware`
property, so the function returns zero for every external window. No `XdndEnter` is sent and
dragging files out to a file manager does nothing. Combined with finding 2, the release is
treated as an internal drop. Under KDE Wayland this is hidden, because KWin's bridge window is
a direct child of the root.

**Fix direction.** Descend from the root child with `XTranslateCoordinates` (or
`XQueryPointer` on each child) until a window with `XdndAware` is found or there are no more
children. The own-window check must then compare against the top-level window, not only
`_avaloniaWin`.

---

## Lower-severity candidates

These did not make the 10-finding limit. They were examined less closely than the items
above.

- [ ] **Empty file list served as success after 10 seconds.** `LinuxDrag.cs:1153`. If
  extraction takes longer than the 10-second spin-wait in `ExportOnSelectionRequest`, an empty
  `text/uri-list` is delivered and the drop is reported as a copy. The target receives
  nothing and no error is shown. The wait also blocks pointer polling and all other event
  handling on that thread.
- [ ] **Late callbacks from a previous drag complete the next one.** `LinuxDrag.cs:1137` and
  `:1197`. `ExportOnXdndFinished` and the `Task.Delay(500)` continuation read the
  `_exportTcs` field when they run, not the task of the drag they belong to. A second drag
  started before they fire is completed early, and `_exportCurrentTarget` /
  `_exportDropSent` are reset under it. Capture the `TaskCompletionSource` in a local, or tag
  each drag with a generation number.
- [ ] **Final pointer position ignored on release.** `LinuxDrag.cs:848`. The release branch
  returns without calling `ExportOnMotion` for the release position, and a target's
  `XdndStatus` may not have arrived yet after a fast flick. The drop can go to the previous
  target or be treated as internal.
- [ ] **Event thread wakes 125 times a second when idle.** `LinuxDrag.cs:829`. The loop
  sleeps 8 ms and polls `XPending` for the lifetime of every `LinuxDrag` instance. Blocking on
  the connection's file descriptor would remove the idle wakeups.
- [ ] **Unhook re-adds a handler for a destroyed window.** `LinuxDrag.cs:766`. `DropTarget`
  disposes on `Closed`, after Avalonia has removed the window from its `Windows` dictionary.
  `TryUnhookAvaloniaDnD` then writes the original handler back, which keeps the closed window
  object alive. Dispose on `Closing`, or only restore the handler if the key is still
  present.
- [ ] **Promise copy runs on the main thread.** `MacOSDrag.cs:346`. `WritePromiseImpl` copies
  synchronously, so the UI freezes while large files are written. Implementing
  `operationQueueForFilePromiseProvider:` would move the copy to a background queue.
- [ ] **"Allow retry" in `EnsureClasses` cannot work.** `MacOSDrag.cs:288` and `:300`.
  Resetting `sDelegateClass` to zero leads to a second `objc_allocateClassPair` with a name
  that is already registered, which returns nil. Every later call then fails.
- [ ] **Stale entries in `sProviderPaths` when a drag fails to start.** `MacOSDrag.cs:621`.
  Providers are added to the map before `beginDraggingSessionWithItems:`; if the session is
  not created, the entries are never removed.
- [ ] **Unused state.** `_exportPaths` and `_exportTempDir` in `LinuxDrag.cs` are written but
  never read, and the `tempDir` parameter of `SetNativeDragPaths` is documented as "deleted on
  XdndFinished", which the code deliberately does not do. `firstIcon` in `MacOSDrag.cs` is
  also unused.
