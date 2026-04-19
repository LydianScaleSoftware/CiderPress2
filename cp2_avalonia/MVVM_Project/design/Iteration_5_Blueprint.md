# Iteration 5 Blueprint: Extract Child ViewModels from MainViewModel

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 Phase 5, §7.22.

---

## Goal

Extract six child ViewModels from `MainViewModel` to reduce its size and
improve separation of concerns. Each child ViewModel owns the properties,
commands, and logic for one distinct UI panel. `MainViewModel` composes them
and mediates inter-panel communication.

**This phase is mandatory** (per §7.22) — without it, `MainViewModel` will
be ~3,000+ lines and unmanageable.

---

## Prerequisites

- Iteration 4B is complete (all dialogs use ViewModels).
- `MainViewModel` is the single large ViewModel containing all panel logic.
- The application builds and runs correctly.

---

## Child ViewModels to Extract

| Child ViewModel | Panel | Key Responsibilities |
|---|---|---|
| `ArchiveTreeViewModel` | Left panel (top) | Archive tree root collection, selection tracking, population, sub-tree close |
| `DirectoryTreeViewModel` | Left panel (bottom) | Directory tree root collection, selection tracking, population |
| `FileListViewModel` | Center panel (main) | File list collection, sorting, column configuration, selection, double-click |
| `CenterInfoViewModel` | Center panel (info) | Info key/value pairs, partition layout, metadata entries |
| `OptionsPanelViewModel` | Right panel | All option checkboxes (add/extract settings), DDCP mode, converter selection |
| `StatusBarViewModel` | Bottom bar | Center status text, right status text, entry counts |

---

## Step-by-Step Instructions

### Step 1: Create ArchiveTreeViewModel

```csharp
// cp2_avalonia/ViewModels/ArchiveTreeViewModel.cs
namespace cp2_avalonia.ViewModels;

using System.Collections.ObjectModel;
using ReactiveUI;

public class ArchiveTreeViewModel : ReactiveObject {
    public ObservableCollection<ArchiveTreeItem> TreeRoot { get; } = new();

    private ArchiveTreeItem? _selectedItem;
    public ArchiveTreeItem? SelectedItem {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    // Moved from MainViewModel:
    // - ArchiveTree selection caching

    /// <summary>
    /// Retains the last non-null SelectedItem. Used by cross-VM
    /// coordination methods (NavToParent, etc.) that need the most
    /// recent meaningful selection even after the tree is cleared.
    /// Updated in the SelectedItem setter: if (value != null) CachedSelectedItem = value;
    /// Reset to null on workspace close.
    /// </summary>
    public ArchiveTreeItem? CachedSelectedItem { get; private set; }
}
```

**Move from MainViewModel:**
- `ArchiveTreeRoot` property → `TreeRoot`
- Selection changed handling → `SelectedItem` setter or a
  `WhenAnyValue(x => x.SelectedItem)` subscription

**Methods that stay on MainViewModel** (they require `WorkTree` access):
- `PopulateArchiveTree()` — calls `ArchiveTreeItem.ConstructTree(root, mWorkTree.RootNode)`;
  populates `ArchiveTree.TreeRoot` but must be called from `MainViewModel`
  where `IWorkspaceService` is available
- `CloseSubTree()` / `CloseSubTree(ArchiveTreeItem)` — mutates `WorkTree`
- `TryOpenNewSubVolumes()` — mutates `WorkTree`
- `ScanForSubVol()` — mutates `WorkTree`

**Refactor tree-item static helpers (§7.2, §7.9):**
`ArchiveTreeItem.SelectItem()` and `SelectBestFrom()` currently take
`MainWindow` and set `mainWin.archiveTree.SelectedItem` directly on the
control. Refactor these methods to set `ArchiveTreeViewModel.SelectedItem`
instead. Scroll-into-view responsibility should be delegated to an attached
behaviour or `IViewActions` interaction.

**Wire in MainViewModel:**
```csharp
public ArchiveTreeViewModel ArchiveTree { get; }

public MainViewModel(...) {
    ArchiveTree = new ArchiveTreeViewModel();

    // React to archive tree selection changes:
    ArchiveTree.WhenAnyValue(x => x.SelectedItem)
        .Subscribe(item => OnArchiveTreeSelectionChanged(item));
    // NOTE: This snippet shows intent only. See Step 7
    // WireChildViewModels() for the full wiring with
    // .DisposeWith(_subscriptions) and null-guard notes.
}
```

**Update AXAML — TreeView selection binding caveat (§7.2):**

Avalonia `TreeView` does not reliably support two-way binding to
`SelectedItem`. Instead, use the existing `IsSelected` property on
`ArchiveTreeItem` (already present and two-way bound via a style setter
in the current AXAML) to drive selection. Subscribe to `IsSelected`-property
changes in `ArchiveTreeViewModel` (or via `ObservableForProperty`) to update
the VM's `SelectedItem` rather than relying on a direct
`TreeView.SelectedItem` binding.

```xml
<!-- Before: -->
<TreeView ItemsSource="{Binding ArchiveTreeRoot}" ...>

<!-- After: -->
<TreeView ItemsSource="{Binding ArchiveTree.TreeRoot}" ...>
<!-- Selection is driven by IsSelected on tree items, not TreeView.SelectedItem binding -->
```

### Step 1½: Wire Bidirectional Selection Sync for ArchiveTreeViewModel

The blueprint rejects `TreeView.SelectedItem` binding (§7.2) and relies on
`IsSelected` on each `ArchiveTreeItem`. This step provides the explicit
bidirectional synchronization between individual item `IsSelected` properties
and `ArchiveTreeViewModel.SelectedItem`.

**Direction 1 — User clicks tree item → VM `SelectedItem` updates:**

Subscribe to each tree item's `IsSelected` changes. When a new item is added
to `TreeRoot` (or a sub-tree is expanded), subscribe recursively. Use
`CompositeDisposable` for subscription lifecycle.

```csharp
// In ArchiveTreeViewModel:
private readonly CompositeDisposable _itemSubscriptions = new();

/// <summary>
/// Subscribes to IsSelected changes on the given item and all its
/// descendants. Call after populating TreeRoot or expanding a sub-tree.
/// </summary>
public void SubscribeToSelectionChanges(ArchiveTreeItem item) {
    item.WhenAnyValue(x => x.IsSelected)
        .Where(selected => selected)
        .Subscribe(_ => SelectedItem = item)
        .DisposeWith(_itemSubscriptions);

    foreach (var child in item.Items)
        SubscribeToSelectionChanges(child);
}

/// <summary>
/// Subscribes to all items in TreeRoot. Call after PopulateArchiveTree().
/// </summary>
public void SubscribeAllSelectionChanges() {
    _itemSubscriptions.Clear();   // dispose previous subscriptions
    foreach (var root in TreeRoot)
        SubscribeToSelectionChanges(root);
}
```

**Direction 2 — Programmatic `SelectedItem` set → item `IsSelected` updates:**

In the `SelectedItem` setter, set `IsSelected = true` on the new value (and
optionally clear the previous selection). This covers `SelectItem()`,
`SelectBestFrom()`, and any other code that sets `SelectedItem` directly.

```csharp
// In ArchiveTreeViewModel:
private ArchiveTreeItem? _selectedItem;
public ArchiveTreeItem? SelectedItem {
    get => _selectedItem;
    set {
        if (_selectedItem == value) return;
        if (_selectedItem != null) _selectedItem.IsSelected = false;
        this.RaiseAndSetIfChanged(ref _selectedItem, value);
        if (_selectedItem != null) {
            _selectedItem.IsSelected = true;
            CachedSelectedItem = _selectedItem;
        }
    }
}
```

**Caller responsibility after tree population:**

In `MainViewModel`, after calling `PopulateArchiveTree()`:
```csharp
ArchiveTree.TreeRoot.Clear();
ArchiveTreeItem.ConstructTree(ArchiveTree.TreeRoot, _workspaceService.WorkTree.RootNode);
ArchiveTree.SubscribeAllSelectionChanges();
```

**Disposal:** `_itemSubscriptions` is disposed in `ArchiveTreeViewModel.Dispose()`.
`ArchiveTreeViewModel` must implement `IDisposable` with a
`CompositeDisposable` that includes `_itemSubscriptions`.

### Step 2: Create DirectoryTreeViewModel

```csharp
// cp2_avalonia/ViewModels/DirectoryTreeViewModel.cs
namespace cp2_avalonia.ViewModels;

using System.Collections.ObjectModel;
using ReactiveUI;

public class DirectoryTreeViewModel : ReactiveObject {
    public ObservableCollection<DirectoryTreeItem> TreeRoot { get; } = new();

    private DirectoryTreeItem? _selectedItem;
    public DirectoryTreeItem? SelectedItem {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    /// <summary>
    /// Retains the last non-null SelectedItem. Used by cross-VM
    /// coordination methods (SyncDirectoryTreeToFileSelection,
    /// NavToParent, etc.). Updated in SelectedItem setter:
    /// if (value != null) CachedSelectedItem = value;
    /// Reset to null on workspace close.
    /// </summary>
    public DirectoryTreeItem? CachedSelectedItem { get; private set; }

    // Moved from MainViewModel:
    // - PopulateDirectoryTree()
    // - VerifyDirectoryTree()
}
```

**Move from MainViewModel:**
- `DirectoryTreeRoot` property → `TreeRoot`
- `PopulateDirectoryTree()` (static method, can stay static or become
  instance method)
- `VerifyDirectoryTree()` (static helper)
- Selection changed handling

**Methods that stay on MainViewModel:**
- `SyncDirectoryTreeToFileSelection()` — cross-VM coordination
  (file list → directory tree), requires access to both `FileList.SelectedItem`
  and `DirectoryTree.SelectedItem` (see Step 7)
- `NavToParent(bool dirOnly)` — cross-VM coordination (checks directory tree
  first, then conditionally archive tree; cannot be split across child VMs)

**Refactor tree-item static helpers (§7.2, §7.9):**
`DirectoryTreeItem.SelectItemByEntry()` currently takes `MainWindow` and
sets `mainWin.directoryTree.SelectedItem` directly on the control. Refactor
to set `DirectoryTreeViewModel.SelectedItem` instead. Scroll-into-view and
focus should be delegated to an attached behaviour or `IViewActions`.

**TreeView selection binding caveat (§7.2):** Same approach as
`ArchiveTreeViewModel` — use `IsSelected` property on `DirectoryTreeItem`
rather than `TreeView.SelectedItem` binding.

**Wire in MainViewModel:**
```csharp
public DirectoryTreeViewModel DirectoryTree { get; }

DirectoryTree = new DirectoryTreeViewModel();
DirectoryTree.WhenAnyValue(x => x.SelectedItem)
    .Subscribe(item => OnDirectoryTreeSelectionChanged(item));
// NOTE: Intent-only snippet. See Step 7 WireChildViewModels() for
// the full wiring with .DisposeWith(_subscriptions) and null-guard.
```

### Step 2½: Wire Bidirectional Selection Sync for DirectoryTreeViewModel

Same pattern as Step 1½, applied to `DirectoryTreeViewModel` and
`DirectoryTreeItem`.

**Direction 1 — User clicks tree item → VM `SelectedItem` updates:**

```csharp
// In DirectoryTreeViewModel:
private readonly CompositeDisposable _itemSubscriptions = new();

public void SubscribeToSelectionChanges(DirectoryTreeItem item) {
    item.WhenAnyValue(x => x.IsSelected)
        .Where(selected => selected)
        .Subscribe(_ => SelectedItem = item)
        .DisposeWith(_itemSubscriptions);

    foreach (var child in item.Items)
        SubscribeToSelectionChanges(child);
}

public void SubscribeAllSelectionChanges() {
    _itemSubscriptions.Clear();
    foreach (var root in TreeRoot)
        SubscribeToSelectionChanges(root);
}
```

**Direction 2 — Programmatic `SelectedItem` set → item `IsSelected` updates:**

```csharp
// In DirectoryTreeViewModel:
private DirectoryTreeItem? _selectedItem;
public DirectoryTreeItem? SelectedItem {
    get => _selectedItem;
    set {
        if (_selectedItem == value) return;
        if (_selectedItem != null) _selectedItem.IsSelected = false;
        this.RaiseAndSetIfChanged(ref _selectedItem, value);
        if (_selectedItem != null) {
            _selectedItem.IsSelected = true;
            CachedSelectedItem = _selectedItem;
        }
    }
}
```

**Caller responsibility after tree population:**

In `MainViewModel`, after calling `PopulateDirectoryTree()`:
```csharp
DirectoryTree.SubscribeAllSelectionChanges();
```

**Disposal:** Same as Step 1½ — `_itemSubscriptions` disposed in
`DirectoryTreeViewModel.Dispose()`. `DirectoryTreeViewModel` must implement
`IDisposable`.

### Step 3: Create FileListViewModel

```csharp
// cp2_avalonia/ViewModels/FileListViewModel.cs
namespace cp2_avalonia.ViewModels;

using System.Collections.ObjectModel;
using ReactiveUI;

public class FileListViewModel : ReactiveObject {
    public ObservableCollection<FileListItem> Items { get; } = new();

    private FileListItem? _selectedItem;
    public FileListItem? SelectedItem {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    // Selection tracking for multi-select:
    public ObservableCollection<FileListItem> SelectedItems { get; } = new();

    // Column widths (persisted):
    private string _columnWidths = string.Empty;
    public string ColumnWidths {
        get => _columnWidths;
        set => this.RaiseAndSetIfChanged(ref _columnWidths, value);
    }

    // Sorting:
    public ReactiveCommand<Unit, Unit> ResetSortCommand { get; }

    private bool _isResetSortEnabled;
    public bool IsResetSortEnabled {
        get => _isResetSortEnabled;
        set => this.RaiseAndSetIfChanged(ref _isResetSortEnabled, value);
    }
    // Set to true whenever a non-default sort is applied; false on ResetSort().

    // Entry counts — updated by PopulateFileList(), read by MainViewModel
    // to relay to StatusBarViewModel.SetEntryCounts():
    private int _lastDirCount;
    public int LastDirCount {
        get => _lastDirCount;
        set => this.RaiseAndSetIfChanged(ref _lastDirCount, value);
    }

    private int _lastFileCount;
    public int LastFileCount {
        get => _lastFileCount;
        set => this.RaiseAndSetIfChanged(ref _lastFileCount, value);
    }

    public FileListViewModel() {
        var canReset = this.WhenAnyValue(x => x.IsResetSortEnabled);
        ResetSortCommand = ReactiveCommand.Create(ResetSort, canReset);
    }

    /// <summary>
    /// Callers on MainViewModel call this before triggering population
    /// to request that the file list receives keyboard focus after the
    /// list is populated. PopulateFileList() reads and clears the flag
    /// internally.
    /// </summary>
    public void RequestFocusAfterPopulate() { _switchFocusToFileList = true; }
    private bool _switchFocusToFileList;

    // Moved from MainViewModel:
    // - PopulateFileList(object currentWorkObject,
    //     DirectoryTreeItem? dirTreeSel, IFileEntry selEntry,
    //     bool focusOnFileList)
    //   Returns (int dirCount, int fileCount) tuple.
    //   Also updates LastDirCount / LastFileCount properties.
    //   MainViewModel passes CurrentWorkObject and
    //   DirectoryTree.SelectedItem at the call site.
    // - PopulateEntriesFromArchive()
    // - PopulateEntriesFromSingleDir()
    // - PopulateEntriesFromFullDisk()
    // - VerifyFileList() static overloads
    //   (no-argument dispatch wrapper stays on MainViewModel —
    //   reads CurrentWorkObject + DirectoryTree.SelectedItem)
}
```

**Move from MainViewModel:**
- `FileList` property → `Items`
- All `PopulateEntries*` methods (note: `PopulateFileList()` signature changes
  to accept `currentWorkObject`, `dirTreeSel`, `selEntry`, and
  `focusOnFileList` as parameters — the child VM has no access to
  `CurrentWorkObject` or `DirectoryTree.SelectedItem`)
- `VerifyFileList()` static overloads (the no-argument dispatch wrapper
  stays on `MainViewModel` — it reads `CurrentWorkObject` and
  `DirectoryTree.SelectedItem`, then delegates to
  `FileList.VerifyFileList(...)` with the appropriate arguments)
- `ResetSortCommand`
- `IsResetSortEnabled` — derived from sort state
  (e.g., `SortColumn != DefaultSortColumn || !SortAscending`)
- Column width properties
- Sort state: `SortColumn`, `SortAscending`, `ApplySort()`, `ResetSort()`,
  `ReapplySort()` (see §7.4)

**Methods that stay on MainViewModel** (cross-VM dependencies):
- `HandleFileListDoubleClick()` — reads `ArchiveTree.SelectedItem`,
  `DirectoryTree.SelectedItem`, accesses `IWorkspaceService.WorkTree`,
  and calls tree-item static helpers for selection. Same rationale as
  `PopulateArchiveTree()`.

**`GetFileSelection()` stays on MainViewModel.** It reads
`FileList.SelectedItems`, `ArchiveTree.SelectedItem`,
`DirectoryTree.SelectedItem`, and `CurrentWorkObject` — all cross-panel
state accessible from `MainViewModel`.

**Multi-select `SelectedItems` caveat:** Avalonia's `DataGrid.SelectedItems`
is a read-only `IList` managed by the control and cannot be directly two-way
bound to an `ObservableCollection`. Handle the `SelectionChanged` event in
`MainWindow.axaml.cs` code-behind, copy the selection to a list, and call
`ViewModel.FileList.SetSelection(items)`. Alternatively, keep
`GetFileSelection()` as a method that reads the DataGrid directly via
`IViewActions` (keeping collection access in the View layer).

**Sort reapplication (§7.4):** `ReapplyFileListSort()` currently accesses
`fileListDataGrid.Columns` directly. After migration, `FileListViewModel`
owns `SortColumn` / `SortAscending` and exposes `ReapplySort()`. View
code-behind subscribes to a `SortReapplyRequested` interaction or applies
the sort after the collection-changed observable fires.

### Step 3½: Extract Inner Classes to Standalone Files

Before creating `CenterInfoViewModel` and `OptionsPanelViewModel`, extract
the following inner classes from `MainWindow` to standalone files in
`cp2_avalonia/Models/`:

- `MainWindow.CenterInfoItem` → `Models/CenterInfoItem.cs`
- `MainWindow.PartitionListItem` → `Models/PartitionListItem.cs`
- `MainWindow.MetadataItem` → `Models/MetadataItem.cs`
- `MainWindow.ConvItem` → `Models/ConvItem.cs`

Update all references in `MainController.cs`, `MainController_Panels.cs`,
and `MainWindow.axaml.cs` to use the new namespace (`cp2_avalonia.Models`).

**Note:** `MetadataItem` implements `INotifyPropertyChanged`. Leave it as-is
(it is a data model, not a ViewModel) — do not convert to `ReactiveObject`.

### Step 4: Create CenterInfoViewModel

```csharp
// cp2_avalonia/ViewModels/CenterInfoViewModel.cs
namespace cp2_avalonia.ViewModels;

using System.Collections.ObjectModel;
using ReactiveUI;

public class CenterInfoViewModel : ReactiveObject {
    public ObservableCollection<CenterInfoItem> CenterInfoList { get; } = new();
    public ObservableCollection<PartitionListItem> PartitionList { get; } = new();
    public ObservableCollection<MetadataItem> MetadataItems { get; } = new();
    public ObservableCollection<Notes.Note> NotesList { get; } = new();

    private bool _showInfo;
    public bool ShowInfo {
        get => _showInfo;
        set => this.RaiseAndSetIfChanged(ref _showInfo, value);
    }

    // Additional properties (actively bound in AXAML):
    // - CenterInfoText1 (one-line blurb about selected object)
    // - CenterInfoText2 (read-write-failure warning text)
    // - ShowDiskUtilityButtons (disk-utility button panel visibility)
    // - ShowPartitionLayout, ShowMetadata, ShowNotes
    // - CanAddMetadataEntry

    // Moved from MainViewModel:
    // - ConfigureCenterInfo()
    // - ClearCenterInfo()
    // - AddInfoItem()
    // - HandleMetadataDoubleClick()
    // - HandleMetadataAddEntry()
    //
    // NOTE: ConfigureCenterPanel() stays on MainViewModel — it is a
    // coordination method that configures FileListViewModel columns,
    // toolbar states, and info panel simultaneously (§7.11).
    //
    // NOTE: HandlePartitionLayoutDoubleClick() stays on MainViewModel —
    // it accesses the archive tree (cross-VM coordination).
    // CenterInfoViewModel fires an Interaction<PartitionListItem, Unit>
    // and MainViewModel handles it.
}
```

### Step 5: Create OptionsPanelViewModel

```csharp
// cp2_avalonia/ViewModels/OptionsPanelViewModel.cs
namespace cp2_avalonia.ViewModels;

using ReactiveUI;

public class OptionsPanelViewModel : ReactiveObject {
    private readonly ISettingsService _settingsService;
    private readonly IClipboardService _clipboardService;

    public OptionsPanelViewModel(ISettingsService settingsService,
            IClipboardService clipboardService) {
        _settingsService = settingsService;
        _clipboardService = clipboardService;
    }

    // All option checkbox properties:
    private bool _addCompress;
    public bool AddCompress {
        get => _addCompress;
        set {
            this.RaiseAndSetIfChanged(ref _addCompress, value);
            _settingsService.SetBool(AppSettings.ADD_COMPRESS, value);
        }
    }
    // Apply this setter pattern (RaiseAndSetIfChanged + SetBool/SetEnum)
    // to all ~20 plain boolean/enum option properties.

    // ... ~20 boolean properties for add/extract/view options:
    // AddRecurse, AddStripPaths, AddStripExt, AddRaw,
    // AddPreserveADF, AddPreserveAS, AddPreserveNAPS,
    // ExtStripPaths, ExtRaw, ExtAddExportExt,
    // ExtPreserveMode (enum), ViewRaw,
    // MacZipEnabled, DosTextConvEnabled, etc.

    // Additional properties (actively bound in AXAML):
    // - IsExportBestChecked / IsExportComboChecked (radio buttons)
    // - IsChecked_AddExtract / IsChecked_ImportExport (DDCP mode radio pair)
    //   NOTE: DDCP setters must call
    //   _ = _clipboardService.ClearIfPendingAsync();
    //   (fire-and-forget, per Pre-Iteration-Notes async-in-setter guidance)
    // - SelectedDDCPModeIndex
    //
    // DDCP cross-notification: DDCPModeIndex, AddExtract, and ImportExport
    // all represent the same underlying setting (AppSettings.DDCP_ADD_EXTRACT).
    // Store the canonical value in a single bool backing field. All three
    // property getters derive from it, and all three setters update the same
    // field and call RaisePropertyChanged for all three property names.
    // Example:
    //   private bool _ddcpAddExtract;
    //   public bool AddExtract {
    //       get => _ddcpAddExtract;
    //       set { _ddcpAddExtract = value;
    //             this.RaisePropertyChanged(nameof(AddExtract));
    //             this.RaisePropertyChanged(nameof(ImportExport));
    //             this.RaisePropertyChanged(nameof(DDCPModeIndex));
    //             _ = _clipboardService.ClearIfPendingAsync();
    //             _settingsService.SetBool(AppSettings.DDCP_ADD_EXTRACT, value);
    //       }
    //   }
    //   public bool ImportExport { get => !_ddcpAddExtract; set => AddExtract = !value; }
    //   public int DDCPModeIndex { get => _ddcpAddExtract ? 0 : 1;
    //                              set => AddExtract = (value == 0); }
    //
    // ExtPreserve* cross-notification: the same canonical-field +
    // cross-notification pattern applies to the four ExtPreserve*
    // radio buttons. Store one PreserveMode _extPreserveMode backing
    // field. All four property getters compare it to their respective
    // enum value; all four setters assign the field and raise
    // RaisePropertyChanged for all four names.
    // Example:
    //   private ExtractFileWorker.PreserveMode _extPreserveMode;
    //   public bool ExtPreserveNone {
    //       get => _extPreserveMode == PreserveMode.None;
    //       set { if (value) { _extPreserveMode = PreserveMode.None;
    //             RaiseAllExtPreserve();
    //             _settingsService.SetEnum(AppSettings.EXT_PRESERVE_MODE, _extPreserveMode); } }
    //   }
    //   // ExtPreserveAS, ExtPreserveADF, ExtPreserveNAPS — same pattern
    //   private void RaiseAllExtPreserve() {
    //       this.RaisePropertyChanged(nameof(ExtPreserveNone));
    //       this.RaisePropertyChanged(nameof(ExtPreserveAS));
    //       this.RaisePropertyChanged(nameof(ExtPreserveADF));
    //       this.RaisePropertyChanged(nameof(ExtPreserveNAPS));
    //   }

    // Converter selection (uses existing ConvItem class, not "ConverterItem"):
    public ObservableCollection<ConvItem> ImportConverters { get; } = new();
    public ObservableCollection<ConvItem> ExportConverters { get; } = new();

    private ConvItem? _selectedImportConverter;
    public ConvItem? SelectedImportConverter {
        get => _selectedImportConverter;
        set => this.RaiseAndSetIfChanged(ref _selectedImportConverter, value);
    }

    // ... etc.

    // Panel visibility:
    private bool _showOptionsPanel;
    public bool ShowOptionsPanel {
        get => _showOptionsPanel;
        set {
            this.RaiseAndSetIfChanged(ref _showOptionsPanel, value);
            // Derived property — always ShowOptionsPanel ? 0 : 90:
            ShowHideRotation = value ? 0 : 90;
        }
    }

    private int _showHideRotation = 90;
    public int ShowHideRotation {
        get => _showHideRotation;
        set => this.RaiseAndSetIfChanged(ref _showHideRotation, value);
    }

    /// <summary>
    /// Re-reads all settings values and updates properties. Called by
    /// MainViewModel after ISettingsService.SettingChanged fires (e.g.,
    /// after EditAppSettings dialog applies changes). Replaces the
    /// former PublishSideOptions() method.
    /// </summary>
    public void RefreshFromSettings() {
        // Re-read all boolean/enum settings from _settingsService
        // and update each property. Also re-read saved converter
        // selections (not the converter lists themselves).
    }

    /// <summary>
    /// Populates ImportConverters and ExportConverters from
    /// ImportFoundry/ExportFoundry. Call once at construction time.
    /// Converter lists are static; only the selection changes on
    /// settings refresh.
    /// </summary>
    public void InitConverters() {
        // ImportFoundry.GetCount() / ExportFoundry.GetCount()
        // Sort, populate ObservableCollections, set initial
        // selection from _settingsService.
    }
}
```

**Move from MainViewModel:**
- All `IsChecked_*` properties (including `IsExportBestChecked`,
  `IsExportComboChecked`, `IsChecked_AddExtract`, `IsChecked_ImportExport`)
- Converter collections and selection (use existing class name `ConvItem`,
  not `ConverterItem`)
- DDCP mode (`SelectedDDCPModeIndex`)
- Options panel visibility and rotation
- `PublishSideOptions()` → replaced by `RefreshFromSettings()` on
  `OptionsPanelViewModel`, called by `MainViewModel` when
  `ISettingsService.SettingChanged` fires

**Dependencies:** `OptionsPanelViewModel` requires both `ISettingsService`
and `IClipboardService` via constructor injection (from `MainViewModel`,
which gets them from DI). `IClipboardService` is needed by the DDCP
property setters to call `ClearIfPendingAsync()`.

### Step 6: Create StatusBarViewModel

```csharp
// cp2_avalonia/ViewModels/StatusBarViewModel.cs
namespace cp2_avalonia.ViewModels;

using ReactiveUI;

public class StatusBarViewModel : ReactiveObject {
    private string _centerText = string.Empty;
    public string CenterText {
        get => _centerText;
        set => this.RaiseAndSetIfChanged(ref _centerText, value);
    }

    private string _rightText = string.Empty;
    public string RightText {
        get => _rightText;
        set => this.RaiseAndSetIfChanged(ref _rightText, value);
    }

    // Moved from MainViewModel:
    // - SetEntryCounts(IFileSystem? fs, int dirCount, int fileCount,
    //     Formatter formatter)
    //   Formatter comes from IWorkspaceService.Formatter at the call
    //   site (MainViewModel passes it in).
    // - ClearEntryCounts()

    /// <summary>
    /// Formats and sets the right status text to show directory count,
    /// file count, and (for IFileSystem) free-space information.
    /// Called by MainViewModel after file list population.
    /// </summary>
    public void SetEntryCounts(IFileSystem? fs, int dirCount, int fileCount,
            Formatter formatter) {
        // Build status string: "42 files, 3 directories" + optional free space
        // Assign to RightText.
    }

    /// <summary>
    /// Clears the entry-count portion of the status bar (sets RightText
    /// to empty). Called when the workspace is closed or the archive
    /// tree selection is cleared.
    /// </summary>
    public void ClearEntryCounts() {
        RightText = string.Empty;
    }
}
```

### Step 7: Update MainViewModel Composition

```csharp
public class MainViewModel : ReactiveObject, IDisposable {
    // Child ViewModels (exposed for AXAML binding):
    public ArchiveTreeViewModel ArchiveTree { get; }
    public DirectoryTreeViewModel DirectoryTree { get; }
    public FileListViewModel FileList { get; }
    public CenterInfoViewModel CenterInfo { get; }
    public OptionsPanelViewModel Options { get; }
    public StatusBarViewModel StatusBar { get; }

    // Cross-panel state (set by OnArchiveTreeSelectionChanged):
    private object? _currentWorkObject;
    public object? CurrentWorkObject {
        get => _currentWorkObject;
        set => this.RaiseAndSetIfChanged(ref _currentWorkObject, value);
    }

    // Subscription lifecycle:
    private readonly CompositeDisposable _subscriptions = new();

    // Re-entrancy guard for file-list ↔ directory-tree sync:
    private bool _syncingSelection;

    // Services (injected):
    private readonly IDialogService _dialogService;
    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsService _settingsService;
    private readonly IClipboardService _clipboardService;
    private readonly IWorkspaceService _workspaceService;

    public MainViewModel(...) {
        ArchiveTree = new ArchiveTreeViewModel();
        DirectoryTree = new DirectoryTreeViewModel();
        FileList = new FileListViewModel();
        CenterInfo = new CenterInfoViewModel();
        Options = new OptionsPanelViewModel(_settingsService, _clipboardService);
        Options.InitConverters();  // populate converter lists from ImportFoundry/ExportFoundry
        Options.RefreshFromSettings();  // initialize backing fields from saved settings
        StatusBar = new StatusBarViewModel();

        // Cross-panel wiring:
        WireChildViewModels();

        // Commands (still on MainViewModel — they coordinate across panels):
        // ...
    }

    private void WireChildViewModels() {
        // When archive tree selection changes → full handler
        // NOTE: The real OnArchiveTreeSelectionChanged is ~90 lines.
        // It must: clear DirectoryTreeRoot unconditionally, update
        // CurrentWorkObject from newSel.WorkTreeNode.DAObject, set
        // CenterInfo.CenterInfoText2 based on ReadWriteOpenFailure,
        // branch on CurrentWorkObject type (IFileSystem vs IArchive vs
        // IDiskImage) to decide whether to populate the directory tree,
        // call ScrollToTop on both trees via IViewActions, and call
        // RefreshAllCommandStates(). See the full
        // ArchiveTree_SelectionChanged in MainController_Panels.cs.
        ArchiveTree.WhenAnyValue(x => x.SelectedItem)
            .Subscribe(item => OnArchiveTreeSelectionChanged(item))
            // NOTE: WhenAnyValue fires immediately with null at construction.
            // Handler must guard against null (existing code already does).
            .DisposeWith(_subscriptions);

        // When directory tree selection changes → populate file list
        DirectoryTree.WhenAnyValue(x => x.SelectedItem)
            .Subscribe(item => OnDirectoryTreeSelectionChanged(item))
            // NOTE: fires immediately with null — handler must guard.
            .DisposeWith(_subscriptions);

        // After file list population, relay entry counts to status bar.
        // FileListViewModel exposes LastDirCount / LastFileCount which are
        // updated during PopulateFileList(). Subscribe to changes:
        FileList.WhenAnyValue(x => x.LastDirCount, x => x.LastFileCount)
            .Subscribe(counts => {
                var (dirCount, fileCount) = counts;
                StatusBar.SetEntryCounts(
                    CurrentWorkObject as IFileSystem,
                    dirCount, fileCount,
                    _workspaceService.Formatter);
            })
            .DisposeWith(_subscriptions);

        // When file list selection changes → sync directory tree (§7.11)
        // SyncDirectoryTreeToFileSelection: reads FileList.SelectedItem,
        // computes targetDir, updates DirectoryTree.SelectedItem if the
        // directory changed. Guarded by _syncingSelection to prevent
        // re-entrancy.
        FileList.WhenAnyValue(x => x.SelectedItem)
            .Subscribe(item => OnFileListSelectionChanged(item))
            // NOTE: fires immediately with null — handler must guard.
            .DisposeWith(_subscriptions);

        // NOTE: Option property → settings persistence is NOT wired here.
        // OptionsPanelViewModel owns both read and write to ISettingsService
        // directly in its property setters. This avoids a feedback cycle
        // with RefreshFromSettings(). Do NOT add WhenAnyValue subscriptions
        // for boolean option properties.

        // Settings → options (inverse direction): when the user edits app
        // settings via the dialog and saves, refresh the options panel.
        _settingsService.SettingChanged
            .Subscribe(_ => Options.RefreshFromSettings())
            .DisposeWith(_subscriptions);
    }

    // Cross-panel coordination methods that stay on MainViewModel:
    // - OnArchiveTreeSelectionChanged() (see note above)
    // - OnDirectoryTreeSelectionChanged()
    // - OnFileListSelectionChanged() (SyncDirectoryTreeToFileSelection)
    // - ConfigureCenterPanel() — coordinates FileListVM columns,
    //   toolbar states, and CenterInfoVM simultaneously
    // - HandlePartitionLayoutDoubleClick() — accesses ArchiveTree
    //   (CenterInfoVM fires Interaction, MainViewModel handles)
    // - GetFileSelection() — reads from multiple child VMs
    // - CloseSubTree() / TryOpenNewSubVolumes() / ScanForSubVol()

    public void Dispose() {
        _subscriptions.Dispose();
        // Dispose child VMs that implement IDisposable:
        (ArchiveTree as IDisposable)?.Dispose();
        (DirectoryTree as IDisposable)?.Dispose();
        (FileList as IDisposable)?.Dispose();
        (CenterInfo as IDisposable)?.Dispose();
        (Options as IDisposable)?.Dispose();
        (StatusBar as IDisposable)?.Dispose();
    }
}
```

**Properties that stay on MainViewModel** (not moved to any child VM):
- `CurrentWorkObject` — cross-panel state, basis for all `canExecute`
  predicates that examine the selected archive type
- `LaunchPanelVisible` / `MainPanelVisible` — top-level layout visibility
- `ShowCenterFileList` / `ShowCenterInfoPanel` — center panel toggle
- `IsFullListEnabled` / `IsDirListEnabled` — toolbar states
- `FullListBorderBrush` / `DirListBorderBrush` / `InfoBorderBrush` — toolbar
  highlight brushes

**Properties that move to FileListViewModel** (with caveats):
- `ShowCol_FileName`, `ShowCol_PathName`, `ShowCol_Format`, `ShowCol_RawLen`,
  `ShowCol_RsrcLen`, `ShowCol_TotalSize` — column visibility. Note: the
  current `SetColumnVisible()` accesses `fileListDataGrid.Columns` directly;
  requires an attached behaviour to bridge VM property → column visibility.
- `ShowSingleDirFileList` — drives which populate method is called

### Step 8: Update AXAML Bindings

Update all bindings in `MainWindow.axaml` to use the child ViewModel paths.
The following categorized list covers all binding path changes:

**Archive Tree panel:**
- `{Binding ArchiveTreeRoot}` → `{Binding ArchiveTree.TreeRoot}`

**Directory Tree panel:**
- `{Binding DirectoryTreeRoot}` → `{Binding DirectoryTree.TreeRoot}`

**File List panel:**
- `{Binding FileList}` → `{Binding FileList.Items}`
- `{Binding SelectedFileListItem}` → `{Binding FileList.SelectedItem}`

**Center Info panel:**
- `{Binding CenterInfoList}` → `{Binding CenterInfo.CenterInfoList}`
- `{Binding CenterInfoText1}` → `{Binding CenterInfo.CenterInfoText1}`
- `{Binding CenterInfoText2}` → `{Binding CenterInfo.CenterInfoText2}`
- `{Binding ShowPartitionLayout}` → `{Binding CenterInfo.ShowPartitionLayout}`
- `{Binding PartitionList}` → `{Binding CenterInfo.PartitionList}`
- `{Binding ShowMetadata}` → `{Binding CenterInfo.ShowMetadata}`
- `{Binding MetadataList}` → `{Binding CenterInfo.MetadataItems}`
- `{Binding CanAddMetadataEntry}` → `{Binding CenterInfo.CanAddMetadataEntry}`
- `{Binding NotesList}` → `{Binding CenterInfo.NotesList}`
- `{Binding ShowNotes}` → `{Binding CenterInfo.ShowNotes}`
- `{Binding ShowDiskUtilityButtons}` → `{Binding CenterInfo.ShowDiskUtilityButtons}`

**Options panel** (`IsChecked_` prefix is dropped in the VM — e.g.,
`IsChecked_AddRecurse` → `Options.AddRecurse`):
- All `{Binding IsChecked_AddRecurse}` → `{Binding Options.AddRecurse}` (and
  similarly for every `IsChecked_*` property — drop the `IsChecked_` prefix)
- `{Binding IsExportBestChecked}` → `{Binding Options.IsExportBestChecked}`
- `{Binding IsExportComboChecked}` → `{Binding Options.IsExportComboChecked}`
- `{Binding ShowOptionsPanel}` → `{Binding Options.ShowOptionsPanel}`
- `{Binding ShowHideRotation}` → `{Binding Options.ShowHideRotation}`
- `{Binding SelectedDDCPModeIndex}` → `{Binding Options.DDCPModeIndex}`
- `{Binding ImportConverters}` → `{Binding Options.ImportConverters}`
- `{Binding ExportConverters}` → `{Binding Options.ExportConverters}`
- `{Binding SelectedImportConverter}` → `{Binding Options.SelectedImportConverter}`
- `{Binding SelectedExportConverter}` → `{Binding Options.SelectedExportConverter}`

**Status Bar:**
- `{Binding CenterStatusText}` → `{Binding StatusBar.CenterText}`
- `{Binding RightStatusText}` → `{Binding StatusBar.RightText}`

**Unchanged (stay on MainViewModel):**
- `{Binding LaunchPanelVisible}`, `{Binding MainPanelVisible}`
- `{Binding ShowCenterFileList}`, `{Binding ShowCenterInfoPanel}`
- `{Binding IsFullListEnabled}`, `{Binding IsDirListEnabled}`
- `{Binding FullListBorderBrush}`, `{Binding DirListBorderBrush}`,
  `{Binding InfoBorderBrush}`
- `{Binding ProgramVersionString}`
- `{Binding ShowRecentFile1}`, `{Binding RecentFileName1}`,
  `{Binding RecentFilePath1}` (and similarly `*2`)
- `{Binding ShowDebugMenu}`, `{Binding IsDebugLogVisible}`,
  `{Binding IsDropTargetVisible}`
- All `Command=` bindings (commands remain on MainViewModel)

```xml
<!-- Representative examples: -->

<!-- Archive Tree -->
<TreeView ItemsSource="{Binding ArchiveTree.TreeRoot}" ...>

<!-- File List -->
<DataGrid ItemsSource="{Binding FileList.Items}"
          SelectedItem="{Binding FileList.SelectedItem}" ...>

<!-- Center Info -->
<ItemsControl ItemsSource="{Binding CenterInfo.CenterInfoList}" ...>

<!-- Options Panel -->
<CheckBox IsChecked="{Binding Options.AddCompress}" ...>
<ComboBox ItemsSource="{Binding Options.ImportConverters}" ...>

<!-- Status Bar -->
<TextBlock Text="{Binding StatusBar.CenterText}" ...>
```

### Step 9: Build and Validate

1. Run `dotnet build` — verify zero errors.
2. Launch and test all panel interactions:

   **Archive tree:**
   - Open a multi-level disk image (disk → partition → filesystem)
   - Navigate tree levels
   - Close sub-tree
   - Navigate to parent

   **Directory tree:**
   - Select different directories
   - Verify file list updates
   - Navigate to parent directory

   **File list:**
   - Verify correct entries appear
   - Sort by columns
   - Reset sort
   - Select entries (single, multi)
   - Double-click directory → navigate
   - Double-click archive → open sub-tree

   **Center info:**
   - Toggle info view (Show Info command)
   - Verify info panel content matches selection
   - Partition layout double-click
   - Metadata panel: add, edit, delete entries

   **Options panel:**
   - Toggle show/hide
   - Change options → verify persistence after restart
   - Change DDCP mode
   - Change import/export converter

   **Status bar:**
   - Verify file/directory counts update on navigation
   - Verify free space display

3. Verify all commands still work (they should — commands remain on
   `MainViewModel` and now delegate to child VMs where needed).

---

## Size Expectations After Extraction

| Component | Estimated Lines |
|---|---|
| `MainViewModel` | ~800–1,200 (commands + coordination) |
| `ArchiveTreeViewModel` | ~80–150 (selection tracking, TreeRoot, IsExpanded state; WorkTree-mutating methods stay on MainViewModel) |
| `DirectoryTreeViewModel` | ~150–250 |
| `FileListViewModel` | ~300–500 |
| `CenterInfoViewModel` | ~200–300 |
| `OptionsPanelViewModel` | ~150–250 |
| `StatusBarViewModel` | ~50–80 |

---

## Child ViewModel Lifecycle & Disposal

Child ViewModels that register their own internal subscriptions (e.g.,
`OptionsPanelViewModel` subscribing to `ISettingsService.SettingChanged`)
must use `CompositeDisposable` collected with `.DisposeWith()` and implement
`IDisposable`. `MainViewModel` disposes each child VM in its own `Dispose()`
method (see Step 7 code).

Child ViewModels in this iteration do not have a paired View and are owned
entirely by `MainViewModel`. Do NOT implement `IActivatableViewModel` on
them — `WhenActivated` blocks would never fire (no View activates them),
creating subscription leaks. Use `IDisposable` + `CompositeDisposable` as
described above (per Pre-Iteration-Notes §4).

Child VMs that are pure property holders with no internal subscriptions
(e.g., `StatusBarViewModel`) do not need to implement `IDisposable`.

---

## What This Enables

- `MainViewModel` is a manageable coordinator (~1,000 lines).
- Each panel is independently testable.
- Future features (e.g., multi-tab file list) can modify
  `FileListViewModel` without touching `MainViewModel`.
- Phase 6 can evaluate multi-viewer and docking on cleanly separated VMs.
