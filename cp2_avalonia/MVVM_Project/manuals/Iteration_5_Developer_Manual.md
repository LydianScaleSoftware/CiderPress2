# Iteration 5 Developer Manual: Extract Child ViewModels from MainViewModel

> **Iteration:** 5
> **Phase:** Phase 5 — Sub-ViewModels & Panel Modularity
> **References:** `MVVM_Notes.md` §6 Phase 5, §7.22, §7.11

---

## Overview

This iteration decomposes `MainViewModel` into a parent coordinator plus six
focused child ViewModels — one for each visual panel of the application. By the
end, each panel's properties, collections, and panel-specific logic live in
their own ViewModel, while `MainViewModel` retains only cross-panel coordination,
commands, and top-level layout state.

This is classified as **mandatory** (MVVM_Notes.md §7.22). Even if
`MainViewModel` were a manageable size today, the child ViewModel extraction is
required to enable future docking/paneling, multi-window architecture, and
effective unit testing.

### How to read this manual

Each section below corresponds to a step in the Iteration 5 Blueprint. For each
step you will find:

1. **What we are going to accomplish** — goals, reasoning, MVVM/ReactiveUI
   concepts, and why this step matters.
2. **To do that, follow these steps** — a numbered, human-oriented procedure
   telling you exactly what to open, search for, add, and modify.
3. **Now that those are done, here's what changed** — a summary of files
   modified, capabilities introduced, and what stays the same.

---

## Prerequisites

Before starting this iteration, confirm:

- Iteration 4B is complete (all dialogs use ViewModels).
- `MainViewModel` is the single large ViewModel containing all panel logic.
- The application builds and runs correctly on the `avalonia_mvvm` branch.
- You have read `Pre-Iteration-Notes.md` for coding conventions.

---

## Table: Child ViewModels to Extract

| Child ViewModel | Panel | Key Responsibilities |
|---|---|---|
| `ArchiveTreeViewModel` | Left panel (top) | Archive tree root collection, selection tracking, population, sub-tree close |
| `DirectoryTreeViewModel` | Left panel (bottom) | Directory tree root collection, selection tracking, population |
| `FileListViewModel` | Center panel (main) | File list collection, sorting, column configuration, selection, double-click |
| `CenterInfoViewModel` | Center panel (info) | Info key/value pairs, partition layout, metadata entries |
| `OptionsPanelViewModel` | Right panel | All option checkboxes (add/extract settings), DDCP mode, converter selection |
| `StatusBarViewModel` | Bottom bar | Center status text, right status text, entry counts |

---

## Step 1: Create ArchiveTreeViewModel

### What we are going to accomplish

The archive tree is the top-left panel showing the hierarchy of disk images,
partitions, and file systems within an opened workspace. Currently, all the
properties and logic for this panel live inside `MainViewModel`. We are going
to pull them out into a dedicated `ArchiveTreeViewModel`.

**Why a child ViewModel?** In MVVM, a child ViewModel (sometimes called a
"sub-ViewModel" or "panel ViewModel") is a ViewModel that is *owned* by a
parent ViewModel and exposed as a property of it. The View (AXAML) binds
through the parent — for example, `{Binding ArchiveTree.TreeRoot}` instead of
`{Binding ArchiveTreeRoot}`. This pattern:

- Reduces the parent ViewModel's line count and cognitive load.
- Makes each panel independently testable in a unit test.
- Prepares for future docking — if the archive tree were in a detachable
  panel, it would need its own ViewModel anyway.

**ReactiveUI concepts involved:**

- `ReactiveObject` — the base class for all ViewModels in ReactiveUI. It
  provides `RaiseAndSetIfChanged()` for property change notification and
  `WhenAnyValue()` for reactive property observation.
- `ObservableCollection<T>` — a .NET collection that fires change events
  when items are added or removed, allowing the UI to update automatically.

**What stays on MainViewModel:** Methods that mutate the `WorkTree` (such as
`PopulateArchiveTree()`, `CloseSubTree()`, `TryOpenNewSubVolumes()`,
`ScanForSubVol()`) stay on `MainViewModel` because they require access to
`IWorkspaceService`. The child ViewModel owns *data and selection*; the parent
ViewModel owns *orchestration and mutation*.

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/ViewModels/ArchiveTreeViewModel.cs`.

2. **Add the class skeleton:**
   ```csharp
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

3. **Open `MainViewModel.cs`.** Search for the `ArchiveTreeRoot` property.
   Move it to `ArchiveTreeViewModel` as `TreeRoot`. Remove it from
   `MainViewModel`.

4. **Move selection handling:** Find any selection-changed logic related to the
   archive tree. The *property* itself (`SelectedItem`) moves to the child VM.
   The *handler* (`OnArchiveTreeSelectionChanged`) stays on `MainViewModel`
   because it coordinates across panels.

5. **Refactor static helpers on `ArchiveTreeItem`** (per §7.2, §7.9 of
   MVVM_Notes.md): Methods like `ArchiveTreeItem.SelectItem()` and
   `SelectBestFrom()` currently take `MainWindow` and set
   `mainWin.archiveTree.SelectedItem` directly on the control. Refactor these
   to set `ArchiveTreeViewModel.SelectedItem` instead. Scroll-into-view
   responsibility should be delegated to an attached behaviour or `IViewActions`
   interaction — the ViewModel must never touch a UI control.

6. **Add the child VM property to `MainViewModel`:**
   ```csharp
   public ArchiveTreeViewModel ArchiveTree { get; }
   ```
   In the `MainViewModel` constructor:
   ```csharp
   ArchiveTree = new ArchiveTreeViewModel();
   ```

7. **Do NOT wire the `WhenAnyValue` subscription yet** — that is done in
   Step 7 (`WireChildViewModels()`). The snippet shown in the blueprint for
   the constructor is intent-only.

8. **Update the AXAML binding** in `MainWindow.axaml`:
   ```xml
   <!-- Before: -->
   <TreeView ItemsSource="{Binding ArchiveTreeRoot}" ...>

   <!-- After: -->
   <TreeView ItemsSource="{Binding ArchiveTree.TreeRoot}" ...>
   ```
   Note: Selection is driven by the `IsSelected` property on each
   `ArchiveTreeItem` (already present and two-way bound via a style setter),
   **not** by a `TreeView.SelectedItem` binding. Avalonia's `TreeView` does not
   reliably support two-way binding to `SelectedItem` (§7.2).

9. **Build** (`dotnet build`) — verify zero errors before proceeding.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/ArchiveTreeViewModel.cs`
- **Modified:** `MainViewModel.cs` — `ArchiveTreeRoot` removed, replaced by
  `ArchiveTree` property (type `ArchiveTreeViewModel`).
- **Modified:** `MainWindow.axaml` — binding path updated.
- **Modified:** `ArchiveTreeItem.cs` — static helpers no longer reference
  `MainWindow`.
- **Behavior unchanged:** The archive tree displays and selects items exactly
  as before. The data just lives in a child ViewModel now.

---

## Step 1½: Wire Bidirectional Selection Sync for ArchiveTreeViewModel

### What we are going to accomplish

Because Avalonia's `TreeView` does not reliably support two-way binding to
`SelectedItem`, we need to build our own synchronization between the
`IsSelected` property on individual tree items and the ViewModel's
`SelectedItem` property. This is a two-direction sync:

- **Direction 1 (User → ViewModel):** When the user clicks a tree item, its
  `IsSelected` becomes `true`. We need to detect this and update
  `ArchiveTreeViewModel.SelectedItem`.
- **Direction 2 (ViewModel → Item):** When code programmatically sets
  `ArchiveTreeViewModel.SelectedItem` (e.g., during `SelectItem()` or
  `SelectBestFrom()`), we need to set `IsSelected = true` on the new item and
  clear it on the old one.

**ReactiveUI concepts involved:**

- `WhenAnyValue(x => x.Property)` — creates an `IObservable` that emits a
  value every time the specified property changes. This is the reactive
  equivalent of subscribing to `PropertyChanged` but is type-safe and
  composable.
- `.Where(pred)` — a LINQ-style filter on an observable sequence. Only values
  matching the predicate pass through.
- `.Subscribe(action)` — the terminal operator that executes `action` for each
  value emitted by the observable.
- `.DisposeWith(disposable)` — attaches the subscription to a
  `CompositeDisposable` so it is automatically unsubscribed when the
  `CompositeDisposable` is disposed. This prevents memory leaks.
- `CompositeDisposable` — a container that holds multiple `IDisposable`
  subscriptions and disposes them all at once. Think of it as a "subscription
  bag" — you toss subscriptions into it, and when you're done, you dispose the
  bag to clean everything up.

### To do that, follow these steps

1. **Open `ArchiveTreeViewModel.cs`.**

2. **Add a `CompositeDisposable` field** for tracking per-item subscriptions:
   ```csharp
   private readonly CompositeDisposable _itemSubscriptions = new();
   ```
   You will need a `using System.Reactive.Disposables;` at the top.

3. **Add the subscription method** that subscribes to `IsSelected` changes on a
   tree item and all its descendants:
   ```csharp
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
   ```
   **What this does:** For every tree item, it watches the `IsSelected`
   property. When `IsSelected` becomes `true` (the `.Where` filter), it
   sets `SelectedItem` on the ViewModel to that item. The subscription is
   added to `_itemSubscriptions` so it can be cleaned up later.

4. **Add the convenience method** to subscribe to all root items:
   ```csharp
   /// <summary>
   /// Subscribes to all items in TreeRoot. Call after PopulateArchiveTree().
   /// </summary>
   public void SubscribeAllSelectionChanges() {
       _itemSubscriptions.Clear();   // dispose previous subscriptions
       foreach (var root in TreeRoot)
           SubscribeToSelectionChanges(root);
   }
   ```

5. **Update the `SelectedItem` setter** to handle Direction 2 (programmatic
   selection → item `IsSelected`):
   ```csharp
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
   **What this does:** When `SelectedItem` is set programmatically, it clears
   `IsSelected` on the old item and sets it on the new one. It also updates
   `CachedSelectedItem` so that cross-VM methods can access the last known
   selection.

6. **In `MainViewModel`**, after calling `PopulateArchiveTree()`, add:
   ```csharp
   ArchiveTree.TreeRoot.Clear();
   ArchiveTreeItem.ConstructTree(ArchiveTree.TreeRoot, _workspaceService.WorkTree.RootNode);
   ArchiveTree.SubscribeAllSelectionChanges();
   ```

7. **Implement `IDisposable`** on `ArchiveTreeViewModel`:
   ```csharp
   public class ArchiveTreeViewModel : ReactiveObject, IDisposable {
       // ... existing code ...

       public void Dispose() {
           _itemSubscriptions.Dispose();
       }
   }
   ```

8. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **Modified:** `ArchiveTreeViewModel.cs` — now has bidirectional selection
  sync and implements `IDisposable`.
- **Modified:** `MainViewModel.cs` — calls `SubscribeAllSelectionChanges()`
  after tree population.
- **New capability:** Selection is now fully managed by the ViewModel layer.
  The `TreeView` control in the View just reflects `IsSelected` on each item
  — it doesn't need to be the source of truth for selection.
- **This enables:** Programmatic selection (e.g., navigating to a specific
  node after opening a sub-volume) works by setting
  `ArchiveTree.SelectedItem = someItem` — no View reference needed.

---

## Step 2: Create DirectoryTreeViewModel

### What we are going to accomplish

The directory tree is the bottom-left panel showing the folder hierarchy within
the currently selected file system. It follows the exact same pattern as
`ArchiveTreeViewModel` — a collection of tree items, a selected item, and
cached selection state.

**Why it mirrors ArchiveTreeViewModel:** Both are tree panels that suffer from
the same Avalonia `TreeView.SelectedItem` binding limitation (§7.2). Both need
bidirectional selection sync. Both need cached selection for cross-panel
coordination. The consistency makes the codebase easier to learn.

**What moves vs. what stays:**
- **Moves:** `DirectoryTreeRoot` property, `PopulateDirectoryTree()` (static
  method), `VerifyDirectoryTree()` (static helper), selection handling.
- **Stays on MainViewModel:** `SyncDirectoryTreeToFileSelection()` (cross-panel:
  file list → directory tree), `NavToParent(bool dirOnly)` (cross-panel: checks
  directory tree first, then conditionally archive tree).

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/ViewModels/DirectoryTreeViewModel.cs`.

2. **Add the class skeleton:**
   ```csharp
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
   }
   ```

3. **Open `MainViewModel.cs`.** Find `DirectoryTreeRoot` and move it to the
   child VM as `TreeRoot`.

4. **Move `PopulateDirectoryTree()`** — this is a static method that builds
   tree items from file system data. It can live on the child VM as a static
   or instance method.

5. **Move `VerifyDirectoryTree()`** — a static helper. Move it to the child VM.

6. **Refactor `DirectoryTreeItem.SelectItemByEntry()`** (§7.2, §7.9): Currently
   takes `MainWindow` and sets `mainWin.directoryTree.SelectedItem` directly.
   Refactor to set `DirectoryTreeViewModel.SelectedItem` instead. Scroll-into-
   view and focus go to an attached behaviour or `IViewActions`.

7. **Add the child VM property to `MainViewModel`:**
   ```csharp
   public DirectoryTreeViewModel DirectoryTree { get; }

   // In constructor:
   DirectoryTree = new DirectoryTreeViewModel();
   ```

8. **Update the AXAML binding** in `MainWindow.axaml`:
   ```xml
   <!-- Before: -->
   <TreeView ItemsSource="{Binding DirectoryTreeRoot}" ...>

   <!-- After: -->
   <TreeView ItemsSource="{Binding DirectoryTree.TreeRoot}" ...>
   ```

9. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/DirectoryTreeViewModel.cs`
- **Modified:** `MainViewModel.cs` — `DirectoryTreeRoot` removed, `DirectoryTree`
  property added.
- **Modified:** `MainWindow.axaml` — binding path updated.
- **Modified:** `DirectoryTreeItem.cs` — static helpers no longer reference
  `MainWindow`.
- **Behavior unchanged:** Directory navigation works exactly as before.

---

## Step 2½: Wire Bidirectional Selection Sync for DirectoryTreeViewModel

### What we are going to accomplish

Same bidirectional selection sync as Step 1½, but applied to
`DirectoryTreeViewModel` and `DirectoryTreeItem`. The pattern is identical
because both tree panels have the same `IsSelected` mechanism.

If you understood Step 1½, this step is a direct copy of that pattern with
different type names.

### To do that, follow these steps

1. **Open `DirectoryTreeViewModel.cs`.**

2. **Add the `CompositeDisposable` field:**
   ```csharp
   private readonly CompositeDisposable _itemSubscriptions = new();
   ```

3. **Add `SubscribeToSelectionChanges` and `SubscribeAllSelectionChanges`:**
   ```csharp
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

4. **Update the `SelectedItem` setter** for Direction 2:
   ```csharp
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

5. **In `MainViewModel`**, after calling `PopulateDirectoryTree()`:
   ```csharp
   DirectoryTree.SubscribeAllSelectionChanges();
   ```

6. **Implement `IDisposable`** on `DirectoryTreeViewModel` — dispose
   `_itemSubscriptions` in `Dispose()`.

7. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **Modified:** `DirectoryTreeViewModel.cs` — bidirectional selection sync
  added, `IDisposable` implemented.
- **Modified:** `MainViewModel.cs` — calls `SubscribeAllSelectionChanges()`
  after directory tree population.
- **Same capability as Step 1½:** Selection is fully ViewModel-managed.
  Directory tree item clicks flow through `IsSelected` → ViewModel, and
  programmatic selection flows ViewModel → `IsSelected`.

---

## Step 3: Create FileListViewModel

### What we are going to accomplish

The file list is the main center panel — a `DataGrid` showing files and
directories in the current selection. It is the most complex child ViewModel
because it owns sorting state, column configuration, entry counts, and
multi-select behavior.

**ReactiveUI concepts involved:**

- `ReactiveCommand.Create(action, canExecute)` — creates a command whose
  execute body is `action` and whose `CanExecute` is driven by the
  `canExecute` observable. When `canExecute` emits `false`, the command's
  bound button/menu item is automatically disabled.
- `this.WhenAnyValue(x => x.SomeProperty)` — when used as a `canExecute`
  parameter, it creates an observable that emits the property's boolean
  value every time it changes. The command automatically re-evaluates
  whether it can execute.

**Multi-select caveat:** Avalonia's `DataGrid.SelectedItems` is a read-only
`IList` managed by the control. It cannot be two-way bound to an
`ObservableCollection`. The solution is to handle `SelectionChanged` in
`MainWindow.axaml.cs` code-behind (a View concern) and copy the selection
into the ViewModel. This is an acceptable code-behind responsibility because
it is pure UI plumbing, not business logic.

**Sort state ownership (§7.4):** The ViewModel owns `SortColumn`,
`SortAscending`, `ApplySort()`, `ResetSort()`, and `ReapplySort()`. The View
code-behind handles the `DataGrid.Sorting` event, maps column headers to a
`ColumnId` enum, and calls the ViewModel. Visual sort-direction indicators on
column headers are applied by the View.

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/ViewModels/FileListViewModel.cs`.

2. **Add the class skeleton** (see the blueprint for the full code). Key
   properties to include:
   - `Items` (`ObservableCollection<FileListItem>`) — the file list data.
   - `SelectedItem` / `SelectedItems` — single and multi-select tracking.
   - `ColumnWidths` — persisted column width string.
   - `SortColumn`, `SortAscending`, `IsResetSortEnabled` — sort state.
   - `LastDirCount`, `LastFileCount` — entry counts set during population,
     read by `MainViewModel` to relay to `StatusBarViewModel`.
   - `ResetSortCommand` — a `ReactiveCommand` whose `canExecute` is driven
     by `IsResetSortEnabled`.
   - `RequestFocusAfterPopulate()` — a method callers use to request that
     the file list gets keyboard focus after the next population.

3. **Open `MainViewModel.cs`.** Find the `FileList` property (the
   `ObservableCollection<FileListItem>`). Move it to the child VM as `Items`.

4. **Move the `PopulateEntries*` methods** to `FileListViewModel`. Note the
   signature change: `PopulateFileList()` must now accept `currentWorkObject`,
   `dirTreeSel`, `selEntry`, and `focusOnFileList` as parameters, because the
   child VM has no access to `CurrentWorkObject` or `DirectoryTree.SelectedItem`.
   The call site in `MainViewModel` passes these values in.

5. **Move `VerifyFileList()` overloads** (the static helpers) to the child VM.
   The no-argument dispatch wrapper stays on `MainViewModel` — it reads
   `CurrentWorkObject` and `DirectoryTree.SelectedItem`, then delegates to
   `FileList.VerifyFileList(...)`.

6. **Move sort state and methods:** `SortColumn`, `SortAscending`,
   `ApplySort()`, `ResetSort()`, `ReapplySort()`.

7. **Move column visibility properties:** `ShowCol_FileName`, `ShowCol_PathName`,
   `ShowCol_Format`, etc. Note: the current `SetColumnVisible()` accesses
   `fileListDataGrid.Columns` directly. After migration, an attached behaviour
   bridges the VM property to column visibility.

8. **Keep `HandleFileListDoubleClick()` on MainViewModel** — it reads
   `ArchiveTree.SelectedItem`, `DirectoryTree.SelectedItem`, accesses
   `IWorkspaceService.WorkTree`, and calls tree-item helpers for selection.
   This is cross-panel coordination.

9. **Keep `GetFileSelection()` on MainViewModel** — it reads
   `FileList.SelectedItems`, `ArchiveTree.SelectedItem`,
   `DirectoryTree.SelectedItem`, and `CurrentWorkObject`.

10. **Add the child VM property to `MainViewModel`:**
    ```csharp
    public FileListViewModel FileList { get; }

    // In constructor:
    FileList = new FileListViewModel();
    ```

11. **Update the AXAML bindings:**
    ```xml
    <!-- Before: -->
    <DataGrid ItemsSource="{Binding FileList}" ...>

    <!-- After: -->
    <DataGrid ItemsSource="{Binding FileList.Items}"
              SelectedItem="{Binding FileList.SelectedItem}" ...>
    ```

12. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/FileListViewModel.cs`
- **Modified:** `MainViewModel.cs` — `FileList` collection replaced by
  `FileList` child VM property. Population call sites updated to pass
  parameters.
- **Modified:** `MainWindow.axaml` — binding paths updated.
- **New capability:** Sort state is now owned by a testable ViewModel.
  `ResetSortCommand` uses `ReactiveCommand` with observable `canExecute`.
- **Behavior unchanged:** File list displays, sorts, and selects as before.

---

## Step 3½: Extract Inner Classes to Standalone Files

### What we are going to accomplish

Before creating `CenterInfoViewModel` and `OptionsPanelViewModel`, we need to
extract four inner classes that are currently nested inside `MainWindow`. These
are data/model classes used by the info panel and options panel. They need to
be standalone so that the new child ViewModels can reference them without
depending on `MainWindow`.

**Why inner classes are a problem:** In the old architecture, `MainWindow` was
the `DataContext`, so nesting data classes inside it was convenient. But now
that ViewModels own the data, these classes need to be independent. Moving them
to `cp2_avalonia/Models/` follows the MVVM file organization (§5 of
MVVM_Notes.md).

### To do that, follow these steps

1. **Identify the four inner classes** in `MainWindow.axaml.cs`:
   - `MainWindow.CenterInfoItem`
   - `MainWindow.PartitionListItem`
   - `MainWindow.MetadataItem`
   - `MainWindow.ConvItem`

2. **Create standalone files** for each in `cp2_avalonia/Models/`:
   - `Models/CenterInfoItem.cs`
   - `Models/PartitionListItem.cs`
   - `Models/MetadataItem.cs`
   - `Models/ConvItem.cs`

3. **Move each class** from `MainWindow.axaml.cs` into its new file. Set the
   namespace to `cp2_avalonia.Models`.

4. **Do NOT convert `MetadataItem` to `ReactiveObject`** — it implements
   `INotifyPropertyChanged` and is a data model, not a ViewModel. Leave it as-is.

5. **Update all references** in `MainController.cs`, `MainController_Panels.cs`
   (if they still exist from an earlier iteration), `MainWindow.axaml.cs`, and
   any ViewModel files to use the new namespace `cp2_avalonia.Models`. Add
   `using cp2_avalonia.Models;` where needed.

6. **Build** — verify zero errors. The application should behave identically.

### Now that those are done, here's what changed

- **New files:** `Models/CenterInfoItem.cs`, `Models/PartitionListItem.cs`,
  `Models/MetadataItem.cs`, `Models/ConvItem.cs`
- **Modified:** `MainWindow.axaml.cs` — inner classes removed.
- **Modified:** Files referencing these types — `using` statements added.
- **Behavior unchanged:** These are pure data classes with no behavioral impact.
- **This enables:** `CenterInfoViewModel` and `OptionsPanelViewModel` can
  reference these types without depending on `MainWindow`.

---

## Step 4: Create CenterInfoViewModel

### What we are going to accomplish

The center info panel shows details about the currently selected archive item:
key/value info pairs, partition layouts, metadata entries, and notes. We are
extracting all of this into `CenterInfoViewModel`.

**What moves:** The info text properties (`CenterInfoText1`, `CenterInfoText2`),
the collections (`CenterInfoList`, `PartitionList`, `MetadataItems`, `NotesList`),
visibility flags (`ShowPartitionLayout`, `ShowMetadata`, `ShowNotes`,
`ShowDiskUtilityButtons`), and panel-specific methods (`ConfigureCenterInfo()`,
`ClearCenterInfo()`, `AddInfoItem()`, `HandleMetadataDoubleClick()`,
`HandleMetadataAddEntry()`).

**What stays on MainViewModel:**
- `ConfigureCenterPanel()` — this is a *coordination* method that configures
  `FileListViewModel` columns, toolbar states, and `CenterInfoViewModel`
  simultaneously. It touches multiple child VMs, so it belongs on the parent.
- `HandlePartitionLayoutDoubleClick()` — accesses the archive tree (cross-panel).
  `CenterInfoViewModel` fires an `Interaction<PartitionListItem, Unit>` and
  `MainViewModel` handles it.

**ReactiveUI concept — `Interaction<TInput, TOutput>`:** An `Interaction` is a
mechanism for a ViewModel to *request* something from a View (or parent VM)
without knowing who handles it. Think of it as a reverse-command: instead of
the View calling the ViewModel, the ViewModel asks "someone please handle this."
The handler registers with `.RegisterHandler(...)`. In this case, when the user
double-clicks a partition layout item, `CenterInfoViewModel` raises an
interaction, and `MainViewModel` handles it by navigating the archive tree.

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/ViewModels/CenterInfoViewModel.cs`.

2. **Add the class skeleton:**
   ```csharp
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

       // Additional properties:
       // - CenterInfoText1, CenterInfoText2
       // - ShowDiskUtilityButtons, ShowPartitionLayout, ShowMetadata, ShowNotes
       // - CanAddMetadataEntry
   }
   ```

3. **Open `MainViewModel.cs`.** Find the center-info properties and collections.
   Move them to `CenterInfoViewModel`.

4. **Move panel-specific methods:** `ConfigureCenterInfo()`, `ClearCenterInfo()`,
   `AddInfoItem()`, `HandleMetadataDoubleClick()`, `HandleMetadataAddEntry()`.

5. **Do NOT move `ConfigureCenterPanel()`** — leave it on `MainViewModel`.

6. **Do NOT move `HandlePartitionLayoutDoubleClick()`** — leave it on
   `MainViewModel`. Add an `Interaction` on `CenterInfoViewModel` for this:
   ```csharp
   public Interaction<PartitionListItem, Unit> PartitionLayoutDoubleClicked { get; } = new();
   ```
   `MainViewModel` registers a handler in `WireChildViewModels()`.

7. **Add the child VM property to `MainViewModel`:**
   ```csharp
   public CenterInfoViewModel CenterInfo { get; }

   // In constructor:
   CenterInfo = new CenterInfoViewModel();
   ```

8. **Update the AXAML bindings** (full list in the blueprint's Step 8):
   ```xml
   {Binding CenterInfoList}    → {Binding CenterInfo.CenterInfoList}
   {Binding CenterInfoText1}   → {Binding CenterInfo.CenterInfoText1}
   {Binding PartitionList}     → {Binding CenterInfo.PartitionList}
   {Binding MetadataList}      → {Binding CenterInfo.MetadataItems}
   {Binding NotesList}         → {Binding CenterInfo.NotesList}
   ```
   (See Step 8 for the comprehensive list.)

9. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/CenterInfoViewModel.cs`
- **Modified:** `MainViewModel.cs` — center info properties and methods
  removed, `CenterInfo` child VM added.
- **Modified:** `MainWindow.axaml` — binding paths updated.
- **New capability:** Info panel data is independently testable. Partition
  layout double-click uses an `Interaction` (the first cross-VM interaction
  pattern in this codebase).
- **Behavior unchanged:** Info panel displays the same data as before.

---

## Step 5: Create OptionsPanelViewModel

### What we are going to accomplish

The options panel (right side) contains ~20 checkboxes for add/extract/view
settings, DDCP mode radio buttons, converter selection combo boxes, and the
panel's show/hide toggle. This is the most complex child ViewModel in terms of
property count and cross-notification patterns.

**Key patterns to understand:**

1. **Settings persistence in setters:** Each option property's setter calls
   both `RaiseAndSetIfChanged` (to notify the UI) *and*
   `_settingsService.SetBool(...)` (to persist the setting). This is an
   intentional design — the ViewModel is the *authoritative owner* of the
   setting during its lifetime.

2. **DDCP cross-notification:** `DDCPModeIndex`, `AddExtract`, and
   `ImportExport` all represent the same underlying setting. Store the
   canonical value in a single `bool` backing field. All three property
   getters derive from it, and all three setters update the same field and
   call `RaisePropertyChanged` for all three property names. This ensures
   the UI stays in sync regardless of which control the user interacts with.

3. **ExtPreserve cross-notification:** The four `ExtPreserve*` radio buttons
   follow the same pattern — one `PreserveMode` backing field, all four
   getters compare against their respective enum value, all four setters
   assign the field and raise all four property names.

4. **`ClearIfPendingAsync()` on DDCP change:** When the DDCP mode changes,
   the clipboard may hold stale data from the previous mode. The setter calls
   `_ = _clipboardService.ClearIfPendingAsync();` (fire-and-forget, per
   Pre-Iteration-Notes async-in-setter guidance).

**Dependency injection:** `OptionsPanelViewModel` requires both
`ISettingsService` and `IClipboardService` via constructor injection. These
are passed from `MainViewModel`, which received them from the DI container.

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/ViewModels/OptionsPanelViewModel.cs`.

2. **Add the class skeleton** with constructor-injected services:
   ```csharp
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
   }
   ```

3. **Move all `IsChecked_*` properties** from `MainViewModel`. Drop the
   `IsChecked_` prefix (e.g., `IsChecked_AddRecurse` → `AddRecurse`). Apply
   the setter pattern:
   ```csharp
   private bool _addCompress;
   public bool AddCompress {
       get => _addCompress;
       set {
           this.RaiseAndSetIfChanged(ref _addCompress, value);
           _settingsService.SetBool(AppSettings.ADD_COMPRESS, value);
       }
   }
   ```

4. **Implement the DDCP cross-notification pattern:**
   ```csharp
   private bool _ddcpAddExtract;
   public bool AddExtract {
       get => _ddcpAddExtract;
       set {
           _ddcpAddExtract = value;
           this.RaisePropertyChanged(nameof(AddExtract));
           this.RaisePropertyChanged(nameof(ImportExport));
           this.RaisePropertyChanged(nameof(DDCPModeIndex));
           _ = _clipboardService.ClearIfPendingAsync();
           _settingsService.SetBool(AppSettings.DDCP_ADD_EXTRACT, value);
       }
   }
   public bool ImportExport { get => !_ddcpAddExtract; set => AddExtract = !value; }
   public int DDCPModeIndex { get => _ddcpAddExtract ? 0 : 1;
                              set => AddExtract = (value == 0); }
   ```

5. **Implement the ExtPreserve cross-notification pattern** (same approach
   with a `PreserveMode` enum backing field and `RaiseAllExtPreserve()`
   helper).

6. **Move converter collections and selection** — use existing class name
   `ConvItem` (not `ConverterItem`):
   ```csharp
   public ObservableCollection<ConvItem> ImportConverters { get; } = new();
   public ObservableCollection<ConvItem> ExportConverters { get; } = new();
   ```

7. **Move panel visibility properties** (`ShowOptionsPanel`, `ShowHideRotation`).

8. **Add `RefreshFromSettings()`** — replaces the former `PublishSideOptions()`.
   Re-reads all settings values from `_settingsService` and updates properties.

9. **Add `InitConverters()`** — populates `ImportConverters` and
   `ExportConverters` from `ImportFoundry`/`ExportFoundry`. Call once at
   construction time.

10. **Add the child VM property to `MainViewModel`:**
    ```csharp
    public OptionsPanelViewModel Options { get; }

    // In constructor:
    Options = new OptionsPanelViewModel(_settingsService, _clipboardService);
    Options.InitConverters();
    Options.RefreshFromSettings();
    ```

11. **Update the AXAML bindings** (full list in Step 8). Key changes:
    - `{Binding IsChecked_AddRecurse}` → `{Binding Options.AddRecurse}`
      (drop the `IsChecked_` prefix for all option properties)
    - `{Binding ShowOptionsPanel}` → `{Binding Options.ShowOptionsPanel}`
    - `{Binding ImportConverters}` → `{Binding Options.ImportConverters}`
    - `{Binding SelectedDDCPModeIndex}` → `{Binding Options.DDCPModeIndex}`

12. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/OptionsPanelViewModel.cs`
- **Modified:** `MainViewModel.cs` — all option properties removed, `Options`
  child VM added with service injection.
- **Modified:** `MainWindow.axaml` — binding paths updated, `IsChecked_` prefix
  dropped.
- **New capability:** Options panel is self-contained and directly testable.
  Cross-notification patterns ensure related radio buttons stay in sync.
  Settings persistence is handled inline in setters.
- **Behavior unchanged:** All checkboxes, radio buttons, and converters
  function as before.

---

## Step 6: Create StatusBarViewModel

### What we are going to accomplish

The status bar is the simplest child ViewModel — it owns two text properties
(`CenterText` and `RightText`) and a method for formatting entry counts with
optional free-space information. This is a good example of a "pure property
holder" child ViewModel.

**Note on disposal:** `StatusBarViewModel` has no internal subscriptions and
no `CompositeDisposable`. It does **not** need to implement `IDisposable`.
The `MainViewModel` will still attempt to dispose it defensively (using a
pattern cast), but nothing happens.

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/ViewModels/StatusBarViewModel.cs`.

2. **Add the class:**
   ```csharp
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

       /// <summary>
       /// Formats and sets the right status text to show directory count,
       /// file count, and (for IFileSystem) free-space information.
       /// Called by MainViewModel after file list population.
       /// </summary>
       public void SetEntryCounts(IFileSystem? fs, int dirCount, int fileCount,
               Formatter formatter) {
           // Build status string and assign to RightText.
       }

       /// <summary>
       /// Clears the entry-count portion of the status bar.
       /// </summary>
       public void ClearEntryCounts() {
           RightText = string.Empty;
       }
   }
   ```

3. **Open `MainViewModel.cs`.** Find `CenterStatusText` and `RightStatusText`.
   Move them to the child VM as `CenterText` and `RightText`.

4. **Move `SetEntryCounts()` and `ClearEntryCounts()`** to the child VM. Note
   that `SetEntryCounts` receives the `Formatter` as a parameter — it comes
   from `IWorkspaceService.Formatter` at the call site in `MainViewModel`.

5. **Add the child VM property to `MainViewModel`:**
   ```csharp
   public StatusBarViewModel StatusBar { get; }

   // In constructor:
   StatusBar = new StatusBarViewModel();
   ```

6. **Update the AXAML bindings:**
   ```xml
   {Binding CenterStatusText} → {Binding StatusBar.CenterText}
   {Binding RightStatusText}  → {Binding StatusBar.RightText}
   ```

7. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/StatusBarViewModel.cs`
- **Modified:** `MainViewModel.cs` — status bar properties removed, `StatusBar`
  child VM added.
- **Modified:** `MainWindow.axaml` — binding paths updated.
- **Behavior unchanged:** Status bar shows the same text.

---

## Step 7: Update MainViewModel Composition

### What we are going to accomplish

Now that all six child ViewModels exist, we need to wire them together in
`MainViewModel`. This is where the *composition* pattern comes to life: the
parent ViewModel creates child VMs, subscribes to their property changes, and
coordinates between them.

**ReactiveUI concept — `WhenAnyValue` subscriptions with `DisposeWith`:**

When `MainViewModel` subscribes to a child VM's property changes, it creates a
subscription that lives for the lifetime of `MainViewModel`. These subscriptions
must be tracked and disposed to prevent memory leaks. The pattern is:

```csharp
childVM.WhenAnyValue(x => x.SomeProperty)
    .Subscribe(value => HandleChange(value))
    .DisposeWith(_subscriptions);
```

`_subscriptions` is a `CompositeDisposable` on `MainViewModel`. When
`MainViewModel.Dispose()` is called, all subscriptions are cleaned up.

**Important behavior of `WhenAnyValue`:** It fires *immediately* with the
current value when you subscribe. So if `ArchiveTree.SelectedItem` is `null`
at construction time, the handler will be called with `null` right away. All
handlers must guard against `null` input.

**Re-entrancy guard:** When file list selection changes, `MainViewModel` syncs
the directory tree selection. But changing the directory tree selection triggers
a directory-tree-selection-changed handler. This could cause infinite loops.
The `_syncingSelection` bool flag prevents this — set it `true` before the
sync, check it at the top of the directory tree handler, clear it after.

### To do that, follow these steps

1. **Open `MainViewModel.cs`.**

2. **Ensure child VM properties are declared:**
   ```csharp
   public ArchiveTreeViewModel ArchiveTree { get; }
   public DirectoryTreeViewModel DirectoryTree { get; }
   public FileListViewModel FileList { get; }
   public CenterInfoViewModel CenterInfo { get; }
   public OptionsPanelViewModel Options { get; }
   public StatusBarViewModel StatusBar { get; }
   ```

3. **Ensure `CompositeDisposable` and re-entrancy guard are declared:**
   ```csharp
   private readonly CompositeDisposable _subscriptions = new();
   private bool _syncingSelection;
   ```

4. **Verify constructor instantiation order:**
   ```csharp
   ArchiveTree = new ArchiveTreeViewModel();
   DirectoryTree = new DirectoryTreeViewModel();
   FileList = new FileListViewModel();
   CenterInfo = new CenterInfoViewModel();
   Options = new OptionsPanelViewModel(_settingsService, _clipboardService);
   Options.InitConverters();
   Options.RefreshFromSettings();
   StatusBar = new StatusBarViewModel();
   ```

5. **Create (or update) the `WireChildViewModels()` method** and call it from
   the constructor. Add the following subscriptions:

   **Archive tree selection → full handler:**
   ```csharp
   ArchiveTree.WhenAnyValue(x => x.SelectedItem)
       .Subscribe(item => OnArchiveTreeSelectionChanged(item))
       .DisposeWith(_subscriptions);
   ```
   The `OnArchiveTreeSelectionChanged` handler is ~90 lines. It clears
   `DirectoryTree.TreeRoot`, updates `CurrentWorkObject`, sets
   `CenterInfo.CenterInfoText2`, branches on the archive type, populates
   the directory tree, and refreshes commands.

   **Directory tree selection → populate file list:**
   ```csharp
   DirectoryTree.WhenAnyValue(x => x.SelectedItem)
       .Subscribe(item => OnDirectoryTreeSelectionChanged(item))
       .DisposeWith(_subscriptions);
   ```

   **File list entry counts → status bar:**
   ```csharp
   FileList.WhenAnyValue(x => x.LastDirCount, x => x.LastFileCount)
       .Subscribe(counts => {
           var (dirCount, fileCount) = counts;
           StatusBar.SetEntryCounts(
               CurrentWorkObject as IFileSystem,
               dirCount, fileCount,
               _workspaceService.Formatter);
       })
       .DisposeWith(_subscriptions);
   ```

   **File list selection → sync directory tree:**
   ```csharp
   FileList.WhenAnyValue(x => x.SelectedItem)
       .Subscribe(item => OnFileListSelectionChanged(item))
       .DisposeWith(_subscriptions);
   ```

   **Settings changes → refresh options panel:**
   ```csharp
   _settingsService.SettingChanged
       .Subscribe(_ => Options.RefreshFromSettings())
       .DisposeWith(_subscriptions);
   ```

6. **Important: Do NOT add `WhenAnyValue` subscriptions for boolean option
   properties.** `OptionsPanelViewModel` owns both read and write to
   `ISettingsService` directly in its property setters. Adding subscriptions
   would create a feedback cycle with `RefreshFromSettings()`.

7. **Verify `CurrentWorkObject` stays on `MainViewModel`:**
   ```csharp
   private object? _currentWorkObject;
   public object? CurrentWorkObject {
       get => _currentWorkObject;
       set => this.RaiseAndSetIfChanged(ref _currentWorkObject, value);
   }
   ```
   This is cross-panel state set by `OnArchiveTreeSelectionChanged`.

8. **Verify these properties also stay on `MainViewModel`:**
   - `LaunchPanelVisible`, `MainPanelVisible`
   - `ShowCenterFileList`, `ShowCenterInfoPanel`
   - `IsFullListEnabled`, `IsDirListEnabled`
   - `FullListBorderBrush`, `DirListBorderBrush`, `InfoBorderBrush`

9. **Implement `IDisposable` on `MainViewModel`:**
   ```csharp
   public void Dispose() {
       _subscriptions.Dispose();
       (ArchiveTree as IDisposable)?.Dispose();
       (DirectoryTree as IDisposable)?.Dispose();
       (FileList as IDisposable)?.Dispose();
       (CenterInfo as IDisposable)?.Dispose();
       (Options as IDisposable)?.Dispose();
       (StatusBar as IDisposable)?.Dispose();
   }
   ```
   Note: casting to `IDisposable` avoids compile errors if a child VM doesn't
   implement `IDisposable` (e.g., `StatusBarViewModel`).

10. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **Modified:** `MainViewModel.cs` — child VMs created in constructor,
  `WireChildViewModels()` establishes reactive subscriptions, `Dispose()`
  cleans up everything.
- **`MainViewModel` is now a coordinator**, not a monolith. It creates child
  VMs, subscribes to their property changes, and routes events between them.
- **The subscription pattern** (`WhenAnyValue → Subscribe → DisposeWith`) is
  the standard ReactiveUI way to observe property changes with proper lifecycle
  management.
- **No new files** in this step — this is wiring existing child VMs together.

---

## Step 8: Update AXAML Bindings

### What we are going to accomplish

With all six child ViewModels in place and wired, we need to update every
binding in `MainWindow.axaml` that referenced a property that has moved. The
binding paths change from `{Binding PropertyName}` to
`{Binding ChildVM.PropertyName}`.

**Why bindings "just work" with child VMs:** Avalonia's binding engine supports
dot-notation paths. When you write `{Binding ArchiveTree.TreeRoot}`, Avalonia
resolves `ArchiveTree` on the `DataContext` (which is `MainViewModel`), then
resolves `TreeRoot` on the returned `ArchiveTreeViewModel`. Property change
notifications propagate through the chain automatically.

### To do that, follow these steps

1. **Open `MainWindow.axaml`.**

2. **Update bindings systematically by panel.** Use find-and-replace where
   possible, but verify each change. The complete list:

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

   **Options panel** (note: `IsChecked_` prefix is dropped):
   - `{Binding IsChecked_AddRecurse}` → `{Binding Options.AddRecurse}`
   - (Apply the same pattern for every `IsChecked_*` property)
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

3. **Verify these bindings remain UNCHANGED** (they stay on `MainViewModel`):
   - `{Binding LaunchPanelVisible}`, `{Binding MainPanelVisible}`
   - `{Binding ShowCenterFileList}`, `{Binding ShowCenterInfoPanel}`
   - `{Binding IsFullListEnabled}`, `{Binding IsDirListEnabled}`
   - `{Binding FullListBorderBrush}`, `{Binding DirListBorderBrush}`,
     `{Binding InfoBorderBrush}`
   - `{Binding ProgramVersionString}`
   - `{Binding ShowRecentFile1}`, `{Binding RecentFileName1}`,
     `{Binding RecentFilePath1}` (and `*2`)
   - `{Binding ShowDebugMenu}`, `{Binding IsDebugLogVisible}`,
     `{Binding IsDropTargetVisible}`
   - All `Command=` bindings (commands remain on `MainViewModel`)

4. **Build** — verify zero errors.

### Now that those are done, here's what changed

- **Modified:** `MainWindow.axaml` — binding paths updated throughout.
- **No behavior change** — this is purely a binding-path update. The data
  flows through child ViewModels now, but the UI renders identically.
- **This is the last major code change** in this iteration. Step 9 is
  validation only.

---

## Step 9: Build and Validate

### What we are going to accomplish

This is the final validation step. We need to verify that the entire
application still works correctly after extracting six child ViewModels and
updating all bindings. Because commands remain on `MainViewModel` (they were
not moved in this iteration), all menu items and toolbar buttons should
continue to work.

### To do that, follow these steps

1. **Run `dotnet build`** — verify zero errors, zero warnings related to
   binding paths or missing properties.

2. **Launch the application** and test each panel systematically:

   **Archive tree:**
   - Open a multi-level disk image (e.g., a disk → partition → filesystem).
   - Navigate tree levels by clicking different items.
   - Close a sub-tree (if the option is available).
   - Use "Navigate to Parent" and verify it works.

   **Directory tree:**
   - Select different directories.
   - Verify the file list updates when you change directory selection.
   - Use "Navigate to Parent" to go up directories.

   **File list:**
   - Verify correct entries appear for each directory/archive selection.
   - Sort by clicking column headers.
   - Reset sort (if the command is available).
   - Select entries — both single-click and multi-select.
   - Double-click a directory → verify navigation.
   - Double-click an archive → verify sub-tree opens.

   **Center info:**
   - Toggle info view (Show Info command).
   - Verify info panel content matches the current archive tree selection.
   - If partition layout is visible, double-click an entry and verify
     archive tree navigates.
   - If metadata panel is visible, test add, edit, and delete metadata
     entries.

   **Options panel:**
   - Toggle the options panel show/hide button.
   - Change various options (add compress, extract raw, etc.).
   - Close and relaunch the application — verify options persisted.
   - Change DDCP mode (Add/Extract vs. Import/Export).
   - Change import/export converter selection.

   **Status bar:**
   - Navigate to different directories/archives and verify file/directory
     counts update.
   - For IFileSystem archives, verify free space is displayed.

3. **Verify all commands still work.** Commands were not moved in this
   iteration — they remain on `MainViewModel` and delegate to child VMs
   where needed. Test representative commands from each menu:
   - File: Open, Close, Recent Files
   - Edit: Copy, Paste, Select All
   - Actions: Add, Extract, Delete, Move
   - Tools: View Files, Edit Sectors
   - Debug menu (if visible)

4. **If any test fails:** The most likely cause is a binding path that wasn't
   updated. Check the Avalonia debug output for binding errors — they will
   show messages like "Could not find property X on type MainViewModel."
   This tells you which binding needs `ChildVM.` prefixed to its path.

### Now that those are done, here's what changed

- **No code changes in this step** — it is pure validation.
- **Confirmed:** The six-child-ViewModel architecture works correctly.
- **Confirmed:** All panels render, select, and update as before.
- **Confirmed:** All commands remain functional.

---

## Size Expectations After Extraction

After completing all steps, the approximate line counts should be:

| Component | Estimated Lines |
|---|---|
| `MainViewModel` | ~800–1,200 (commands + coordination) |
| `ArchiveTreeViewModel` | ~80–150 |
| `DirectoryTreeViewModel` | ~150–250 |
| `FileListViewModel` | ~300–500 |
| `CenterInfoViewModel` | ~200–300 |
| `OptionsPanelViewModel` | ~150–250 |
| `StatusBarViewModel` | ~50–80 |

`MainViewModel` drops from ~3,000+ lines to ~800–1,200 lines. The total line
count across all files may increase slightly (due to class scaffolding and
constructor injection), but each individual file is focused and manageable.

---

## Child ViewModel Lifecycle & Disposal — Summary

| Child ViewModel | Has internal subscriptions? | Implements `IDisposable`? | Implements `IActivatableViewModel`? |
|---|---|---|---|
| `ArchiveTreeViewModel` | Yes (`_itemSubscriptions`) | Yes | **No** |
| `DirectoryTreeViewModel` | Yes (`_itemSubscriptions`) | Yes | **No** |
| `FileListViewModel` | Possibly | Only if needed | **No** |
| `CenterInfoViewModel` | Possibly | Only if needed | **No** |
| `OptionsPanelViewModel` | Possibly | Only if needed | **No** |
| `StatusBarViewModel` | No | No | **No** |

**Why no `IActivatableViewModel`?** These child ViewModels do not have their
own paired View — they are panels within `MainWindow`, not standalone windows.
`IActivatableViewModel` and `WhenActivated` require a View to activate them.
Since no View activates these child VMs, `WhenActivated` blocks would never
fire, creating subscription leaks. Use `IDisposable` + `CompositeDisposable`
instead (per Pre-Iteration-Notes §4).

---

## What This Enables

With this iteration complete:

- **`MainViewModel` is a manageable coordinator** (~1,000 lines) instead of a
  monolith (~3,000+ lines). It creates child VMs, wires subscriptions, and
  handles cross-panel coordination.
- **Each panel is independently testable.** You can instantiate
  `FileListViewModel` in a unit test, call `PopulateFileList(...)`, and assert
  on `Items.Count` without launching the application.
- **Future features are localized.** If you want to add a multi-tab file list,
  you modify `FileListViewModel` without touching `MainViewModel` or any other
  child VM.
- **Docking readiness.** Each child ViewModel is self-contained with no
  references to sibling ViewModels or to `MainWindow`. A future docking
  framework (e.g., Avalonia Dock) can host any child VM in a detachable panel.
- **Phase 6** (multi-viewer, docking evaluation, polish) can proceed on cleanly
  separated ViewModels.
