# MVVM Architecture Refactor — Analysis & Plan

## Scope

This document covers changes **only** within the `cp2_avalonia` project.
The supporting libraries (`DiskArc`, `AppCommon`, `CommonUtil`, `FileConv`, etc.)
remain unchanged; MVVM ViewModels will consume them through their existing public APIs.

The MVVM refactor is the **foundational prerequisite** for several other goals
listed in `KNOWN_ISSUES.md` § "Future Major Rework":

- Allow multiple viewers/editors at once (multi-document workflow)
- Implement dynamic windowing/paneling (VS/Code-style docking, splits, tabs)
- Move to a single-process, multi-window architecture
- Add unit testing with xUnit for MVVM code

Design decisions throughout this refactor should **accommodate** those future
directions even though they are not implemented in this effort.

---

## 1. Current Architecture Summary

### 1.1 How It Works Today

The Avalonia GUI is a **code-behind-heavy** port of the original WPF version.
All state, commands, business logic, and UI manipulation live in three tightly
coupled layers:

| Layer | Files | Role |
|---|---|---|
| **Window code-behind** | `MainWindow.axaml.cs` (~1,700 lines) | Owns all ICommand properties, all bindable UI state (panel visibility, column visibility, toolbar toggle state, options panel checkboxes, recent files, status bar text, file/metadata/partition lists, converter lists, sort state, drag-drop handlers), and implements `INotifyPropertyChanged`. Sets `DataContext = this`. |
| **Controller** | `MainController.cs` + `MainController_Panels.cs` (~1,800+ lines combined) | Holds the `WorkTree`, `Formatter`, `AppHook`; performs all file open/close, recent-file management, settings load/save, and every Actions-menu operation (add, extract, delete, move, edit attributes, etc.). Directly reads and writes properties on `mMainWin` (the `MainWindow` reference). |
| **Dialog code-behinds** | `EditSector.axaml.cs`, `EditAppSettings.axaml.cs`, `FileViewer.axaml.cs`, `EditAttributes.axaml.cs`, `CreateDiskImage.axaml.cs`, etc. | Each dialog is its own `Window : INotifyPropertyChanged` with `DataContext = this`, mixing UI state and domain logic in code-behind. |

### 1.2 Key Anti-Patterns (from an MVVM perspective)

1. **Window is the DataContext.**
   `MainWindow` sets `DataContext = this` and implements `INotifyPropertyChanged`
   directly, so every bound property is a member of the Window class. This makes
   the entire UI state untestable without instantiating Avalonia.

2. **Controller ↔ Window circular dependency.**
   `MainController` holds `mMainWin` and freely accesses UI controls
   (`mMainWin.fileListDataGrid`, `mMainWin.archiveTree`, `mMainWin.directoryTree`)
   and data collections (`mMainWin.FileList`, `mMainWin.ArchiveTreeRoot`, etc.).
   The Window in turn calls `mMainCtrl.*` for every user action.

3. **Commands defined in code-behind.**
   All ~50 `ICommand` properties are created in the `MainWindow` constructor
   with inline lambdas that call into `mMainCtrl`. Their `canExecute` predicates
   reach back into controller state (`mMainCtrl.IsFileOpen`,
   `mMainCtrl.CanWrite`, `mMainCtrl.AreFileEntriesSelected`, etc.).

4. **Direct control manipulation from non-UI code.**
   `MainController` and `MainController_Panels` reference Avalonia controls:
   - `mMainWin.fileListDataGrid.SelectedItem`
   - `mMainWin.fileListDataGrid.SelectedItems`
   - `mMainWin.fileListDataGrid.ScrollIntoView(...)`
   - `mMainWin.archiveTree.SelectedItem`
   - `mMainWin.directoryTree.SelectedItem`
   - `mMainWin.directoryTree.Focus()`
   - `mMainWin.Cursor = new Cursor(...)`

5. **Nested UI types inside the Window.**
   `ConvItem`, `CenterInfoItem`, `PartitionListItem`, `MetadataItem` are inner
   classes of `MainWindow`, coupling data models to the view.

6. **Dialog construction with `Window` owner references.**
   Every dialog is created by passing `mMainWin` as the owner and calling
   `dialog.ShowDialog(mMainWin)`. The controller needs the Window reference
   to show any dialog.

7. **AppSettings read/written from everywhere.**
   Both `MainWindow` properties and `MainController` methods directly access
   `AppSettings.Global`, mixing persistence concerns with view state.

---

## 2. Target Architecture

```
┌───────────────────────────────────────────────────────┐
│                       Views                           │
│  MainWindow.axaml(.cs)  — thin shell, binds to VM     │
│  EditSector.axaml(.cs)  — thin shell, binds to VM     │
│  FileViewer.axaml(.cs)  — thin shell, binds to VM     │
│  ...                                                  │
│  ┌─────────────────────────────────────────────────┐  │
│  │  (Future: Docking/Panel Host)                   │  │
│  │  Multiple FileViewer instances, detachable      │  │
│  │  panels, tabbed documents                       │  │
│  └─────────────────────────────────────────────────┘  │
└───────────────────┬───────────────────────────────────┘
                    │ DataContext binding
┌───────────────────▼───────────────────────────────────┐
│                   ViewModels                          │
│  MainViewModel                                        │
│    ├── ArchiveTreeViewModel                           │
│    ├── DirectoryTreeViewModel                         │
│    ├── FileListViewModel                              │
│    ├── CenterInfoViewModel                            │
│    ├── OptionsPanelViewModel                          │
│    └── StatusBarViewModel                             │
│  EditSectorViewModel                                  │
│  FileViewerViewModel  ← multiple instances supported  │
│  EditAppSettingsViewModel                             │
│  ...                                                  │
└───────────────────┬───────────────────────────────────┘
                    │ uses
┌───────────────────▼───────────────────────────────────┐
│                   Services (DI-registered singletons) │
│  IDialogService         — modal + modeless dialogs    │
│  IFilePickerService     — open/save file pickers      │
│  ISettingsService       — read/write AppSettings      │
│  IClipboardService      — clipboard operations        │
│  IViewerService         — viewer registry/lifecycle    │
│  WorkspaceService       — WorkTree lifecycle,         │
│                           open/close/recent files     │
└───────────────────┬───────────────────────────────────┘
                    │ uses
┌───────────────────▼───────────────────────────────────┐
│              Existing Libraries (unchanged)           │
│  DiskArc, AppCommon, CommonUtil, FileConv             │
└───────────────────────────────────────────────────────┘
```

### 2.1 Technology Decisions

| Decision | Choice | Rationale |
|---|---|---|
| **MVVM framework** | **ReactiveUI** (`Avalonia.ReactiveUI` integration) | First-class Avalonia support, `ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`, `Interaction<,>` for dialogs. |
| **DI container** | **Microsoft.Extensions.DependencyInjection** | Start with basic service registration; expand as needed for testing. |
| **File reorganization** | **Incremental** | Move files into `ViewModels/`, `Services/`, `Models/` as each phase touches them — avoids overwhelming diffs and keeps cherry-picking between branches manageable. |
| **Base class** | `ReactiveObject` | Replaces hand-rolled `INotifyPropertyChanged`; provides `RaiseAndSetIfChanged`, `WhenAnyValue`, reactive property change streams. |
| **Commands** | `ReactiveCommand<TParam, TResult>` | Replaces `RelayCommand`; built-in async support, observable `CanExecute` via `WhenAnyValue`, automatic busy-state tracking. |
| **Dialog invocation** | `Interaction<TInput, TOutput>` + `IDialogService` | ViewModels raise interactions; Views handle them to show dialogs. |

### 2.2 Design Principles

- **ViewModels own all state** — every bound property moves out of code-behind.
- **Views are declarative shells** — code-behind is limited to pure UI concerns
  (focus management, drag-drop plumbing, control-level event wiring).
- **Services abstract platform dependencies** — dialogs, file pickers, clipboard,
  and settings are injectable, enabling unit testing without Avalonia.
- **Commands live in ViewModels** — `ReactiveCommand` instances in ViewModels;
  `canExecute` observables use `WhenAnyValue` over ViewModel properties.
- **No ViewModel → View reference** — ViewModels never touch Avalonia controls
  or `Window` objects.
- **Reactive pipelines where natural** — use `WhenAnyValue`, `ObservableAsPropertyHelper`,
  and `Subscribe` for derived state, but keep things readable; not every property
  needs a reactive chain.
- **Design for multi-instance** — ViewModels (especially `FileViewerViewModel`)
  must be designed as independent, self-contained units that can be instantiated
  multiple times concurrently. Avoid static/singleton state in ViewModels.
- **Panels as composable units** — child ViewModels (`ArchiveTreeViewModel`,
  `FileListViewModel`, `CenterInfoViewModel`, etc.) should be self-contained
  enough that they could be hosted in dockable panels in the future.

### 2.3 Future Architecture Directions

The MVVM refactor is designed to **enable** (not implement) the following
future capabilities from `KNOWN_ISSUES.md`:

| Future Goal | How MVVM Enables It |
|---|---|
| **Multiple concurrent FileViewers** | `FileViewerViewModel` is a self-contained `ReactiveObject` with no singleton state. `IViewerService` (DI singleton) manages the viewer registry. `IDialogService.ShowModeless()` supports multiple instances. |
| **Dynamic windowing / docking** (Avalonia Dock) | Child ViewModels are self-contained and don't reference `MainWindow` or each other's Views. A docking host can bind to any child VM. |
| **Single-process, multi-window** ("File → New Window") | `WorkspaceService` is a DI singleton shared across windows. Each window gets its own `MainViewModel` instance. Clipboard/undo coordination goes through shared services. |
| **Panel modularity** (detachable/rearrangeable panels) | Each panel (archive tree, file list, info, options) has its own ViewModel. Panels communicate through the parent VM or shared services, never through direct View references. |
| **Unit testing** | ViewModels and services are testable without Avalonia runtime. |

**Design rule:** When making MVVM decisions, ask "would this still work if
there were two FileViewers open at once?" and "would this still work if
the file list panel were in a separate docked window?" If the answer is no,
choose a different approach.

---

## 3. Inventory of Items to Migrate

### 3.1 MainWindow Code-Behind → MainViewModel

The following categories of members currently in `MainWindow.axaml.cs` must
move to a `MainViewModel` (or child ViewModels):

| Category | Approx. Count | Examples |
|---|---|---|
| ICommand properties | ~50 | `OpenCommand`, `CloseCommand`, `CopyCommand`, `ViewFilesCommand`, `EditSectorsCommand`, etc. |
| Panel visibility flags | ~8 | `LaunchPanelVisible`, `MainPanelVisible`, `ShowOptionsPanel`, `ShowCenterFileList`, `ShowCenterInfoPanel`, `ShowDebugMenu`, etc. |
| Status bar text | 2 | `CenterStatusText`, `RightStatusText` |
| Options panel toggle props | ~20 | `IsChecked_AddExtract`, `IsChecked_ImportExport`, `IsChecked_AddCompress`, `IsChecked_ExtPreserveNone`, etc. |
| File list & column visibility | ~8 | `FileList`, `ShowCol_FileName`, `ShowCol_PathName`, `ShowCol_Format`, `ShowSingleDirFileList`, etc. |
| Tree collections | 2 | `ArchiveTreeRoot`, `DirectoryTreeRoot` |
| Info panel data | ~10 | `CenterInfoText1`, `CenterInfoText2`, `CenterInfoList`, `PartitionList`, `NotesList`, `MetadataList`, `ShowDiskUtilityButtons`, etc. |
| Converter lists | 4 | `ImportConverters`, `ExportConverters`, `SelectedImportConverter`, `SelectedExportConverter` |
| Recent file links | ~6 | `RecentFileName1`, `RecentFilePath1`, etc. |
| Toolbar highlight brushes | 3 | `FullListBorderBrush`, `DirListBorderBrush`, `InfoBorderBrush` |
| Inner classes | 4 | `ConvItem`, `CenterInfoItem`, `PartitionListItem`, `MetadataItem` |
| Helper methods | ~15 | `ConfigureCenterPanel()`, `PublishSideOptions()`, `SetMetadataList()`, `PopulateRecentFilesMenu()`, etc. |

### 3.2 MainController → Merge into MainViewModel + Services

`MainController.cs` and `MainController_Panels.cs` contain:

| Category | Destination |
|---|---|
| WorkTree lifecycle (open, close, dispose) | `WorkspaceService` |
| Settings load/save/apply | `ISettingsService` + ViewModel init |
| Recent files management | `WorkspaceService` or `RecentFilesService` |
| Archive/Directory tree population | `MainViewModel` (or sub-VMs) |
| File list population & verification | `FileListViewModel` |
| Selection state (`CachedArchiveTreeSelection`, etc.) | `MainViewModel` properties |
| All Actions (Add, Extract, Delete, Move, EditAttributes, etc.) | `MainViewModel` methods (calling services) |
| CanExecute helper properties (`IsFileOpen`, `CanWrite`, `AreFileEntriesSelected`, etc.) | `MainViewModel` computed properties |
| Direct UI control access (see §1.2 #4) | Replaced by ViewModel properties + View-side behaviors |
| Dialog creation (`new EditAttributes(mMainWin, ...)`) | `IDialogService.ShowDialog<T>(...)` |
| File picker calls (`PlatformUtil.AskFileToOpen()`, `StorageProvider`) | `IFilePickerService` |

### 3.3 Dialog Windows

Each dialog follows the same pattern: `Window : INotifyPropertyChanged` with
`DataContext = this`. Each needs:

| Dialog | ViewModel to Create |
|---|---|
| `EditSector.axaml.cs` | `EditSectorViewModel` |
| `EditAppSettings.axaml.cs` | `EditAppSettingsViewModel` |
| `Tools/FileViewer.axaml.cs` | `FileViewerViewModel` — **multi-instance**: designed for concurrent viewers (see §2.3, §7.10) |
| `EditAttributes.axaml.cs` | `EditAttributesViewModel` |
| `CreateDiskImage.axaml.cs` | `CreateDiskImageViewModel` |
| `CreateFileArchive.axaml.cs` | `CreateFileArchiveViewModel` |
| `SaveAsDisk.axaml.cs` | `SaveAsDiskViewModel` |
| `ReplacePartition.axaml.cs` | `ReplacePartitionViewModel` |
| `CreateDirectory.axaml.cs` | `CreateDirectoryViewModel` |
| `FindFile.axaml.cs` | `FindFileViewModel` |
| `EditMetadata.axaml.cs` | `EditMetadataViewModel` |
| `AddMetadata.axaml.cs` | `AddMetadataViewModel` |
| `EditConvertOpts.axaml.cs` | `EditConvertOptsViewModel` |
| `AboutBox.axaml.cs` | (minimal; may not need a VM) |
| `Tools/LogViewer.axaml.cs` | `LogViewerViewModel` |
| `Tools/ShowText.axaml.cs` | `ShowTextViewModel` (or parametric) |
| `Tools/DropTarget.axaml.cs` | `DropTargetViewModel` |
| `Common/WorkProgress.axaml.cs` | `WorkProgressViewModel` |
| `Actions/OverwriteQueryDialog.axaml.cs` | (simple; may not need a VM) |
| `LibTest/TestManager.axaml.cs` | `TestManagerViewModel` |
| `LibTest/BulkCompress.axaml.cs` | `BulkCompressViewModel` |

### 3.4 Data/Model Classes

These already exist as standalone classes and are reasonably clean; they may
need minor adjustments:

| Class | Notes |
|---|---|
| `FileListItem` | Already a POCO-like data object; keep as-is or make it a lightweight VM. Currently has static helper methods that reference Avalonia `DataGrid` — those move to View code. |
| `ArchiveTreeItem` | Reactive tree-node model. See refactoring details below. |
| `DirectoryTreeItem` | Same pattern as `ArchiveTreeItem`. See refactoring details below. |
| `ClipInfo` | Pure data class; no changes needed. |
| `DebugMessageLog` | Pure data/event class; no changes needed. |

#### ArchiveTreeItem / DirectoryTreeItem Refactoring

These classes are best understood as **reactive tree-node models** — not
pure domain models and not top-level ViewModels. The domain data they wrap
(`WorkTree.Node`, `IFileEntry`) is already provided by the library types;
a separate model wrapper would be pointless indirection. The UI state they
carry (`IsExpanded`, `IsSelected`, `Items`, `Name`) is inherently display-
facing and inseparable from the tree binding.

**During the refactor:**
- Change base class from hand-rolled `INotifyPropertyChanged` to
  `ReactiveObject`; properties use `this.RaiseAndSetIfChanged(...)`.
- **Move UI-coupled static methods** (`SelectItem(MainWindow, ...)`,
  `SelectBestFrom(TreeView, ...)`, `SelectItemByEntry(MainWindow, ...)`)
  into `MainViewModel` or sub-ViewModels. The `MainWindow` / `TreeView`
  parameters are eliminated; selection is done by setting a VM property
  and raising an interaction for scroll-into-view.
- **Keep pure data methods** (`FindItemByEntry`, `FindItemByDAObject`,
  `ConstructTree`) on the classes — they don't touch UI.
- **Move icon resolution out of the constructor.** Replace
  `Application.Current!.FindResource()` with a value converter or
  injected icon-resolver so the class has no Avalonia runtime dependency.
- **Selection management** (`PurgeSelectionsExcept`, `PurgeSelections`)
  stays — it's pure tree-walking logic.
- **Place in `Models/`** folder. They're reactive model objects (a common
  ReactiveUI pattern for items in bound collections), not top-level VMs.

This approach preserves future compatibility: the items are self-contained
data+state objects that any View (including docked panels) can bind to.

### 3.5 Existing Infrastructure to Keep / Replace

| Item | Notes |
|---|---|
| `RelayCommand` | **Replace** with `ReactiveCommand<TParam, TResult>` as commands migrate to ViewModels. The existing `RelayCommand` can remain during transition but will be fully retired. |
| `AppSettings` / `SettingsHolder` | Keep the underlying storage; wrap with `ISettingsService` for VM consumption. |
| `WindowPlacement` | View-only concern; stays in code-behind. |
| `BitmapUtil`, `PlatformUtil` | Utility classes; no MVVM impact. |
| `Actions/*Progress` classes | Keep as `IWorker` implementations; invoked from VM via `IDialogService`. |

---

## 4. Services to Introduce

All services are registered via `Microsoft.Extensions.DependencyInjection`
in `App.axaml.cs` and injected into ViewModels through constructor parameters.
Start with the services listed below; additional service abstractions can be
introduced on a case-by-case basis in future iterations.

### 4.1 IDialogService

```csharp
public interface IDialogService
{
    Task<bool?> ShowDialogAsync<TViewModel>(TViewModel viewModel);
    Task<MBResult> ShowMessageAsync(string text, string caption,
        MBButton button = MBButton.OK, MBIcon icon = MBIcon.None);
    Task<bool> ShowConfirmAsync(string text, string caption);
    void ShowModeless<TViewModel>(TViewModel viewModel);
}
```

The implementation registers ViewModel→View mappings (e.g.,
`EditSectorViewModel` → `EditSector` window) and handles `ShowDialog`
plumbing. This eliminates all `new SomeDialog(mMainWin, ...)` calls from
ViewModels.

**Multi-instance support:** `ShowModeless` must support multiple concurrent
instances of the same ViewModel type (e.g., multiple `FileViewerViewModel`
instances). The implementation should create a new View for each call, not
reuse a singleton. The calling ViewModel (e.g., `MainViewModel`) tracks
active modeless VMs in a collection.

For simple cases, ReactiveUI's `Interaction<TInput, TOutput>` can also be
used to request dialog display from a ViewModel, with the View registering
a handler.

### 4.2 IFilePickerService

```csharp
public interface IFilePickerService
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FilePickerFileType>? filters = null);
    Task<string?> SaveFileAsync(string title, string? suggestedName,
        IReadOnlyList<FilePickerFileType>? filters = null);
    Task<string?> OpenFolderAsync(string title, string? initialDir = null);
}
```

Wraps Avalonia's `StorageProvider` calls so that ViewModels don't need
`TopLevel.GetTopLevel(mMainWin)`.

### 4.3 ISettingsService

```csharp
public interface ISettingsService
{
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
    int GetInt(string key, int defaultValue);
    void SetInt(string key, int value);
    string GetString(string key, string defaultValue);
    void SetString(string key, string value);
    T GetEnum<T>(string key, T defaultValue) where T : struct, Enum;
    void SetEnum<T>(string key, T value) where T : struct, Enum;
    void Load();
    void Save();
}
```

Thin wrapper around the existing `AppSettings.Global` / `SettingsHolder`.

### 4.4 IClipboardService

```csharp
public interface IClipboardService
{
    Task SetFilesAsync(IEnumerable<string> paths);
    Task<IEnumerable<string>?> GetFilesAsync();
    void ClearIfPending();
}
```

### 4.5 WorkspaceService

Encapsulates the `WorkTree` lifecycle, recent-files management, and
formatter setup that currently lives in `MainController`:

```csharp
public class WorkspaceService
{
    public WorkTree? WorkTree { get; }
    public bool IsFileOpen { get; }
    public bool CanWrite { get; }
    public string WorkPathName { get; }
    public Formatter Formatter { get; }
    public AppHook AppHook { get; }
    public List<string> RecentFilePaths { get; }

    public Task OpenAsync(string path, bool readOnly, AutoOpenDepth depth);
    public bool Close();
    // ...
}
```

### 4.6 IViewerService

Centralized registry for active `FileViewerViewModel` instances.
Registered as a DI singleton. Viewers self-register on construction
and self-deregister on disposal. Provides source-scoped cleanup when
a file is closed. See §7.10 for lifecycle gotchas.

The viewer registry is **global** (not per-workspace or per-window), with
source-file tagging for scoped cleanup. `IViewerService` owns the active
viewer collection — not `MainViewModel`. During file close, `MainViewModel`
calls `CloseViewersForSource(...)` before `WorkTree` is disposed.

```csharp
public interface IViewerService
{
    IReadOnlyList<FileViewerViewModel> ActiveViewers { get; }
    void Register(FileViewerViewModel viewer);
    void Unregister(FileViewerViewModel viewer);
    void CloseViewersForSource(string workPathName);
}
```

---

## 5. Proposed Folder Structure

```
cp2_avalonia/
├── ViewModels/
│   ├── MainViewModel.cs                (extends ReactiveObject)
│   ├── FileListViewModel.cs
│   ├── ArchiveTreeViewModel.cs         (wraps ArchiveTreeItem collection)
│   ├── DirectoryTreeViewModel.cs       (wraps DirectoryTreeItem collection)
│   ├── CenterInfoViewModel.cs
│   ├── OptionsPanelViewModel.cs
│   ├── StatusBarViewModel.cs
│   ├── EditSectorViewModel.cs
│   ├── EditAppSettingsViewModel.cs
│   ├── FileViewerViewModel.cs          (multi-instance: supports concurrent viewers)
│   ├── EditAttributesViewModel.cs
│   ├── CreateDiskImageViewModel.cs
│   ├── CreateFileArchiveViewModel.cs
│   ├── CreateDirectoryViewModel.cs
│   ├── SaveAsDiskViewModel.cs
│   ├── ReplacePartitionViewModel.cs
│   ├── FindFileViewModel.cs
│   ├── EditMetadataViewModel.cs
│   ├── EditConvertOptsViewModel.cs
│   └── LogViewerViewModel.cs
├── Services/
│   ├── IDialogService.cs
│   ├── DialogService.cs
│   ├── IFilePickerService.cs
│   ├── FilePickerService.cs
│   ├── ISettingsService.cs
│   ├── SettingsService.cs
│   ├── IClipboardService.cs
│   ├── ClipboardService.cs
│   ├── IViewerService.cs
│   ├── ViewerService.cs
│   └── WorkspaceService.cs
├── Models/
│   ├── FileListItem.cs                 (moved from root, static UI helpers removed)
│   ├── ArchiveTreeItem.cs              (moved from root, static UI helpers removed)
│   ├── DirectoryTreeItem.cs            (moved from root, static UI helpers removed)
│   ├── CenterInfoItem.cs              (extracted from MainWindow)
│   ├── PartitionListItem.cs           (extracted from MainWindow)
│   ├── MetadataItem.cs                (extracted from MainWindow)
│   └── ConvItem.cs                    (extracted from MainWindow)
├── Views/                              (existing AXAML files, thinned code-behind)
│   ├── MainWindow.axaml(.cs)
│   ├── EditSector.axaml(.cs)
│   ├── EditAppSettings.axaml(.cs)
│   └── ...
├── Common/                             (existing utilities)
│   ├── RelayCommand.cs                 (kept during transition; retired once all commands are ReactiveCommand)
│   ├── WindowPlacement.cs
│   ├── BitmapUtil.cs
│   └── ...
├── Actions/                            (existing IWorker implementations, unchanged)
├── Tools/                              (FileViewer, LogViewer, etc. — thinned)
└── LibTest/                            (test runner dialogs — thinned)
```

---

## 6. Migration Strategy — Phased Approach

### Phase 0: Preparation (non-breaking)

1. **Add ReactiveUI NuGet packages** — add `Avalonia.ReactiveUI` (which pulls
   in `ReactiveUI` transitively) to `cp2_avalonia.csproj`.
2. **Add `Microsoft.Extensions.DependencyInjection`** NuGet package.
3. **Wire up ReactiveUI in `App.axaml.cs`** — call `.UseReactiveUI()` in the
   Avalonia app builder chain; set up a basic `IServiceProvider` with
   `ServiceCollection` for DI registration.
4. **Extract inner classes** from `MainWindow` (`ConvItem`, `CenterInfoItem`,
   `PartitionListItem`, `MetadataItem`) into standalone files under `Models/`.
5. **Create service interfaces** (`IDialogService`, `IFilePickerService`,
   `ISettingsService`, `IClipboardService`) with concrete implementations,
   registered in the DI container.
6. **Refactor static helper methods** in `ArchiveTreeItem`, `DirectoryTreeItem`,
   `FileListItem` that take `MainWindow` or Avalonia control references — split
   into model-only logic (stays) and view-specific logic (moves to View
   code-behind or attached behaviors).

**Note:** No custom `ViewModelBase` or `AsyncRelayCommand` needed — ReactiveUI's
`ReactiveObject` and `ReactiveCommand` provide these capabilities.

**Validation:** Application behavior is 100% unchanged after Phase 0.

### Phase 1: MainViewModel — Core State

1. Create `MainViewModel` inheriting `ReactiveObject`.
2. Move all **bindable properties** from `MainWindow.axaml.cs`:
   - Panel visibility (`LaunchPanelVisible`, `MainPanelVisible`, etc.)
   - Status bar text
   - Tree collections (`ArchiveTreeRoot`, `DirectoryTreeRoot`)
   - File list (`FileList`)
   - Center info data
   - Column visibility flags
   - Options panel toggles
   - Recent file properties
   - Toolbar brush properties
3. Change `MainWindow.axaml` root `DataContext` to `MainViewModel` instance.
4. Update `MainWindow.axaml.cs`:
   - Remove `INotifyPropertyChanged` implementation
   - Resolve `MainViewModel` from DI (or construct with injected services),
     set as `DataContext`
   - Keep only pure-UI code (drag-drop handlers, focus management, sort plumbing,
     pointer events, column auto-size)
5. `MainWindow.axaml` bindings should continue working unchanged (property names
   stay the same).
6. Properties in `MainViewModel` use `this.RaiseAndSetIfChanged(ref field, value)`
   (ReactiveObject pattern) or `[Reactive]` attribute where appropriate.

**Validation:** Build, run, verify all panels render and data binds correctly.

### Phase 2: Commands → MainViewModel

1. Move all ~50 command properties from `MainWindow` to `MainViewModel`,
   converting from `RelayCommand` to `ReactiveCommand<Unit, Unit>` (or
   appropriate type parameters).
2. `canExecute` observables use `this.WhenAnyValue(x => x.IsFileOpen, ...)`
   instead of manual `canExecute` delegates referencing `mMainCtrl.*`.
3. `MainWindow.axaml` bindings (`{Binding OpenCommand}`, etc.) work unchanged
   because `DataContext` is now the ViewModel.
4. `RefreshAllCommandStates()` is **eliminated** — `ReactiveCommand` automatically
   re-evaluates `CanExecute` when the observed properties change via
   `WhenAnyValue`. No manual invalidation needed.
5. Async commands (`ReactiveCommand.CreateFromTask(...)`) replace the current
   pattern of wrapping `async` lambdas in synchronous `RelayCommand`.

**Validation:** Verify all menu items, toolbar buttons, and key bindings work.

### Phase 3: Dissolve MainController

1. **Merge `MainController` logic into `MainViewModel`** and services:
   - File open/close → `WorkspaceService` (DI-injected) + VM orchestration
   - Settings load/save → `ISettingsService` (DI-injected)
   - Actions (add, extract, delete, etc.) → VM methods calling services
   - Tree population → VM methods
   - File list population → `FileListViewModel` or VM method
2. **Eliminate the `mMainWin` back-reference.** Anywhere `MainController` accesses
   `mMainWin.SomeProperty`, the ViewModel now owns that property directly.
   Anywhere it accesses a control (`mMainWin.fileListDataGrid`), replace with:
   - ViewModel property + View binding (for selection, scroll position)
   - `IDialogService` (for showing dialogs)
   - `IFilePickerService` (for file/folder pickers)
   - ReactiveUI `Interaction<,>` requests (for focus, cursor changes,
     scroll-into-view, and other view-specific actions)
3. Delete `MainController.cs` and `MainController_Panels.cs`.

**Validation:** Full regression test of all features.

### Phase 4: Dialog ViewModels

For each dialog, repeat the pattern:
1. Create `SomeDialogViewModel` in `ViewModels/`.
2. Move all bindable properties and logic from dialog code-behind to the VM.
3. Register the ViewModel→View mapping in `DialogService`.
4. Thin the dialog code-behind to `InitializeComponent()` + minimal event handlers.
5. The main `MainViewModel` creates the dialog VM, calls
   `IDialogService.ShowDialogAsync(vm)`, and reads results from the VM.

Priority order (most complex / highest value first):
1. `EditSectorViewModel` (complex state, hex editing)
2. `FileViewerViewModel` (tabs, converter options, bitmap display;
   **must support multiple concurrent instances** — see §2.3, §7.10)
3. `EditAppSettingsViewModel` (settings copy, apply/cancel)
4. `EditAttributesViewModel`
5. Remaining dialogs in any order

### Phase 5: Sub-ViewModels & Panel Modularity

1. Extract child ViewModels if `MainViewModel` is too large:
   - `ArchiveTreeViewModel` — selection, population, sub-tree close
   - `DirectoryTreeViewModel` — selection, population
   - `FileListViewModel` — population, sorting, column visibility
   - `OptionsPanelViewModel` — all add/extract/import/export toggles
   - `CenterInfoViewModel` — info text, metadata, partitions, notes
   - `StatusBarViewModel` — entry counts, free space
2. Wire up child VMs as properties of `MainViewModel`.
3. Use ReactiveUI's `WhenAnyValue` and `ObservableAsPropertyHelper<T>` to
   compose derived state across parent/child ViewModels.
4. Retire `RelayCommand.cs` once all commands have been converted to
   `ReactiveCommand`.
5. **Ensure each child ViewModel is self-contained** — no direct references
   to sibling ViewModels or to `MainWindow`. Communication flows through
   the parent `MainViewModel` or shared services. This is the prerequisite
   for future docking/paneling (see §7.11).

### Phase 6: Multi-Viewer & Future Preparation (Optional)

This phase is **not required** for the core MVVM refactor but can be
undertaken once Phases 0–5 are stable:

1. Convert `FileViewer` from modal dialog to modeless window.
2. `IViewerService` manages the active viewer registry; viewers
   self-register/deregister (see §4.6, §7.10).
3. Add FileViewer side panel with zoom controls (per `KNOWN_ISSUES.md`
   "Necessary Improvements").
4. Evaluate and integrate a docking framework (e.g., Avalonia Dock) for
   panel rearrangement.
5. Prototype single-process, multi-window support ("File → New Window").

---

## 7. Specific Technical Challenges

### 7.1 DataGrid Control Access

`MainController_Panels` directly accesses `mMainWin.fileListDataGrid` for:
- `SelectedItem` / `SelectedItems` — replace with VM `SelectedFileListItem`
  and `SelectedFileListItems` properties, bound via `SelectedItem="{Binding ...}"`.
- `ScrollIntoView(...)` — use an attached behavior or interaction trigger
  (e.g., a `ScrollIntoViewBehavior` that watches a VM property change).
- `SelectAll()` — expose a `SelectAllRequested` event or use an attached behavior.

### 7.2 TreeView Selection

`archiveTree.SelectedItem` and `directoryTree.SelectedItem` are set from
`MainController`. Replace with:
- VM `SelectedArchiveTreeItem` / `SelectedDirectoryTreeItem` properties.
- Two-way binding or a selection behavior that synchronizes TreeView selection
  with the VM property (Avalonia TreeView selection binding can be tricky; may
  need an attached property or `TreeView.SelectedItem` binding with a converter).

### 7.3 Drag-and-Drop

Drag-drop handlers in `MainWindow.axaml.cs` (launch panel drop, file list
drag/drop, directory tree drop) are inherently view-level Avalonia code.
These should stay in code-behind but delegate the **data operations** to the
ViewModel (e.g., `ViewModel.HandleFileDrop(paths)`,
`ViewModel.MoveFiles(entries, targetDir)`).

### 7.4 Column Sorting

The current `FileListDataGrid_Sorting` handler manipulates `FileList`
(an `ObservableCollection`) directly and tracks sort state (`mSortColumn`,
`mSortAscending`).

**Decision: ViewModel owns sort state; View forwards the click event and
applies visual indicators only.**

Sort state must survive file-list repopulation (`ReapplyFileListSort()` is
called from controller logic after rebuilds), should be persistable across
sessions, is domain-meaningful (testable), and must travel with the panel
if the file list is detached to a docked window in the future.

**Implementation plan:**
- Promote `FileListItem.ItemComparer.ColumnId` to a public enum (on
  `FileListItem` or `FileListViewModel`).
- `FileListViewModel` (or `MainViewModel` initially) owns `SortColumn`
  (`ColumnId?`), `SortAscending` (`bool`), `IsResetSortEnabled`
  (computed), `ApplySort()`, `ResetSort()`, and `ReapplySort()`.
- `ItemComparer` constructor changes from `(DataGridColumn, bool)` to
  `(ColumnId, bool)` — no Avalonia dependency.
- View code-behind handles `DataGridColumnEventArgs`, maps
  `col.Header` → `ColumnId`, calls `ViewModel.SetSort(columnId, asc)`,
  and applies/clears visual sort-direction indicators on column headers.
- `mSuppressSort` stays in View code-behind (pointer/resize-grip plumbing).

### 7.5 Toast Notifications

`PostNotification()` manipulates `toastBorder` and `toastText` directly.
Replace with a VM `ToastMessage` / `ToastIsVisible` property and bind the
toast UI to it. Use a timer in the VM or a View-side animation trigger.

### 7.6 Cursor Changes

`mMainWin.Cursor = new Cursor(StandardCursorType.Wait)` — use a VM
`IsBusy` property bound to a cursor-switching behavior or style trigger.

### 7.7 Platform-Specific Visibility

`openPhysicalDriveMenuItem.IsVisible = false` on non-Windows — keep in
View code-behind or use a platform converter.

### 7.8 Window Title

`mMainWin.Title = sb.ToString()` — bind `Title="{Binding WindowTitle}"` in
AXAML; set `WindowTitle` property in VM.

### 7.9 Focus Management

Several places call `.Focus()` on tree views or data grids. These are
view concerns — keep in code-behind, triggered by VM property changes or
interaction requests.

### 7.10 Multiple Concurrent FileViewers

`KNOWN_ISSUES.md` calls for allowing multiple viewers/editors at once.
This has several design implications:

- **`FileViewerViewModel` must be fully self-contained.** Each instance
  holds its own file data, converter state, tab selection, and display
  settings. No static or singleton state.
- **`IViewerService` (DI singleton) manages the viewer registry.** Each
  `FileViewerViewModel` is injected with `IViewerService`, registers
  itself on construction, and calls `Unregister` on disposal. The service
  tracks all active viewers centrally — not `MainViewModel`. This
  decouples viewer lifecycle from any single window and works naturally
  with multi-window (single-process) architecture.
- **Source-file association.** Each viewer holds a source identifier
  (e.g., `WorkPathName`) so that `IViewerService.CloseViewersForSource(...)`
  can tear down viewers whose underlying file is being closed.
- **`IDialogService.ShowModeless()`** creates a new View per call.
  The service must not cache or reuse modeless windows.
- **Resource management:** Each viewer may hold open file handles or
  decoded data. `FileViewerViewModel` should implement `IDisposable` or
  use ReactiveUI's `WhenDeactivated` to release resources when closed.
- **Side panel/toolbar for FileViewer** (from `KNOWN_ISSUES.md`
  "Necessary Improvements"): zoom controls, Ctrl+Mouse/Ctrl+± should
  be per-viewer instance state, not global. This is **deferred** — part
  of a broader FileViewer redesign. The current MVVM refactor ensures
  `FileViewerViewModel` is flexible enough to accommodate it later.

#### Gotchas to Watch For

The service-based viewer registry introduces scenarios that are easy to
get wrong. Watch for these during implementation:

1. **Stale viewers after file close.** When `WorkspaceService.Close()` is
   called, all viewers for that source must be closed/disposed *before*
   the underlying `WorkTree` is disposed. If a viewer still holds a
   reference to file data from a disposed `WorkTree`, it will crash or
   show corrupt data. Ensure `CloseViewersForSource(...)` is synchronous
   and completes before `WorkTree.Dispose()`.

2. **Viewer disposal race conditions.** A user may close a viewer window
   at the same moment the parent file is being closed. Both paths call
   `Unregister`. Ensure the service's collection is thread-safe or
   marshal all mutations to the UI thread.

3. **Orphaned registrations.** If a viewer's `Dispose`/`WhenDeactivated`
   fails to fire (e.g., the View is closed without triggering the
   expected lifecycle event), the service will retain a dead reference.
   Consider a weak-reference fallback or periodic cleanup, and verify
   the Avalonia window lifecycle guarantees.

4. **Re-opening the same file.** If a user closes a file and re-opens it,
   the `WorkPathName` may be identical but the `WorkTree` instance is
   new. Viewers from the old session must not survive into the new one.
   `CloseViewersForSource` must be called during close, not deferred.

5. **Cross-window viewer ownership.** In the future multi-window model,
   a viewer opened from Window A may outlive Window A if the user closes
   that window but keeps the viewer. The service-based approach handles
   this naturally (the service is a singleton, not owned by any window),
   but UI parenting (which window "owns" the viewer for task-bar
   grouping, z-order, etc.) needs separate consideration.

### 7.11 Panel Modularity & Composability

The current `MainWindow` has a fixed layout: archive tree (left), directory
tree (left-bottom), file list / info panel (center), options panel (right),
status bar (bottom). `KNOWN_ISSUES.md` envisions user-preferred layouts
(VS/Code-style docking, splits, tabs).

The MVVM refactor prepares for this by:

- **Making each panel a child ViewModel** with its own self-contained state
  and no cross-references to sibling panel Views.
- **Communicating through the parent ViewModel** (`MainViewModel`) or shared
  services rather than direct panel-to-panel messaging.
- **Avoiding hardcoded layout assumptions in ViewModels.** For example,
  `FileListViewModel` should not assume it's in the center of a `Grid`;
  it should work identically whether rendered inline, in a tab, or in a
  detached window.

Docking framework integration is **deferred** until after MVVM Phases 0-5
are complete and stable. The self-contained child VM pattern ensures a
docking framework (e.g., [Avalonia Dock](https://github.com/wieslawsoltes/Dock))
can be introduced later without re-architecting.

### 7.12 Single-Process, Multi-Window Architecture (Future)

`KNOWN_ISSUES.md` envisions a single-process model where multiple
`MainWindow` instances share a process for safe workspace access,
consistent undo/redo, and reliable drag/drop.

The MVVM refactor enables this by:

- **`WorkspaceService` as a DI singleton** — shared across all windows.
  Each `MainWindow` gets its own `MainViewModel`, but they share the
  same workspace and clipboard services.
- **No global mutable state in ViewModels** — each `MainViewModel`
  instance is independent.
- **`IClipboardService` coordination** — the service handles cross-window
  clipboard state centrally.
- **Settings are centralized** via `ISettingsService` (DI singleton).

This architecture is not implemented in the current MVVM phases but the
design choices above ensure it's achievable without re-architecting.

---

## 8. Testing Opportunities

Once ViewModels are separated from Views:

| Test Type | What It Covers |
|---|---|
| **ViewModel unit tests** | Command `canExecute` logic, property change notifications, state transitions (open → file loaded → panel visible), options panel toggle behavior. |
| **Service unit tests** | `WorkspaceService` open/close lifecycle with mock `WorkTree`; `SettingsService` round-trip. |
| **Integration tests** | ViewModel + real services (minus UI), e.g., open a real disk image, verify `FileList` population. |

Recommended framework: **xUnit** (as noted in `KNOWN_ISSUES.md`).

---

## 9. Estimated Effort by Phase

| Phase | Scope | Relative Effort | Risk |
|---|---|---|---|
| 0 — Preparation | Infrastructure, no behavior change | Low | Very Low |
| 1 — MainViewModel Core | ~100 properties + AXAML rebinding | Medium | Medium (binding breakage) |
| 2 — Commands | ~50 commands | Medium | Low |
| 3 — Dissolve Controller | Merge ~1,800 lines of logic | High | High (regression risk) |
| 4 — Dialog VMs | ~20 dialogs | High (volume) | Medium |
| 5 — Sub-VMs & Panel Modularity | Decompose large VM, self-contained panels | Medium | Low |
| 6 — Multi-Viewer & Future (optional) | Modeless FileViewer, docking eval | Medium | Medium |

**Total:** Phases 0–5 are the core MVVM refactor. Phase 6 is optional and
addresses the multi-viewer and docking goals from `KNOWN_ISSUES.md`. Each
phase should be completed and tested before moving to the next. The phased
approach ensures the application remains functional at every step.

---

## 10. Design Decisions Index

All design decisions are documented in-place throughout this document.
This index provides a quick reference to where each key decision is discussed:

| Decision | Location |
|---|---|
| MVVM framework (ReactiveUI) | §2.1 Technology Decisions |
| DI container (MS.Extensions.DI) | §2.1 Technology Decisions |
| File/folder reorganization (incremental) | §2.1 Technology Decisions |
| ArchiveTreeItem / DirectoryTreeItem identity | §3.4 Data/Model Classes |
| Column sort state ownership | §7.4 Column Sorting |
| Multi-viewer lifecycle (IViewerService) | §4.6 IViewerService, §7.10 Multiple Concurrent FileViewers |
| Docking framework timing (deferred) | §7.11 Panel Modularity & Composability |
| FileViewer side panel (deferred) | §7.10 Multiple Concurrent FileViewers |

---

## 11. NuGet Packages to Add

| Package | Purpose |
|---|---|
| `Avalonia.ReactiveUI` | ReactiveUI integration for Avalonia (pulls in `ReactiveUI` transitively) |
| `Microsoft.Extensions.DependencyInjection` | Service registration and resolution |

---

## 12. Iteration Blueprints

Detailed step-by-step implementation instructions for each phase live in
`cp2_avalonia/MVVM_Project/`. Each iteration blueprint follows the format
established in `cp2_avalonia/guidance/Iteration_0_Blueprint.md`:

- **Goal** — what the iteration accomplishes
- **Prerequisites** — branch, prior iterations completed
- **Step-by-Step Instructions** — exact code, file paths, and validation steps

---

## 13. References

- Avalonia MVVM pattern: https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern
- ReactiveUI documentation: https://www.reactiveui.net/docs/
- ReactiveUI + Avalonia: https://www.reactiveui.net/docs/getting-started/installation/avalonia
- Microsoft.Extensions.DependencyInjection: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection
- Existing `RelayCommand` (to be retired): `cp2_avalonia/Common/RelayCommand.cs`
- Current known issues: `cp2_avalonia/KNOWN_ISSUES.md` (Future Major Rework section)
- Porting notes: `cp2_avalonia/PORTING_NOTES.md`
- Coding conventions: `cp2_avalonia/guidance/Pre-Iteration-Notes.md`
- Porting overview: `cp2_avalonia/guidance/PORTING_OVERVIEW.md`
