# Iteration 3B Blueprint: Dissolve MainController into ViewModel + Services

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 Phase 3, §7.13–
> §7.24.

---

## Goal

Merge all logic from `MainController.cs` (~2,700 lines) and
`MainController_Panels.cs` (~1,186 lines) into `MainViewModel`, service
implementations, and helper classes. Delete both controller files. After this
iteration, `MainWindow` is a thin View with no controller reference, and all
commands execute through the ViewModel and services.

**This is the largest and most complex phase.** Work method-by-method, building
and testing frequently.

---

## Prerequisites

- Iteration 3A is complete (all service interfaces/implementations exist,
  DI container is configured, services are injected into `MainViewModel`).
- All commands on `MainViewModel` currently delegate to `mController`.
- The application builds and runs correctly.

---

## Strategy

Move methods in dependency order — start with leaf utilities that have no
downstream callers, then work upward to the commands that call them.

### Migration Destination Rules

| Controller Method Category | Destination |
|---|---|
| Business logic (add, extract, delete, test, edit, move) | `MainViewModel` private/internal methods |
| WorkTree lifecycle (open, close, populate trees) | `IWorkspaceService` → `WorkspaceService` |
| Dialog creation (new DialogName → ShowDialog) | Replace with `_dialogService.ShowDialogAsync<TVM>(vm)` |
| File picker calls (StorageProvider.*) | Replace with `_filePickerService.OpenFileAsync(...)` etc. |
| Clipboard operations | Replace with `_clipboardService.*` |
| Settings load/save/apply | Replace with `_settingsService.*` |
| State query properties (CanWrite, IsFileOpen, ...) | Already on `MainViewModel` (from Iteration 1A) |
| UI wiring (populate trees, lists, info panel) | `MainViewModel` methods (Phase 5 extracts to child VMs) |
| Navigation (tree selection handlers) | `MainViewModel` methods |
| Window lifecycle (WindowLoaded, WindowClosing) | Split: init → `MainViewModel.Initialize()`, cleanup → `MainViewModel.Shutdown()` |

---

## Step-by-Step Instructions

### Step 0a: Extract `AutoOpenDepth` Enum

`AutoOpenDepth` is currently a nested enum inside `MainController`
(`MainController.AutoOpenDepth`). It is referenced by `IWorkspaceService.OpenAsync()`
and `WorkspaceService.OpenAsync()`. It must be extracted to a standalone file
**before** Step 1 so that the service compiles independently of `MainController`.

1. Create `cp2_avalonia/Models/AutoOpenDepth.cs`:

```csharp
// cp2_avalonia/Models/AutoOpenDepth.cs
namespace cp2_avalonia.Models;

/// <summary>
/// Depth limit for automatic sub-archive opening.
/// Extracted from MainController for use by IWorkspaceService.
/// </summary>
public enum AutoOpenDepth {
    Unknown = 0,
    Shallow,
    SubVol,
    Max
}
```

2. Add `using cp2_avalonia.Models;` to:
   - `IWorkspaceService.cs`
   - `WorkspaceService.cs`
   - `MainViewModel.cs`
   - Any other file that references `AutoOpenDepth`

3. In `MainController.cs`, replace the nested enum with:
   ```csharp
   // AutoOpenDepth enum extracted to cp2_avalonia/Models/AutoOpenDepth.cs
   ```
   Add `using cp2_avalonia.Models;` so existing controller code still compiles
   until the controller is deleted in Step 7.

4. Build and verify zero errors.

### Step 0b: WorkProgress Dialog Strategy for Phase 3B

`WorkProgressViewModel` is a **Phase 4B deliverable** and does not exist in
Phase 3B. Groups A, C, and E methods that show a `WorkProgress` dialog must
continue using the existing `WorkProgress` class directly during Phase 3B.

**Phase 3B rule for progress dialogs:**
- Keep `new WorkProgress(parentWindow, prog, isIndeterminate)` calls as-is
  in migrated VM methods.
- The VM obtains the parent `Window` via `IDialogHost.GetParentWindow()`
  (already available from Phase 3A).
- Do **not** attempt `_dialogService.ShowDialogAsync<WorkProgressViewModel>(...)`
  — that type does not exist yet.

**Phase 4B migration note:** When `WorkProgressViewModel` is created in Phase
4B, revisit every `new WorkProgress(...)` call site in `MainViewModel` and
replace with `await _dialogService.ShowDialogAsync<WorkProgressViewModel>(vm)`.
The Step 3 group tables below use the notation "WorkProgress dialog" to mean
the existing `WorkProgress` class in Phase 3B.

### Step 0c: Define `IViewActions` Interface

`IViewActions` is referenced by migrated ViewModel code in Steps 3, 4, and 5.
It must be defined **before** any of those steps begin.

1. Create `cp2_avalonia/IViewActions.cs`:

```csharp
// cp2_avalonia/IViewActions.cs
namespace cp2_avalonia;

using System.Collections.Generic;

/// <summary>
/// Interface for view-level operations that cannot be achieved through
/// data binding alone (scroll, focus, cursor, native menu, multi-select).
/// Implemented by MainWindow; passed to MainViewModel at construction.
/// </summary>
public interface IViewActions {
    // --- Scroll/Focus (genuinely view-level; cannot be done via bindings) ---
    void ScrollFileListTo(object item);
    void ScrollFileListToTop();
    void ScrollDirectoryTreeToTop();
    void FocusFileList();
    void SetFileListSelectionFocus(int index);

    // --- Multi-select (DataGrid multi-select is not bindable in Avalonia) ---
    void SelectAllFileListItems();
    void SetFileListSelection(IList<FileListItem> items);

    // --- Toast/Notification (timer animation requires imperative control) ---
    void ShowToast(string message, bool success);

    // --- Cursor (direct Window.Cursor manipulation) ---
    void SetCursorBusy(bool busy);

    // --- Recent files menu (native platform menu construction) ---
    void PopulateRecentFilesMenu();
}
```

2. `MainWindow` implements `IViewActions`:
```csharp
public partial class MainWindow : Window, IDialogHost, IViewActions {
    // ... implement each method ...
}
```

3. Wire into `MainViewModel` constructor:
```csharp
private readonly IViewActions mViewActions;

public MainViewModel(
    IDialogHost dialogHost,
    IViewActions viewActions,
    IWorkspaceService workspaceService,
    ISettingsService settingsService,
    IClipboardService clipboardService,
    IFilePickerService filePickerService) {
    mViewActions = viewActions;
    // ...
}
```

4. `MainWindow` passes `this` at construction:
```csharp
var vm = new MainViewModel(
    this,    // IDialogHost
    this,    // IViewActions
    App.Services.GetRequiredService<IWorkspaceService>(),
    ...);
DataContext = vm;
```

5. Build and verify zero errors.

### Step 1: Implement `WorkspaceService`

Create the concrete implementation of `IWorkspaceService`:

```csharp
// cp2_avalonia/Services/WorkspaceService.cs
namespace cp2_avalonia.Services;

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommonUtil;
using DiskArc;
using AppCommon;

public class WorkspaceService : IWorkspaceService {
    private readonly ISettingsService _settings;

    public WorkTree? WorkTree { get; private set; }
    public bool IsFileOpen => WorkTree != null;
    public string WorkPathName { get; private set; } = string.Empty;

    public Formatter Formatter { get; private set; }
    public AppHook AppHook { get; }
    public ObservableCollection<string> RecentFilePaths { get; } = new();

    public DebugMessageLog DebugLog { get; }

    public WorkspaceService(ISettingsService settings) {
        _settings = settings;
        DebugLog = new DebugMessageLog();
        AppHook = new AppHook(DebugLog);
        Formatter = new Formatter(new Formatter.FormatConfig());
    }

    public async Task<WorkTree> OpenAsync(string path, bool readOnly, AutoOpenDepth depth) {
        // Move logic from MainController.DoOpenWorkFile:
        // - Set WorkPathName, update RecentFilePaths
        // - (Tree population stays in MainViewModel for now)
        // NOTE: WorkTree is NOT constructed here directly. The actual
        // WorkTree construction happens inside OpenProgress.DoWork() on a
        // background thread managed by WorkProgress. Two viable patterns:
        //
        // Pattern A (preferred): OpenAsync calls OpenProgress.DoWork()
        //   on Task.Run(), returning the completed WorkTree. The VM
        //   command body handles the WorkProgress dialog wrapper.
        //
        // Pattern B: The VM command body creates the WorkTree via
        //   WorkProgress/OpenProgress and calls AttachWorkTree(WorkTree)
        //   to register state.
        throw new NotImplementedException("Move from MainController.DoOpenWorkFile");
    }

    public bool Close() {
        // Move logic from MainController.CloseWorkFile:
        // - Dispose WorkTree, set null
        // - Clear WorkPathName
        throw new NotImplementedException("Move from MainController.CloseWorkFile");
    }
}
```

**Migration steps — move these methods from MainController:**

1. `DoOpenWorkFile` core logic (lines ~139–189) → `WorkspaceService.OpenAsync()`
   - The WorkTree is constructed inside `OpenProgress.DoWork()` which runs
     on a background thread managed by `WorkProgress`. Use Pattern A or B
     (described in the code skeleton above) to separate the service from
     the progress dialog.
   - Keep: path recording, depth limiting
   - Keep: `DepthLimit()` as a private static helper

2. `CloseWorkFile` core logic (lines ~190–215) → `WorkspaceService.Close()`
   - Strip UI cleanup (clearing trees/lists stays in ViewModel)
   - Keep: WorkTree disposal
   - **Clipboard cleanup stays in VM:** `WorkspaceService` has no access to
     `IClipboardService`. The VM's own `CloseWorkFile()` must call
     `await _clipboardService.ClearIfPendingAsync()` immediately after
     `_workspaceService.Close()` returns.

3. `UpdateRecentFilesList` (line ~289) → `WorkspaceService` private method
4. `UnpackRecentFileList` (line ~314) → `WorkspaceService` constructor/Load
5. `UpdateRecentLinks()` → `MainViewModel`. Migrate property assignments
   as VM properties (`RecentFilePath1`, `RecentFileName1`, etc.), then call
   `mViewActions.PopulateRecentFilesMenu()` at the end of the method body
   for the native menu update (imperative; cannot be driven by binding).
6. `UpdateTitle` (line ~297) → `MainViewModel` (UI binding concern)
7. `OpenRecentFile(int)` (line ~376) → `MainViewModel` (accesses `RecentFilePaths`)
8. `DropOpenWorkFile(string)` (line ~142) → `MainViewModel` (drag-drop open path)
9. `ShowFileError(string)` (line ~498) → Replace with `_dialogService.ShowMessageAsync()`

**`CanWrite` note:** Do **not** expose `CanWrite` on `WorkspaceService`. The
controller's `CanWrite` (MCP line 112) returns whether the *currently selected
archive tree node* is writable (`arcTreeSel.WorkTreeNode.IsReadOnly == false`),
which differs from file-level read-only status. Keep `CanWrite` as a computed
property on `MainViewModel` that reads from
`SelectedArchiveTreeItem.WorkTreeNode.IsReadOnly`.

**Remove `CanWrite` from interface:** The Phase 3A `IWorkspaceService` interface
contains `bool CanWrite { get; }`. Remove it from both `IWorkspaceService` and
the `WorkspaceService` implementation as part of Step 1. `CanWrite` is a
computed property on `MainViewModel` only.

**Add `DebugLog` to interface:** The Phase 3A `IWorkspaceService` interface does
not include `DebugLog`. Add `DebugMessageLog DebugLog { get; }` to
`IWorkspaceService` so that the Group F `Debug_ShowDebugLog()` migration can
access it via `_workspaceService.DebugLog`.

**Register in DI container (App.axaml.cs):**
```csharp
sc.AddSingleton<IWorkspaceService>(
    sp => new WorkspaceService(sp.GetRequiredService<ISettingsService>()));
```

**Add to MainViewModel constructor:**
```csharp
private readonly IWorkspaceService _workspaceService;

public MainViewModel(..., IWorkspaceService workspaceService) {
    _workspaceService = workspaceService;
    // ...
}
```

### Step 2: Move Settings Logic

Move from `MainController`:
- `LoadAppSettings()` → call `_settingsService` methods in
  `MainViewModel.Initialize()`
- `SaveAppSettings()` → call `_settingsService.Save()` in
  `MainViewModel.Shutdown()`
- `ApplyAppSettings()` → `MainViewModel.ApplySettings()` (reads via
  `_settingsService`, sets VM properties for panel sizes, column widths, etc.)

**`PublishSideOptions()` elimination:** `MainWindow.PublishSideOptions()` is
called by `ApplyAppSettings()` to raise `PropertyChanged` for all options-panel
properties. After migration, this is redundant — the VM subscribes to
`_settingsService.SettingChanged` and raises `PropertyChanged` for each
affected VM property directly via `RaiseAndSetIfChanged`. Delete
`PublishSideOptions()` when `ApplySettings()` is migrated.

**`WindowPlacement.Restore(mMainWin, ...)`** (also in `ApplyAppSettings()`)
stays in code-behind — `MainWindow.Window_Loaded` calls it directly using the
settings values read from `_settingsService`. This is a pure View concern.

**`mIsFirstApplySettings`** (MC line 451): One-shot boolean guard. The
window-placement restore and `LeftPanelWidth` restoration in
`ApplyAppSettings()` run only when this flag is true, then it is set to false.
Without it, every subsequent `ApplySettings()` call (e.g., from the settings
dialog Apply button) would snap the window back to saved position. Carry to
VM as `private bool mIsFirstApplySettings = true`.

**Save-side `mMainWin` accesses:** `SaveAppSettings()` (MC line 424) reads
`WindowPlacement.Save(mMainWin)` and `mMainWin.LeftPanelWidth` before saving.
These reads also stay in code-behind — the `Window_Closing` handler (or
`Shutdown()` caller in code-behind) must read `WindowPlacement.Save(this)`
and `LeftPanelWidth`, store them via `_settingsService.Set*(...)`, and then
call `vm.Shutdown()` which invokes `_settingsService.Save()`.

**`EditAppSettings()` (MC line 2240):** Move to `MainViewModel`. This method
uses a `SettingsApplied` event that fires each time the user clicks **Apply**
without closing the dialog (live preview). The generic
`_dialogService.ShowDialogAsync<T>()` pattern does not cover this.
`EditAppSettingsViewModel` must expose an `ApplyRequested` observable (or
`Action` callback). `MainViewModel.EditAppSettings()` subscribes before
showing the dialog and calls `ApplySettings()` on each emission.

### Step 3: Move Business-Logic Methods into MainViewModel

Move these groups of methods, converting controller patterns as you go:

**Pattern for each method:**
1. Copy the method body from `MainController[_Panels].cs` to `MainViewModel`.
2. Replace `mMainWin.PropertyName` → `this.PropertyName` (properties are
   already on the ViewModel from Iteration 1A).

**VM-owned collection properties:** The following `ObservableCollection<T>`
properties must live on `MainViewModel` (not `MainWindow`) so that Group G
population methods can use `this.FileList`, etc., and AXAML DataGrid/TreeView
bindings resolve against the `DataContext`:
- `FileList` — `ObservableCollection<FileListItem>`
- `ArchiveTreeRoot` — `ObservableCollection<ArchiveTreeItem>`
- `DirectoryTreeRoot` — `ObservableCollection<DirectoryTreeItem>`

If these already moved in Iteration 1A, confirm they are on the VM. If not,
move them now. Once on the VM, remove `ClearTreesAndLists()` from
`IViewActions` — the VM clears its own collections directly
(`FileList.Clear()`, etc.). Any remaining view-only clearing (e.g.,
`IsFullListEnabled = false`) can stay in code-behind if needed.
3. Replace `new DialogName(mMainWin, ...) → await ShowDialog(mMainWin)`
   with `await _dialogService.ShowDialogAsync<TViewModel>(vm)`.
4. Replace `TopLevel.GetTopLevel(mMainWin).StorageProvider.*` with
   `await _filePickerService.*`.
5. Replace `AppSettings.Global.Get/Set*(...)` with
   `_settingsService.Get/Set*(...)`.
6. Remove `mMainWin.` prefix from property accesses that are now `this.`
7. Keep `async Task` signatures; convert `async void` to `async Task`.

**Private dialog helpers:** `ShowMessageAsync(string, string)` (MC line 2644)
and `ShowConfirmAsync(string, string)` (MC line 2678) are private ad-hoc dialog
implementations. They are called ~10 times across Groups A, B, C, and D. Delete
each helper at the time its consuming method is migrated; replace calls within
VM methods with `await _dialogService.ShowMessageAsync(...)` and
`await _dialogService.ShowConfirmAsync(...)`.

**Toast notifications:** `mMainWin.PostNotification(msg, success)` is called
from ~8 places across Groups A, C, and E (after Add, Delete, Save As Disk
Image, Replace Partition, etc.). Per MVVM_Notes.md §7.5, replace with VM
properties `ToastMessage` / `ToastIsSuccess` / `ToastIsVisible`. Add
`ShowToast(string msg, bool success)` to `IViewActions` if the
`DispatcherTimer` animation must remain in code-behind, or implement a
timer-based approach in the VM. Either way, all `PostNotification` calls
become `ShowToast(...)` or VM property sets.

**Group A — File Operations (add, extract, delete, test, move):**

| Method | Notes |
|---|---|
| `AddFiles()` | Uses file picker → `_filePickerService.OpenFilesAsync()` |
| `ImportFiles()` | Same as AddFiles but with import converter spec |
| `HandleAddImport()` | Core add/import logic; calls `AddPaths()` |
| `AddPaths()` | Bulk add with WorkProgress dialog |
| `ConfigureAddOpts()` | Settings → `_settingsService` |
| `GetImportSpec()` | Settings lookup |
| `ExtractFiles()` | Folder picker → `_filePickerService.OpenFolderAsync()` |
| `ExportFiles()` | Same with export spec |
| `HandleExtractExport()` | Core extract/export logic with WorkProgress |
| `GetExportSpec()` | Settings lookup |
| `GetDefaultExportSpecs()` | Static helper, stays as-is |
| `DeleteFiles()` | WorkProgress dialog |
| `TestFiles()` | WorkProgress, ShowText for report |
| `MoveFiles()` | WorkProgress dialog. **Multi-select rebuild:** After moving entries, `MoveFiles()` clears the DataGrid selection and re-selects the moved items. Add `void SetFileListSelection(IList<FileListItem> items)` to `IViewActions` (clears existing selection and selects the provided items). The migrated VM calls `mViewActions.SetFileListSelection(rebuiltItems)` after reconstructing moved `FileListItem` objects. |
| `TryOpenNewSubVolumes()` | Called at end of `AddPaths()` and `PasteOrDrop()` — scans for newly-added entries that can be opened as sub-volumes and expands the archive tree. Belongs on VM alongside Group G population methods. |
| `AddDirEntries(...)` | Moves with `GetFileSelection` |
| `ShiftDirectories(...)` | Moves with `MoveFiles` |
| `GetCommonPathPrefix(...)` | Static helper, moves with `AddPaths` |

**Group B — Edit/Attributes:**

| Method | Notes |
|---|---|
| `EditAttributes()` | → `_dialogService.ShowDialogAsync<EditAttributesViewModel>(...)` |
| `EditDirAttributes()` | Same pattern |
| `EditAttributesImpl()` | Core logic (MacZip handling) |
| `FinishEditAttributes()` | Post-edit UI refresh |
| `CreateDirectory()` | → `_dialogService.ShowDialogAsync<CreateDirectoryViewModel>(...)` |

**Group C — Disk/Sector Operations:**

| Method | Notes |
|---|---|
| `EditBlocksSectors()` | → `_dialogService.ShowDialogAsync<EditSectorViewModel>(...)`. Note: `EditSector.SectorEditMode` enum must be promoted to a shared location (e.g., `DiskArcNode.cs` or a dedicated enum file) before or as part of Phase 3B, so the ViewModel can reference it without depending on the dialog class. |
| `SaveAsDiskImage()` | → `_dialogService` + file picker |
| `ReplacePartition()` | File picker + dialog |
| `Defragment()` | WorkProgress dialog |
| `ScanForSubVol()` | Direct WorkTree operation |
| `CloseSubTree()` | Direct tree manipulation |
| `ScanForBadBlocks()` | Stub — `CanExecute` = false until implemented. Ensure VM exposes a `ReactiveCommand` with `canExecute: Observable.Return(false)` so the AXAML binding doesn't break. |

**Group D — View/Navigation:**

| Method | Notes |
|---|---|
| `ViewFiles()` | `FileViewerViewModel` does not exist until Phase 4A. For now, relocate the current `FileViewer` dialog call from the controller to the VM, calling the dialog directly with its existing constructor. Replace with `_dialogService.ShowModeless<FileViewerViewModel>(...)` in Phase 4A. |
| `NavToParent()` | Tree navigation (direct property manipulation) |
| `HandleFileListDoubleClick()` | Navigation + open sub-archive |
| `HandlePartitionLayoutDoubleClick()` | Navigation |
| `FindFiles()` | **Modal** dialog with event callback — the actual code uses `await dialog.ShowDialog<bool?>(mMainWin)` with a `FindRequested` event subscription, not `ShowModeless`. `FindFileViewModel` (Phase 4B) must expose a `FindRequested` observable; `MainViewModel.FindFiles()` subscribes before showing the dialog and calls `DoFindFiles()` on each emission. Note: `UpdateFindState()` calls `ArchiveTreeItem.SelectItem(mMainWin, ...)` and needs `IViewActions` treatment (see Step 6). |
| `DoFindFiles()`, `FindInTree()`, etc. | Static search helpers, copy as-is |
| `HandleMetadataDoubleClick()` | MCP line 1132 — called from MW `MetadataList_DoubleTapped`. Calls `mMainWin.UpdateMetadata()`, `mMainWin.RemoveMetadata()`. |
| `HandleMetadataAddEntry()` | MCP line 1161 — called from MW `Metadata_AddEntryButtonClick`. Calls `mMainWin.AddMetadata()`, `mMainWin.SetMetadataList()`. |

**Group E — Clipboard:**

| Method | Notes |
|---|---|
| `CopyToClipboard()` | → `_clipboardService.SetFilesAsync()` + WorkProgress |
| `PasteOrDrop()` | → `_clipboardService.GetFilesAsync()` + WorkProgress |
| `PasteExternalFiles()` | URI parsing + AddPaths |
| `ClearClipboardIfPending()` | → `_clipboardService.ClearIfPendingAsync()`. **Note:** Currently `async void` — called from three synchronous property setters in MW: `IsChecked_AddExtract.set`, `IsChecked_ImportExport.set`, `SelectedDDCPModeIndex.set`. Keep as `async void` (fire-and-forget from setter semantics) or convert to `async Task` with `_ = ` discard at call sites. Document chosen approach. These three setters must be updated in Step 5 to call the VM instead of `mMainCtrl`. |
| `CleanupClipTemp()` | Temp dir cleanup |

**Group F — Debug Commands:**

| Method | Notes |
|---|---|
| `Debug_ShowDebugLog()` | Modeless toggle (keep reference on VM). Needs the `DebugMessageLog` instance — obtain via `_workspaceService.DebugLog`. Must subscribe to the window's `Closed` event to reset the reference and update `IsDebugLogOpen`: `mDebugLogViewer.Closed += (_, _) => { mDebugLogViewer = null; this.RaisePropertyChanged(nameof(IsDebugLogOpen)); };` |
| `Debug_DiskArcLibTests()` | → `_dialogService.ShowDialogAsync(...)` |
| `Debug_FileConvLibTests()` | Same |
| `Debug_BulkCompressTest()` | Same |
| `Debug_ShowSystemInfo()` | → `_dialogService.ShowDialogAsync(...)` (this is a **modal** dialog, not modeless — the source uses `dialog.ShowDialog(mMainWin)`) |
| `Debug_ShowDropTarget()` | Modeless toggle — same `Closed` event pattern as `Debug_ShowDebugLog()` for `mDebugDropTarget` / `IsDropTargetOpen`. |
| `Debug_ConvertANI()` | File picker + export |

**Group G — UI Population:**

| Method | Notes |
|---|---|
| `PopulateArchiveTree()` | → `MainViewModel.PopulateArchiveTree()` |
| `PopulateDirectoryTree()` | → `MainViewModel.PopulateDirectoryTree()` |
| `PopulateFileList()` | → `MainViewModel.PopulateFileList()` |
| `PopulateEntriesFromArchive()` | Helper stays with PopulateFileList |
| `PopulateEntriesFromSingleDir()` | Same |
| `PopulateEntriesFromFullDisk()` | Same |
| `RefreshDirAndFileList()` | → `MainViewModel.RefreshDirAndFileList()` |
| `ConfigureCenterInfo()` | → `MainViewModel.ConfigureCenterInfo()` |
| `SetEntryCounts()` | → `MainViewModel.SetEntryCounts()` |
| `VerifyDirectoryTree()` | Static helper, copy as-is |
| `VerifyFileList()` (overloads) | Static helpers |

**`ShowFullListCommand` / `ShowDirListCommand` (MW lines 1107–1130):** These
command bodies mix view-level properties (`PreferSingleDirList`,
`ShowSingleDirFileList`, `SetShowCenterInfo()`) with a controller call
(`mMainCtrl.PopulateFileList(IFileEntry.NO_ENTRY, false)`). After
`PopulateFileList()` moves to the VM in Group G:
- `PreferSingleDirList` and `ShowSingleDirFileList` migrate to VM properties
  (they are app-state, not pure-view concerns).
- `ShowFullListCommand` and `ShowDirListCommand` move to the VM and call
  `PopulateFileList()` directly after updating the above properties.
- `SetShowCenterInfo()` is replaced by setting the VM's `ShowCenterInfo`
  property (already covered by `ConfigureCenterInfo()` migration).

**Group H — Selection/State:**

| Method | Notes |
|---|---|
| `GetSelectedArcDir()` | → `MainViewModel.GetSelectedArcDir()` |
| `GetFileSelection()` | → `MainViewModel.GetFileSelection()` |
| `ArchiveTree_SelectionChanged()` | → `MainViewModel` method |
| `DirectoryTree_SelectionChanged()` | → `MainViewModel` method |
| `SyncDirectoryTreeToFileSelection()` | → `MainViewModel` method |
| `CheckPasteDropOkay()` | → `MainViewModel` method |

**VM fields required from Group H:**

- **`mSyncingSelection`** (MCP line 46): Boolean re-entrancy guard that
  prevents infinite event loops. `SyncDirectoryTreeToFileSelection()` (called
  from `DirectoryTree_SelectionChanged`) programmatically changes the tree
  selection, which would otherwise trigger another `SelectionChanged` event.
  Carry this field to the VM and check it at the top of the migrated
  `DirectoryTree_SelectionChanged()`.

- **`mSwitchFocusToFileList`** (MCP line 41): Controls whether
  `DirectoryTree_SelectionChanged()` ends with focus on the file list
  (`IViewActions.FocusFileList()`) or not. Set in `RefreshDirAndFileList()`
  and `HandleFileListDoubleClick()` to coordinate a two-step sequence:
  (1) programmatic selection change triggers `DirectoryTree_SelectionChanged`,
  (2) which uses the flag to decide whether to redirect focus. Carry this
  field to the VM.

- **`CurrentWorkObject`** (MCP line 65): `private object? CurrentWorkObject` —
  stores the currently-selected DA object (`IDiskImage`, `IFileSystem`,
  `IArchive`, `Partition`, etc.). Set in `ArchiveTree_SelectionChanged()` to
  `newSel.WorkTreeNode.DAObject`. All computed state properties
  (`IsDiskImageSelected`, `CanEditBlocks`, `CanWrite`, `HasChunks`, etc.)
  read from it. Also drives `GetCurrentWorkChunks()` and
  `ConfigureCenterInfo()`. Carry to VM as `private object? mCurrentWorkObject`.

- **`CachedArchiveTreeSelection`** (MCP line 58): `internal` property
  tracking the last-confirmed archive tree selection. Set in
  `ArchiveTree_SelectionChanged()`, read by `NavToParent()`, cleared in
  `CloseWorkFile()`. Carry to VM.

- **`CachedDirectoryTreeSelection`** (MCP line 53): `internal` property
  tracking the last-confirmed directory tree selection. Set in
  `DirectoryTree_SelectionChanged()` and `SyncDirectoryTreeToFileSelection()`,
  read by `NavToParent()`, cleared in `CloseWorkFile()`. Carry to VM.

### Step 4: Wire Up Lifecycle Methods

Replace controller lifecycle calls from `MainWindow`:

**Before (MainWindow.axaml.cs):**
```csharp
private void Window_Loaded(object sender, RoutedEventArgs e) {
    mMainCtrl.WindowLoaded();
}
private void Window_Closing(object sender, WindowClosingEventArgs e) {
    mMainCtrl.WindowClosing();
}
```

**After:**
```csharp
private void Window_Loaded(object sender, RoutedEventArgs e) {
    if (DataContext is MainViewModel vm) {
        vm.Initialize();
    }
}
private void Window_Closing(object sender, WindowClosingEventArgs e) {
    if (DataContext is MainViewModel vm) {
        vm.Shutdown();
    }
}
```

**MainViewModel methods:**
```csharp
public void Initialize() {
    // Moved from MainController.WindowLoaded:
    // - Run startup self-tests (copy from WindowLoaded() verbatim —
    //   Debug.Assert(RangeSet.Test()), CommonUtil.Version.Test(),
    //   CircularBitBuffer.DebugTest(), etc.)
    // - Load settings
    // - Apply settings (window placement, column widths, recent files)
    // - Open command-line file if provided
}

public void Shutdown() {
    // Moved from MainController.WindowClosing:
    // - Close debug log window
    // - Cleanup clipboard temp
    // - Save settings
}
```

**VM command body for file open (post-open sequence):**

After `_workspaceService.OpenAsync(...)` returns, the VM must perform the
following UI-state updates. Without this sequence, the UI will not reflect
the opened file:

```csharp
public async Task OpenWorkFileAsync() {
    if (!CloseWorkFile()) return;
    string? path = await _filePickerService.OpenFileAsync(...);
    if (path == null) return;
    SetCursorBusy(true);  // via IViewActions
    try {
        await _workspaceService.OpenAsync(path, readOnly: false, depth);
        if (!_workspaceService.IsFileOpen) return;
        PopulateArchiveTree();           // Group G
        UpdateTitle();                   // lifecycle
        UpdateRecentLinks();             // lifecycle
        LaunchPanelVisible = false;      // VM property
        MainPanelVisible  = true;        // VM property
        // ReactiveCommand canExecute auto-refreshes via WhenAnyValue
    } finally {
        SetCursorBusy(false);
    }
}
```

### Step 5: Wire View-Only Event Handlers

Some controller methods handle pure View events (drag-drop, key press,
selection changed). These stay as thin pass-throughs in `MainWindow`
code-behind:

```csharp
// MainWindow.axaml.cs — retained code-behind event handlers:
private void ArchiveTree_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (DataContext is MainViewModel vm) {
        var sel = archiveTree.SelectedItem as ArchiveTreeItem;
        vm.OnArchiveTreeSelectionChanged(sel);  // passes null on deselection
    }
}

private void DirectoryTree_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (DataContext is MainViewModel vm) {
        var sel = directoryTree.SelectedItem as DirectoryTreeItem;
        vm.OnDirectoryTreeSelectionChanged(sel);  // passes null on deselection
    }
}

private void FileList_DoubleTapped(object sender, TappedEventArgs e) {
    if (DataContext is MainViewModel vm) {
        vm.HandleFileListDoubleClick();
    }
}

private void PartitionLayout_DoubleTapped(object sender, TappedEventArgs e) {
    // (column-header auto-size logic stays here — copy verbatim from MW)
    if (DataContext is MainViewModel vm &&
        (sender as DataGrid)?.SelectedItem is PartitionListItem pli) {
        var arcTreeSel = archiveTree.SelectedItem as ArchiveTreeItem;
        vm.HandlePartitionLayoutDoubleClick(pli, arcTreeSel);
    }
}

private async void MetadataList_DoubleTapped(object sender, TappedEventArgs e) {
    // (column-header auto-size logic stays here — copy verbatim from MW)
    if (DataContext is MainViewModel vm &&
        (sender as DataGrid)?.SelectedItem is MetadataItem item) {
        await vm.HandleMetadataDoubleClick(item, 0, 0);
    }
}

private async void Metadata_AddEntryButtonClick(object sender, RoutedEventArgs e) {
    if (DataContext is MainViewModel vm) {
        await vm.HandleMetadataAddEntry();
    }
}
```

### Step 6: Handle Direct Control Access

The controller has several methods that access View controls directly
(e.g., `mMainWin.fileListDataGrid.SelectedItem`). These must be mediated:

**Panel visibility and debug menu properties:** The following properties are
set by controller methods migrating to the VM (`DoOpenWorkFile`, `CloseWorkFile`,
`ApplyAppSettings`). They must become VM properties bound in AXAML:

- `LaunchPanelVisible` (bool) — controls launch/drop panel visibility
- `MainPanelVisible` (bool) — controls main content panel visibility
- `ShowDebugMenu` (bool) — controls debug menu visibility

Update AXAML bindings from `MainWindow`-property references to
`DataContext`-property bindings (e.g., `<Panel IsVisible="{Binding LaunchPanelVisible}">`).

**Options-panel properties migration:** The ~15 `IsChecked_*` properties
(e.g., `IsChecked_AddCompress`, `IsChecked_ExtPreserveAS`) and
`SelectedDDCPModeIndex` currently live on `MainWindow` and read/write
`AppSettings.Global.*` directly. They receive UI-refresh notifications only
via `PublishSideOptions()`. These properties must migrate from `MainWindow`
to `MainViewModel` as part of Step 2 (Settings). In the VM, they read/write
via `_settingsService.Get/SetBool(...)`, and their AXAML bindings change
from implicit Window context to `{Binding IsChecked_AddCompress}` resolved
against `MainViewModel`.

**Import/export converter properties migration:** Four additional properties
must migrate alongside `IsChecked_*`:
- `ImportConverters` (`List<ConvItem>`) — ComboBox `ItemsSource` (AXAML)
- `ExportConverters` (`List<ConvItem>`) — ComboBox `ItemsSource` (AXAML)
- `SelectedImportConverter` (`ConvItem?`) — ComboBox `SelectedItem`; setter
  writes chosen tag via `_settingsService.SetString(...)`
- `SelectedExportConverter` (`ConvItem?`) — same

`InitImportExportConfig()` (MW line 483) populates both lists from
`ImportFoundry`/`ExportFoundry` and restores saved selection. Move to
`MainViewModel` and call from `Initialize()`. Read saved tags via
`_settingsService.GetString(AppSettings.CONV_IMPORT_TAG, ...)`.

**Option A — Expose via ViewModel properties (preferred):**
```csharp
// MainViewModel:
private FileListItem? mSelectedFileListItem;
public FileListItem? SelectedFileListItem {
    get => mSelectedFileListItem;
    set => this.RaiseAndSetIfChanged(ref mSelectedFileListItem, value);
}
```
Bind in AXAML: `<DataGrid SelectedItem="{Binding SelectedFileListItem}">`

**Option B — Retain view reference for scroll/focus (acceptable):**
```csharp
// Methods that need scroll-to or focus stay in code-behind and are
// exposed via a thin interface or direct call from ViewModel:
public void ScrollFileListTo(FileListItem item) {
    fileListDataGrid.ScrollIntoView(item, null);
}
```
The ViewModel calls this through `IDialogHost` extended interface or a
dedicated `IViewActions` interface (defined in **Step 0c** above — see that
step for the complete interface specification):
```csharp
// IViewActions — canonical definition is in Step 0c.
// Repeated here for quick reference (subset):
public interface IViewActions {
    void ScrollFileListTo(object item);
    void FocusFileList();
    void ScrollDirectoryTreeToTop();
    void ScrollFileListToTop();
    void SetFileListSelectionFocus(int index);
    void ShowToast(string message, bool success);
    void SetCursorBusy(bool busy);
    void PopulateRecentFilesMenu();
    void SelectAllFileListItems();
    void SetFileListSelection(IList<FileListItem> items);
}
```

**Methods that do NOT belong on `IViewActions`** — the following are
data/state operations that should be `MainViewModel` methods or properties
with AXAML bindings, not imperative view calls:
- `ConfigureCenterPanel(...)` → set VM boolean properties (`HasInfoOnly`,
  `IsFullListEnabled`, `IsDirListEnabled`, `ShowCol_Format`, etc.)
- `ClearCenterInfo()` → VM clears its own collections directly
- `SetNotesList(...)` → VM populates `NotesList` collection + `ShowNotes`
- `SetPartitionList(...)` → VM populates `PartitionList` collection +
  `ShowPartitionLayout`
- `SetMetadataList(...)` → VM populates `MetadataList` collection +
  `ShowMetadata`, `CanAddMetadataEntry`
- `UpdateMetadata(...)`, `AddMetadata(...)`, `RemoveMetadata(...)` → VM
  mutates its own `MetadataList` collection
- `ReapplyFileListSort()` → VM method on `FileListViewModel` (or
  `MainViewModel` initially) since it sorts the bound `FileList` collection
```
`MainWindow` implements `IViewActions`. Pass to ViewModel at construction
(already wired in **Step 0c** above).

**`IViewActions` wiring (reference):** `IViewActions` is a view-side object,
not a DI service. The constructor signature and wiring code are defined in
Step 0c. The following is repeated here for convenience:
```csharp
// MainViewModel constructor:
private readonly IViewActions mViewActions;

public MainViewModel(
    IDialogHost dialogHost,
    IViewActions viewActions,         // ← new
    IWorkspaceService workspaceService,
    ISettingsService settingsService,
    IClipboardService clipboardService,
    IFilePickerService filePickerService) {
    mViewActions = viewActions;
    // ...
}
```
```csharp
// MainWindow (constructor or Loaded):
var vm = new MainViewModel(
    this,    // IDialogHost
    this,    // IViewActions
    App.Services.GetRequiredService<IWorkspaceService>(),
    App.Services.GetRequiredService<ISettingsService>(),
    App.Services.GetRequiredService<IClipboardService>(),
    App.Services.GetRequiredService<IFilePickerService>());
DataContext = vm;
```

**`SelectAllCommand` stays in code-behind:** `SelectAllCommand` (MW line 1018)
calls `fileListDataGrid.SelectAll()` — a purely view-level action with no
business logic. It can remain in `MainWindow` code-behind with its AXAML
binding resolving against the window rather than the `DataContext`.
Alternatively, the VM can call `mViewActions.SelectAllFileListItems()` if
the command is migrated. **AXAML binding fix required:** After `DataContext = vm`,
`{Binding SelectAllCommand}` resolves against the VM (which lacks this command).
Update to `{Binding SelectAllCommand, RelativeSource={RelativeSource AncestorType=Window}}`.

**`ResetSortCommand` stays in code-behind:** `ResetSortCommand` (MW line 1164)
is also purely view-level — it clears DataGrid column sort indicators
(`col.Tag`), clears `mSortColumn`, sets `mSortAscending`, and sets
`IsResetSortEnabled = false`. It does not call `mMainCtrl`. It stays in
`MainWindow` code-behind; its `CanExecute` guard (`IsResetSortEnabled`)
similarly stays as an MW property. **AXAML binding fix required:** Same as
`SelectAllCommand` — update to
`{Binding ResetSortCommand, RelativeSource={RelativeSource AncestorType=Window}}`.

**Static helper methods refactoring:** The following static methods currently
take `MainWindow` as a parameter and cannot be called from a ViewModel:

- `ArchiveTreeItem.SelectItem(MainWindow, item)` — refactor to take
  `IViewActions` (or the relevant tree control interface) instead
- `ArchiveTreeItem.SelectBestFrom(mainWin.archiveTree, ...)` — refactor
  to accept the tree collection directly
- `DirectoryTreeItem.SelectItemByEntry(MainWindow, ...)` — refactor to
  take `IViewActions`
- `FileListItem.SetSelectionFocusByEntry(fileList, mainWin.fileListDataGrid, ...)`
  — refactor to take `IViewActions`

These must be refactored before or during the Group G/H migration so that
VM code can call them without a `MainWindow` reference.

### Step 7: Delete Controller Files

Once all methods have been moved and the build passes:

1. Delete `cp2_avalonia/MainController.cs`
2. Delete `cp2_avalonia/MainController_Panels.cs`
3. Remove `mMainCtrl` field and all references from `MainWindow.axaml.cs`
4. Remove `SetController()` from `MainViewModel`
5. Remove `mMainWin` field type (no longer `MainWindow`, replaced by
   `IDialogHost` + `IViewActions`)

### Step 8: Build and Validate

1. Run `dotnet build` — verify zero errors. Fix any remaining
   `mMainWin.` or `mController.` references.
2. Launch the application.
3. **Complete functional test** — every feature exercised:
   - Open/close files
   - All menu commands
   - Add/extract/delete/test files
   - Edit attributes, create directory
   - Copy/paste (same-process)
   - Edit sectors/blocks
   - New disk image, new file archive
   - Save as disk image, replace partition
   - Find files
   - View selected files
   - Debug menu items
   - Settings dialog (edit, apply, persist on restart)
   - Recent files list
   - Drag-and-drop file open
   - Window resize / panel resize (persisted)
   - macOS native menu (About, Settings, Quit)

---

## Incremental Migration Strategy

**Do NOT attempt to move all methods at once.** Follow this order to maintain
a compiling, runnable application at every step:

0a. **Extract `AutoOpenDepth`** (Step 0a) — prerequisite for Step 1
0b. **WorkProgress strategy** (Step 0b) — understand Phase 3B dialog rules
0c. **Define `IViewActions`** (Step 0c) — prerequisite for Steps 3–5
1. **Settings** (Step 2) — small, no dependencies
2. **State query properties** (Group H — `GetFileSelection()`,
   `GetSelectedArcDir()`, `CheckPasteDropOkay()`, `mSyncingSelection`,
   `mSwitchFocusToFileList`, and all computed state properties) — migrate
   these **before** Groups A–E because those groups depend on them
3. **UI population** (Group G) — needed by everything
4. **Lifecycle** (Step 4) — connects init/shutdown
5. **Navigation** (Group D nav subset) — tree selection handlers
6. **File operations** (Group A) — largest group, do one at a time
7. **Edit/Attributes** (Group B)
8. **Disk operations** (Group C)
9. **Clipboard** (Group E)
10. **Debug** (Group F) — least critical
11. **Delete controller files** (Step 7) — only after everything else compiles

Build and test after moving each group.

---

## What This Enables

- `MainController.cs` and `MainController_Panels.cs` are deleted.
- `MainViewModel` is self-contained with injected services.
- Phase 4 can create dialog ViewModels with `IDialogService` integration.
- Phase 5 can extract child ViewModels from `MainViewModel` methods.
