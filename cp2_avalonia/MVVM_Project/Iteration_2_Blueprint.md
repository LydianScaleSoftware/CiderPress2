# Iteration 2 Blueprint: Commands → MainViewModel

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 Phase 2, §7.13, §7.16.

---

## Goal

Move all 51 `ICommand` properties from `MainWindow.axaml.cs` to `MainViewModel`,
converting each from `RelayCommand` to `ReactiveCommand`. Eliminate
`RefreshAllCommandStates()` and `InvalidateCommands()` — `ReactiveCommand`
auto-evaluates `CanExecute` via `WhenAnyValue`. After this iteration, all
commands live on the ViewModel and the AXAML bindings work unchanged.

---

## Prerequisites

- Iterations 1A and 1B are complete (`MainViewModel` is the DataContext,
  all bindable properties live on the VM, controller has `mViewModel` reference).
- The application builds and runs correctly.

---

## Step-by-Step Instructions

### Step 1: Add Required Usings to MainViewModel

```csharp
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
```

### Step 2: Create Command Properties on MainViewModel

First, remove the 51 temporary `ICommand?` pass-through property declarations
from `MainViewModel` that were added in Iteration 1B. They are identifiable
by their `{ get; set; }` mutability and `ICommand?` type. The
`ReactiveCommand<Unit, Unit>` declarations below replace them entirely.

For each of the 51 commands, create a `ReactiveCommand` property. Commands
are initialized in the constructor.

**Property declarations** (all public, read-only after construction):

```csharp
// File menu
public ReactiveCommand<Unit, Unit> NewDiskImageCommand { get; }
public ReactiveCommand<Unit, Unit> NewFileArchiveCommand { get; }
public ReactiveCommand<Unit, Unit> OpenCommand { get; }
public ReactiveCommand<Unit, Unit> OpenPhysicalDriveCommand { get; }
public ReactiveCommand<Unit, Unit> CloseCommand { get; }
public ReactiveCommand<Unit, Unit> ExitCommand { get; }

// Recent files
public ReactiveCommand<Unit, Unit> RecentFile1Command { get; }
public ReactiveCommand<Unit, Unit> RecentFile2Command { get; }
public ReactiveCommand<Unit, Unit> RecentFile3Command { get; }
public ReactiveCommand<Unit, Unit> RecentFile4Command { get; }
public ReactiveCommand<Unit, Unit> RecentFile5Command { get; }
public ReactiveCommand<Unit, Unit> RecentFile6Command { get; }

// Edit menu
public ReactiveCommand<Unit, Unit> CopyCommand { get; }
public ReactiveCommand<Unit, Unit> PasteCommand { get; }
public ReactiveCommand<Unit, Unit> FindCommand { get; }
public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
public ReactiveCommand<Unit, Unit> EditAppSettingsCommand { get; }

// Actions menu
public ReactiveCommand<Unit, Unit> ViewFilesCommand { get; }
public ReactiveCommand<Unit, Unit> AddFilesCommand { get; }
public ReactiveCommand<Unit, Unit> ImportFilesCommand { get; }
public ReactiveCommand<Unit, Unit> ExtractFilesCommand { get; }
public ReactiveCommand<Unit, Unit> ExportFilesCommand { get; }
public ReactiveCommand<Unit, Unit> DeleteFilesCommand { get; }
public ReactiveCommand<Unit, Unit> TestFilesCommand { get; }
public ReactiveCommand<Unit, Unit> EditAttributesCommand { get; }
public ReactiveCommand<Unit, Unit> CreateDirectoryCommand { get; }
public ReactiveCommand<Unit, Unit> EditDirAttributesCommand { get; }
public ReactiveCommand<Unit, Unit> EditSectorsCommand { get; }
public ReactiveCommand<Unit, Unit> EditBlocksCommand { get; }
public ReactiveCommand<Unit, Unit> SaveAsDiskImageCommand { get; }
public ReactiveCommand<Unit, Unit> ReplacePartitionCommand { get; }
public ReactiveCommand<Unit, Unit> ScanForBadBlocksCommand { get; }
public ReactiveCommand<Unit, Unit> ScanForSubVolCommand { get; }
public ReactiveCommand<Unit, Unit> DefragmentCommand { get; }
public ReactiveCommand<Unit, Unit> CloseSubTreeCommand { get; }

// View menu
public ReactiveCommand<Unit, Unit> ShowFullListCommand { get; }
public ReactiveCommand<Unit, Unit> ShowDirListCommand { get; }
public ReactiveCommand<Unit, Unit> ShowInfoCommand { get; }

// Navigate
public ReactiveCommand<Unit, Unit> NavToParentDirCommand { get; }
public ReactiveCommand<Unit, Unit> NavToParentCommand { get; }

// Help
public ReactiveCommand<Unit, Unit> HelpCommand { get; }
public ReactiveCommand<Unit, Unit> AboutCommand { get; }

// Debug
public ReactiveCommand<Unit, Unit> Debug_DiskArcLibTestCommand { get; }
public ReactiveCommand<Unit, Unit> Debug_FileConvLibTestCommand { get; }
public ReactiveCommand<Unit, Unit> Debug_BulkCompressTestCommand { get; }
public ReactiveCommand<Unit, Unit> Debug_ShowSystemInfoCommand { get; }
public ReactiveCommand<Unit, Unit> Debug_ShowDebugLogCommand { get; }
public ReactiveCommand<Unit, Unit> Debug_ShowDropTargetCommand { get; }
public ReactiveCommand<Unit, Unit> Debug_ConvertANICommand { get; }

// Toolbar
public ReactiveCommand<Unit, Unit> ResetSortCommand { get; }
public ReactiveCommand<Unit, Unit> ToggleInfoCommand { get; }
```

### Step 2A: Cross-Reference Key Bindings

`MainWindow.axaml` contains 18 `KeyBinding` entries that bind to command
properties by name. After creating the ViewModel command properties (Step 2),
confirm that every key binding name matches a ViewModel property exactly:

| Gesture | Command Property |
|---|---|
| Ctrl+C | `CopyCommand` |
| Ctrl+V | `PasteCommand` |
| Enter | `ViewFilesCommand` |
| Delete | `DeleteFilesCommand` |
| Alt+Up | `NavToParentCommand` |
| Ctrl+I | `ToggleInfoCommand` |
| Ctrl+Shift+A | `AddFilesCommand` |
| Ctrl+E | `ExtractFilesCommand` |
| Alt+Enter | `EditAttributesCommand` |
| Ctrl+Shift+N | `CreateDirectoryCommand` |
| Ctrl+Shift+W | `CloseSubTreeCommand` |
| Ctrl+Shift+T | `Debug_DiskArcLibTestCommand` |
| Ctrl+Shift+1 | `RecentFile1Command` |
| Ctrl+Shift+2 | `RecentFile2Command` |
| Ctrl+Shift+3 | `RecentFile3Command` |
| Ctrl+Shift+4 | `RecentFile4Command` |
| Ctrl+Shift+5 | `RecentFile5Command` |
| Ctrl+Shift+6 | `RecentFile6Command` |

A name mismatch silently breaks the key binding with no build error.

### Step 3: Initialize Commands in the Constructor

In the `MainViewModel` constructor, create each command with its `canExecute`
observable and its execute action. The execute actions **delegate to
`mController`** during this interim period (the controller is dissolved in
Phase 3).

**Store the controller reference:**

```csharp
private MainController? mController;

/// <summary>
/// Set by MainWindow after construction. Temporary coupling removed in Phase 3.
/// </summary>
public void SetController(MainController controller) {
    mController = controller;
}
```

**Call site:** In `MainWindow.axaml.cs`, after constructing the controller,
wire the ViewModel's controller reference:
```csharp
mMainCtrl = new MainController(this, ViewModel);
ViewModel.SetController(mMainCtrl);   // <-- add this line
```

#### 3.0 Add Interim Helper Methods to `MainController.cs`

Before creating any commands, add the following methods to
`MainController.cs`. These provide ViewModel-callable entry points for
command bodies that currently access the Window or its controls directly.
All four are temporary — they are removed when the controller is dissolved
in Phase 3.

```csharp
/// <summary>Close the main window (called by ExitCommand).</summary>
public void RequestClose() => mMainWin.Close();

/// <summary>Show the About dialog (called by AboutCommand).</summary>
public async Task ShowAboutDialog() {
    var dlg = new AboutBox();
    await dlg.ShowDialog(mMainWin);
}

/// <summary>Select all items in the file list (called by SelectAllCommand).</summary>
public void SelectAll() => mMainWin.SelectAllFileListItems();

/// <summary>Clear column sort state and repopulate (called by ResetSortCommand).</summary>
public void ResetSort() {
    mMainWin.ClearColumnSortTags();
    mMainWin.ClearSortColumn();
    PopulateFileList(IFileEntry.NO_ENTRY, false);
    mViewModel.IsResetSortEnabled = false;
}

/// <summary>Show a "not implemented" dialog (called by OpenPhysicalDriveCommand).</summary>
public async Task NotImplemented(string featureName) {
    // Mirrors the original MainWindow.NotImplemented() behavior.
    var dlg = new Avalonia.Controls.Window {
        Title = "Not Implemented",
        Content = new Avalonia.Controls.TextBlock {
            Text = featureName + " is not yet implemented.",
            Margin = new Avalonia.Thickness(20)
        },
        SizeToContent = Avalonia.Controls.SizeToContent.WidthAndHeight
    };
    await dlg.ShowDialog(mMainWin);
}
```

Also add to `MainWindow.axaml.cs`:
```csharp
/// <summary>Select all items in the file list DataGrid.</summary>
internal void SelectAllFileListItems() => fileListDataGrid.SelectAll();

/// <summary>Clear sort tags on all file list DataGrid columns.</summary>
internal void ClearColumnSortTags() {
    foreach (DataGridColumn col in fileListDataGrid.Columns) {
        col.Tag = null;
    }
}

/// <summary>Reset the tracked sort column (called by MainController.ResetSort).</summary>
internal void ClearSortColumn() => mSortColumn = null;
```

#### 3.1 Async vs. Sync Classification

Use `ReactiveCommand.CreateFromTask(...)` for async commands and
`ReactiveCommand.Create(...)` for sync commands. Wrapping an `async` lambda
in `ReactiveCommand.Create` (sync) loses the return value, prevents
`ThrownExceptions` from catching async faults, and can cause unobserved
exceptions.

**Sync commands** (use `ReactiveCommand.Create`):
`ExitCommand`, `CloseCommand`, `SelectAllCommand`, `ScanForBadBlocksCommand`,
`ScanForSubVolCommand`, `CloseSubTreeCommand`,
`ShowFullListCommand`, `ShowDirListCommand`, `ShowInfoCommand`,
`NavToParentDirCommand`, `NavToParentCommand`, `Debug_ShowSystemInfoCommand`,
`Debug_ShowDebugLogCommand`, `Debug_ShowDropTargetCommand`, `ResetSortCommand`,
`ToggleInfoCommand`, `HelpCommand`.

**Async commands** (use `ReactiveCommand.CreateFromTask`):
`OpenCommand`, `NewDiskImageCommand`, `NewFileArchiveCommand`,
`RecentFile1–6Commands`, `CopyCommand`, `PasteCommand`, `FindCommand`,
`EditAppSettingsCommand`, `ViewFilesCommand`, `AddFilesCommand`,
`ImportFilesCommand`, `ExtractFilesCommand`, `ExportFilesCommand`,
`DeleteFilesCommand`, `TestFilesCommand`, `EditAttributesCommand`,
`CreateDirectoryCommand`, `EditDirAttributesCommand`, `EditSectorsCommand`,
`EditBlocksCommand`, `SaveAsDiskImageCommand`, `ReplacePartitionCommand`,
`DefragmentCommand`, `OpenPhysicalDriveCommand`,
`Debug_DiskArcLibTestCommand`,
`Debug_FileConvLibTestCommand`, `Debug_BulkCompressTestCommand`,
`Debug_ConvertANICommand`, `AboutCommand`.

#### 3.2 canExecute Observable Patterns

**Common shared observables** (define once, reuse):

```csharp
var canWhenOpen = this.WhenAnyValue(x => x.IsFileOpen);

var canWrite = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.CanWrite,
    (open, write) => open && write);
```

**Complete canExecute reference table:**

| canExecute Condition | Commands |
|---|---|
| Always enabled (no canExecute) | `OpenCommand`, `NewDiskImageCommand`, `NewFileArchiveCommand`, `RecentFile1–6Commands`, `EditAppSettingsCommand`, `Debug_DiskArcLibTestCommand`, `Debug_FileConvLibTestCommand`, `Debug_BulkCompressTestCommand`, `Debug_ShowSystemInfoCommand`, `Debug_ShowDebugLogCommand`, `Debug_ShowDropTargetCommand`, `ExitCommand`, `HelpCommand`, `AboutCommand` |
| `IsFileOpen` | `CloseCommand`, `ShowInfoCommand`, `ToggleInfoCommand`, `SelectAllCommand` |
| `IsFileOpen && CanWrite && IsHierarchicalFileSystemSelected` | `CreateDirectoryCommand` |
| `IsFileOpen && IsFileSystemSelected` | `EditDirAttributesCommand`, `ScanForSubVolCommand` |
| `IsFileOpen && CanEditSectors` | `EditSectorsCommand` |
| `IsFileOpen && CanEditBlocks` | `EditBlocksCommand` |
| `IsFileOpen && IsDiskOrPartitionSelected && HasChunks` | `SaveAsDiskImageCommand` |
| `IsFileOpen && CanWrite && IsPartitionSelected` | `ReplacePartitionCommand` |
| `IsFileOpen && IsDefragmentableSelected && CanWrite` | `DefragmentCommand` |
| `IsFileOpen && IsClosableTreeSelected` | `CloseSubTreeCommand` |
| `IsFileOpen && IsSingleEntrySelected` | `EditAttributesCommand` |
| `IsFileOpen && AreFileEntriesSelected` | `FindCommand` |
| `IsFileOpen && IsHierarchicalFileSystemSelected && !IsSelectedDirRoot` | `NavToParentDirCommand` |
| `IsFileOpen && ((IsHierarchicalFileSystemSelected && !IsSelectedDirRoot) \|\| !IsSelectedArchiveRoot)` | `NavToParentCommand` |
| `IsANISelected` | `Debug_ConvertANICommand` |
| `IsFullListEnabled` | `ShowFullListCommand` |
| `IsDirListEnabled` | `ShowDirListCommand` |
| `IsResetSortEnabled` | `ResetSortCommand` |
| Always disabled | `ScanForBadBlocksCommand` |
| Always enabled (stub) | `OpenPhysicalDriveCommand` |
| **Commands that also require `ShowCenterFileList`** (see §3.5 below) | `CopyCommand`, `ViewFilesCommand`, `AddFilesCommand`, `ImportFilesCommand`, `ExtractFilesCommand`, `ExportFilesCommand`, `DeleteFilesCommand`, `TestFilesCommand` |
| **Commands that use `IsMultiFileItemSelected`** (see §3.5 below) | `PasteCommand`, `AddFilesCommand`, `ImportFilesCommand`, `DeleteFilesCommand` |

#### 3.3 Standard Controller-Delegation Examples

```csharp
// Sync, with canExecute:
CloseCommand = ReactiveCommand.Create(
    () => mController!.CloseWorkFile(), canWhenOpen);

// Async, always enabled:
OpenCommand = ReactiveCommand.CreateFromTask(
    () => mController!.OpenWorkFile());

// Async with canExecute:
var canSingleSelect = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.IsSingleEntrySelected,
    (open, single) => open && single);

EditAttributesCommand = ReactiveCommand.CreateFromTask(
    () => mController!.EditAttributes(), canSingleSelect);

// Async with disk-specific canExecute:
var canSaveAsDisk = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.IsDiskOrPartitionSelected, x => x.HasChunks,
    (open, diskPart, chunks) => open && diskPart && chunks);

SaveAsDiskImageCommand = ReactiveCommand.CreateFromTask(
    () => mController!.SaveAsDiskImage(), canSaveAsDisk);

var canReplacePartition = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.CanWrite, x => x.IsPartitionSelected,
    (open, write, part) => open && write && part);

ReplacePartitionCommand = ReactiveCommand.CreateFromTask(
    () => mController!.ReplacePartition(), canReplacePartition);

// Always disabled (not yet implemented):
ScanForBadBlocksCommand = ReactiveCommand.Create(
    () => { /* Not yet implemented */ },
    Observable.Return(false));

// Always enabled — preserves existing NotImplemented() stub behavior:
OpenPhysicalDriveCommand = ReactiveCommand.CreateFromTask(
    () => mController!.NotImplemented("Open Physical Drive"));
```

> **Note:** `ScanForBadBlocksCommand` is always-disabled (no stub exists).
> `OpenPhysicalDriveCommand` is always-enabled, preserving the existing
> `NotImplemented("Open Physical Drive")` stub dialog to maintain behavioral parity.

#### 3.4 Special-Case Commands

Several commands cannot use the simple `() => mController!.DoSomething()`
pattern. Handle each as described below.

**`ExitCommand`** — calls `Window.Close()`, not a controller method. Interim
solution: add `MainController.RequestClose()` that calls `mMainWin.Close()`.
```csharp
ExitCommand = ReactiveCommand.Create(() => mController!.RequestClose());
```

**`HelpCommand`** — opens a browser URL with `Process.Start()`. No view
dependency; inline the body:
```csharp
HelpCommand = ReactiveCommand.Create(() => {
    try {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = "https://ciderpress2.com/gui-manual/",
            UseShellExecute = true
        });
    } catch (Exception) { /* ignore — no browser available */ }
});
```

**`AboutCommand`** — shows a dialog requiring a parent `Window` reference.
Interim solution: delegate to controller (it has the Window reference):
```csharp
AboutCommand = ReactiveCommand.CreateFromTask(
    () => mController!.ShowAboutDialog());
```
The controller method wraps `new AboutBox().ShowDialog(mMainWin)`. This
temporary coupling is removed in Phase 3 when `IDialogService` takes over.

**`SelectAllCommand`** — calls `fileListDataGrid.SelectAll()`, a direct
DataGrid control reference the ViewModel must not hold. Interim solution:
add `MainController.SelectAll()` that delegates to `mMainWin.SelectAllFileListItems()`
(defined in §3.0):
```csharp
SelectAllCommand = ReactiveCommand.Create(
    () => mController!.SelectAll(), canWhenOpen);
```

**`ResetSortCommand`** — has a hybrid body: clears DataGrid column tags
(view-specific) then calls `mController.PopulateFileList(...)`. Interim
solution: add `MainController.ResetSort()` that performs both operations
via `mMainWin`:
```csharp
ResetSortCommand = ReactiveCommand.Create(
    () => mController!.ResetSort(),
    this.WhenAnyValue(x => x.IsResetSortEnabled));
```

**`ShowFullListCommand` / `ShowDirListCommand` / `ShowInfoCommand` /
`ToggleInfoCommand`** — all four center-panel commands currently call
`SetShowCenterInfo()`, a `MainWindow` method that (a) checks `HasInfoOnly`,
(b) sets `mShowCenterInfo`, and (c) updates toolbar border brush properties.

Move `SetShowCenterInfo()` to the ViewModel so all four commands can call it
directly. The `HasInfoOnly` guard must be preserved — without it,
ShowFullList/ShowDirList would incorrectly switch away from the info panel
when only the info panel is available (e.g., archive tree root with no file
system).

`HasInfoOnly` is currently a private property on `MainWindow`. Since the
ViewModel's `SetShowCenterInfo()` references it, it must become a ViewModel
property. Add to `MainViewModel`:
```csharp
private bool mHasInfoOnly;
public bool HasInfoOnly {
    get => mHasInfoOnly;
    set => this.RaiseAndSetIfChanged(ref mHasInfoOnly, value);
}
```
Then apply these three changes to `MainWindow.axaml.cs`:

1. In `ConfigureCenterPanel()`, replace the `HasInfoOnly` assignment and
   if-else block with direct ViewModel property writes:
   ```csharp
   // Replace:
   HasInfoOnly = isInfoOnly;
   if (HasInfoOnly) { SetShowCenterInfo(CenterPanelChange.Info); }
   else             { SetShowCenterInfo(CenterPanelChange.Files); }
   // With:
   mViewModel.HasInfoOnly = isInfoOnly;
   mViewModel.ShowCenterInfo = isInfoOnly;  // true → Info panel, false → File list
   ```
   (The guard inside `SetShowCenterInfo` is not needed here because
   `ConfigureCenterPanel` is establishing the correct state, not responding
   to a user toggle.)

2. Delete `MainWindow.SetShowCenterInfo(CenterPanelChange req)` entirely.
   After Step 5 removes the four command-lambda callers and item 1 above
   replaces the `ConfigureCenterPanel` callers, this method has zero call
   sites. It must be deleted rather than left as dead code because it
   references the removed `HasInfoOnly` field and will not compile.

3. Delete the private `HasInfoOnly` property and its backing field
   (`mHasInfoOnly`) from `MainWindow.axaml.cs` — ownership has moved to
   `MainViewModel`.

The controller call sites in `MainController_Panels.cs` continue to call
`mMainWin.ConfigureCenterPanel(isInfoOnly: …)` as before — the ViewModel
write now happens inside that method.

The toolbar border brush updates (`InfoBorderBrush`, `FullListBorderBrush`,
`DirListBorderBrush`) should be handled reactively rather than imperatively.
Defer the brush logic to Phase 5: add AXAML data triggers or reactive
subscriptions that derive brush state from `ShowCenterInfo` and
`ShowSingleDirFileList`. For Phase 2, the brush updates are temporarily lost
(toolbar buttons won't highlight) — acceptable as an interim regression.

ViewModel helper (add to `MainViewModel`):
```csharp
private void SetShowCenterInfo(bool showInfo) {
    if (HasInfoOnly && !showInfo) {
        return;  // guard: can't switch away from info when it's the only option
    }
    ShowCenterInfo = showInfo;
}
```

Command bodies:
```csharp
ShowFullListCommand = ReactiveCommand.Create(() => {
    PreferSingleDirList = false;
    if (ShowSingleDirFileList) {
        ShowSingleDirFileList = false;
        mController!.PopulateFileList(IFileEntry.NO_ENTRY, false);
    }
    SetShowCenterInfo(false);
}, this.WhenAnyValue(x => x.IsFullListEnabled));

ShowDirListCommand = ReactiveCommand.Create(() => {
    PreferSingleDirList = true;
    if (!ShowSingleDirFileList) {
        ShowSingleDirFileList = true;
        mController!.PopulateFileList(IFileEntry.NO_ENTRY, false);
    }
    SetShowCenterInfo(false);
}, this.WhenAnyValue(x => x.IsDirListEnabled));

ShowInfoCommand = ReactiveCommand.Create(
    () => SetShowCenterInfo(true), canWhenOpen);

ToggleInfoCommand = ReactiveCommand.Create(
    () => SetShowCenterInfo(!ShowCenterInfo), canWhenOpen);
```

**`Debug_ShowDebugLogCommand` / `Debug_ShowDropTargetCommand`** — these
write VM properties after the controller call (post-call write-back):
```csharp
Debug_ShowDebugLogCommand = ReactiveCommand.Create(() => {
    mController!.Debug_ShowDebugLog();
    IsDebugLogVisible = mController.IsDebugLogOpen;
});

Debug_ShowDropTargetCommand = ReactiveCommand.Create(() => {
    mController!.Debug_ShowDropTarget();
    IsDropTargetVisible = mController.IsDropTargetOpen;
});
```

**`Debug_ConvertANICommand`** — uses `IsANISelected` (a controller state
flag that must be present on the ViewModel). Ensure `IsANISelected` exists
as a reactive property on `MainViewModel` (added in Phase 1A or add now):
```csharp
Debug_ConvertANICommand = ReactiveCommand.CreateFromTask(
    () => mController!.Debug_ConvertANI(),
    this.WhenAnyValue(x => x.IsANISelected));
```

#### 3.5 `ShowCenterFileList` and `IsMultiFileItemSelected`

Eight commands include `ShowCenterFileList` in their source `canExecute`
(five in the table below; the remaining three appear in the
`IsMultiFileItemSelected` table that follows). Four commands use
`IsMultiFileItemSelected` (distinct from `AreFileEntriesSelected`). Both
must be reactive properties on `MainViewModel` for `WhenAnyValue` to observe them.

**`ShowCenterFileList`** — computed from `!ShowCenterInfo` (the inverse of
the center-info-panel toggle). If `ShowCenterInfo` is already a VM property,
add `ShowCenterFileList` as a derived reactive property, or use
`!ShowCenterInfo` directly in the `WhenAnyValue` expressions.

Affected commands and their full canExecute predicates:

| Command | canExecute |
|---|---|
| `CopyCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `ViewFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `ExtractFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `ExportFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `TestFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |

Example for a 5-property canExecute:
```csharp
var canDeleteFiles = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.CanWrite,
    x => x.IsMultiFileItemSelected, x => x.AreFileEntriesSelected,
    x => x.ShowCenterFileList,
    (open, write, multi, sel, fileList) => open && write && multi && sel && fileList);

DeleteFilesCommand = ReactiveCommand.CreateFromTask(
    () => mController!.DeleteFiles(), canDeleteFiles);
```

**`IsMultiFileItemSelected`** — must be a ViewModel property (confirm it was
added in Phase 1A, or add it now). Affected commands and their full predicates:

| Command | canExecute |
|---|---|
| `PasteCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected` |
| `AddFilesCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected && ShowCenterFileList` |
| `ImportFilesCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected && ShowCenterFileList` |
| `DeleteFilesCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected && AreFileEntriesSelected && ShowCenterFileList` |

### Step 4: Subscribe to ThrownExceptions

After creating all commands, subscribe to `ThrownExceptions` on each.
Use an array/loop pattern to avoid missing any command:

```csharp
// Helper method on MainViewModel:
private void SubscribeErrors(ReactiveCommand<Unit, Unit> cmd) {
    cmd.ThrownExceptions.Subscribe(ex => {
        // TODO: Replace with IDialogService.ShowMessageAsync in Phase 3
        System.Diagnostics.Debug.WriteLine($"Command error: {ex.Message}");
    });
}

// In constructor, after all commands — iterate over all 51:
foreach (var cmd in new ReactiveCommand<Unit, Unit>[] {
    NewDiskImageCommand, NewFileArchiveCommand, OpenCommand,
    OpenPhysicalDriveCommand, CloseCommand, ExitCommand,
    RecentFile1Command, RecentFile2Command, RecentFile3Command,
    RecentFile4Command, RecentFile5Command, RecentFile6Command,
    CopyCommand, PasteCommand, FindCommand, SelectAllCommand,
    EditAppSettingsCommand, ViewFilesCommand, AddFilesCommand,
    ImportFilesCommand, ExtractFilesCommand, ExportFilesCommand,
    DeleteFilesCommand, TestFilesCommand, EditAttributesCommand,
    CreateDirectoryCommand, EditDirAttributesCommand,
    EditSectorsCommand, EditBlocksCommand, SaveAsDiskImageCommand,
    ReplacePartitionCommand, ScanForBadBlocksCommand,
    ScanForSubVolCommand, DefragmentCommand, CloseSubTreeCommand,
    ShowFullListCommand, ShowDirListCommand, ShowInfoCommand,
    NavToParentDirCommand, NavToParentCommand,
    HelpCommand, AboutCommand,
    Debug_DiskArcLibTestCommand, Debug_FileConvLibTestCommand,
    Debug_BulkCompressTestCommand, Debug_ShowSystemInfoCommand,
    Debug_ShowDebugLogCommand, Debug_ShowDropTargetCommand,
    Debug_ConvertANICommand,
    ResetSortCommand, ToggleInfoCommand
}) {
    SubscribeErrors(cmd);
}
```

### Step 5: Remove Commands from MainWindow.axaml.cs

Remove all 51 `ICommand` property declarations and their initialization code
from the `MainWindow` constructor. The command creation code (the large block
of `new RelayCommand(...)` calls) is deleted entirely.

Also remove the Iteration 1B pass-through assignment block — the lines of
the form `ViewModel.OpenCommand = OpenCommand;` for all 51 commands. These
temporary assignments are superseded now that the ViewModel owns the
`ReactiveCommand` instances directly.

### Step 5B: Remove Orphaned `ResetSortCommand` References

After Step 5 removes the `ResetSortCommand` property from `MainWindow`,
two lines in `MainWindow.axaml.cs` still reference it as a Window property
and will cause compile errors:

1. In `FileListDataGrid_Sorting` (≈line 1582), delete:
   ```csharp
   ((RelayCommand)ResetSortCommand).RaiseCanExecuteChanged();
   ```
   The preceding line `IsResetSortEnabled = true;` already triggers the
   `ReactiveCommand`'s `WhenAnyValue(x => x.IsResetSortEnabled)` canExecute
   observable — no manual invalidation needed.

2. In the `ResetSortCommand` initialization lambda (≈line 1174), delete:
   ```csharp
   ((RelayCommand?)ResetSortCommand)?.RaiseCanExecuteChanged();
   ```
   This line is inside the command body that is being removed entirely in
   Step 5, but if the lambda is migrated to the controller's `ResetSort()`
   method (Step 3.0), ensure the `RaiseCanExecuteChanged()` call is **not**
   carried over. Setting `IsResetSortEnabled = false` is sufficient.

### Step 5A: Update `PopulateRecentFilesMenu`

`MainWindow.PopulateRecentFilesMenu()` builds an `ICommand[]` array
referencing the Window-owned command properties. After Step 5 removes
those properties, this method must read from the ViewModel instead:

```csharp
var vm = DataContext as MainViewModel;
ICommand[] commands = {
    vm!.RecentFile1Command, vm.RecentFile2Command, vm.RecentFile3Command,
    vm.RecentFile4Command, vm.RecentFile5Command, vm.RecentFile6Command
};
```

`ReactiveCommand<Unit, Unit>` implements `ICommand`, so the rest of the
method (building `MenuItem` items and native macOS `NativeMenuItem` items)
works unchanged.

### Step 6: Remove Command Invalidation Infrastructure

Three removal targets — all are superseded by `ReactiveCommand` auto-evaluation
via `WhenAnyValue`:

1. **Delete `InvalidateCommands()`** from `MainWindow.axaml.cs`. This is the
   reflection-based method that iterates all `ICommand` properties and calls
   `RaiseCanExecuteChanged()` on each.

2. **Delete `RefreshAllCommandStates()`** from `MainController_Panels.cs`. This
   method explicitly calls `RaiseCanExecuteChanged()` on ~30 individual
   `RelayCommand` references. With `ReactiveCommand`, `CanExecute` re-evaluates
   automatically when observed properties change. Also remove the two calls to
   `RefreshAllCommandStates()` in the same file — inside
   `ArchiveTree_SelectionChanged` (≈line 429) and
   `DirectoryTree_SelectionChanged` (≈line 496). Leaving these call sites after
   deleting the method will produce compile errors.

3. **Delete the `mMainCtrl.RefreshAllCommandStates()` call** in
   `MainWindow.axaml.cs` inside `FileListDataGrid_SelectionChanged` (≈line 1537).
   This call site also references the deleted method and will cause a compile
   error. The `ReactiveCommand` `canExecute` observables make it unnecessary.

4. **Remove the two `mMainWin.InvalidateCommands()` calls** in
   `MainController.cs` — one in the file-open path and one in the file-close
   path. These reference the method deleted in step 1; leaving them causes
   compile errors.

The ViewModel boolean properties from Iteration 1B (which the controller
already sets) drive `ReactiveCommand` `CanExecute` state automatically.

### Step 7: Update App.axaml.cs Native Menu Handlers

`App.axaml.cs` has native macOS menu handlers that execute commands on
`MainWindow`:

```csharp
private void OnNativeAboutClick(object? sender, EventArgs e) =>
    GetMainWindow()?.AboutCommand?.Execute(null);
```

These must now go through the ViewModel.

**Important:** `ReactiveCommand<Unit, Unit>.Execute()` takes a `Unit`
parameter (a value type). Passing `null` will not compile. Use
`Unit.Default`:

```csharp
private MainViewModel? GetMainViewModel() =>
    (GetMainWindow()?.DataContext) as MainViewModel;

private void OnNativeAboutClick(object? sender, EventArgs e) =>
    GetMainViewModel()?.AboutCommand.Execute(Unit.Default).Subscribe();

private void OnNativeSettingsClick(object? sender, EventArgs e) =>
    GetMainViewModel()?.EditAppSettingsCommand.Execute(Unit.Default).Subscribe();

private void OnNativeQuitClick(object? sender, EventArgs e) =>
    GetMainViewModel()?.ExitCommand.Execute(Unit.Default).Subscribe();
```

Note: `ReactiveCommand.Execute()` returns an `IObservable<Unit>` that must
be subscribed to (`.Subscribe()` with no args for fire-and-forget). The
null-conditional `?.` on the command itself is removed — if `GetMainViewModel()`
is null the entire expression short-circuits safely; but if it is non-null,
the command is guaranteed to exist (it's set in the constructor).

### Step 8: Build and Validate

1. Run `dotnet build` — verify zero errors.
2. Launch the application.
3. **Systematically verify every command:**

   **File menu:**
   - File → New Disk Image
   - File → New File Archive
   - File → Open (open a disk image)
   - File → Close
   - File → Recent Files (if any)
   - File → Exit

   **Edit menu:**
   - Edit → Copy (with files selected)
   - Edit → Paste
   - Edit → Find
   - Edit → Select All
   - Edit → Application Settings

   **Actions menu (with a file open and entries selected):**
   - View Files, Add Files, Import Files
   - Extract Files, Export Files
   - Delete Files, Test Files
   - Edit Attributes, Create Directory
   - Edit Sectors / Edit Blocks (with appropriate disk image)
   - Save As Disk Image, Replace Partition (with appropriate image)

   **View menu:**
   - Full List, Directory List, Info toggles
   - Navigate to Parent Dir, Navigate to Parent

   **Help menu:**
   - Help, About

   **Toolbar:**
   - Reset Sort, Toggle Info

4. **Verify canExecute state:**
   - With no file open: only Open, New, Settings, Help, About, Exit should
     be enabled
   - Open a file in read-only mode: write commands should be disabled
   - Select entries: selection-dependent commands should enable
   - Deselect: selection-dependent commands should disable

5. **Verify macOS native menu** (if on macOS): About, Settings, Quit.

**Expected result:** All commands work identically through the ViewModel.
No `RelayCommand` instances remain on `MainWindow`.

---

## What This Enables

- All commands now live on `MainViewModel` with reactive `CanExecute`.
- Phase 3 will dissolve `MainController`, moving command bodies into the VM
  and services. The `mController!.DoSomething()` calls will be inlined.
- `RelayCommand` is no longer used by `MainWindow` (it may still be used by
  dialog code-behind until Phase 4).
