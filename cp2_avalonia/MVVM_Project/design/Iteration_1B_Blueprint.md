# Iteration 1B Blueprint: Wire MainViewModel as DataContext

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 Phase 1, §7.13, §7.20.

---

## Goal

Switch `MainWindow.DataContext` from `this` to a `MainViewModel` instance,
remove the duplicate properties from `MainWindow.axaml.cs`, and wire the
**interim controller reference** so that `MainController` can read/write
ViewModel properties. After this iteration, all AXAML bindings resolve against
`MainViewModel` and the application still works identically.

---

## Prerequisites

- Iteration 1A is complete (`MainViewModel` exists with all properties).
- The application builds and runs correctly.

---

## Step-by-Step Instructions

### Step 0: Change MainWindow Base Class to ReactiveWindow

MVVM_Notes.md §6 Phase 1B step 2 requires `MainWindow` to inherit from
`ReactiveWindow<MainViewModel>`. This provides the typed `ViewModel` property,
enables `WhenActivated` for subscription lifecycle management, and implements
`IViewFor<MainViewModel>` (needed by downstream iterations).

In `MainWindow.axaml.cs`:

1. Change the class declaration:
   ```csharp
   // Before:
   public partial class MainWindow : Window, INotifyPropertyChanged {

   // After:
   public partial class MainWindow : ReactiveWindow<MainViewModel> {
   ```
2. Add `using ReactiveUI;` if not already present.
3. Remove the hand-rolled `INotifyPropertyChanged` implementation
   (`PropertyChanged` event, `OnPropertyChanged()` method) — `ReactiveWindow`
   provides change notification through its base class chain.
4. Do **not** add a manual `ViewModel` cast accessor — `ReactiveWindow<T>`
   provides a typed `ViewModel` property automatically.

In `MainWindow.axaml`:

1. Update the root element namespace (if not already present):
   ```xml
   xmlns:rxui="https://reactiveui.net"
   ```
2. Change the root element from `<Window ...>` to:
   ```xml
   <rxui:ReactiveWindow x:TypeArguments="vm:MainViewModel" ...>
   ```
   (where `vm:` is the namespace prefix for `cp2_avalonia.ViewModels`).

### Step 1: Instantiate MainViewModel in MainWindow

In `MainWindow.axaml.cs`, in the constructor (or `Loaded` handler):

1. Replace `DataContext = this;` with:

```csharp
var viewModel = new MainViewModel();
DataContext = viewModel;
```

2. `ReactiveWindow<MainViewModel>` provides a typed `ViewModel` property
   automatically — no manual cast accessor needed. Use `this.ViewModel`
   (or just `ViewModel`) throughout code-behind.

### Step 2: Give MainController a VM Reference + Forward Commands

Currently `MainController` takes `MainWindow` in its constructor. Add a
`MainViewModel` parameter (or set it as a property after construction):

```csharp
// In MainController.cs:
private MainViewModel mViewModel;

// Option A: Constructor parameter
public MainController(MainWindow mainWin, MainViewModel viewModel) {
    mMainWin = mainWin;
    mViewModel = viewModel;
    // ...
}

// Option B: Property setter (if constructor changes are disruptive)
public void SetViewModel(MainViewModel vm) { mViewModel = vm; }
```

In `MainWindow.axaml.cs`, update the `MainController` creation to pass the VM:

```csharp
mMainCtrl = new MainController(this, ViewModel);
```

**Forward all 51 commands to `MainViewModel` as temporary pass-through properties.**
After Step 1 changes `DataContext` from `this` to `MainViewModel`, all AXAML
command bindings (`{Binding OpenCommand}`, etc.) resolve against the ViewModel.
Since commands remain on `MainWindow` until Iteration 2, nulls would silently
disable every menu item, toolbar button, and key binding. To prevent this:

1. Add a plain `ICommand?` property on `MainViewModel` for each command
   (no `RaiseAndSetIfChanged` needed — they are set once and never change):
   ```csharp
   // In MainViewModel — temporary, removed in Iteration 2:
   public ICommand? OpenCommand { get; set; }
   public ICommand? CloseCommand { get; set; }
   // ... for all 51 commands
   ```
   The complete list is found by scanning all `public ICommand` property
   declarations in `MainWindow.axaml.cs` (~lines 58–122). The
   `RefreshAllCommandStates()` method in `MainController_Panels.cs` names
   31 of them and serves as a useful cross-check.
2. In `MainWindow.axaml.cs`, after creating both the VM and the controller,
   assign each command:
   ```csharp
   ViewModel.OpenCommand = OpenCommand;
   ViewModel.CloseCommand = CloseCommand;
   // ... for all 51 commands
   ```

These pass-through properties will be replaced by `ReactiveCommand` instances
in Iteration 2.

### Step 3: Redirect Controller Property Access

In `MainController.cs` and `MainController_Panels.cs`, systematically replace
references to `mMainWin.PropertyName` with `mViewModel.PropertyName` for every
property that has moved to `MainViewModel`. This is the bulk of the work.

**Pattern:**
```csharp
// Before:
mMainWin.LaunchPanelVisible = true;
mMainWin.MainPanelVisible = true;
mMainWin.CenterStatusText = "Ready";

// After:
mViewModel.LaunchPanelVisible = true;
mViewModel.MainPanelVisible = true;
mViewModel.CenterStatusText = "Ready";
```

**Complete list of properties to redirect** (all from Iteration 1A):
- Panel visibility: `LaunchPanelVisible`, `MainPanelVisible`, `ShowOptionsPanel`, `ShowHideRotation`
- Debug: `ShowDebugMenu`, `IsDebugLogVisible`, `IsDropTargetVisible`
- Status: `CenterStatusText`, `RightStatusText`, `ProgramVersionString`
- Trees: `ArchiveTreeRoot`, `DirectoryTreeRoot`
- File list: `FileList`
- Recent files: `RecentFileName1/2`, `RecentFilePath1/2`, `ShowRecentFile1/2`
- Converters: `ImportConverters`, `ExportConverters`, `SelectedImportConverter`, `SelectedExportConverter`
- Options toggles: All `IsChecked_*` properties, `SelectedDDCPModeIndex`, `IsExportBestChecked`, `IsExportComboChecked`
- Toolbar: `FullListBorderBrush`, `DirListBorderBrush`, `InfoBorderBrush`
- Center panel: `ShowCenterFileList`, `ShowCenterInfoPanel`, `IsFullListEnabled`, `IsDirListEnabled`, `IsResetSortEnabled`, `ShowSingleDirFileList`
- Columns: `ShowCol_FileName`, `ShowCol_PathName`, `ShowCol_Format`, `ShowCol_RawLen`, `ShowCol_RsrcLen`, `ShowCol_TotalSize`
- Info: `CenterInfoText1`, `CenterInfoText2`, `CenterInfoList`, `ShowDiskUtilityButtons`, `ShowPartitionLayout`, `PartitionList`, `ShowNotes`, `NotesList`, `MetadataList`, `ShowMetadata`, `CanAddMetadataEntry`

**Also update type qualifiers.** In addition to property prefixes, update any
`MainWindow.`-qualified TYPE references in the controller to use the unqualified
name (these classes were moved to `cp2_avalonia/Models/` in Iteration 0):

```csharp
// Before:
mMainWin.CenterInfoList.Add(new MainWindow.CenterInfoItem(name + ":", value));
public void HandlePartitionLayoutDoubleClick(MainWindow.PartitionListItem item, ...)
public async Task HandleMetadataDoubleClick(MainWindow.MetadataItem item, ...)

// After (add `using cp2_avalonia.Models;` to MainController_Panels.cs if needed):
mViewModel.CenterInfoList.Add(new CenterInfoItem(name + ":", value));
public void HandlePartitionLayoutDoubleClick(PartitionListItem item, ...)
public async Task HandleMetadataDoubleClick(MetadataItem item, ...)
```

### Step 4: Redirect Controller Method Calls

For helper methods that moved to `MainViewModel` in 1A, change calls:

```csharp
// Before:
mMainWin.ClearCenterInfo();
mMainWin.ConfigureCenterPanel(isInfoOnly, isArchive, isHierarchic, hasRsrc, hasRaw);
mMainWin.PublishSideOptions();

// After:
mViewModel.ClearCenterInfo();
mViewModel.ConfigureCenterPanel(isInfoOnly, isArchive, isHierarchic, hasRsrc, hasRaw);
mViewModel.PublishSideOptions();
```

Also redirect these additional methods that operate on ViewModel-owned data:

```csharp
// Before:
mMainWin.ClearTreesAndLists();
mMainWin.SetNotesList(notes);
mMainWin.SetPartitionList(parts);
mMainWin.SetMetadataList(metaObj);

// After:
mViewModel.ClearTreesAndLists();
mViewModel.SetNotesList(notes);
mViewModel.SetPartitionList(parts);
mViewModel.SetMetadataList(metaObj);
```

Also redirect the three individual metadata mutation methods:

```csharp
// Before:
mMainWin.RemoveMetadata(item.Key);
mMainWin.UpdateMetadata(item.Key, value);
mMainWin.AddMetadata(entry, value);

// After:
mViewModel.RemoveMetadata(item.Key);
mViewModel.UpdateMetadata(item.Key, value);
mViewModel.AddMetadata(entry, value);
```

`ClearTreesAndLists()` clears `ArchiveTreeRoot`, `DirectoryTreeRoot`, `FileList`,
`IsFullListEnabled`, and `IsDirListEnabled` — all ViewModel properties.
`SetNotesList`, `SetPartitionList`, `SetMetadataList`, `UpdateMetadata`,
`AddMetadata`, and `RemoveMetadata` operate on the ViewModel-owned
`MetadataList` `ObservableCollection`.

### Step 5: Keep View-Only References in Code-Behind

Some `mMainWin.` references in `MainController` access **Avalonia controls**
and must continue going through `mMainWin` (not the ViewModel):

- `mMainWin.fileListDataGrid` (SelectedItem, SelectedItems, ScrollIntoView)
- `mMainWin.archiveTree` (TreeView control)
- `mMainWin.directoryTree` (TreeView control)
- `mMainWin.Cursor` (wait cursor)
- `mMainWin.Title` (window title — bind to VM in AXAML instead)
- `mMainWin.Activate()` (bring window to front)

Also keep `mMainWin` calls for methods that stayed in code-behind:
- `mMainWin.PostNotification(msg, success)`
- `mMainWin.FileList_ScrollToTop()`
- `mMainWin.FileList_SetSelectionFocus()`
- `mMainWin.DirectoryTree_ScrollToTop()`
- `mMainWin.ReapplyFileListSort()`
- `mMainWin.InvalidateCommands()`
- `mMainWin.PopulateRecentFilesMenu(RecentFilePaths)` (constructs `MenuItem`
  objects and manipulates native macOS `NativeMenuItem` sub-menus — purely view code)
- `mMainWin.LeftPanelWidth` (reads/writes `mainTriptychPanel.ColumnDefinitions[0].Width`;
  no binding mechanism exists, so settings save/restore must go through `mMainWin`)
- `mMainWin.SelectedFileListItem` (reads `fileListDataGrid.SelectedItem`)
- `mMainWin.SelectedArchiveTreeItem` (reads `archiveTree.SelectedItem`)
- `mMainWin.SelectedDirectoryTreeItem` (reads `directoryTree.SelectedItem`)

The three `Selected*` properties are control-backed read-through accessors with
no AXAML binding for selection. They must remain on `MainWindow` until Phase 3B
introduces the ViewModel-owned selection pattern (VM property + View-side sync
from `SelectionChanged` handler).

These will be further refactored in later iterations. For now, the controller
retains the `mMainWin` reference alongside `mViewModel`.

### Step 6: Remove Duplicate Properties from MainWindow

Remove all the property declarations, backing fields, and `OnPropertyChanged`
calls from `MainWindow.axaml.cs` that have moved to `MainViewModel`. Also
remove the `OnPropertyChanged` method and the `INotifyPropertyChanged`
implementation if no properties remain.

**Keep in MainWindow.axaml.cs:**
- The `INotifyPropertyChanged` implementation **only if** some properties
  still remain (if all moved, remove it entirely)
- Event handlers (drag-drop, sorting, selection changed, etc.)
- Named control references (for code-behind access)
- The constructor (minus command creation, which moves in Iteration 2)
- `PostNotification()`, scroll/focus methods, and other UI-specific methods

**Important:** After removing properties, update any code-behind event handlers
and command lambdas that referenced them directly to go through the ViewModel
instead:

1. `ShowHideOptionsButton_Click` — change `ShowOptionsPanel = !ShowOptionsPanel`
   to `ViewModel.ShowOptionsPanel = !ViewModel.ShowOptionsPanel`.
2. `Debug_ShowDebugLogCommand` lambda — change `IsDebugLogVisible = mMainCtrl.IsDebugLogOpen`
   to `ViewModel.IsDebugLogVisible = mMainCtrl.IsDebugLogOpen`.
3. `Debug_ShowDropTargetCommand` lambda — change `IsDropTargetVisible = mMainCtrl.IsDropTargetOpen`
   to `ViewModel.IsDropTargetVisible = mMainCtrl.IsDropTargetOpen`.
4. `ShowFullListCommand` lambda — change `PreferSingleDirList = false` to
   `ViewModel.PreferSingleDirList = false`; change `ShowSingleDirFileList`
   reads/writes to `ViewModel.ShowSingleDirFileList`; change canExecute
   `IsFullListEnabled` to `ViewModel.IsFullListEnabled`.
5. `ShowDirListCommand` lambda — change `PreferSingleDirList = true` to
   `ViewModel.PreferSingleDirList = true`; change `ShowSingleDirFileList`
   reads/writes to `ViewModel.ShowSingleDirFileList`; change canExecute
   `IsDirListEnabled` to `ViewModel.IsDirListEnabled`.
6. `ResetSortCommand` lambda — change `IsResetSortEnabled = false` to
   `ViewModel.IsResetSortEnabled = false`; change canExecute `IsResetSortEnabled`
   to `ViewModel.IsResetSortEnabled`.
7. `FileListDataGrid_Sorting` handler — change `IsResetSortEnabled = true` to
   `ViewModel.IsResetSortEnabled = true`; change `FileList` accesses to
   `ViewModel.FileList`.
8. `SetShowCenterInfo()` — this private method is called by
   `ShowFullListCommand`, `ShowDirListCommand`, `ShowInfoCommand`, and
   `ToggleInfoCommand`. It references `ShowCenterFileList`,
   `ShowCenterInfoPanel`, `InfoBorderBrush`, `FullListBorderBrush`,
   `DirListBorderBrush`, and `ShowSingleDirFileList` — all removed.
   **Note:** Iteration 1A created `SetShowCenterInfo(bool showInfo)` on
   `MainViewModel`. **Delete** that bool overload and replace it with the
   enum-based version below. The bool signature cannot represent the Toggle
   case needed by `ToggleInfoCommand`. The full switch logic from the
   existing `MainWindow` implementation must be preserved.

   Move the logic to `MainViewModel` as an `internal` method and have
   the MainWindow wrapper delegate:
   ```csharp
   // MainViewModel (replaces the 1A-created bool overload):
   internal void SetShowCenterInfo(CenterPanelChange req) {
       if (HasInfoOnly && req != CenterPanelChange.Info) {
           Debug.WriteLine("Ignoring attempt to switch to file list");
           return;
       }
       switch (req) {
           case CenterPanelChange.Info:   mShowCenterInfo = true;  break;
           case CenterPanelChange.Files:  mShowCenterInfo = false; break;
           case CenterPanelChange.Toggle: mShowCenterInfo = !mShowCenterInfo; break;
       }
       this.RaisePropertyChanged(nameof(ShowCenterFileList));
       this.RaisePropertyChanged(nameof(ShowCenterInfoPanel));
       if (mShowCenterInfo) {
           InfoBorderBrush = ToolbarHighlightBrush;
           FullListBorderBrush = DirListBorderBrush = ToolbarNohiBrush;
       } else if (ShowSingleDirFileList) {
           DirListBorderBrush = ToolbarHighlightBrush;
           FullListBorderBrush = InfoBorderBrush = ToolbarNohiBrush;
       } else {
           FullListBorderBrush = ToolbarHighlightBrush;
           DirListBorderBrush = InfoBorderBrush = ToolbarNohiBrush;
       }
   }

   // MainWindow.axaml.cs (thin wrapper for Phase 1B command lambdas):
   private void SetShowCenterInfo(CenterPanelChange req) {
       ViewModel.SetShowCenterInfo(req);
   }
   ```
   When moving `SetShowCenterInfo()` to `MainViewModel`, also move these
   private members that it (and `ConfigureCenterPanel()`) depend on:
   - `mHasInfoOnly` / `HasInfoOnly` — private field + property
   - `ToolbarHighlightBrush` — `private static readonly IBrush` (`Brushes.Green`)
   - `ToolbarNohiBrush` — `private static readonly IBrush` (`Brushes.Transparent`)

   Also add a temporary private property on `MainViewModel` for
   `PreferSingleDirList` (read by `ConfigureCenterPanel()`, written by
   `ShowFullListCommand`/`ShowDirListCommand` lambdas):
   ```csharp
   // In MainViewModel — temporary, promoted to ISettingsService in Phase 3A.
   // Must be internal so ShowFullListCommand/ShowDirListCommand lambdas in
   // MainWindow.axaml.cs can access it:
   internal bool PreferSingleDirList {
       get => AppSettings.Global.GetBool(AppSettings.FILE_LIST_PREFER_SINGLE, true);
       set => AppSettings.Global.SetBool(AppSettings.FILE_LIST_PREFER_SINGLE, value);
   }
   ```

   Move `CenterPanelChange` enum to `cp2_avalonia/Models/CenterPanelChange.cs`:
   ```csharp
   // cp2_avalonia/Models/CenterPanelChange.cs
   namespace cp2_avalonia.Models;
   public enum CenterPanelChange { Unknown = 0, Files, Info, Toggle }
   ```
   Both `MainWindow` and `MainViewModel` reference the short name
   `CenterPanelChange.Files` with `using cp2_avalonia.Models;`. Delete the
   original enum definition from `MainWindow.axaml.cs`.

9. `FileList_ScrollToTop()`, `FileList_SetSelectionFocus()`, and
   `ReapplyFileListSort()` — these methods remain in `MainWindow.axaml.cs`
   (Step 5) but reference `FileList` directly (e.g., `FileList[0]`,
   `FileList.Count`, iterating and clearing `FileList`). Since `FileList`
   has been removed from `MainWindow` and moved to `MainViewModel`, update
   all bare `FileList` references in these methods to `ViewModel.FileList`.

10. **`ShowCenterFileList` in `canExecute` lambdas and event handlers** —
    Step 6 removes `ShowCenterFileList` from `MainWindow`, but approximately
    eight command `canExecute` lambdas reference it directly:
    `ViewFilesCommand`, `AddFilesCommand`, `ImportFilesCommand`,
    `ExtractFilesCommand`, `ExportFilesCommand`, `DeleteFilesCommand`,
    `TestFilesCommand`, and `EditAttributesCommand` all contain
    `&& ShowCenterFileList` in their `canExecute` predicates. The
    `FileListDataGrid_DragOver` handler also references it.
    Replace every bare `ShowCenterFileList` in these locations with
    `ViewModel.ShowCenterFileList`. (Alternatively, add a private
    forwarding property `private bool ShowCenterFileList => ViewModel.ShowCenterFileList;`
    on `MainWindow` during Iteration 1B to minimize churn — either approach is
    acceptable as long as it compiles.)

**Also delete the following method bodies** from `MainWindow.axaml.cs`, since
they have been fully migrated to `MainViewModel` (Step 4 already redirected
all controller calls to the ViewModel copies):

- `ConfigureCenterPanel()` — references `ShowSingleDirFileList`,
  `IsFullListEnabled`, `IsDirListEnabled`, `ShowCol_*`, and calls
  `SetShowCenterInfo()` (all removed/moved).
- `PublishSideOptions()` — calls `OnPropertyChanged` for every `IsChecked_*`
  and converter property (all removed).
- `ClearTreesAndLists()` — sets `IsFullListEnabled = false`,
  `IsDirListEnabled = false` (removed).
- `ClearCenterInfo()` — sets `ShowDiskUtilityButtons`, `ShowPartitionLayout`,
  `ShowNotes`, `ShowMetadata`, `CanAddMetadataEntry` and clears collections
  (all removed).
- `SetNotesList()`, `SetPartitionList()`, `SetMetadataList()` — populate
  ViewModel-owned collections.
- `UpdateMetadata()`, `AddMetadata()`, `RemoveMetadata()` — mutate the
  ViewModel-owned `MetadataList` collection.
- `InitImportExportConfig()` — adds to `ImportConverters`/`ExportConverters`
  and sets `SelectedImportConverter`/`SelectedExportConverter` (all removed).
  **This is called from the Loaded handler** — update that call to
  `ViewModel.InitImportExportConfig()` (or remove it if the ViewModel's
  constructor already performs this initialization).

**Wire column visibility via `WhenAnyValue` subscriptions.** The six `ShowCol_*`
properties previously called `SetColumnVisible()` directly in their `MainWindow`
setters. Since DataGrid columns are not in the visual tree, AXAML `IsVisible`
bindings do not work (see comment at line 658 of `MainWindow.axaml`). Add
`WhenAnyValue` subscriptions in `MainWindow.axaml.cs` (in the Loaded handler,
after `fileListDataGrid` is guaranteed to exist):

```csharp
// In MainWindow.axaml.cs Loaded handler, inside WhenActivated:
this.WhenActivated(disposables => {
    ViewModel.WhenAnyValue(vm => vm.ShowCol_FileName)
        .Subscribe(v => SetColumnVisible("Filename", v))
        .DisposeWith(disposables);
    ViewModel.WhenAnyValue(vm => vm.ShowCol_PathName)
        .Subscribe(v => SetColumnVisible("Pathname", v))
        .DisposeWith(disposables);
    ViewModel.WhenAnyValue(vm => vm.ShowCol_Format)
        .Subscribe(v => SetColumnVisible("Data Fmt", v))
        .DisposeWith(disposables);
    ViewModel.WhenAnyValue(vm => vm.ShowCol_RawLen)
        .Subscribe(v => SetColumnVisible("Raw Len", v))
        .DisposeWith(disposables);
    ViewModel.WhenAnyValue(vm => vm.ShowCol_RsrcLen)
        .Subscribe(v => SetColumnVisible("Rsrc Len", v))
        .DisposeWith(disposables);
    ViewModel.WhenAnyValue(vm => vm.ShowCol_TotalSize)
        .Subscribe(v => SetColumnVisible("Total Size", v))
        .DisposeWith(disposables);
});
```

The `WhenActivated` + `DisposeWith` pattern is required by Pre-Iteration-Notes.md
(enforcement rule: subscriptions must wire disposal at the point of creation).
`ReactiveWindow<MainViewModel>` (from Step 0) provides `WhenActivated` support.
Add `using ReactiveUI;` and `using System.Reactive.Disposables;` if not already
present.

These subscriptions replace the inline `SetColumnVisible()` calls that the
MainWindow property setters previously provided. `SetColumnVisible()` itself
remains on `MainWindow` as a private helper.

### Step 7: Bind Window Title in AXAML

Add a `WindowTitle` property to `MainViewModel` if not already present.
In `MainWindow.axaml`, bind the title:

```xml
<Window ... Title="{Binding WindowTitle}">
```

Remove any `mMainWin.Title = ...` assignments from `MainController` and
replace with `mViewModel.WindowTitle = ...`.

### Step 8: Add CanExecute State Properties to ViewModel

**8a.** Add the following boolean properties to `MainViewModel` (using the
standard `RaiseAndSetIfChanged` pattern). These will be consumed by
`ReactiveCommand.CanExecute` in Phase 2:

- `IsFileOpen`
- `CanWrite`
- `AreFileEntriesSelected`
- `IsSingleEntrySelected`
- `IsMultiFileItemSelected`

(Add others as needed based on the controller's existing computed predicates.)

**8b.** In `MainController`, set these VM properties at the points where
the underlying state changes. For example:
- After opening/closing a file: `mViewModel.IsFileOpen = mWorkTree != null;`
- After selection changes: the code-behind `FileListDataGrid_SelectionChanged`
  handler should set `ViewModel.AreFileEntriesSelected = fileListDataGrid.SelectedIndex >= 0;`

**8c.** Leave `RefreshAllCommandStates()` **unchanged** for now. Its body is
a sequence of `RaiseCanExecuteChanged()` calls on `MainWindow`'s `RelayCommand`
instances — this is correct while commands remain on `MainWindow`. It will be
**eliminated** in Iteration 2 when `ReactiveCommand` auto-reevaluates via
`WhenAnyValue`.

### Step 9: Build and Validate

1. Run `dotnet build` — verify zero errors.
2. Launch the application.
3. **Verify all panels render correctly:**
   - Launch panel visible on startup
   - Open a disk image → main panel appears, launch panel hides
   - Archive tree populates
   - Directory tree populates
   - File list populates
   - Center info panel shows disk information
   - Options panel toggles work
   - Column visibility settings work
4. **Verify status bar** updates on file open/navigation.
5. **Verify recent files** menu works.
6. **Verify toolbar highlights** (full list/dir list/info) toggle correctly.
7. **Verify all menu items and toolbar buttons execute correctly** — not just
   that they appear enabled, but that they actually fire their actions:
   - Open a file → toolbar "Show Full List" button should be enabled *and*
     respond to click.
   - Confirm at least one keyboard shortcut (e.g., Ctrl+O) works.
   - Confirm context menu items in the file list work.
   (Commands still use `RelayCommand` on `MainWindow`; they are forwarded to
   the ViewModel as pass-through `ICommand?` properties — those become
   `ReactiveCommand` in Iteration 2.)
   **Note:** In Avalonia, a null command binding silently disables the control
   with no error. If pass-through forwarding is broken, controls will appear
   grayed out rather than producing an exception.
8. Close and reopen — verify settings persist.

**Expected result:** The application is functionally identical, but
`DataContext` is now `MainViewModel` instead of `MainWindow`. The controller
reads/writes VM properties via `mViewModel`.

---

## What This Enables

- Iteration 2 will move all 51 commands from `MainWindow` to `MainViewModel`
  as `ReactiveCommand` instances, using `WhenAnyValue` over the VM's
  `IsFileOpen`, `CanWrite`, etc. properties for `canExecute`.
- AXAML `{Binding SomeCommand}` will resolve against the ViewModel.
