# Iteration 4B Blueprint: Remaining Dialog ViewModels

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §3.3, §6 Phase 4,
> §7.14.

---

## Goal

Convert all remaining dialogs (medium and simple complexity) from code-behind
to MVVM ViewModels. After this iteration, every dialog in the application uses
a ViewModel class, and the `IDialogService` mapping dictionary is complete.

---

## Prerequisites

- Iteration 4A is complete (all complex dialog ViewModels created and
  working).
- The application builds and runs correctly.

---

## Target Dialogs (Phase 4B)

### Medium Complexity (100–500 lines)

| Dialog | Lines | Modality | Purpose |
|---|---|---|---|
| `EditConvertOpts` | ~319 | Modal | Import/export converter option configuration |
| `EditAppSettings` | ~246 | Modal | Application-wide settings (tabs, theme, audio) |
| `WorkProgress` | ~248 | Modal | Cancellable async progress with overwrite queries |
| `LogViewer` (Tools) | ~247 | Modeless | Debug log viewer with auto-scroll, save, copy |
| `DropTarget` (Tools) | ~202 | Modeless | Debug clipboard/drag-drop inspector |
| `ReplacePartition` | ~238 | Modal | Replace disk partition from file |

### Simple Complexity (< 175 lines)

| Dialog | Lines | Modality | Purpose |
|---|---|---|---|
| `CreateDirectory` | ~175 | Modal | Enter directory name with validation |
| `EditMetadata` | ~144 | Modal | Edit a single metadata key/value |
| `CreateFileArchive` | ~130 | Modal | Select archive format (Binary2, NuFX, Zip) |
| `FindFile` | ~135 | Modeless | Search for file by name pattern |
| `AboutBox` | ~113 | Modal | Version, legal, runtime info display |
| `AddMetadata` | ~103 | Modal | Add new metadata entry |
| `OverwriteQueryDialog` | ~93 | Modal | Confirm file overwrite with "apply to all" |
| `ShowText` (Tools) | ~77 | Modal/Modeless | Simple text display window |

---

## Conversion Pattern

Same pattern as Phase 4A (see Iteration_4A_Blueprint.md §Conversion Pattern):
1. Create `ViewModels/{DialogName}ViewModel.cs` extending `ReactiveObject`
2. Move properties and business logic from code-behind
3. Add `ReactiveCommand` for button actions
4. **Update the View base class:** Change `Window` to
   `ReactiveWindow<TViewModel>` in **both** files:
   - `.axaml.cs`: `public partial class MyDialog : ReactiveWindow<MyDialogViewModel>`
   - `.axaml`: Replace the root element `<Window ...>` / `</Window>` with
     `<rxui:ReactiveWindow x:TypeArguments="vm:MyDialogViewModel" ...>` /
     `</rxui:ReactiveWindow>`. Add namespace declarations:
     ```xml
     xmlns:rxui="http://reactiveui.net"
     xmlns:vm="clr-namespace:cp2_avalonia.ViewModels"
     ```
   This is required for `this.WhenActivated(...)` to compile.
5. Strip code-behind to UI-only
6. Register ViewModel→View mapping in `RegisterDialogMappings()`
7. Update callers in `MainViewModel`
8. Build and test

### Modal Close Mechanism

Every modal ViewModel exposes a standard `CloseInteraction` so its
`OkCommand` and `CancelCommand` can signal the View to call `Close(result)`:

```csharp
public Interaction<bool, Unit> CloseInteraction { get; } = new();
```

Because `Handle()` returns `Task<Unit>`, commands that call it must be
created with `ReactiveCommand.CreateFromTask`, not `ReactiveCommand.Create`:

```csharp
OkCommand = ReactiveCommand.CreateFromTask(async () => {
    // validation / side-effects here
    await CloseInteraction.Handle(true);
});
CancelCommand = ReactiveCommand.CreateFromTask(async () =>
    await CloseInteraction.Handle(false));
```

The same applies to any command that calls `CopyInteraction.Handle()` or
any other `Interaction.Handle()`.

- View code-behind subscribes via `WhenActivated`:
  ```csharp
  this.WhenActivated(d =>
      ViewModel!.CloseInteraction.RegisterHandler(ctx => {
          Close(ctx.Input);
          ctx.SetOutput(Unit.Default);
      }).DisposeWith(d));
  ```

This applies to all 4B modal dialogs: `EditConvertOpts`, `EditAppSettings`,
`ReplacePartition`, `CreateDirectory`, `EditMetadata`, `CreateFileArchive`,
`AboutBox`, `AddMetadata`, `OverwriteQueryDialog`, and `ShowText` (when
modal). `WorkProgressViewModel` already specifies this pattern.

---

## Dialog-Specific Instructions

### 1. EditConvertOpts (~319 lines)

**ViewModel:** `ViewModels/EditConvertOptsViewModel.cs`

```csharp
public EditConvertOptsViewModel(bool isExport, ISettingsService settingsService)
```

- The parameter is `isExport` (true = export, false = import) — matches the
  existing source convention. Do not rename to `isImport`.
- Move converter option mapping logic (dynamic control generation stays in
  code-behind or uses DataTemplates)
- Move `ConverterListItem` inner class to `EditConvertOptsViewModel.cs`
  (or `Models/ConverterListItem.cs`). The `ConfigOptCtrl` helper classes
  (`ControlMapItem`, `ToggleButtonMapItem`, `TextBoxMapItem`,
  `RadioButtonGroupItem` in `Tools/ConfigOptCtrl.cs`) reference Avalonia
  control types and must remain in the View layer.
- Read/write settings via `_settingsService` (not raw `SettingsHolder`)
- **Settings commit exception:** `OkCommand` calls
  `AppSettings.Global.MergeSettings(mSettings)` directly. The accumulated-
  changes pattern (empty `SettingsHolder` → set individual keys → merge on
  OK) has no equivalent in `ISettingsService`, and `SettingsHolder` does not
  expose its keys for enumeration. This is the one ViewModel that accesses
  `AppSettings.Global` directly; document it as a deliberate exception.
- Commands: `OkCommand`, `CancelCommand`

### 2. EditAppSettings (~246 lines)

**ViewModel:** `ViewModels/EditAppSettingsViewModel.cs`

```csharp
public EditAppSettingsViewModel(ISettingsService settingsService,
    IDialogService dialogService)
```

- Move all settings properties (theme mode, audio algorithm, feature toggles)
- Move Apply logic
- Commands: `OkCommand`, `ApplyCommand`, `CancelCommand`,
  `ConfigureImportOptionsCommand`, `ConfigureExportOptionsCommand`
- `ConfigureImportOptionsCommand` opens `EditConvertOptsViewModel(false, settingsService)`
  via `_dialogService.ShowDialogAsync(...)`. After the sub-dialog returns
  `true`, call a `RefreshFromGlobal()` method on `EditAppSettingsViewModel`
  that re-reads any properties that `EditConvertOpts.OkCommand` may have
  updated in `AppSettings.Global` (equivalent to the source's
  `OnSettingsApplied()` call in `ConfigureImportOptions_Click`).
- `ConfigureExportOptionsCommand` opens `EditConvertOptsViewModel(true, settingsService)`
  via `_dialogService.ShowDialogAsync(...)`. Same `RefreshFromGlobal()` call
  after `true` return.
- On OK/Apply, write values through `_settingsService`. Writing through
  `_settingsService` raises `SettingChanged` for each changed key.
  `MainViewModel` subscribes to `_settingsService.SettingChanged` and
  re-applies theme and other app-wide settings reactively. No additional
  notification event is needed in `EditAppSettingsViewModel`.
- **Settings commit exception:** The source `ApplySettings()` calls
  `AppSettings.Global.ReplaceSettings(mSettings)` — a bulk-replace of all
  settings. `ISettingsService` has no `ReplaceSettings` method. Two
  approaches:
  1. Call `AppSettings.Global.ReplaceSettings(mSettings)` directly in
     `ApplyCommand` (deliberate exception, same as EditConvertOpts's
     `MergeSettings` exception).
  2. Write individual `_settingsService.Set*()` calls. This fires
     `SettingChanged` N times. If partial-update ordering causes visible
     artifacts (e.g., theme flickers), suppress reactions in
     `MainViewModel` using a guard flag while settings are being written.
  Choose one approach. If option 1, document it alongside the
  EditConvertOpts exception as the second ViewModel that accesses
  `AppSettings.Global` directly.
- Retire the `SettingsAppliedHandler` delegate and `SettingsApplied` event —
  they are replaced by `ISettingsService.SettingChanged`. Remove the
  controller's subscription to `SettingsApplied`.

### 3. WorkProgress (~248 lines)

**ViewModel:** `ViewModels/WorkProgressViewModel.cs`

```csharp
public WorkProgressViewModel(IWorker worker, bool isIndeterminate)
```

- Move progress state, cancel flag, status text, progress percentage
- **Threading model:** Retain `BackgroundWorker` inside
  `WorkProgressViewModel`. Switching to `Task`+`IProgress<T>` would require
  changing the `IWorker` interface and all its callers in `Actions/`.
  The `ProgressChanged` handler body moves to the ViewModel; since
  `ProgressChanged` already fires on the UI thread (via
  `SynchronizationContext`), no additional `Dispatcher` marshaling is needed
  for property updates.
- **OverwriteQuery handling:** Inside the `ProgressChanged` handler (already
  on the UI thread), assign the ViewModel to a local variable before awaiting
  so result properties can be read afterwards:
  ```csharp
  var oqVm = new OverwriteQueryViewModel(oq.Facts);
  bool? ok = await _dialogService.ShowDialogAsync(oqVm);
  if (ok == true) {
      oq.SetResult(oqVm.Result, oqVm.UseForAll);
  } else {
      oq.SetResult(CallbackFacts.Results.Cancel, false);
  }
  // SetResult() internally calls Monitor.Pulse to resume the worker thread.
  ```
  The `IDialogService` instance passed to
  `WorkProgressViewModel` must use the `WorkProgress` view window as its
  owner (not `MainWindow`) to preserve correct modal stacking. One approach:
  pass a separate `IDialogService` instance configured with the WorkProgress
  window, or expose a secondary `IDialogHost` from WorkProgress's code-behind.
- **`IDialogService` injection timing:** `WorkProgressViewModel` is created
  in `MainViewModel` before the `WorkProgress` View exists, so the
  constructor cannot receive a WorkProgress-owned `IDialogService`. Use
  post-construction injection:
  1. Declare `internal void SetDialogService(IDialogService ds)` on
     `WorkProgressViewModel`.
  2. In `WorkProgress.axaml.cs`, in the `Activated` or `OnLoaded` handler:
     ```csharp
     ViewModel!.SetDialogService(new DialogService(this));
     ```
     where `this` (the `WorkProgress` window) implements `IDialogHost`.
  3. `BackgroundWorker` must not start until `SetDialogService` has been
     called. Guard this by having `RunWorkerAsync()` called from
     `SetDialogService` itself, or by checking a flag.
  This creates a `DialogService` without going through DI — a deliberate
  exception acceptable for this one case, since the owner window is
  transient and only known at runtime.

  **Registration sharing:** The `DialogService` registration dictionary
  must be **static** so that all instances (including this ad-hoc one)
  share the mappings registered at startup by `RegisterDialogMappings()`.
  Without this, `ShowDialogAsync<OverwriteQueryViewModel>()` would throw
  at runtime because the per-window instance has no registered mappings.

  > **Testing note:** Static registration state persists across tests.
  > Add an `internal static void ClearMappings()` method (or use
  > `[InternalsVisibleTo]`) so test fixtures can reset the dictionary
  > between runs.
- **Nested-type migration:** Move all three public nested types to
  `WorkProgressViewModel.cs`:
  - `IWorker` — 7 classes in `Actions/` implement `WorkProgress.IWorker`;
    update them to `WorkProgressViewModel.IWorker`.
  - `OverwriteQuery` — `ProgressUtil.cs` creates
    `new WorkProgress.OverwriteQuery(...)`; update to
    `WorkProgressViewModel.OverwriteQuery`.
  - `MessageBoxQuery` — `ProgressUtil.cs` creates
    `new WorkProgress.MessageBoxQuery(...)` (4 call sites); update to
    `WorkProgressViewModel.MessageBoxQuery`.
- **MessageBoxQuery handling:** Handle `MessageBoxQuery` in `ProgressChanged`
  the same way as `OverwriteQuery` — show a message dialog on the UI thread
  via `_dialogService.ShowMessageAsync(...)`, then pulse the waiting worker
  thread with the result.
- **Closure mechanism:** The ViewModel cannot call `Close()` on the View.
  Expose a `CloseInteraction` (`Interaction<bool, Unit>`) that
  `RunWorkerCompleted` raises with the boolean result. The View's code-behind
  registers a handler via `WhenActivated` that calls `Close(result)` when
  the interaction fires.
- **Caller result pattern:** Callers in `MainViewModel` read the result from
  `ShowDialogAsync`'s `bool?` return value directly:
  ```csharp
  bool? ok = await _dialogService.ShowDialogAsync(progressVM);
  if (ok == true) { /* success path */ }
  ```
  Do not expose a separate `DialogResult` property on
  `WorkProgressViewModel` — the `CloseInteraction.Handle(bool)` value is
  what Avalonia surfaces as the `bool?` dialog result. The existing 6 call
  sites that read `workDialog.DialogResult` must be rewritten to use the
  `bool?` return.
- Commands: `CancelCommand`

**Important:** This dialog is used by many operations (add, extract, delete,
test, defragment, copy, paste, scan). Ensure the ViewModel interface is
generic enough for all callers.

### 4. LogViewer (~247 lines, Tools/)

**ViewModel:** `ViewModels/LogViewerViewModel.cs`

```csharp
public LogViewerViewModel(DebugMessageLog messageLog)
```

- Move log text property, auto-scroll flag
- **Data model:** The ViewModel holds
  `ObservableCollection<LogEntry> LogEntries` (the AXAML `ListBox.ItemsSource`
  binding target) and `bool AutoScroll`.
- **Event subscription:** In the constructor, replay stored entries and
  subscribe to new log events:
  ```csharp
  foreach (var e in messageLog.GetLogs())
      LogEntries.Add(new LogEntry(e));
  messageLog.RaiseLogEvent += HandleLogEvent;
  ```
  `HandleLogEvent` fires on background threads — marshal to the UI thread:
  ```csharp
  private void HandleLogEvent(object? s, DebugMessageLog.LogEventArgs e) =>
      Dispatcher.UIThread.Post(() => LogEntries.Add(new LogEntry(e.Entry)));
  ```
- **Unsubscription:** The View's `Window_Closed` handler calls a method on
  the ViewModel to unsubscribe (`_messageLog.RaiseLogEvent -= HandleLogEvent`)
  and fires `ViewModel!.ClosedSubject.OnNext(Unit.Default)`. The auto-scroll
  scroll-to-end logic (`logListBox.AddHandler(ScrollViewer.ScrollChangedEvent,
  ...)`) stays in code-behind.
- `SaveCommand` raises a `SaveInteraction` (`Interaction<string, Unit>`).
  The ViewModel provides the formatted log text as the interaction input.
  The View's code-behind handler calls
  `StorageProvider.SaveFilePickerAsync(...)`, opens the write stream via
  `IStorageFile.OpenWriteAsync()`, and writes the text. This preserves the
  sandboxed-stream pattern the source uses (important on macOS App
  Sandbox). Do not use `File.WriteAllText(path, ...)` — the
  `IStorageFile` stream may not correspond to an accessible file path.
- `CopyCommand` writes plain text (formatted log lines) to the system
  clipboard. `IClipboardService` is scoped to CP2's internal `ClipInfo`
  format and has no `SetTextAsync(string)` method. Retain the copy-to-
  clipboard logic in code-behind via a thin helper. The ViewModel exposes
  `public Interaction<string, Unit> CopyInteraction { get; } = new();`.
  `CopyCommand` calls `await CopyInteraction.Handle(formattedLogText)`.
  The View's code-behind handler calls
  `TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(ctx.Input)` and
  sets `ctx.SetOutput(Unit.Default)`. Similar to the DropTarget pattern.
- Move `LogEntry` wrapper class to `Models/LogEntry.cs` (or nest it in
  `LogViewerViewModel.cs`). Update the AXAML namespace reference if the
  class moves out of `cp2_avalonia.Tools`.
- Commands: `SaveCommand`, `CopyCommand`
- **Modeless toggle management:** `MainViewModel` holds a
  `LogViewerViewModel?` field. `Debug_ShowDebugLog()` toggles:
  - If null → create the VM, call `_dialogService.ShowModeless(vm)`.
    Subscribe to `vm.Closed.Take(1)` to null the field and set
    `IsDebugLogOpen = false` when the user closes the window via X.
  - If not null → call
    `await _logViewerVm.RequestCloseInteraction.Handle(Unit.Default)`
    to programmatically close the window.
  - Expose `bool IsDebugLogOpen` as a reactive property on
    `MainViewModel` for AXAML menu check-mark binding.
- **Modeless lifecycle observables:** Expose on the ViewModel:
  - `Subject<Unit> ClosedSubject` — backing field. The View's
    code-behind fires `ViewModel!.ClosedSubject.OnNext(Unit.Default)`
    in its `Window_Closed` handler.
  - `IObservable<Unit> Closed => ClosedSubject.AsObservable()` — public
    property for external subscribers (e.g., `MainViewModel` subscribes
    to `vm.Closed.Take(1)` to null the field on window close).
  - `Interaction<Unit, Unit> RequestCloseInteraction` — the View's
    code-behind registers a handler in `WhenActivated` that calls
    `Close()` when the interaction fires.

### 5. DropTarget (~202 lines, Tools/)

**ViewModel:** `ViewModels/DropTargetViewModel.cs`

- Debug-only, lower priority
- The ViewModel holds the resulting formatted text as a reactive `TextArea`
  string property, set by the View code-behind after processing.
- **Modeless toggle management:** Same pattern as LogViewer.
  `MainViewModel` holds a `DropTargetViewModel?` field.
  `Debug_ShowDropTarget()` toggles open/close using the same
  `Closed` / `RequestCloseInteraction` mechanism. Expose `bool
  IsDropTargetOpen` as a reactive property on `MainViewModel` for AXAML
  menu check-mark binding.
- **`Closed` event wiring:** The existing `DropTarget.axaml` has no
  `Closed=` attribute (unlike LogViewer which already has one). Add
  `Closed="Window_Closed"` to the `<rxui:ReactiveWindow>` root element
  in `DropTarget.axaml`, and add the corresponding handler in
  `DropTarget.axaml.cs`:
  ```csharp
  private void Window_Closed(object? sender, EventArgs e) {
      ViewModel!.ClosedSubject.OnNext(Unit.Default);
  }
  ```
- **Modeless lifecycle observables:** Same pattern as LogViewer §4:
  - `Subject<Unit> ClosedSubject` — backing field.
  - `IObservable<Unit> Closed => ClosedSubject.AsObservable()` — public property.
  - `Interaction<Unit, Unit> RequestCloseInteraction` — View handler calls `Close()`.

**Retained in code-behind** (Avalonia view-layer types that cannot move to VM):
- Programmatic `DragDrop.DropEvent` / `DragDrop.DragOverEvent` handler registration
- `DoPasteAsync()` — raw clipboard access via `TopLevel`
- `TextArea_Drop()` / `TextArea_DragOver()` — take Avalonia `DragEventArgs`
- `ShowDataObject(IDataObject)` — takes Avalonia `IDataObject`

### 6. ReplacePartition (~238 lines)

**ViewModel:** `ViewModels/ReplacePartitionViewModel.cs`

```csharp
public ReplacePartitionViewModel(
    Partition destination,
    IChunkAccess sourceChunks,
    EnableWriteFunc writeFunc,
    Formatter formatter,
    AppHook appHook,
    IDialogService dialogService)
```

- Move `EnableWriteFunc` delegate definition to
  `ReplacePartitionViewModel.cs`. Update all external references
  (e.g., `ReplacePartition.EnableWriteFunc` → `ReplacePartitionViewModel.EnableWriteFunc`).
- Move compatibility validation, source/dest info display
- **IBrush convention:** Replace `SizeDiffForeground` (`IBrush`) with a
  boolean (e.g., `IsSizeCompatible`). Use an AXAML style trigger or value
  converter in the View to select the brush color.
- Replace `PlatformUtil.ShowMessageAsync(this, ...)` calls with
  `await _dialogService.ShowMessageAsync(message, caption)` in the ViewModel.
- The `OkCommand` body calls `DiskImageTypes.CopyDisk(srcChunks, dstChunks,
  out errorCount)`. Wrap this in `await Task.Run(...)` to avoid blocking
  the UI thread — `CopyDisk` iterates every sector/block in a tight I/O
  loop. Because `out` parameters cannot be used inside lambda expressions,
  refactor the call:
  ```csharp
  int errorCount = 0;
  await Task.Run(() => {
      DiskImageTypes.CopyDisk(_srcChunks, _dstChunks, out int ec);
      errorCount = ec;
  });
  ```
  **Cross-reference:** Phase 4A must place `CopyDisk` in
  `Models/DiskImageTypes.cs` as `internal static` (not in
  `SaveAsDiskViewModel` as private). Both `SaveAsDiskViewModel` and
  `ReplacePartitionViewModel` call this shared method.
- Commands: `OkCommand` (with canExecute based on compatibility),
  `CancelCommand`

### 7. CreateDirectory (~175 lines)

**ViewModel:** `ViewModels/CreateDirectoryViewModel.cs`

```csharp
public CreateDirectoryViewModel(
    IFileSystem fileSystem,
    IFileEntry parentDir,
    Func<string, bool> isValidName,
    string syntaxRules)
```

- Move name input, validation, error message
- The source defines an inner delegate `IsValidDirNameFunc`. The ViewModel
  replaces this with `Func<string, bool>` for simplicity. Update all call
  sites that reference `CreateDirectory.IsValidDirNameFunc` to pass a
  `Func<string, bool>` instead.
- **IBrush convention:** Replace `SyntaxRulesForeground` and
  `UniqueNameForeground` (`IBrush`) with booleans (e.g., `IsNameValid`,
  `IsSyntaxHintVisible`). Use an AXAML style trigger or value converter
  in the View to select the brush color.
- Commands: `OkCommand` (canExecute when name is valid), `CancelCommand`
- Output properties (read by caller after OK): `NewFileName` — the
  validated directory name entered by the user.

### 8. EditMetadata (~144 lines)

**ViewModel:** `ViewModels/EditMetadataViewModel.cs`

```csharp
public EditMetadataViewModel(IMetadata metadata, string key)
```

- Move value property, validation, delete flag
- **`CanEdit` property:** Expose a `bool CanEdit` property on
  `EditMetadataViewModel`, initialized from
  `metadata.GetMetaEntry(key)!.CanEdit`. In the constructor, when
  `CanEdit` is false, set `ValueSyntaxText` to
  `"This entry can't be edited."` (otherwise set it to
  `entry.ValueSyntax`). Add `IsReadOnly="{Binding !CanEdit}"` to the
  `valueTextBox` in `EditMetadata.axaml` (Avalonia supports `!` negation
  syntax in bindings). The `Window_Opened` focus/select-all logic (which
  checks `CanEdit`) may remain in code-behind and read
  `ViewModel!.CanEdit`.
- **IBrush convention:** Replace `ValueSyntaxForeground` (`IBrush`) with a
  boolean (e.g., `IsValueValid`). Use an AXAML style trigger or value
  converter in the View to select the brush color.
- Commands: `OkCommand`, `DeleteCommand`, `CancelCommand`
- Output properties (read by caller after OK): `DoDelete` (bool),
  `KeyText`, `ValueText`.

### 9. CreateFileArchive (~130 lines)

**ViewModel:** `ViewModels/CreateFileArchiveViewModel.cs`

```csharp
public CreateFileArchiveViewModel(ISettingsService settingsService)
```

- Move the three radio-button state properties (`IsChecked_Binary2`,
  `IsChecked_NuFX`, `IsChecked_Zip`) and the backing `FileKind Kind`
  enum value.
- Constructor reads the last `NEW_ARC_MODE` setting via `_settingsService`
  to initialize the selected radio button.
- `OkCommand` writes the selected format via
  `_settingsService.SetEnum(AppSettings.NEW_ARC_MODE, Kind)` before closing.
- Commands: `OkCommand`, `CancelCommand`
- Output properties (read by caller after OK): `Kind` (`FileKind` enum —
  the selected format). The existing source uses `Kind` as the property
  name; keep it to minimize caller changes.

### 10. FindFile (~135 lines)

**ViewModel:** `ViewModels/FindFileViewModel.cs`

- Move search text, search options (forward/backward, case)
- Move `FindFileReq` inner class to `Models/FindFileReq.cs`.
- Expose `IObservable<FindFileReq> FindRequested` on `FindFileViewModel`
  as a `Subject<FindFileReq>`. `FindNextCommand` and `FindPrevCommand` fire
  this observable with the current search parameters. `MainViewModel`
  subscribes to `FindRequested` after opening the window to handle
  navigation.
- **`DoFindFiles` handler complexity:** The `FindRequested` subscriber in
  `MainViewModel` replaces the existing `DoFindFiles` in `MainController`.
  This handler accesses `SelectedArchiveTreeItem`, `ArchiveTreeRoot`,
  `FileList`, `PostNotification`, and several static helpers
  (`ArchiveTreeItem.SelectItem`, `DirectoryTreeItem.SelectItemByEntry`,
  `FileListItem.SelectAndView`) that currently take `MainWindow` as a
  parameter. These must be available as reactive VM properties or service
  calls by the time Phase 4B runs (established in Phases 1A and 3B).
  Verify that `ArchiveTreeRoot`, `SelectedArchiveTreeItem`, `FileList`,
  and `PostNotification` are accessible from `MainViewModel` before
  implementing the `FindRequested` handler. The `FindFileState` private
  class and `FindInTree` helper also migrate into `MainViewModel` (or a
  dedicated service).
- **Modality change:** The current code uses `ShowDialog` (modal). This
  intentionally changes to modeless via `_dialogService.ShowModeless(findFileVm)`.
  `MainViewModel` retains a reference to the `FindFileViewModel` instance
  to subscribe to `FindRequested`. `MainViewModel` subscribes to
  `findFileVm.Closed.Take(1)` to dispose the `FindRequested` subscription
  and null the field when the user closes FindFile. `MainViewModel` must
  also close the FindFile window (via `RequestCloseInteraction`) when the
  workspace closes, to avoid stale-selection errors on an empty archive
  tree. `FindFileViewModel` exposes the same `ClosedSubject` /
  `Closed` / `RequestCloseInteraction` modeless lifecycle observables
  as LogViewer (see §4 for the backing-field / public-property pattern).
- **Persistent search state:** The current code has `static` fields
  `sLastSearch` and `sCurrentArchiveOnly` that preserve the last search
  configuration across dialog invocations. Replicate using `static` fields
  on `FindFileViewModel` (simplest, matches current behavior) or by
  reading/writing via `_settingsService` (cleaner, survives process
  restart). `FindNextCommand` and `FindPrevCommand` handlers must
  **update** these static fields from the current property values before
  executing the search (equivalent to the existing `SaveConfig()` call
  in `FindPrev_Click` / `FindNext_Click`).
- Commands: `FindNextCommand`, `FindPrevCommand`, `CloseCommand`

### 11. AboutBox (~113 lines)

**ViewModel:** `ViewModels/AboutBoxViewModel.cs`

- Move version strings, legal text, runtime info
- Move `GetRuntimeDataDir()` and the `File.ReadAllText(LegalStuff.txt)`
  call into the `AboutBoxViewModel` constructor. The existing code already
  catches `Exception` and sets `LegalStuffText` to the error message on
  failure — preserve this behavior. The path-traversal helper is pure
  logic with no Avalonia dependency and belongs in the ViewModel.
- Minimal — mostly display-only properties
- Commands: `CloseCommand`
- Retain `WebsiteLink_Tapped` in code-behind — it calls a static utility
  (`CommonUtil.ShellCommand.OpenUrl`) with a hardcoded URL and has no
  testable logic to move.

### 12. AddMetadata (~103 lines)

**ViewModel:** `ViewModels/AddMetadataViewModel.cs`

```csharp
public AddMetadataViewModel(IMetadata metadata)
```

- Move key/value input, available keys list, validation
- **IBrush convention:** Replace `KeySyntaxForeground` and
  `ValueSyntaxForeground` (`IBrush`) with booleans (e.g., `IsKeyValid`,
  `IsValueValid`). Use an AXAML style trigger or value converter in the
  View to select the brush color.
- Commands: `OkCommand` (canExecute when key is valid), `CancelCommand`
- Output properties (read by caller after OK): `KeyText`, `ValueText`.

### 13. OverwriteQueryDialog (~93 lines, Actions/)

**ViewModel:** `ViewModels/OverwriteQueryViewModel.cs`

```csharp
public OverwriteQueryViewModel(CallbackFacts facts)
```

- Move file detail display properties (name, size, dates)
- Move "apply to all" checkbox state
- Commands: `OverwriteCommand`, `SkipCommand`, `CancelCommand`
  - `OverwriteCommand` sets `Result = CallbackFacts.Results.Overwrite`,
    then calls `await CloseInteraction.Handle(true)`.
  - `SkipCommand` sets `Result = CallbackFacts.Results.Skip`,
    then calls `await CloseInteraction.Handle(true)`.
  - `CancelCommand` calls `await CloseInteraction.Handle(false)`.
  - The caller reads `Result` and `UseForAll` only when
    `ShowDialogAsync` returns `true` (i.e., Overwrite or Skip).
- Uses the standard `CloseInteraction` defined in §Conversion Pattern →
  Modal Close Mechanism.
- Result: `Result` property and `UseForAll` flag read by caller

### 14. ShowText (~77 lines, Tools/)

**ViewModel:** `ViewModels/ShowTextViewModel.cs`

```csharp
public ShowTextViewModel(string displayText)
```

- Single property: `DisplayText` (matches existing AXAML binding name;
  do not rename to `Text`)
- Commands: `CloseCommand`
- **Modality varies by call site:** System Info (`Debug_ShowSystemInfo`)
  calls `_dialogService.ShowDialogAsync(new ShowTextViewModel(...))` (modal).
  Test Failures result reporting calls
  `_dialogService.ShowModeless(new ShowTextViewModel(...))` (modeless).
  Both share the same `ShowTextViewModel`.
- Simplest dialog — good first conversion for warm-up

---

## Step-by-Step Instructions

### Step 1: Convert Simple Dialogs First

Start with the simplest dialogs for quick wins and pattern practice:

1. **ShowText** → build and test
2. **AboutBox** → build and test
3. **AddMetadata** → build and test
4. **EditMetadata** → build and test
5. **CreateFileArchive** → build and test
6. **CreateDirectory** → build and test
7. **OverwriteQueryDialog** → build and test

### Step 2: Convert Medium Dialogs

8. **FindFile** → build and test
9. **ReplacePartition** → build and test
10. **EditAppSettings** → build and test
11. **EditConvertOpts** → build and test
12. **LogViewer** → build and test
13. **DropTarget** → build and test
14. **WorkProgress** → build and test (most impactful — used by many
    operations)

### Step 3: Complete Dialog Mapping Registration

Verify `RegisterDialogMappings()` in `App.axaml.cs` contains all entries:

```csharp
private void RegisterDialogMappings() {
    var ds = Services.GetRequiredService<IDialogService>();

    // Phase 4A (complex)
    ds.Register<EditSectorViewModel, EditSector>();
    ds.Register<FileViewerViewModel, Tools.FileViewer>();
    ds.Register<EditAttributesViewModel, EditAttributes>();
    ds.Register<CreateDiskImageViewModel, CreateDiskImage>();
    ds.Register<SaveAsDiskViewModel, SaveAsDisk>();
    ds.Register<TestManagerViewModel, LibTest.TestManager>();
    ds.Register<BulkCompressViewModel, LibTest.BulkCompress>();

    // Phase 4B (medium + simple)
    ds.Register<EditConvertOptsViewModel, EditConvertOpts>();
    ds.Register<EditAppSettingsViewModel, EditAppSettings>();
    ds.Register<WorkProgressViewModel, Common.WorkProgress>();
    // Modeless dialogs: LogViewer, DropTarget, FindFile, and ShowText
    // (when used modeless) use ShowModeless(), not ShowDialogAsync().
    // They still need a mapping entry.
    ds.Register<LogViewerViewModel, Tools.LogViewer>();
    ds.Register<DropTargetViewModel, Tools.DropTarget>();
    ds.Register<ReplacePartitionViewModel, ReplacePartition>();
    ds.Register<CreateDirectoryViewModel, CreateDirectory>();
    ds.Register<EditMetadataViewModel, EditMetadata>();
    ds.Register<CreateFileArchiveViewModel, CreateFileArchive>();
    ds.Register<FindFileViewModel, FindFile>();
    ds.Register<AboutBoxViewModel, AboutBox>();
    ds.Register<AddMetadataViewModel, AddMetadata>();
    ds.Register<OverwriteQueryViewModel, Actions.OverwriteQueryDialog>();
    ds.Register<ShowTextViewModel, Tools.ShowText>();
}
```

### Step 4: Retire RelayCommand

**Prerequisite:** Phase 2 (Iteration 2: Command Migration) must be complete.
`MainWindow.axaml.cs` has 51 `RelayCommand` instantiations for non-dialog
commands (e.g., `ExitCommand`, `HelpCommand`, menu commands). These are
converted to `ReactiveCommand` in Phase 2, not Phase 4B. If Phase 2 has not
yet run, skip this step — `RelayCommand.cs` cannot be retired until both
Phase 2 and Phase 4B are done.

After both phases are complete, `RelayCommand` should have zero usages.

1. Search for remaining `RelayCommand` references:
   ```
   grep -rn "RelayCommand" cp2_avalonia/
   ```
2. If none remain, delete `cp2_avalonia/Common/RelayCommand.cs`.
3. If some remain, they must be converted before retirement.

### Step 5: Final Build and Validation

1. Run `dotnet build` — verify zero errors.
2. Comprehensive test — exercise every dialog in the application:

   **Simple dialogs:**
   - Help → About
   - File → New File Archive (format selection)
   - Actions → Create Directory
   - Metadata panel → Add, Edit, Delete
   - Actions during add/extract → Overwrite query (add duplicate files)
   - Debug → System Info (ShowText)

   **Medium dialogs:**
   - Edit → Application Settings (change theme, verify persistence)
   - Actions → Find Files (modeless, search, navigate results)
   - Actions → Replace Partition
   - Debug → Show Debug Log (modeless toggle)
   - Debug → Drop Target (modeless toggle)
   - Any operation with progress (WorkProgress)
   - Import/Export with converter options (EditConvertOpts)

3. Verify modeless windows:
   - LogViewer toggles on/off correctly
   - DropTarget toggles on/off correctly
   - FindFile stays open, results navigate correctly
   - FileViewer (from 4A) supports multiple instances

---

## What This Enables

- Every dialog uses MVVM (ViewModel + thin code-behind).
- `RelayCommand` can be deleted from the project.
- `IDialogService` handles all dialog presentation uniformly.
- Dialog ViewModels are unit-testable in isolation.
- Phase 5 can extract child ViewModels from `MainViewModel`.
