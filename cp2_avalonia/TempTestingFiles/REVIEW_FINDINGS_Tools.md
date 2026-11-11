# Code Review Findings: cp2_avalonia/Tools

Review date: 2026-11-10. Reviewed at commit `dc9481e` (branch `docs/platform-review-findings`).
Scope: every file in `Tools/` in full (`ConfigOptCtrl.cs`, `DropTarget`, `FancyTextHelper.cs`,
`LogViewer`, `ShowText`), plus their call sites in `Views/FileViewer.axaml.cs`,
`Views/EditConvertOpts.axaml.cs`, `ViewModels/FileViewerViewModel.cs`,
`ViewModels/EditConvertOptsViewModel.cs` and `ViewModels/LogViewerViewModel.cs`.

**Status of these findings:** they come from reading the code only. Nothing was run, and no
verification pass was done. Confirm each one before fixing it. Line numbers refer to the
commit above and will drift.

Mark an item done by changing `[ ]` to `[x]` and adding the fixing commit.

## Summary

| # | Area | File | Finding | Confidence |
|---|------|------|---------|------------|
| 1 | Log viewer | LogViewer.axaml.cs:65 | Controls are read on the thread that raised the log event | Medium |
| 2 | Converter options | ConfigOptCtrl.cs:311 | `ConfigureControls` throws when an option key is missing | Medium |
| 3 | Log viewer | LogViewer.axaml.cs:37 | Save and copy errors are unhandled and end the app | High |
| 4 | Converter options | ConfigOptCtrl.cs:166 | `mIgnoreChange` guard never suppresses `TextChanged` | Medium |
| 5 | Fancy text | FancyTextHelper.cs:29 | The whole class is unreferenced dead code | High |

Confidence is how sure the review is that the defect exists as described, not how severe it is.
The list is ordered by severity, most severe first.

---

## 1. Controls are read on the thread that raised the log event

- [ ] Fixed
- **Where:** `Tools/LogViewer.axaml.cs:65` (`ScrollToEnd`), subscribed at line 39. The event
  comes from `LogViewerViewModel.HandleLogEvent` (`ViewModels/LogViewerViewModel.cs`, near
  line 170).
- **Category:** threading.

**Problem.** `HandleLogEvent` carries the note "may be called on a background thread". It adds
to the `ObservableCollection` and raises `ScrollToEndRequested(false)` on that same thread.
`ScrollToEnd` then calls `IsScrolledToBottom()` before it posts to the dispatcher. That method
walks the visual tree and reads `sv.Offset`, `sv.Extent` and `sv.Viewport`. Avalonia checks
thread access on these reads and throws `InvalidOperationException` from a non-UI thread. The
`LogEntries.Add` call just before it has the same problem, because the `ListBox` handles the
collection change on the calling thread.

**How to reproduce.** Open DEBUG > Show Debug Log. Then open a work file
(`WorkspaceService.OpenAsync` builds the `WorkTree` under `Task.Run`, and it logs through
`AppHook`), or run any add, extract or paste action.

**Result.** Expected: an exception inside the worker thread, so the open or action fails with
an "invalid thread" message, or the app crashes. This was not reproduced during the review,
which is why confidence is Medium.

**Fix direction.** Marshal once, at the top. In `HandleLogEvent`, if not on the UI thread,
post the whole body (`mAllEntries.Add`, `LogEntries.Add`, the event) to
`Dispatcher.UIThread`. `ScrollToEnd` can then stay as it is. `mAllEntries` is also a plain
`List` written from several threads today, and the same change fixes that.

---

## 2. `ConfigureControls` throws when an option key is missing

- [ ] Fixed
- **Where:** `Tools/ConfigOptCtrl.cs:311` (`string defaultVal = convOptions[optDef.OptTag];`).
  The key is removed in `FileViewerViewModel.SetConvOption` (line 1670) and
  `EditConvertOptsViewModel.UpdateOption`.
- **Category:** unhandled exception.

**Problem.** `ConfigureControls` assumes the dictionary holds every option tag. That is true
for a dictionary fresh from `LoadExportOptions`, which seeds defaults. It is not true after the
user clears a value: both view models call `Remove(tag)` when the new value is empty. In the
file viewer the dictionary without the key is cloned into the history entry, and
`TryConsumePendingConverterOptions` hands that clone straight back to `ConfigureControls` on
history navigation.

**How to reproduce.** In the file viewer choose the Random-Access Text converter. Clear the
"Record length" box. View a different file, then press Back.

**Result.** `KeyNotFoundException` in the `SelectionChanged` handler on the UI thread.

**Fix direction.** In `ConfigureControls` use
`convOptions.TryGetValue(optDef.OptTag, out var v) ? v : optDef.DefaultVal`. That fixes every
caller. Storing the default instead of removing the key in the view models would also work,
but needs the change in two places.

---

## 3. Save and copy errors are unhandled and end the app

- [ ] Fixed
- **Where:** `Tools/LogViewer.axaml.cs:37-38` (the `async () => await DoSave()` and
  `DoCopy()` lambdas), with the I/O at lines 104-106 and 113.
- **Category:** unhandled exception.

**Problem.** `SaveRequested` and `CopyRequested` are `Action` events, so the async lambdas are
`async void`. `DoSave` and `DoCopy` have no `try`/`catch`. An exception from `OpenWriteAsync`,
the `StreamWriter` flush, or `SetTextAsync` is rethrown on the UI synchronization context as
an unhandled exception.

**How to reproduce.** Click "Save to File" and pick a file that is read-only, locked by
another process, or on a full or removed volume.

**Result.** The application terminates instead of showing an error.

**Fix direction.** Wrap the bodies of `DoSave` and `DoCopy` in `try`/`catch`, log with
`AppLog.E`, and show the message with `PlatformUtil.ShowMessageAsync`.
`DropTarget.DoPasteAsync` is called the same way (line 45) and should get the same treatment,
though it only reads the clipboard.

---

## 4. `mIgnoreChange` guard never suppresses `TextChanged`

- [ ] Fixed
- **Where:** `Tools/ConfigOptCtrl.cs:166-168` (`TextBoxMapItem.AssignControl`) and lines
  149-151 (`OnTextChanged`). Compensating code is in `Views/FileViewer.axaml.cs:428` and
  `Views/EditConvertOpts.axaml.cs:89`.
- **Category:** fix at the wrong depth.

**Problem.** The guard sets `mIgnoreChange`, assigns `mBox.Text`, and clears the flag, all
synchronously. Avalonia 11 raises `TextBox.TextChanged` from a posted dispatcher callback, so
the flag is already false when the handler runs. The comment on line 165 ("Suppress the
TextChanged event fired by the initial value assignment") describes behaviour the code does
not have. Both callers work around this with their own flags, each reset with
`Dispatcher.UIThread.Post`.

**Result.** The real suppression lives in two callers, and a new caller would get a spurious
`Updater` call on every assignment. In the digits-only filter, typing a non-digit rewrites
`Text` under the dead guard, so `Updater` runs twice. In the file viewer that means
`FormatFile` runs twice for that keystroke.

**Fix direction.** Check the Avalonia source or docs for the event timing first, since this
was inferred from the callers' workaround and not tested. Then either handle the synchronous
`TextChanging` event, or keep the last value sent to `Updater` and skip the call when the
text is unchanged. After that the posted flags in the two views can go.

---

## 5. `FancyTextHelper` is unreferenced dead code

- [ ] Fixed
- **Where:** `Tools/FancyTextHelper.cs` (all 294 lines).
- **Category:** dead code.

**Problem.** Nothing in the solution references `FancyTextHelper`. The viewer renders through
`Controls/FancyTextViewer` now.

**Result.** A file of rendering rules (theme brush mapping, font-size reset, point sizes used
as device-independent pixels) that looks current but never runs. A rendering fix can land here
by mistake.

**Fix direction.** Delete the file. If any of its rules are still wanted, check that
`Controls/FancyTextViewer` already has them first.

---

## Smaller observations

- `DropTarget.axaml.cs:113-131`: `TextArea_PreviewKeyDown` and `Window_KeyDown` have identical
  bodies. One shared method would do.
- `DropTarget.axaml.cs`, `LogViewer.axaml.cs`, `ShowText.axaml.cs`: each subscribes to view
  model events inside `DataContextChanged` and never unsubscribes. Harmless while each window
  gets one view model for its lifetime.
- `ConfigOptCtrl.cs:267`: `dict[kvp.Key] = ...` inside a `foreach` over the same dictionary.
  This is allowed on .NET 5 and later (the project targets net10.0), but it reads like a bug
  next to the deferred `badKeys` removal just below it.
- `ConfigOptCtrl.cs:189`: the digits filter treats an all-zero value as unset. Fine for the
  only current integer option (record length), but it is a rule for every `IntValue` option.

## Suggested order

1. Item 3 is small and removes a crash.
2. Item 2 is a one-line change.
3. Item 1 needs a reproduction first; the fix is in `LogViewerViewModel`.
4. Items 4 and 5 are cleanup and can wait.
