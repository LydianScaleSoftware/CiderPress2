# Code Review Findings: cp2_avalonia/Common

Review date: 2026-11-10. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: all 9 files in `Common/`, plus their call sites in `Program.cs`,
`Views/MainWindow.axaml.cs`, `ViewModels/MainViewModel.cs`,
`ViewModels/FileViewerViewModel.cs`, `ViewModels/WorkProgressViewModel.cs`,
`Actions/ProgressUtil.cs` and `Services/SettingsService.cs`. The WPF originals in
`cp2_wpf/WinUtil.cs` and `cp2_wpf/WPFCommon/AnimatedGifEncoder.cs` were read for comparison.

**Status of these findings:** they come from reading the code only. Nothing was run, and no
verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

## Summary

| # | Area | File | Finding | Confidence |
|---|------|------|---------|------------|
| 1 | Settings | PlatformUtil.cs:75 | Settings directory is the executable directory | Medium |
| 2 | Window | WindowPlacement.cs:92 | Minimized and FullScreen state and bounds are saved | High |
| 3 | Window | WindowPlacement.cs:107 | Restore never checks the saved position is on a screen | High |
| 4 | Window | WindowPlacement.cs:48 | Normal-bounds cache can capture maximized bounds | Medium |
| 5 | Progress | WorkProgress.axaml.cs:43 | `async void` query handlers can block the worker forever | Medium |
| 6 | Progress | WorkProgress.axaml.cs:51 | Worker message box ignores `Button`/`Image`, always OK | High |
| 7 | Bitmap | BitmapUtil.cs:70 | Palette lookup is unchecked against the palette length | Low |
| 8 | Bitmap | BitmapUtil.cs:66 | Indexed conversion allocates a full BGRA temporary copy | High |
| 9 | Cleanup | MessageBoxEnums.cs:22 | `MBResult`/`MBButton`/`MBIcon` are unused duplicates | High |
| 10 | Cleanup | PlatformUtil.cs:40 | `AskFileToOpen`, `FILE_FILTER_*`, `IsAdministrator` are dead code | High |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

Items 2, 3, 6, 8, 9 and 10 can be confirmed by reading the code alone. Items 1 and 4 depend on
platform or Avalonia behaviour that was reasoned about, not tested. Items 5 and 7 are real
gaps, but no current input is known to trigger them.

---

## 1. Settings directory is the executable directory

- [ ] Fixed
- **Where:** `Common/PlatformUtil.cs:75` (`GetRuntimeDataDir`). Callers:
  `Services/SettingsService.cs:84` and `103`, `ViewModels/AboutBoxViewModel.cs:70`.
- **Category:** design, packaging.

This is the same defect as item 7 in `REVIEW_FINDINGS_Services.md`. See that item for the
problem, the per-platform results and the fix direction. Fix it once and mark both.

One point specific to this file: `AboutBoxViewModel` uses the same method to find the legal
text file, which is a read-only file shipped with the application. If the settings move to a
per-user directory, that caller must keep using the application directory. Splitting the
method in two (application data directory, user settings directory) makes this explicit.

---

## 2. Minimized and FullScreen state and bounds are saved

- [ ] Fixed
- **Where:** `Common/WindowPlacement.cs:79-92` (`Save`) and `110-112` (`Restore`). Called from
  the `Closing` handler at `Views/MainWindow.axaml.cs:590`.
- **Category:** logic error.

**Problem.** `Save` substitutes the cached normal bounds only when the state is `Maximized`.
For `Minimized` and `FullScreen` it falls through to `window.Position`, `window.Width` and
`window.Height`, and it stores the state name as it is. `Restore` then applies whatever state
was stored.

**How to reproduce.** Minimize the main window, then quit from the taskbar or Dock menu.
Relaunch. For the second case, put the window in macOS full screen, quit, relaunch.

**Result.** After quitting while minimized, the app starts minimized. On Windows the saved
position is (-32000, -32000), so restoring the window from the taskbar still leaves it
off-screen. After quitting in full screen, the app starts in full screen, and the full-screen
size is remembered as the normal size.

**Fix direction.** In `Save`, use the cached normal bounds for every state other than
`Normal`. Store `Minimized` and `FullScreen` as `Normal` (WPF's placement code restored a
minimized window as normal). In `Restore`, accept only `Normal` and `Maximized`.

---

## 3. Restore never checks the saved position is on a screen

- [ ] Fixed
- **Where:** `Common/WindowPlacement.cs:107-109` (`Restore`). Called from
  `Views/MainWindow.axaml.cs:461`.
- **Category:** missing validation, regression from WPF.

**Problem.** `Restore` assigns the saved pixel position and size with no check against the
screens that exist now. The Win32 `SetWindowPlacement` call that this class replaces moved
the window back into the visible work area by itself.

**How to reproduce.** Move the window to an external monitor and quit. Disconnect the
monitor, or change its resolution or scaling. Relaunch.

**Result.** The window opens entirely or mostly off-screen. The only way to get it back is to
delete the settings file or use a platform window-management shortcut.

**Fix direction.** After deserializing, look through `window.Screens.All` for a screen whose
`WorkingArea` intersects the saved rectangle by a useful margin (for example 100 pixels of the
title bar). If none does, skip the position and let the platform place the window. Clamp the
size to the working area of the chosen screen. Remember that the position is in physical
pixels and the size in device-independent units, so convert with that screen's `Scaling`.

---

## 4. Normal-bounds cache can capture maximized bounds

- [ ] Fixed
- **Where:** `Common/WindowPlacement.cs:47-53` (`TrackNormalBounds`, `UpdateCache`).
- **Category:** event ordering, platform dependent.

**Problem.** `UpdateCache` records the window bounds whenever `PositionChanged` or `Resized`
fires and `WindowState` is `Normal`. During a maximize transition, a backend may raise those
events before it updates `WindowState`. On Win32 the move and size messages arrive before the
state change is reported. If so, the cache is overwritten with the maximized position and
size, and the cache then no longer serves its purpose.

**How to reproduce.** On Windows, size the window to something small, maximize it, quit.
Relaunch and un-maximize.

**Result if the ordering is as described.** The window stays screen-sized at the screen
origin, not at its earlier size and position.

**Confirm first.** Log `WindowState`, `Position`, `Width` and `Height` in both handlers while
maximizing, on Windows, macOS and Linux. The ordering was not confirmed on any platform.

**Fix direction.** Keep the previous cache entry as well as the current one, and when
`WindowState` changes to a non-normal state, discard any entry recorded since the last
confirmed normal state. A simpler option is to update the cache with a posted callback
(`Dispatcher.UIThread.Post`) so that it runs after the state property has settled. The static
dictionary keyed by window can also go: only the main window is tracked, so an instance
object owned by `MainWindow` is enough.

---

## 5. `async void` query handlers can block the worker forever

- [ ] Fixed
- **Where:** `Common/WorkProgress.axaml.cs:43-53` (`OnDataContextChanged`), with
  `MessageBoxQuery.WaitForResult` and `OverwriteQuery.WaitForResult` in
  `ViewModels/WorkProgressViewModel.cs:61` and `89`.
- **Category:** missing error handling, possible hang.

**Problem.** The two handlers are `async` lambdas assigned to `Action<T>` events, so they are
`async void`. The worker thread waits in `Monitor.Wait` until the handler calls `SetResult`.
There is no `try`/`finally`. If anything throws before `SetResult` (building
`OverwriteQueryViewModel`, constructing the dialog, or `ShowDialog`), the result is never
set.

**Result.** The worker thread never wakes. `IsBusy` stays true, so `Window_Closing` cancels
every attempt to close the progress window, and the cancel request has no effect because the
worker is not polling. The exception itself escapes as an unhandled `async void` exception.

No path that currently throws here is known. The cost of the gap is that any future fault in
the overwrite dialog turns into a hang.

**Fix direction.** Wrap each handler body in `try`/`catch`/`finally`. Log the exception, and
in `finally` call `SetResult` with `Cancel` if no result was set. The same handlers are added
again each time `DataContext` changes and are never removed; subscribe once, or unsubscribe
from the old view model.

---

## 6. Worker message box ignores `Button`/`Image`, always OK

- [ ] Fixed
- **Where:** `Common/WorkProgress.axaml.cs:50-53`, using `PlatformUtil.ShowMessageAsync` at
  `Common/PlatformUtil.cs:103`. The existing alternative is `MessageBoxViewModel`, shown by
  `Services/DialogService.cs:81`.
- **Category:** duplicated code, lost information.

**Problem.** `MessageBoxQuery` carries `Button` and `Image`, and every caller in
`Actions/ProgressUtil.cs` passes `MBIcon.Error` or `MBIcon.Information`. The handler ignores
both and always sets `MBResult.OK`. It shows the text through
`PlatformUtil.ShowMessageAsync`, a second message box built in code, which has no icon and no
default or cancel button.

**Result.** Worker error messages appear without an error icon. Enter and Esc do not dismiss
the box. A future query that asks for `OKCancel` or `YesNo` would show a single OK button and
report OK. Two message box implementations have to be kept consistent.

**Fix direction.** Show the same view that `DialogService` uses, with this window as owner:
create a `MessageBoxViewModel(mq.Text, mq.Caption, mq.Button, mq.Image)`, show its dialog, and
pass the view model's `Result` to `mq.SetResult`. `PlatformUtil.ShowMessageAsync` then has no
callers and can be deleted. Combine this with item 5, which touches the same lines.

---

## 7. Palette lookup is unchecked against the palette length

- [ ] Fixed
- **Where:** `Common/BitmapUtil.cs:68-80` (`ExpandIndexed`). Called from
  `ViewModels/FileViewerViewModel.cs:1296`, `1301` and `1308`.
- **Category:** missing bounds check, difference from WPF.

**Problem.** `Bitmap8.GetColors()` returns an array only as long as the number of colors
defined in the palette, not 256 entries. `ExpandIndexed` reads `palette[t]` for every pixel
without a check. `Bitmap8.SetPixelIndex` rejects out-of-range indices, but a bitmap that keeps
the default empty palette, or one whose palette is replaced by a smaller one after drawing,
holds indices beyond the array.

**Result.** `IndexOutOfRangeException` during the file viewer's preview update. The WPF
version passed the palette to an `Indexed8` bitmap and did not fail on such input.

No converter is known to produce such a bitmap today.

**Fix direction.** Copy the palette into a 256-entry array first (unused entries black), then
index that. This also removes the bounds check from the inner loop.

**Related, same function.** Line 79 turns palette alpha 0 into 255 ("0 alpha means fully
opaque here"). The WPF version passed palette alpha through unchanged, and the direct-color
path in this file also leaves alpha alone. If any converter relies on a transparent palette
entry, it now draws opaque. Decide which behaviour is intended and make both paths agree.

---

## 8. Indexed conversion allocates a full BGRA temporary copy

- [ ] Fixed
- **Where:** `Common/BitmapUtil.cs:52-53` and `65-82` (`ExpandIndexed`, then `CopyRowByRow`).
- **Category:** efficiency.

**Problem.** For an indexed bitmap, `ExpandIndexed` builds a managed array of
`width * height * 4` bytes, and `CopyRowByRow` then copies that array into the locked
framebuffer. The array is discarded straight after.

**Result.** `FileViewerViewModel` converts on every magnification change, using a bitmap that
`ScaleUp` has already enlarged. At the 4096 x 4096 limit this is a 64 MB large-object-heap
allocation for each zoom step.

**Fix direction.** Expand one row at a time into a reusable `width * 4` buffer and copy that
row to `dest + row * rowBytes`. Alternatively write `uint` pixels directly through the
framebuffer address. Either removes the large temporary.

---

## 9. `MBResult`/`MBButton`/`MBIcon` are unused duplicates

- [ ] Fixed
- **Where:** `Common/MessageBoxEnums.cs:22-32`. The enums in use are in
  `Services/IDialogService.cs:24-26`.
- **Category:** dead code.

**Problem.** Nothing refers to the `Common` copies. They have already drifted from the
`Services` versions (`Info` against `Information`, and an extra `None` result). Because both
namespaces define the same names, any file that imports both must qualify them, which is why
`MainViewModel.cs` and `WorkProgress.axaml.cs` write `Services.MBResult`, `Services.MBButton`
and `Services.MBIcon`.

**Fix direction.** Delete `Common/MessageBoxEnums.cs`. The `Services.` qualifiers can then be
removed.

---

## 10. `AskFileToOpen`, `FILE_FILTER_*`, `IsAdministrator` are dead code

- [ ] Fixed
- **Where:** `Common/PlatformUtil.cs:30-35` (`FILE_FILTER_ALL`, `FILE_FILTER_KNOWN`), `40-68`
  (`AskFileToOpen`), `93-95` (`IsAdministrator`). Also `Common/InverseBooleanConverter.cs`,
  registered as `InvertBool` at `App.axaml:160`.
- **Category:** dead code, duplicated data.

**Problem.** None of these has a caller, and no XAML file refers to `InvertBool`. The list of
known extensions appears twice in this file and the copies disagree: `FILE_FILTER_KNOWN`
includes `*.sit`, the pattern list in `AskFileToOpen` does not. `AskFileToOpen` also reads
`files[0].Path.LocalPath` where the rest of the code prefers `TryGetLocalPath()`.

**Fix direction.** Delete the unused members. If a list of known extensions is needed for the
real open-file picker, keep one copy and build the picker patterns from it. Delete
`InverseBooleanConverter` and its resource entry unless a use is planned; Avalonia bindings
support negation directly (`{Binding !Flag}`).

---

## Also noticed

These did not make the list above. None was verified.

- `CommandLineOptions.cs:80` treats every argument that starts with `-` as an option. A file
  whose name starts with `-` can only be opened as `./-name`. There is no `--` terminator.
- `CommandLineOptions.cs:42` documents `FilePath` as set only for a regular file path, but
  line 114 also sets it for a physical drive. `Program.cs:59` relies on the latter.
- `AnimatedGifEncoder.cs:152` rounds the delay to hundredths of a second. `DoConvertANI`
  turns a zero delay into 1 ms, which rounds to 0; most viewers then use about 100 ms. The WPF
  version behaves the same way.
- `AnimatedGifEncoder.cs:151` never sets the transparency flag. The WPF version carried it
  over from the per-frame encoder. ANI frames have no transparent color, so the only current
  caller is not affected.
- `PlatformUtil.cs:78` walks up at most six levels. A development build run from inside a
  `.app` bundle under `bin/` is deeper than that and falls back to the bundle directory.
- `WorkProgress.axaml.cs:58` and `62` use `VM!`. Both throw `NullReferenceException` if the
  window is shown without a `WorkProgressViewModel` as its `DataContext`.
- `WorkProgress.axaml.cs:62-65`: closing the window with the title-bar button requests
  cancellation but leaves the Cancel button enabled, unlike `CancelCommand`.
- `WindowPlacement.cs:57`: `[Serializable()]` has no effect on `System.Text.Json` and can be
  removed.

## Suggested order

1. Items 9 and 10 (deletions only, no behaviour change).
2. Items 5 and 6 together (same handler).
3. Items 2 and 3, then item 4 after confirming the event order on each platform.
4. Item 1 together with item 7 of `REVIEW_FINDINGS_Services.md`.
5. Items 7 and 8 together (same function).
