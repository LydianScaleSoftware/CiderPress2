# Iteration 4A Blueprint: Complex Dialog ViewModels

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §3.3, §6 Phase 4,
> §7.14.

---

## Goal

Create ViewModels for the **complex** dialogs (500+ lines with significant
business logic). Extract logic from code-behind into separate ViewModel
classes, register the ViewModel→View mappings in `DialogService`, and convert
AXAML bindings. After this iteration, all complex dialogs follow MVVM.

---

## Prerequisites

- Iteration 3B is complete (controller dissolved, `MainViewModel` uses
  services, `IDialogService` is operational).
- All service interfaces and the DI container are in place.
- The application builds and runs correctly.
- **`IClipboardService` must include `Task SetTextAsync(string text)`.**
  This method wraps `Avalonia.Input.Platform.IClipboard.SetTextAsync` and
  is required by `EditSectorViewModel.CopyToClipboardCommand` and
  `FileViewerViewModel.CopyTextCommand`. If Phase 3A's interface definition
  omitted it, add it to `IClipboardService` and its implementation before
  starting this iteration.

---

## Target Dialogs (Phase 4A)

| Dialog | Lines | Modality | Complexity Notes |
|---|---|---|---|
| `EditSector` | ~1,194 | Modal | Hex grid, sector/block navigation, read/write, inner `SectorRow` class |
| `FileViewer` (Tools) | ~1,081 | Modal | Format conversion, magnification, text/hex/graphics tabs, export, find |
| `EditAttributes` | ~1,050 | Modal | ProDOS/HFS types, timestamps, access flags, MacZip handling |
| `CreateDiskImage` | ~580 | Modal | Filesystem selection, size validation, volume params, disk creation |
| `SaveAsDisk` | ~728 | Modal | File format options, chunk access, progress |
| `TestManager` (LibTest) | ~351 | Modal | Test runner, progress, failure browser (debug-only) |
| `BulkCompress` (LibTest) | ~308 | Modal | Compression benchmark, progress (debug-only) |

---

## Conversion Pattern (Repeated for Each Dialog)

For every dialog listed above, follow this pattern:

### A. Create the ViewModel Class

```
cp2_avalonia/ViewModels/{DialogName}ViewModel.cs
```

1. Create a class extending `ReactiveObject`.
2. Move all bindable properties from the `.axaml.cs` code-behind into the
   ViewModel, converting `INotifyPropertyChanged` → `RaiseAndSetIfChanged`.
3. Move all business logic (validation, computation, data transformation)
   into the ViewModel.
4. Move inner data classes (e.g., `EditSector.SectorRow`), enums, and
   delegates into the ViewModel file or a companion file. Convert
   `INotifyPropertyChanged` → `ReactiveObject` on data classes.
5. Add `ReactiveCommand` properties for any button actions.
6. Accept domain objects via the constructor (not View references).
7. **Validation-color properties:** Do not place `IBrush`-typed properties
   in the ViewModel (they are Avalonia view-layer types). Instead, expose a
   `bool` (e.g., `IsTrackBlockValid`) from the ViewModel and use an AXAML
   style trigger or value converter in the View to select the brush color.
8. **Imperative command invocation from code-behind:** When code-behind
   must invoke a `ReactiveCommand` (e.g., keyboard shortcut handlers),
   use the `ICommand` interface cast:
   `((ICommand)ViewModel.SomeCommand).Execute(null)`. Do **not** call
   `.Execute()` directly — `ReactiveCommand.Execute()` returns an
   `IObservable<T>` and has no effect without a subscription.

### B. Update the View (.axaml.cs)

1. Change the base class from `Window` to
   `ReactiveWindow<{DialogName}ViewModel>` (from `ReactiveUI.Avalonia`).
   This provides typed `ViewModel` access and `WhenActivated` support.
2. Remove `DataContext = this`.
3. The View's `DataContext` will be set by `DialogService.ShowDialogAsync`.
4. Register the `CloseInteraction` handler in the constructor (or
   `WhenActivated`):
   ```csharp
   this.WhenActivated(d => {
       ViewModel!.CloseInteraction.RegisterHandler(ctx => {
           Close(ctx.Input);        // ctx.Input is the bool dialog result
           ctx.SetOutput(Unit.Default);
       }).DisposeWith(d);
   });
   ```
5. Keep only:
   - `InitializeComponent()`
   - Event handlers that are purely visual (scroll, focus, drag-drop
     gesture start)
   - Window lifecycle (`Opened`, `Closing`) that delegates to the ViewModel

### C. Update the AXAML

1. Change `Binding PropertyName` to `Binding PropertyName` (unchanged if
   DataContext was already `this`).
2. Change `Click="Handler"` for buttons to
   `Command="{Binding SomeCommand}"`.
3. Remove `x:Name` references that were used only for code-behind access
   to controls — prefer bindings to ViewModel properties.

### D. Register the Mapping

In `App.axaml.cs` → `RegisterDialogMappings()`:

```csharp
ds.Register<EditSectorViewModel, EditSector>();
```

### E. Update Callers

In `MainViewModel`, change:
```csharp
// Before (legacy):
var dlg = new EditSector(chunks, mode, writeFunc, formatter);
await dlg.ShowDialog(host.GetOwnerWindow());

// After:
var vm = new EditSectorViewModel(chunks, mode, writeFunc, formatter, _dialogService, _clipboardService);
var result = await _dialogService.ShowDialogAsync(vm);
```

**Note:** Per Phase 3A, `IDialogService.ShowDialogAsync(vm)` accepts a
pre-constructed ViewModel instance. The service creates the View via the
registered factory, assigns `DataContext = vm`, and calls `ShowDialog`.
Confirm the actual signature against `Services/DialogService.cs` before
writing callers.

### F. Modal Close Protocol

ViewModels must not hold View references, so dialog dismissal uses
ReactiveUI's `Interaction<TInput, TOutput>` pattern:

1. **ViewModel** exposes:
   ```csharp
   public Interaction<bool, Unit> CloseInteraction { get; } = new();
   ```
2. **`OkCommand`** calls:
   ```csharp
   await CloseInteraction.Handle(true);
   ```
3. **`CancelCommand`** calls:
   ```csharp
   await CloseInteraction.Handle(false);
   ```
4. **View** registers a handler (in the constructor or `WhenActivated`):
   ```csharp
   this.WhenActivated(d => {
       ViewModel!.CloseInteraction.RegisterHandler(ctx => {
           Close(ctx.Input);        // ctx.Input is the bool dialog result
           ctx.SetOutput(Unit.Default);
       }).DisposeWith(d);
   });
   ```

This keeps the ViewModel ignorant of the View while providing a clean
async close signal. `DialogService.ShowDialogAsync` returns the `bool?`
result from `Window.ShowDialog`.

For dialogs that use a close-guard (e.g., EditSector's dirty check),
`CancelCommand` still calls `CloseInteraction.Handle(false)`. The
`Window_Closing` guard in code-behind handles the dirty-check logic
before allowing the window to actually close.

---

## Dialog-Specific Instructions

### 1. EditSector (~1,194 lines)

**ViewModel:** `ViewModels/EditSectorViewModel.cs`

**Constructor parameters:**
```csharp
public EditSectorViewModel(
    IChunkAccess chunks,
    SectorEditMode editMode,
    EnableWriteFunc? writeFunc,
    Formatter formatter,
    IDialogService dialogService,
    IClipboardService clipboardService)
```

All `ShowConfirmAsync` and `ShowMessageAsync` calls in the business logic
(discard-changes confirmation, write-error messages, enable-write-access
confirmation) use `_dialogService`.

**Key properties to move:**
- Sector/block navigation state (current track, sector, block number)
- Grid data: `ObservableCollection<SectorRow>` (inner class → nested class
  in ViewModel)
- Read-only flag, dirty flag
- Text encoding mode
- Status text
- Validation-color booleans: replace `TrackBlockLabelForeground` and
  `SectorLabelForeground` (`IBrush`) with `IsTrackBlockValid` and
  `IsSectorValid` (`bool`). Use AXAML style triggers to select brushes.
- The three display-base fields (`sTrackNumBase`, `sSectorNumBase`,
  `sBlockNumBase`) must remain `static` on `EditSectorViewModel` to
  preserve cross-invocation persistence of the user's decimal/hex choice.

**Inner types to migrate to `EditSectorViewModel.cs`:**
- `SectorRow` class
- `SectorEditMode` enum (referenced externally by `MainViewModel` and
  `MainWindow.axaml.cs`)
- `EnableWriteFunc` delegate (referenced by `MainViewModel`)
- `TxtConvMode` enum (referenced by `SectorRow`)
- `BlockOrderItem` class (populates block-order combobox)
- `SectorOrderItem` class (populates sector-skew combobox)

Update all external references (e.g., `EditSector.SectorEditMode` →
`EditSectorViewModel.SectorEditMode`).

**Note on `SectorRow`:** `SectorRow` does not use `RaiseAndSetIfChanged`
because its column properties (`C0`–`Cf`) write into a shared byte buffer
via `Set(col, value)`. Replace `OnPropertyChanged()` with
`this.RaisePropertyChanged(nameof(C0))` (etc.) directly in each setter.

**Output:** The ViewModel exposes `bool WritesEnabled { get; private set; }`
set to `true` inside `TryEnableWrites()`. After `ShowDialogAsync` returns,
the caller reads `vm.WritesEnabled` and conditionally reprocesses the disk
image.

**Commands:**
- `ReadSectorCommand` / `ReadBlockCommand`
- `WriteSectorCommand` / `WriteBlockCommand`
- `PrevSectorCommand` / `NextSectorCommand`
- `PrevBlockCommand` / `NextBlockCommand`
- `CopyToClipboardCommand` — formats `mBuffer` as a hex dump via
  `_formatter.FormatHexDump(mBuffer)` and calls
  `_clipboardService.SetTextAsync(dumpText)`. Bound to the Copy button in
  AXAML; the Ctrl+C keyboard handler in code-behind invokes
  `((ICommand)ViewModel.CopyToClipboardCommand).Execute(null)`.

**Retained in code-behind:**
- DataGrid cell editing commit/cancel (pure UI event handling)
- Keyboard navigation within hex grid

**Close-guard pattern:** `Window_Closing` in code-behind reads
`ViewModel.IsDirty`. If dirty, it cancels the event (`e.Cancel = true`) and
calls `ViewModel.ConfirmDiscardChangesAsync()` (which uses
`IDialogService.ShowConfirmAsync`). On confirmation, code-behind sets a
`mUserConfirmedClose` guard flag and calls `Close()`. The guard flag stays
in code-behind.

### 2. FileViewer (~1,081 lines, Tools/)

**ViewModel:** `ViewModels/FileViewerViewModel.cs`

**Constructor — uses Init pattern:**
```csharp
public FileViewerViewModel(
    object archiveOrFileSystem,
    List<IFileEntry> entries,
    int initialIndex,
    AppHook appHook,
    ISettingsService settingsService,
    IFilePickerService filePickerService,
    IClipboardService clipboardService,
    IWorkspaceService workspaceService)
```

**Key properties to move:**
- Current file entry, file index
- Conversion format list, selected format
- Display content (text, hex dump, bitmap)
- Magnification level
- Find text, find state
- Export format options
- `IsDOSRaw` — getter/setter must use `ISettingsService` (not
  `AppSettings.Global` directly)
- `MAC_ZIP_ENABLED` — read from `ISettingsService` in `ShowFile()`; do not
  access `AppSettings.Global` directly
- `bool IsOptionsBoxEnabled` — derived from `SelectedForkTab == Tab.Data`
  (the options panel is only relevant for the Data fork)
- `bool IsDOSRawEnabled` — set in `ShowFile()` based on
  `mArchiveOrFileSystem is DOS`
- `bool IsDataTabEnabled`, `bool IsRsrcTabEnabled`,
  `bool IsNoteTabEnabled` — set in `ShowFile()` to enable/disable each
  fork `TabItem`. All three are observable (`RaiseAndSetIfChanged`). AXAML
  binds each `TabItem.IsEnabled` to the corresponding property.
  `SelectEnabledTab()` reads these to pick the active tab.

**Binding properties for `ShowFile()` combobox:** The ViewModel exposes
`ObservableCollection<ConverterComboItem> ConverterList` and
`int SelectedConverterIndex` (observable). The AXAML binds
`convComboBox.ItemsSource` and `convComboBox.SelectedIndex` to these. The
code-behind's `ConvComboBox_SelectionChanged` calls
`ViewModel.OnConverterSelected()` instead of accessing the control directly.
Scroll reset (`ScrollToHome()`) stays in code-behind, triggered by a
ViewModel event or `WhenAnyValue` on `SelectedFileIndex`.

**Inner types to migrate to `FileViewerViewModel.cs`:**
- `ConverterComboItem` class (wraps `Converter` and display name for the
  conversion combobox; used by `ShowFile()`, `ConfigureControls()`, etc.)
- `DisplayItemType` enum (used by `SetDisplayType()` to drive which
  display panel is visible)
- `Tab` enum (`Unknown`, `Data`, `Rsrc`, `Note`) — used by fork-tab
  selection logic

**Fork-tab selection:** `SelectEnabledTab()` logic moves to the ViewModel.
The ViewModel exposes a `SelectedForkTab` property (type `Tab` enum,
observable via `RaiseAndSetIfChanged`). The AXAML binds
`tabControl.SelectedIndex` (or `SelectedItem` via a converter) to
`SelectedForkTab`. The `ShowTab()` method in code-behind is eliminated
in favor of the AXAML binding.

**Commands:**
- `PrevFileCommand` / `NextFileCommand`
- `ExportCommand` — calls `_filePickerService.SaveFileAsync(…)` for export
- `FindNextCommand` / `FindPrevCommand`
- `CopyTextCommand` — calls `_clipboardService.SetTextAsync(…)` for copy.
  When `SelectedForkTab == Tab.Note`, copies `_noteText` instead of the
  current converter output
- `SaveDefaultsCommand` — calls `_settingsService.SetString(settingKey,
  optStr)` and sets `IsSaveDefaultsEnabled = false`. The AXAML button binds
  to this command. `IsSaveDefaultsEnabled` is an observable property updated
  by `ShowFile()` and `SaveDefaultsCommand`.

**Fork text content delivery:** The ViewModel stores
`private IConvOutput? _curDataOutput` and
`private IConvOutput? _curRsrcOutput` after `FormatFile()` runs, and
exposes public observable properties `IConvOutput? DataOutput` and
`IConvOutput? RsrcOutput`. The code-behind subscribes via
`WhenAnyValue(x => x.DataOutput)` and applies the content: for
`FancyText` it calls `FancyTextHelper.Apply(dataForkTextEditor, fancy)`;
for `SimpleText`, `CellGrid`, or error cases it assigns
`dataForkTextEditor.Document = new TextDocument(text)`. The same pattern
applies for `rsrcForkTextEditor` using `RsrcOutput`.
`FancyTextHelper.Apply()` must remain in code-behind because it takes a
named `TextEditor` control reference.

**Note text field:** `FormatFile()` stores the rendered note text as
`private string _noteText` after computing `comboNotes.ToString()`.
`CopyTextCommand` uses `_noteText` when `SelectedForkTab == Tab.Note`.
`DoFind()` uses `_noteText` as the search corpus for the Note fork.
The code-behind populates the note text editor by subscribing to
`ViewModel.WhenAnyValue(x => x.NoteText)` and assigning
`noteTextEditor.Document = new TextDocument(value)` (AvaloniaEdit
does not support direct string binding on `Document`).

**Find subsystem:** The ViewModel handles the search logic: `DoFind(bool
forward)` searches within the pre-computed text content (stored as ViewModel
fields from `FormatFile()`), using `SelectedForkTab` to choose the correct
fork and the corresponding search corpus:
- **Note fork:** `_noteText`
- **Data fork:** extracted from `_curDataOutput` — for `FancyText` use
  `fancy.Text.ToString()`; for `SimpleText`/`CellGrid`/error use `.Text`.
  Optionally cache as `string _dataForkText` in `FormatFile()`.
- **Rsrc fork:** same extraction from `_curRsrcOutput` (optionally cached
  as `string _rsrcForkText`).

`DoFind` returns the found character offset (or -1). `FindNextCommand` /
`FindPrevCommand` invoke `DoFind` and raise a `SearchResultFound(int offset)`
event (or update an observable `FindResultOffset` property) for code-behind
to consume. The code-behind subscribes and calls `editor.Select(offset,
length)` and `editor.ScrollTo(…)` on the appropriate named editor control.
The Enter-key handler for the search box remains in code-behind and invokes
`((ICommand)ViewModel.FindNextCommand).Execute(null)`.

**Retained in code-behind:**
- Image rendering (DrawingContext operations)
- Scroll synchronization
- Keyboard shortcuts
- Find result highlighting: code-behind subscribes to ViewModel's
  `FindResultOffset` and calls `editor.Select(…)` / `editor.ScrollTo(…)`
- **Magnification sizing:** `previewImage.Width` / `previewImage.Height`
  are set from code-behind. The ViewModel exposes `double MagnificationTick`
  (observable, bound to `magnificationSlider.Value` in AXAML; `Slider.Value`
  is `double` — cast to `int` inside `ConfigureMagnification()`),
  `double PreviewImageWidth`, and `double PreviewImageHeight` (observable,
  bound to `previewImage.Width` / `previewImage.Height` in AXAML).
  `ConfigureMagnification()` moves to the ViewModel and reads
  `MagnificationTick` instead of `magnificationSlider.Value`, and writes
  `PreviewImageWidth` / `PreviewImageHeight` instead of named-control
  dimensions. `MagnificationSlider_ValueChanged` is eliminated — the AXAML
  binding on `MagnificationTick` drives recalculation via `WhenAnyValue`.
- **ConfigOptCtrl dynamic options panel:** `CreateControlMap()` and
  `ConfigureControls()` remain in code-behind because they take direct
  control references (`RadioButton[]`, `TextBox`). `UpdateOption()` forwards
  to the ViewModel's `SetConvOption(string tag, string value)` method which
  updates `mConvOptions` and re-formats. The code-behind reads the
  converter's `OptionDefs` to configure the static named controls.

**`IDisposable`:** `FileViewerViewModel` must implement `IDisposable`.
`Dispose()` closes open streams, deletes temp files (`DeleteTempFiles()`),
and releases any other resources. It also disposes the
`WorkspaceModified` subscription (stored in a `mWorkspaceModifiedSub` field).
The code-behind's `OnClosed` calls `ViewModel.Dispose()`.
`FindStaleTempFiles()` moves to the ViewModel class
as a static utility (independent of instance lifetime).

**Source-archive-modified notification:** The constructor takes
`IWorkspaceService` and subscribes to `WorkspaceModified`:

```csharp
mWorkspaceModifiedSub = workspaceService.WorkspaceModified
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(_ => IsSourceModifiedWarningVisible = true);
```

`IsSourceModifiedWarningVisible` is a `bool` observable property
(`RaiseAndSetIfChanged`). The AXAML binds a warning banner's
`IsVisible` to this property. The viewer does not auto-refresh — the
user decides whether to close or continue viewing stale data.

**Note:** FileViewer remains **modal** in Phase 4A (matching current behavior).
Conversion to modeless and `IViewerService` integration is deferred to Phase 6
(per MVVM_Notes.md §6).

### 3. EditAttributes (~1,050 lines)

**ViewModel:** `ViewModels/EditAttributesViewModel.cs`

**Constructor parameters:**
```csharp
public EditAttributesViewModel(
    object archiveOrFileSystem,
    IFileEntry entry,
    IFileEntry adfEntry,
    FileAttribs initialAttribs,
    bool isReadOnly)
```

**Key properties to move:**
- File name, file type, aux type
- ProDOS type combo items
- HFS file type, creator
- Timestamps (create, modify, access)
- Access flags (read, write, delete, rename, etc.)
- Comment text
- Validation error messages
- Validation-color booleans: replace `SyntaxRulesForeground`,
  `UniqueNameForeground`, `ProAuxForeground`, `HFSTypeForeground`,
  `HFSCreatorForeground`, `CreateWhenForeground`, `ModWhenForeground`
  (`IBrush`) with corresponding `bool` properties (e.g., `IsSyntaxValid`).
  Use AXAML style triggers to select brushes.
- Store the filename validation function as `Func<string, bool> _isValidFunc`
  in the ViewModel, initialized from `arc.IsValidFileName` /
  `fs.IsValidFileName` / `fs.IsValidVolumeName` in the constructor (same
  logic as the current View constructor). `CheckFileNameValidity()` uses it.
- `SelectedProTypeItem` (`ProTypeListItem?`, observable) — initialized in
  the constructor by scanning `ProTypeList` for the entry matching
  `NewAttribs.FileType`. Setter updates `NewAttribs.FileType` and triggers
  validation. Bind to `proTypeCombo.SelectedItem` in AXAML. Eliminates
  `Loaded_FileType()` and `ProTypeCombo_SelectionChanged`.

**Inner types to migrate to `EditAttributesViewModel.cs`:**
- `ProTypeListItem` class (item type for ProDOS type combobox `ItemsSource`)

**Commands:**
- `OkCommand` (with canExecute based on validation) — calls
  `await CloseInteraction.Handle(true)` to dismiss the dialog.
- `CancelCommand` — calls `await CloseInteraction.Handle(false)`.

**Output:** `ShowDialogAsync<EditAttributesViewModel>` returns `bool?`. The
caller reads `vm.NewAttribs` only when the return value is `true`. `OkCommand`
closes the dialog via `CloseInteraction` and the ViewModel retains
`NewAttribs`; `CancelCommand` closes without modifying it.

**Retained in code-behind:**
- The `OnOpened` handler: calls `fileNameTextBox.SelectAll()` and
  `fileNameTextBox.Focus()`. These are UI-only operations.
  (`Loaded_FileType()` is eliminated — replaced by the bound
  `SelectedProTypeItem` property on the ViewModel.)

### 4. CreateDiskImage (~580 lines)

**ViewModel:** `ViewModels/CreateDiskImageViewModel.cs`

**Constructor parameters:**
```csharp
public CreateDiskImageViewModel(
    AppHook appHook,
    IFilePickerService filePickerService,
    ISettingsService settingsService,
    IDialogService dialogService)
```

Replace all `AppSettings.Global.GetEnum/SetEnum/GetString/…` calls with
the corresponding `ISettingsService` methods. Replace `ShowErrorAsync(…)`
calls in `OkCommand` with `await _dialogService.ShowMessageAsync(message,
"Error")`.

**Key properties to move:**
- Disk size selection (list of size options)
- Filesystem selection (list of supported filesystems per size)
- File type / container format
- Volume name, volume number
- Custom size parameters
- Reserve boot tracks flag
- Validation state
- Validation-color booleans: replace `SizeDescForeground` and
  `SizeLimitForeground` (`IBrush`) with `bool` properties. Use AXAML style
  triggers to select brushes.

**Inner types to migrate:**
- `DiskSizeValue` enum — move to `Models/DiskImageTypes.cs` (referenced by
  `SaveAsDiskViewModel`; must be accessible cross-dialog)
- `FileTypeValue` enum — move to `Models/DiskImageTypes.cs` (referenced by
  `SaveAsDisk` via using alias `CreateDiskImage.FileTypeValue`)

Update `SaveAsDisk`'s using alias and all other references to point to
`cp2_avalonia.Models.DiskImageTypes`.

**Commands:**
- `OkCommand` (creates disk, returns result path via dialog result) —
  calls `_filePickerService.SaveFileAsync(…)` instead of the static
  `SelectOutputFile(TopLevel, …)` method. The file-type/extension logic
  moves into the ViewModel. On success, calls
  `await CloseInteraction.Handle(true)` to dismiss the dialog.
- `CancelCommand` — calls `await CloseInteraction.Handle(false)`.

**Cursor management:** `OkCommand.IsExecuting` (a built-in `ReactiveCommand`
`IObservable<bool>`) drives cursor changes from code-behind: the View
subscribes and sets `this.Cursor` accordingly.

**Output:** The ViewModel exposes a `CreatedDiskPath` property set after
successful creation. The caller checks that `ShowDialogAsync` returned
`true` then reads `vm.CreatedDiskPath`.

### 5. SaveAsDisk (~728 lines)

**ViewModel:** `ViewModels/SaveAsDiskViewModel.cs`

**Constructor parameters:**
```csharp
public SaveAsDiskViewModel(
    object currentWorkObject,
    IChunkAccess chunks,
    Formatter formatter,
    AppHook appHook,
    IFilePickerService filePickerService,
    ISettingsService settingsService,
    IDialogService dialogService)
```

Replace all `AppSettings.Global` calls with `ISettingsService` methods.
Replace all `PlatformUtil.ShowMessageAsync(this, message, title)` calls
in `OkCommand` with `await _dialogService.ShowMessageAsync(message, title)`.

**Key properties to move:**
- File format selection (list of available output formats)
- Chunk access info (sector count, block count)
- Estimated output size
- Selected format's capabilities

**Commands:**
- `OkCommand` — calls `_filePickerService.SaveFileAsync(…)` instead of
  `CreateDiskImage.SelectOutputFile(this, …)`. Duplicate or refactor the
  file-type/extension logic into the ViewModel (or a shared helper in
  `Models/DiskImageTypes.cs`). On success, calls
  `await CloseInteraction.Handle(true)` to dismiss the dialog.
- `CancelCommand` — calls `await CloseInteraction.Handle(false)`.

`CopyDisk()` (`internal static`) moves to `SaveAsDiskViewModel` as a
`private static` method (or to `Models/DiskImageTypes.cs`). No external
callers need updating — it is only referenced from `CreateImage()` /
`OkCommand`.

**Cursor management:** Same pattern as CreateDiskImage — subscribe to
`OkCommand.IsExecuting` in code-behind to drive cursor changes.

**Output:** The ViewModel exposes a `PathName` property set after successful
creation. The caller checks that `ShowDialogAsync` returned `true` then
reads `vm.PathName`.

### 6. TestManager (~351 lines, LibTest/, debug-only)

**ViewModel:** `ViewModels/TestManagerViewModel.cs`

ViewModel goes in `ViewModels/` following the standard placement rule, even
though the corresponding view is in `LibTest/`.

Lower priority — debug-only dialog. Convert following the same pattern.

**Constructor parameters:**
```csharp
public TestManagerViewModel(string testLibName, string testIfaceName)
```
No service injections needed — the dialog has no file pickers, clipboard
use, or nested dialogs.

**Background work:** The current code uses `BackgroundWorker` (with
`WorkerReportsProgress` and `WorkerSupportsCancellation`). **Retain
`BackgroundWorker` inside the ViewModel** rather than converting to
`async ReactiveCommand`. The worker's `DoWork`, `ProgressChanged`, and
`RunWorkerCompleted` handlers move into the ViewModel. `ProgressChanged`
updates observable properties that the View binds to.

**Key properties to move:**
- `bool IsNotRunning` — enables/disables Run button
- `string RunButtonLabel` — toggles "Run Test" / "Cancel"
- `ObservableCollection<TestRunner.TestResult> OutputItems` — failure list
  for the ComboBox
- `bool IsOutputSelectEnabled` — enables ComboBox
- `bool IsOutputRetained` — retain-output checkbox
- Progress text (formatted by `ProgressTextTransformer`)
- `List<TestRunner.TestResult> mLastResults` — full results (private field)
- `string SelectedOutputText` — observable; displays the formatted
  exception chain for the selected failure. AXAML binds
  `outputTextBox.Text` to this property.

**Colored progress delivery:** The ViewModel exposes an
`IObservable<(string text, Color color)> ProgressAppended` (or an event)
that the `ProgressChanged` handler pushes `(text, color)` pairs into.
Code-behind subscribes and calls `AppendColoredText(text, color)` on
`progressTextEditor`. The `mProgressTransformer`
(`DocumentColorizingTransformer`) and its `AddSpan()` logic remain in
code-behind. `ResetDialog()` is triggered by a ViewModel
`ResetRequested` event; code-behind clears
`progressTextEditor.Document.Text` and calls
`mProgressTransformer.Clear()`.

**Output selection:** The ViewModel exposes
`OnOutputSelectChanged(int index)` which reads `mLastResults[index]`,
formats the exception chain, and sets `SelectedOutputText`.
Code-behind's `OutputSelectComboBox_SelectedIndexChanged` calls
`ViewModel.OnOutputSelectChanged(e.SelectedIndex)`. When all tests
pass, the ViewModel sets `SelectedOutputText` to the "all passed"
message directly in `RunWorkerCompleted`.

**Commands:**
- `RunCancelCommand` — starts or cancels the `BackgroundWorker`
- `CloseCommand` — calls `await CloseInteraction.Handle(false)` to
  dismiss the dialog.

**Retained in code-behind:**
- `Window_Closing` guard (cancels worker if busy)
- `AppendColoredText()` — receives `(text, color)` from ViewModel's
  `ProgressAppended` observable and calls `progressTextEditor.Document.Insert`
  + `mProgressTransformer.AddSpan()` + auto-scroll
- `OutputSelectComboBox_SelectedIndexChanged` — forwards to
  `ViewModel.OnOutputSelectChanged(index)`

### 7. BulkCompress (~308 lines, LibTest/, debug-only)

**ViewModel:** `ViewModels/BulkCompressViewModel.cs`

ViewModel goes in `ViewModels/` (same rule as TestManager).

Lower priority — debug-only dialog.

**Constructor parameters:**
```csharp
public BulkCompressViewModel(AppHook appHook, IFilePickerService filePickerService)
```
`IFilePickerService` is needed for the "Choose File" button.

**Background work:** Same `BackgroundWorker` pattern as TestManager.
**Retain `BackgroundWorker` inside the ViewModel.**

**Key properties to move:**
- `bool CanStartRunning` — enables Run button (true when `PathName` is set)
- `string PathName` — file/directory path (bound to TextBox)
- `string RunButtonLabel` — "Run Test" / "Cancel"
- `string ProgressMsg` — progress status text
- `string LogText` — accumulates `ProgressMessage.Text` values during the
  run; the code-behind subscribes to `WhenAnyValue(x => x.LogText)` and
  sets `logTextBox.Text = value` and `logTextBox.CaretIndex = value.Length`
- `bool CanChooseFile` — set to `false` when the `BackgroundWorker` starts
  and `true` when `RunWorkerCompleted` fires; AXAML binds
  `chooseFileButton.IsEnabled` to this property
- `CompressionFormat SelectedCompressionFormat` — public observable
  property (initialized to `CompressionFormat.NuLZW2` in the constructor).
  Each `RadioButton.IsChecked` is bound via a value converter in AXAML
  (e.g., `EnumToBoolConverter`) so the radio group drives the property
  directly without code-behind forwarding.

**Commands:**
- `ChooseFileCommand` — calls `_filePickerService.OpenFileAsync(…)` and
  sets `PathName`
- `RunCancelCommand` — starts or cancels the `BackgroundWorker`
- `CloseCommand` — calls `await CloseInteraction.Handle(false)` to
  dismiss the dialog.

**Retained in code-behind:**
- `Window_Closing` guard (cancels worker if busy)

---

## Step-by-Step Instructions

### Step 1: Create the `ViewModels/` Directory

```
cp2_avalonia/ViewModels/
```

(This may already exist from Iteration 1A if `MainViewModel.cs` was placed
there.)

### Step 2: Convert EditSector

Follow the conversion pattern (A–E above). This is the most complex dialog.

1. Create `ViewModels/EditSectorViewModel.cs`
2. Move `SectorRow`, `SectorEditMode`, `EnableWriteFunc`, `TxtConvMode`,
   `BlockOrderItem`, and `SectorOrderItem` into the ViewModel file
3. Move all properties and business logic
4. Create commands for navigation and read/write
5. Strip code-behind to UI-only handlers
6. Register mapping
7. Update caller in `MainViewModel.EditBlocksSectors()`
8. Update all external references to migrated types
9. **Build and test:** Open a disk image → Actions → Edit Sectors

### Step 3: Convert FileViewer

1. Create `ViewModels/FileViewerViewModel.cs`
2. Move conversion logic, file entry navigation, format selection
3. Keep image rendering in code-behind
4. Register mapping
5. Update caller in `MainViewModel.ViewFiles()`
6. **Build and test:** Select files → Actions → View Files

### Step 4: Convert EditAttributes

1. Create `ViewModels/EditAttributesViewModel.cs`
2. Move all attribute properties and validation
3. Register mapping
4. Update caller in `MainViewModel.EditAttributes()` and
   `MainViewModel.EditDirAttributes()`
5. **Build and test:** Select file → Actions → Edit Attributes

### Step 5: Convert CreateDiskImage

1. Create `ViewModels/CreateDiskImageViewModel.cs`
2. Move size/filesystem/format selection logic
3. Move `DiskSizeValue` and `FileTypeValue` enums to `Models/DiskImageTypes.cs`
4. Register mapping
5. Update caller in `MainViewModel.NewDiskImage()`
6. **Build and test:** File → New Disk Image

### Step 6: Convert SaveAsDisk

1. Create `ViewModels/SaveAsDiskViewModel.cs`
2. Move format selection logic
3. Move `CopyDisk()` (static, ~45 lines) to `SaveAsDiskViewModel` as a
   `private static` helper — it is referenced only from `OkCommand`
4. Register mapping
5. Update caller in `MainViewModel.SaveAsDiskImage()`
6. **Build and test:** Actions → Save As Disk Image

### Step 7: Convert TestManager and BulkCompress (debug-only)

1. Create `TestManagerViewModel` — move `BackgroundWorker` + handlers,
   observable properties, and `RunCancelCommand` into the ViewModel
2. Create `BulkCompressViewModel` — same pattern; wire `ChooseFileCommand`
   to `IFilePickerService`
3. Register mappings for both in `App.axaml.cs`
4. Update callers in debug commands
5. **Build and test:** Debug menu items

### Step 8: Final Build and Validation

1. Run `dotnet build` — verify zero errors.
2. Systematically test every converted dialog:
   - EditSector: navigate sectors, read/write, hex editing
   - FileViewer: browse files, switch formats, magnify, export, find
   - EditAttributes: change name/type/dates, save, verify
   - CreateDiskImage: all filesystem types, custom sizes
   - SaveAsDisk: all format options
   - TestManager: run tests, verify results
   - BulkCompress: run benchmark

---

## What This Enables

- All complex dialogs are testable in isolation (ViewModel unit tests).
- `IDialogService` handles all dialog presentation.
- Phase 4B converts remaining simple/medium dialogs.
