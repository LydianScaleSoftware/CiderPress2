# Iteration 1B Developer Manual: Wire MainViewModel as DataContext

> **Iteration ID:** 1B
>
> **Prerequisites:**
> - Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
>   conventions, and coding rules.
> - Reference: `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 Phase 1, §7.13, §7.20.
> - Iteration 1A is complete (`MainViewModel` exists with all properties).
> - The application builds and runs correctly.

---

## Overview

This iteration is the moment where the MVVM pattern "goes live" in the
application. Up to now (Iteration 1A), you created a `MainViewModel` with
all the bindable properties, but nothing was actually using it — `MainWindow`
was still `DataContext = this`, the old way. In this iteration, you flip the
switch: `MainWindow`'s `DataContext` becomes a `MainViewModel` instance, and
every AXAML binding in the window resolves against the ViewModel instead of
the Window itself.

This is the single most consequential change in the entire MVVM migration.
When it's done, the application looks and behaves identically to the user,
but under the hood, the data flow has fundamentally changed: View → ViewModel
instead of View → itself.

---

## Step 0: Change MainWindow Base Class to ReactiveWindow

### What we are going to accomplish

Before we can set up the ViewModel as the DataContext, we need to change
`MainWindow` from a plain Avalonia `Window` to a `ReactiveWindow<MainViewModel>`.
This is a ReactiveUI concept that is central to how ReactiveUI integrates with
Avalonia.

**Why `ReactiveWindow<T>`?**

In standard Avalonia, a `Window` is just a window — it doesn't know or care
about ViewModels. ReactiveUI provides `ReactiveWindow<T>`, which is a
specialized `Window` subclass that:

1. **Provides a typed `ViewModel` property.** Instead of casting `DataContext`
   to `MainViewModel` every time you need it, you just use `this.ViewModel`,
   which is already typed as `MainViewModel`.

2. **Implements `IViewFor<MainViewModel>`.** This is ReactiveUI's standard
   interface for connecting a View to its ViewModel. Downstream iterations
   and ReactiveUI features (like `WhenActivated`) depend on this.

3. **Enables `WhenActivated`.** This is a ReactiveUI lifecycle hook that lets
   you set up subscriptions (reactive pipelines) that are automatically
   disposed when the window deactivates. This prevents memory leaks from
   lingering subscriptions. You'll use this in Step 6 for column visibility
   wiring.

4. **Provides change notification through its base class chain.** This means
   the hand-rolled `INotifyPropertyChanged` implementation currently in
   `MainWindow` (the `PropertyChanged` event and `OnPropertyChanged()` method)
   is no longer needed — `ReactiveWindow` already handles property change
   notification via `ReactiveObject` in its inheritance chain.

**What is `INotifyPropertyChanged`?** It's a .NET interface that data binding
depends on. When a property changes, the object fires a `PropertyChanged`
event, and the UI framework (Avalonia) listens for that event to update the
UI. Currently, `MainWindow` implements this interface manually. After switching
to `ReactiveWindow<MainViewModel>`, the ViewModel handles this via
`ReactiveObject.RaiseAndSetIfChanged()`, and the Window itself doesn't need
its own implementation.

### To do that, follow these steps

1. **Open `MainWindow.axaml.cs`.**

2. **Change the class declaration.** Find:
   ```csharp
   public partial class MainWindow : Window, INotifyPropertyChanged {
   ```
   Replace with:
   ```csharp
   public partial class MainWindow : ReactiveWindow<MainViewModel> {
   ```

3. **Add `using ReactiveUI;`** at the top of the file if not already present.

4. **Remove the hand-rolled `INotifyPropertyChanged` implementation.** Search
   for:
   - The `PropertyChanged` event declaration (e.g., `public event PropertyChangedEventHandler? PropertyChanged;`)
   - The `OnPropertyChanged()` method (the helper method that fires the event)

   Delete both. `ReactiveWindow` provides change notification through its
   base class chain, so these are redundant.

5. **Do NOT add a manual `ViewModel` property or cast accessor.**
   `ReactiveWindow<MainViewModel>` automatically provides a typed `ViewModel`
   property. You can use `this.ViewModel` (or just `ViewModel`) throughout
   code-behind to access the `MainViewModel` instance.

6. **Open `MainWindow.axaml`.**

7. **Add the ReactiveUI XML namespace** (if not already present):
   ```xml
   xmlns:rxui="https://reactiveui.net"
   ```

8. **Change the root element** from `<Window ...>` to:
   ```xml
   <rxui:ReactiveWindow x:TypeArguments="vm:MainViewModel" ...>
   ```
   Where `vm:` is the namespace prefix for `cp2_avalonia.ViewModels` (e.g.,
   `xmlns:vm="clr-namespace:cp2_avalonia.ViewModels"`). Also change the
   closing tag from `</Window>` to `</rxui:ReactiveWindow>`.

9. **Build** (`dotnet build`) to verify the base class change compiles. You
   may see errors about the removed `INotifyPropertyChanged` members — those
   are expected and will be resolved as you proceed.

### Now that those are done, here's what changed

- **Modified:** `MainWindow.axaml.cs` — base class changed from `Window` to
  `ReactiveWindow<MainViewModel>`, hand-rolled `INotifyPropertyChanged`
  removed.
- **Modified:** `MainWindow.axaml` — root element changed to
  `rxui:ReactiveWindow` with type argument.
- **New capability:** `MainWindow` now has a typed `ViewModel` property
  and supports `WhenActivated` for subscription lifecycle management.
- **Behavior unchanged:** The application still compiles and runs as before
  (DataContext hasn't switched yet — that's Step 1).

---

## Step 1: Instantiate MainViewModel in MainWindow

### What we are going to accomplish

This is where the actual DataContext switch happens. Currently, `MainWindow`
sets `DataContext = this` — meaning every `{Binding PropertyName}` in the
AXAML resolves against the `MainWindow` class itself. We're going to change
it to `DataContext = new MainViewModel()`, so bindings resolve against the
ViewModel instead.

**What is `DataContext`?** In Avalonia (and WPF), `DataContext` is the object
that all `{Binding}` expressions resolve against. If `DataContext` is a
`MainWindow`, then `{Binding CenterStatusText}` looks for a
`CenterStatusText` property on `MainWindow`. If `DataContext` is a
`MainViewModel`, it looks for it on `MainViewModel` instead.

Because Iteration 1A already created all the same property names on
`MainViewModel`, the AXAML bindings don't need to change at all — they'll
find the same property names, just on a different object.

**Why is this a separate step from Step 0?** Step 0 prepared the plumbing
(base class, typed `ViewModel` property). This step actually flips the
switch. Keeping them separate makes it easier to diagnose issues if
something goes wrong.

### To do that, follow these steps

1. **Open `MainWindow.axaml.cs`.**

2. **Find `DataContext = this;`** in the constructor (or `Loaded` handler).

3. **Replace it with:**
   ```csharp
   var viewModel = new MainViewModel();
   DataContext = viewModel;
   ```

4. **After this line, you can use `this.ViewModel`** (or just `ViewModel`)
   to access the `MainViewModel` instance with proper typing. The
   `ReactiveWindow<MainViewModel>` base class syncs its typed `ViewModel`
   property with `DataContext` automatically.

5. **Do NOT** add a manual cast accessor like
   `private MainViewModel VM => (MainViewModel)DataContext!;`. The
   `ReactiveWindow<T>.ViewModel` property already does this.

6. **Build** to verify — you'll likely see many errors because controller
   and code-behind code still references `MainWindow` properties that are
   now on the ViewModel. That's expected; Steps 2–6 fix those.

### Now that those are done, here's what changed

- **Modified:** `MainWindow.axaml.cs` — `DataContext` now points to a
  `MainViewModel` instance instead of `this`.
- **New capability:** All AXAML `{Binding}` expressions now resolve against
  `MainViewModel`.
- **Temporarily broken:** Controller code and code-behind event handlers
  still reference `MainWindow` properties that have moved. This is fixed
  in the following steps.

---

## Step 2: Give MainController a VM Reference + Forward Commands

### What we are going to accomplish

`MainController` is the large class (~3,900 lines across two files) that
holds all the business logic. It currently takes a `MainWindow` reference
in its constructor (`mMainWin`) and reads/writes properties directly on the
window. Since those properties now live on `MainViewModel`, the controller
needs a reference to the ViewModel.

Additionally, there's a critical problem to solve: **commands**. The
application has 51 `ICommand` properties that are bound in AXAML (e.g.,
`{Binding OpenCommand}`). Before this iteration, those bindings found the
commands on `MainWindow` because `DataContext` was `this`. Now that
`DataContext` is the ViewModel, those bindings will look for commands on
`MainViewModel` — and find nothing, because the commands are still defined
on `MainWindow`.

**What happens when a command binding resolves to null?** In Avalonia, it
silently disables the control. Every menu item, toolbar button, and keyboard
shortcut bound to a command would appear grayed out — no error, no exception,
just silently broken.

The solution is **temporary pass-through properties**: add plain `ICommand?`
properties to `MainViewModel` and copy the command references from
`MainWindow` to the ViewModel after both are created. This keeps the
existing `RelayCommand` instances on `MainWindow` (they'll move to the
ViewModel as `ReactiveCommand` in Iteration 2), but makes them discoverable
by AXAML bindings.

**What is a pass-through property?** It's a simple property that holds a
reference to an object owned elsewhere. Here, `MainViewModel.OpenCommand`
just holds a reference to the same `ICommand` object that `MainWindow`
created. The AXAML binding finds it on the ViewModel, invokes it, and the
command lambda runs — which calls `MainController` methods, just like before.

### To do that, follow these steps

1. **Open `MainController.cs`.**

2. **Add a `MainViewModel` field:**
   ```csharp
   private MainViewModel mViewModel;
   ```

3. **Choose how to pass the ViewModel to the controller.** Two options:

   **Option A — Constructor parameter** (preferred if constructor changes
   are manageable):
   ```csharp
   public MainController(MainWindow mainWin, MainViewModel viewModel) {
       mMainWin = mainWin;
       mViewModel = viewModel;
       // ... existing constructor body
   }
   ```

   **Option B — Property setter** (if constructor changes are too disruptive):
   ```csharp
   public void SetViewModel(MainViewModel vm) { mViewModel = vm; }
   ```

4. **Open `MainWindow.axaml.cs`.** Update the `MainController` creation to
   pass the ViewModel:
   ```csharp
   mMainCtrl = new MainController(this, ViewModel);
   ```
   Make sure this line comes *after* the `DataContext = viewModel;` line from
   Step 1, so that `ViewModel` is non-null.

5. **Add pass-through `ICommand?` properties to `MainViewModel`** for all 51
   commands. Open `MainViewModel.cs` and add:
   ```csharp
   // Temporary pass-through properties — removed in Iteration 2.
   public ICommand? OpenCommand { get; set; }
   public ICommand? CloseCommand { get; set; }
   // ... for all 51 commands
   ```
   These don't need `RaiseAndSetIfChanged` — they're set once during
   initialization and never change.

   **Where to find the complete list:** Scan all `public ICommand` property
   declarations in `MainWindow.axaml.cs` (approximately lines 58–122).
   Cross-check against the `RefreshAllCommandStates()` method in
   `MainController_Panels.cs`, which names 31 of them.

6. **In `MainWindow.axaml.cs`, after creating both the ViewModel and the
   controller, assign each command:**
   ```csharp
   ViewModel.OpenCommand = OpenCommand;
   ViewModel.CloseCommand = CloseCommand;
   // ... for all 51 commands
   ```

7. **Build** to verify the controller accepts the new parameter and the
   pass-through properties compile.

### Now that those are done, here's what changed

- **Modified:** `MainController.cs` — now holds `mViewModel` reference
  alongside `mMainWin`.
- **Modified:** `MainViewModel.cs` — 51 temporary `ICommand?` pass-through
  properties added.
- **Modified:** `MainWindow.axaml.cs` — controller creation updated,
  command pass-throughs wired.
- **New capability:** AXAML command bindings now resolve against the
  ViewModel and find the existing `RelayCommand` instances. All menu items,
  toolbar buttons, and keyboard shortcuts work again.
- **What stays the same:** Commands still use `RelayCommand` on `MainWindow`.
  The `RelayCommand` → `ReactiveCommand` conversion happens in Iteration 2.

---

## Step 3: Redirect Controller Property Access

### What we are going to accomplish

This is the largest step in this iteration. The controller (`MainController.cs`
and `MainController_Panels.cs`) has hundreds of references to
`mMainWin.PropertyName` — reading and writing properties that now live on
`MainViewModel`. Every one of these must change to `mViewModel.PropertyName`.

This is purely mechanical: find-and-replace with careful attention to which
properties have moved (all the ones from Iteration 1A) versus which ones
must stay on `MainWindow` (view-only concerns like control references — see
Step 5).

**Why can't we just do a global find-and-replace?** Because some
`mMainWin.` references should *not* change. The controller still needs
`mMainWin` for:
- Avalonia control references (`fileListDataGrid`, `archiveTree`, etc.)
- Window-level operations (`Cursor`, `Title`, `Activate()`)
- View-only methods (`PostNotification`, scroll/focus helpers)

Step 5 lists exactly what stays on `mMainWin`. Everything else moves to
`mViewModel`.

**Also update type qualifiers.** Some controller code uses
`MainWindow.CenterInfoItem`, `MainWindow.PartitionListItem`, etc. — inner
classes that were extracted to `cp2_avalonia/Models/` in Iteration 0. Update
these to use the unqualified names with a `using cp2_avalonia.Models;`
directive.

### To do that, follow these steps

1. **Open `MainController.cs` and `MainController_Panels.cs`.**

2. **Systematically replace** `mMainWin.PropertyName` with
   `mViewModel.PropertyName` for every property that moved to
   `MainViewModel` in Iteration 1A. The pattern is:

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

3. **The complete list of properties to redirect** (all moved in 1A):

   - **Panel visibility:** `LaunchPanelVisible`, `MainPanelVisible`,
     `ShowOptionsPanel`, `ShowHideRotation`
   - **Debug:** `ShowDebugMenu`, `IsDebugLogVisible`, `IsDropTargetVisible`
   - **Status:** `CenterStatusText`, `RightStatusText`,
     `ProgramVersionString`
   - **Trees:** `ArchiveTreeRoot`, `DirectoryTreeRoot`
   - **File list:** `FileList`
   - **Recent files:** `RecentFileName1`, `RecentFileName2`,
     `RecentFilePath1`, `RecentFilePath2`, `ShowRecentFile1`,
     `ShowRecentFile2`
   - **Converters:** `ImportConverters`, `ExportConverters`,
     `SelectedImportConverter`, `SelectedExportConverter`
   - **Options toggles:** All `IsChecked_*` properties,
     `SelectedDDCPModeIndex`, `IsExportBestChecked`, `IsExportComboChecked`
   - **Toolbar:** `FullListBorderBrush`, `DirListBorderBrush`,
     `InfoBorderBrush`
   - **Center panel:** `ShowCenterFileList`, `ShowCenterInfoPanel`,
     `IsFullListEnabled`, `IsDirListEnabled`, `IsResetSortEnabled`,
     `ShowSingleDirFileList`
   - **Columns:** `ShowCol_FileName`, `ShowCol_PathName`, `ShowCol_Format`,
     `ShowCol_RawLen`, `ShowCol_RsrcLen`, `ShowCol_TotalSize`
   - **Info:** `CenterInfoText1`, `CenterInfoText2`, `CenterInfoList`,
     `ShowDiskUtilityButtons`, `ShowPartitionLayout`, `PartitionList`,
     `ShowNotes`, `NotesList`, `MetadataList`, `ShowMetadata`,
     `CanAddMetadataEntry`

4. **Update type qualifiers.** Find references like
   `MainWindow.CenterInfoItem`, `MainWindow.PartitionListItem`, and
   `MainWindow.MetadataItem` in the controller files. Replace with the
   unqualified class names:

   ```csharp
   // Before:
   mMainWin.CenterInfoList.Add(new MainWindow.CenterInfoItem(name + ":", value));
   public void HandlePartitionLayoutDoubleClick(MainWindow.PartitionListItem item, ...)
   public async Task HandleMetadataDoubleClick(MainWindow.MetadataItem item, ...)

   // After:
   mViewModel.CenterInfoList.Add(new CenterInfoItem(name + ":", value));
   public void HandlePartitionLayoutDoubleClick(PartitionListItem item, ...)
   public async Task HandleMetadataDoubleClick(MetadataItem item, ...)
   ```

5. **Add `using cp2_avalonia.Models;`** to `MainController_Panels.cs` (and
   `MainController.cs` if needed) to resolve the unqualified type names.

6. **Do NOT change** any `mMainWin.` reference that accesses Avalonia
   controls or view-only methods. See Step 5 for the complete list of what
   stays on `mMainWin`.

7. **Build** to catch any missed references. Compiler errors will point to
   properties that no longer exist on `MainWindow` but haven't been
   redirected yet.

### Now that those are done, here's what changed

- **Modified:** `MainController.cs`, `MainController_Panels.cs` — all
  ViewModel property accesses redirected from `mMainWin` to `mViewModel`.
- **Behavior unchanged:** The controller reads and writes the same properties
  with the same values — they just live on a different object now.
- **This enables:** The upcoming removal of duplicate properties from
  `MainWindow` (Step 6) — the controller no longer depends on them.

---

## Step 4: Redirect Controller Method Calls

### What we are going to accomplish

In addition to properties, the controller calls several helper methods that
were moved to `MainViewModel` in Iteration 1A. These calls must be redirected
from `mMainWin` to `mViewModel`.

This is similar to Step 3 but for methods instead of properties. Some of
these methods operate on ViewModel-owned collections (`MetadataList`,
`NotesList`, etc.) and must live on the ViewModel because the data they
manipulate lives there.

### To do that, follow these steps

1. **In `MainController.cs` and `MainController_Panels.cs`, redirect these
   method calls:**

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

2. **Also redirect these additional methods** that operate on
   ViewModel-owned data:

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

3. **Redirect the three metadata mutation methods:**

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

4. **Why these methods live on the ViewModel:**
   - `ClearTreesAndLists()` clears `ArchiveTreeRoot`, `DirectoryTreeRoot`,
     `FileList`, `IsFullListEnabled`, and `IsDirListEnabled` — all
     ViewModel properties.
   - `SetNotesList`, `SetPartitionList`, `SetMetadataList` populate
     ViewModel-owned `ObservableCollection`s.
   - `UpdateMetadata`, `AddMetadata`, `RemoveMetadata` mutate the
     ViewModel-owned `MetadataList` collection.
   - `ConfigureCenterPanel` sets multiple ViewModel properties
     (column visibility, panel modes).
   - `PublishSideOptions` fires `PropertyChanged` for all `IsChecked_*`
     properties.

5. **Build** to verify all method calls resolve.

### Now that those are done, here's what changed

- **Modified:** `MainController.cs`, `MainController_Panels.cs` — helper
  method calls redirected from `mMainWin` to `mViewModel`.
- **Behavior unchanged:** Same methods, same logic, different target object.
- **This enables:** Step 6 can safely remove these method implementations
  from `MainWindow.axaml.cs`.

---

## Step 5: Keep View-Only References in Code-Behind

### What we are going to accomplish

This step is about what **not** to change. It establishes the boundary
between what belongs to the ViewModel (data, state, business logic) and
what belongs to the View (Avalonia controls, UI plumbing).

**The MVVM rule:** ViewModels must never reference Avalonia controls. A
ViewModel should work without any UI framework — you should be able to write
unit tests against it without instantiating Avalonia. Anything that touches
an Avalonia control (`DataGrid`, `TreeView`, `Window.Cursor`, etc.) must
stay in code-behind.

MVVM_Notes.md §7.20 lists the categories of code that stay in `MainWindow`:
window lifecycle events, drag-and-drop handlers, DataGrid sorting plumbing,
keyboard shortcuts, named control access, toast animation, window placement,
and platform-specific visibility.

This step ensures you don't accidentally redirect something that should
stay on `mMainWin`.

### To do that, follow these steps

1. **Review the following `mMainWin.` references in the controller — these
   must remain unchanged** (they access Avalonia controls or view-only
   behavior):

   **Avalonia control references:**
   - `mMainWin.fileListDataGrid` (SelectedItem, SelectedItems, ScrollIntoView)
   - `mMainWin.archiveTree` (TreeView control)
   - `mMainWin.directoryTree` (TreeView control)
   - `mMainWin.Cursor` (wait cursor)
   - `mMainWin.Title` (window title — will be bound to VM in Step 7)
   - `mMainWin.Activate()` (bring window to front)

   **View-only methods that stay on `MainWindow`:**
   - `mMainWin.PostNotification(msg, success)`
   - `mMainWin.FileList_ScrollToTop()`
   - `mMainWin.FileList_SetSelectionFocus()`
   - `mMainWin.DirectoryTree_ScrollToTop()`
   - `mMainWin.ReapplyFileListSort()`
   - `mMainWin.InvalidateCommands()`
   - `mMainWin.PopulateRecentFilesMenu(RecentFilePaths)` — constructs
     `MenuItem` objects and manipulates native macOS `NativeMenuItem`
     sub-menus; purely view code.
   - `mMainWin.LeftPanelWidth` — reads/writes
     `mainTriptychPanel.ColumnDefinitions[0].Width`; no binding mechanism
     exists, so settings save/restore must go through `mMainWin`.

   **Control-backed read-through properties (stay until Phase 3B):**
   - `mMainWin.SelectedFileListItem` — reads `fileListDataGrid.SelectedItem`
   - `mMainWin.SelectedArchiveTreeItem` — reads `archiveTree.SelectedItem`
   - `mMainWin.SelectedDirectoryTreeItem` — reads `directoryTree.SelectedItem`

   These three are control-backed accessors with no AXAML binding for
   selection. They stay on `MainWindow` until Phase 3B introduces the
   ViewModel-owned selection pattern (VM property + View-side sync from
   `SelectionChanged` handler).

2. **Do not change any of these.** The controller retains the `mMainWin`
   reference alongside `mViewModel` for exactly these view-only concerns.

3. **If you're unsure** whether a reference should stay or move, ask:
   "Does this touch an Avalonia control, or does it deal with pure data?"
   If it touches a control → stays. If it's pure data → moves to ViewModel.

### Now that those are done, here's what changed

- **No files modified** in this step — it's a checklist of what to preserve.
- **The controller now has two references:** `mMainWin` (for view-only
  operations) and `mViewModel` (for data/state). This dual-reference pattern
  is temporary — `mMainWin` references shrink in each subsequent iteration
  and are fully eliminated in Phase 3B when `MainController` is dissolved.

---

## Step 6: Remove Duplicate Properties from MainWindow

### What we are going to accomplish

This is the cleanup step: now that the controller reads/writes properties
on `mViewModel`, and AXAML bindings resolve against the ViewModel, the
duplicate property declarations on `MainWindow.axaml.cs` are dead code.
Removing them eliminates confusion about which copy is the "real" one and
prevents bugs where someone accidentally updates the wrong copy.

This step also handles a significant piece of logic migration:
`SetShowCenterInfo()` and its supporting members. This method controls
which panel (file list vs. info panel) is visible in the center area, and
it references multiple properties that are being removed from `MainWindow`.
The method logic must move to `MainViewModel`.

**What is `CenterPanelChange`?** It's an enum that represents the three ways
the center panel can change: show files, show info, or toggle between them.
The existing implementation in `MainWindow` uses a boolean version of
`SetShowCenterInfo()`, but that can't represent the "toggle" case. The
blueprint calls for replacing it with an enum-based version.

**What is `WhenAnyValue`?** This is a ReactiveUI method that creates an
observable (a stream of values) from a property. Every time the property
changes, the observable emits the new value. You can then `Subscribe` to
that observable to react to changes. In this step, we use `WhenAnyValue`
to watch the ViewModel's `ShowCol_*` properties and call
`SetColumnVisible()` whenever they change — replacing the inline calls that
were previously in the `MainWindow` property setters.

**What is `DisposeWith`?** When you subscribe to an observable, the
subscription stays alive until explicitly disposed. `DisposeWith(disposables)`
ties the subscription's lifetime to the `WhenActivated` scope — when the
window deactivates, the subscription is automatically disposed. This prevents
memory leaks.

### To do that, follow these steps

This is a large step with multiple sub-tasks. Work through them in order.

#### 6a. Remove property declarations and backing fields

1. **Open `MainWindow.axaml.cs`.**

2. **Delete all property declarations, backing fields, and
   `OnPropertyChanged` calls** for every property that moved to
   `MainViewModel` in Iteration 1A. This includes all the properties listed
   in Step 3's redirect list.

3. **If all properties have been removed,** also remove the
   `OnPropertyChanged()` helper method and any remaining
   `INotifyPropertyChanged` artifacts (if not already removed in Step 0).

4. **Keep in `MainWindow.axaml.cs`:**
   - Event handlers (drag-drop, sorting, selection changed, etc.)
   - Named control references (for code-behind access)
   - The constructor (minus command creation, which moves in Iteration 2)
   - `PostNotification()`, scroll/focus methods, and other UI-specific methods

#### 6b. Update code-behind event handlers and command lambdas

After removing properties, code-behind event handlers and command lambdas
that referenced them directly must use the ViewModel instead. Update these
specific locations:

1. **`ShowHideOptionsButton_Click`** — change:
   ```csharp
   ShowOptionsPanel = !ShowOptionsPanel
   ```
   to:
   ```csharp
   ViewModel.ShowOptionsPanel = !ViewModel.ShowOptionsPanel
   ```

2. **`Debug_ShowDebugLogCommand` lambda** — change:
   ```csharp
   IsDebugLogVisible = mMainCtrl.IsDebugLogOpen
   ```
   to:
   ```csharp
   ViewModel.IsDebugLogVisible = mMainCtrl.IsDebugLogOpen
   ```

3. **`Debug_ShowDropTargetCommand` lambda** — change:
   ```csharp
   IsDropTargetVisible = mMainCtrl.IsDropTargetOpen
   ```
   to:
   ```csharp
   ViewModel.IsDropTargetVisible = mMainCtrl.IsDropTargetOpen
   ```

4. **`ShowFullListCommand` lambda** — change:
   - `PreferSingleDirList = false` → `ViewModel.PreferSingleDirList = false`
   - `ShowSingleDirFileList` reads/writes → `ViewModel.ShowSingleDirFileList`
   - canExecute `IsFullListEnabled` → `ViewModel.IsFullListEnabled`

5. **`ShowDirListCommand` lambda** — change:
   - `PreferSingleDirList = true` → `ViewModel.PreferSingleDirList = true`
   - `ShowSingleDirFileList` reads/writes → `ViewModel.ShowSingleDirFileList`
   - canExecute `IsDirListEnabled` → `ViewModel.IsDirListEnabled`

6. **`ResetSortCommand` lambda** — change:
   - `IsResetSortEnabled = false` → `ViewModel.IsResetSortEnabled = false`
   - canExecute `IsResetSortEnabled` → `ViewModel.IsResetSortEnabled`

7. **`FileListDataGrid_Sorting` handler** — change:
   - `IsResetSortEnabled = true` → `ViewModel.IsResetSortEnabled = true`
   - `FileList` accesses → `ViewModel.FileList`

8. **`ShowCenterFileList` in `canExecute` lambdas and event handlers** —
   Step 6 removes `ShowCenterFileList` from `MainWindow`, but approximately
   eight command `canExecute` lambdas reference it directly:
   `ViewFilesCommand`, `AddFilesCommand`, `ImportFilesCommand`,
   `ExtractFilesCommand`, `ExportFilesCommand`, `DeleteFilesCommand`,
   `TestFilesCommand`, and `EditAttributesCommand` — all contain
   `&& ShowCenterFileList` in their `canExecute` predicates. The
   `FileListDataGrid_DragOver` handler also references it.

   Replace every bare `ShowCenterFileList` in these locations with
   `ViewModel.ShowCenterFileList`. Alternatively, add a private forwarding
   property: `private bool ShowCenterFileList => ViewModel.ShowCenterFileList;`
   to minimize churn — either approach is acceptable.

9. **`FileList_ScrollToTop()`, `FileList_SetSelectionFocus()`, and
   `ReapplyFileListSort()`** — these methods stay in `MainWindow.axaml.cs`
   (Step 5) but reference `FileList` directly (e.g., `FileList[0]`,
   `FileList.Count`, iterating/clearing). Since `FileList` has moved to
   `MainViewModel`, update all bare `FileList` references in these methods
   to `ViewModel.FileList`.

#### 6c. Migrate `SetShowCenterInfo()` to MainViewModel

The existing `SetShowCenterInfo()` method in `MainWindow` controls center
panel visibility. It references multiple properties being removed. The
boolean overload created in Iteration 1A is insufficient — it can't
represent the "toggle" case needed by `ToggleInfoCommand`.

1. **Create `cp2_avalonia/Models/CenterPanelChange.cs`:**
   ```csharp
   namespace cp2_avalonia.Models;
   public enum CenterPanelChange { Unknown = 0, Files, Info, Toggle }
   ```

2. **Delete the original `CenterPanelChange` enum** from
   `MainWindow.axaml.cs` (if one exists there).

3. **In `MainViewModel`, replace the 1A-created `SetShowCenterInfo(bool)`
   overload** with the full enum-based version:
   ```csharp
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
   ```

4. **Move these private members to `MainViewModel`** (needed by
   `SetShowCenterInfo()` and `ConfigureCenterPanel()`):
   - `mHasInfoOnly` / `HasInfoOnly` — private field + property
   - `ToolbarHighlightBrush` — `private static readonly IBrush`
     (`Brushes.Green`)
   - `ToolbarNohiBrush` — `private static readonly IBrush`
     (`Brushes.Transparent`)

5. **Add a temporary `PreferSingleDirList` property to `MainViewModel`:**
   ```csharp
   // Temporary — promoted to ISettingsService in Phase 3A.
   internal bool PreferSingleDirList {
       get => AppSettings.Global.GetBool(AppSettings.FILE_LIST_PREFER_SINGLE, true);
       set => AppSettings.Global.SetBool(AppSettings.FILE_LIST_PREFER_SINGLE, value);
   }
   ```
   This must be `internal` so `ShowFullListCommand`/`ShowDirListCommand`
   lambdas in `MainWindow.axaml.cs` can access it.

6. **In `MainWindow.axaml.cs`, add a thin wrapper** that delegates to the
   ViewModel:
   ```csharp
   private void SetShowCenterInfo(CenterPanelChange req) {
       ViewModel.SetShowCenterInfo(req);
   }
   ```

7. **Add `using cp2_avalonia.Models;`** to both `MainWindow.axaml.cs` and
   `MainViewModel.cs` to resolve `CenterPanelChange`.

#### 6d. Delete migrated method bodies from MainWindow

Delete these method implementations from `MainWindow.axaml.cs` — they have
been fully migrated to `MainViewModel` and all controller calls already
point to the ViewModel copies:

- `ConfigureCenterPanel()` — references removed properties
- `PublishSideOptions()` — calls `OnPropertyChanged` for removed properties
- `ClearTreesAndLists()` — sets removed properties
- `ClearCenterInfo()` — sets removed properties and clears removed
  collections
- `SetNotesList()`, `SetPartitionList()`, `SetMetadataList()` — populate
  ViewModel-owned collections
- `UpdateMetadata()`, `AddMetadata()`, `RemoveMetadata()` — mutate
  ViewModel-owned `MetadataList`
- `InitImportExportConfig()` — adds to removed converter lists. **Update
  the call site in the Loaded handler** to
  `ViewModel.InitImportExportConfig()` (or remove it if the ViewModel
  constructor already performs this initialization).

#### 6e. Wire column visibility via `WhenAnyValue` subscriptions

The six `ShowCol_*` properties previously called `SetColumnVisible()`
directly in their `MainWindow` setters. DataGrid columns are not in the
visual tree, so AXAML `IsVisible` bindings don't work on them. Instead,
use `WhenAnyValue` subscriptions to watch the ViewModel properties and
call the `SetColumnVisible()` helper.

**In `MainWindow.axaml.cs`, in the Loaded handler, add a `WhenActivated`
block:**

```csharp
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

**How this works, step by step:**

- `this.WhenActivated(disposables => { ... })` — provided by
  `ReactiveWindow<MainViewModel>` (Step 0). The code inside runs when the
  window activates. The `disposables` parameter collects all subscriptions
  so they're automatically cleaned up when the window deactivates.

- `ViewModel.WhenAnyValue(vm => vm.ShowCol_FileName)` — creates an
  observable that emits the current value of `ShowCol_FileName` immediately
  and emits again every time the property changes.

- `.Subscribe(v => SetColumnVisible("Filename", v))` — whenever the
  observable emits a value, call `SetColumnVisible` with that value.

- `.DisposeWith(disposables)` — ties this subscription's lifetime to the
  `WhenActivated` scope. When the window deactivates, the subscription is
  disposed and stops listening.

**Required using directives** (add if not already present):
```csharp
using ReactiveUI;
using System.Reactive.Disposables;
```

`SetColumnVisible()` itself remains on `MainWindow` as a private helper —
it directly manipulates DataGrid column objects, which is a view concern.

### Now that those are done, here's what changed

- **Modified:** `MainWindow.axaml.cs` — duplicate properties removed;
  event handlers and command lambdas updated to use `ViewModel.`; migrated
  methods deleted; `WhenAnyValue` subscriptions added for column visibility.
- **Created:** `cp2_avalonia/Models/CenterPanelChange.cs` — new enum.
- **Modified:** `MainViewModel.cs` — `SetShowCenterInfo()` upgraded to
  enum-based version; supporting members (`HasInfoOnly`,
  `ToolbarHighlightBrush`, `ToolbarNohiBrush`, `PreferSingleDirList`) added.
- **New capability:** Column visibility reacts to ViewModel property changes
  via `WhenAnyValue`, replacing the old inline setter calls.
- **`MainWindow.axaml.cs` is significantly smaller** — all data properties
  are gone; only UI-specific code remains.

---

## Step 7: Bind Window Title in AXAML

### What we are going to accomplish

The window title (shown in the title bar) is currently set by the controller
via `mMainWin.Title = ...`. Since `Title` is a Window property that can be
bound in AXAML, we can replace the controller's direct assignment with a
ViewModel property and an AXAML binding. This is a small, clean example of
the MVVM pattern: the ViewModel owns the data, the View binds to it
declaratively.

### To do that, follow these steps

1. **In `MainViewModel.cs`, add a `WindowTitle` property** if not already
   present (it may have been created in 1A):
   ```csharp
   private string mWindowTitle = "CiderPress II";
   public string WindowTitle {
       get => mWindowTitle;
       set => this.RaiseAndSetIfChanged(ref mWindowTitle, value);
   }
   ```

2. **In `MainWindow.axaml`, bind the title:**
   ```xml
   Title="{Binding WindowTitle}"
   ```
   This goes on the root element (the `rxui:ReactiveWindow` tag from Step 0).

3. **In `MainController.cs`, replace** any `mMainWin.Title = ...`
   assignments with `mViewModel.WindowTitle = ...`.

4. **Build and run** — verify the title bar shows the correct text on
   startup and updates when a file is opened.

### Now that those are done, here's what changed

- **Modified:** `MainViewModel.cs` — `WindowTitle` property added (if not
  already present).
- **Modified:** `MainWindow.axaml` — `Title` bound to `WindowTitle`.
- **Modified:** `MainController.cs` — title assignment redirected to
  ViewModel.
- **Behavior unchanged:** Title bar displays the same text as before.
- **This is a clean MVVM example:** ViewModel owns the data, AXAML declares
  the binding, controller writes to the ViewModel — no direct View
  manipulation.

---

## Step 8: Add CanExecute State Properties to ViewModel

### What we are going to accomplish

Commands have two parts: what they *do* (the execute action) and when they
*can* do it (the canExecute predicate). Currently, canExecute logic
references controller properties like `mMainCtrl.IsFileOpen` and
`mMainCtrl.CanWrite`. In Iteration 2, when commands become `ReactiveCommand`
instances on the ViewModel, they'll use `WhenAnyValue` to observe ViewModel
properties for canExecute.

**What is `WhenAnyValue` for canExecute?** Instead of manually calling
`RaiseCanExecuteChanged()` every time state changes (the current approach),
`ReactiveCommand` takes an `IObservable<bool>` for canExecute. You create
this observable with `WhenAnyValue`:
```csharp
var canOpen = this.WhenAnyValue(x => x.IsFileOpen, isOpen => !isOpen);
OpenCommand = ReactiveCommand.Create(() => DoOpen(), canOpen);
```
Every time `IsFileOpen` changes, ReactiveUI automatically re-evaluates
whether the command can execute and updates the UI (enabling/disabling
buttons). No manual invalidation needed.

This step prepares the ViewModel properties that Iteration 2 will consume.
The properties are added now and set by the controller at the appropriate
state-change points.

### To do that, follow these steps

#### 8a. Add boolean state properties to MainViewModel

Add the following properties using the standard `RaiseAndSetIfChanged`
pattern:

```csharp
private bool mIsFileOpen;
public bool IsFileOpen {
    get => mIsFileOpen;
    set => this.RaiseAndSetIfChanged(ref mIsFileOpen, value);
}

private bool mCanWrite;
public bool CanWrite {
    get => mCanWrite;
    set => this.RaiseAndSetIfChanged(ref mCanWrite, value);
}

private bool mAreFileEntriesSelected;
public bool AreFileEntriesSelected {
    get => mAreFileEntriesSelected;
    set => this.RaiseAndSetIfChanged(ref mAreFileEntriesSelected, value);
}

private bool mIsSingleEntrySelected;
public bool IsSingleEntrySelected {
    get => mIsSingleEntrySelected;
    set => this.RaiseAndSetIfChanged(ref mIsSingleEntrySelected, value);
}

private bool mIsMultiFileItemSelected;
public bool IsMultiFileItemSelected {
    get => mIsMultiFileItemSelected;
    set => this.RaiseAndSetIfChanged(ref mIsMultiFileItemSelected, value);
}
```

Add others as needed based on the controller's existing computed predicates.

#### 8b. Set these properties at state-change points

In `MainController.cs`:

- After opening/closing a file:
  ```csharp
  mViewModel.IsFileOpen = mWorkTree != null;
  ```

- In `MainWindow.axaml.cs`, in the `FileListDataGrid_SelectionChanged`
  handler:
  ```csharp
  ViewModel.AreFileEntriesSelected = fileListDataGrid.SelectedIndex >= 0;
  ```

The exact placement depends on where the existing code currently updates
the equivalent state. Look for the existing `canExecute` predicates in
command definitions and the `RefreshAllCommandStates()` body to find all
the state-change points.

#### 8c. Leave `RefreshAllCommandStates()` unchanged

`RefreshAllCommandStates()` in `MainController_Panels.cs` is a sequence
of `RaiseCanExecuteChanged()` calls on `MainWindow`'s `RelayCommand`
instances. This is correct for now — commands are still `RelayCommand` on
`MainWindow` (forwarded through pass-through properties).

**Do not modify or remove this method.** It will be eliminated in
Iteration 2 when `ReactiveCommand` auto-reevaluates via `WhenAnyValue`.

### Now that those are done, here's what changed

- **Modified:** `MainViewModel.cs` — canExecute state properties added.
- **Modified:** `MainController.cs` — sets VM state properties at
  appropriate points.
- **Modified:** `MainWindow.axaml.cs` — selection handler updates VM
  properties.
- **Behavior unchanged:** The existing `RefreshAllCommandStates()` mechanism
  continues to work. The new VM properties are set but not yet consumed by
  commands.
- **This enables:** Iteration 2, where `ReactiveCommand.CanExecute`
  observes these properties via `WhenAnyValue`, eliminating the need for
  manual `RaiseCanExecuteChanged()`.

---

## Step 9: Build and Validate

### What we are going to accomplish

This is the comprehensive verification step. The goal is to confirm that
after all the changes in Steps 0–8, the application behaves **identically**
to how it did before this iteration. The DataContext is different, the
property ownership is different, but the user should see no difference.

**Why "identical" matters:** The MVVM migration is a structural refactor,
not a feature change. If anything behaves differently, something was wired
incorrectly. The most common failure mode is null command bindings (controls
appear grayed out) or missed property redirects (data doesn't appear).

### To do that, follow these steps

1. **Run `dotnet build`** — verify zero errors, zero warnings (or only
   pre-existing warnings).

2. **Launch the application.**

3. **Verify all panels render correctly:**
   - Launch panel visible on startup
   - Open a disk image → main panel appears, launch panel hides
   - Archive tree populates with the disk structure
   - Directory tree populates
   - File list populates with file entries
   - Center info panel shows disk information
   - Options panel toggles work (click the show/hide button)
   - Column visibility settings work (toggle columns from the menu)

4. **Verify status bar** updates on file open/navigation — center and right
   sections should show file count and disk info.

5. **Verify recent files** menu works — recently opened files appear and
   can be re-opened.

6. **Verify toolbar highlights** — the full-list / dir-list / info buttons
   in the toolbar should toggle correctly with visual highlights.

7. **Verify all menu items and toolbar buttons execute correctly.** This is
   the critical check — not just that they appear enabled, but that they
   actually fire their actions:
   - Open a file → toolbar "Show Full List" button should be enabled *and*
     respond to click.
   - Confirm at least one keyboard shortcut (e.g., Ctrl+O / Cmd+O) works.
   - Confirm context menu items in the file list work.
   - Try several Actions menu items (Add, Extract, Delete if applicable).

   **Troubleshooting:** If any menu item or button appears grayed out that
   shouldn't be, the command pass-through (Step 2) is likely broken for that
   specific command. In Avalonia, a null command binding silently disables
   the control — no error, no exception. Check that the pass-through
   property on `MainViewModel` is being set.

8. **Close and reopen** — verify settings persist (window position, column
   visibility, options panel state).

**Expected result:** The application is functionally identical. `DataContext`
is now `MainViewModel` instead of `MainWindow`. The controller reads/writes
VM properties via `mViewModel`. All AXAML bindings resolve against the
ViewModel.

### Now that those are done, here's what changed

This step doesn't modify any files — it validates all previous steps.

**Summary of the entire iteration:**

| File | Change |
|---|---|
| `MainWindow.axaml` | Root element → `ReactiveWindow`; `Title` bound to VM |
| `MainWindow.axaml.cs` | Base class → `ReactiveWindow<MainViewModel>`; `DataContext` → VM; duplicate properties removed; event handlers redirected; `WhenAnyValue` subscriptions added |
| `MainViewModel.cs` | 51 pass-through `ICommand?` properties; `SetShowCenterInfo()` upgraded; `HasInfoOnly`, brush constants, `PreferSingleDirList` added; canExecute state properties added |
| `MainController.cs` | `mViewModel` field added; property/method accesses redirected; title assignment redirected; canExecute state properties set |
| `MainController_Panels.cs` | Property/method accesses redirected; type qualifiers updated |
| `Models/CenterPanelChange.cs` | New file — enum for center panel switching |

---

## What This Enables (Looking Ahead)

With this iteration complete, the MVVM foundation is in place:

- **Iteration 2** will move all 51 commands from `MainWindow` to
  `MainViewModel` as `ReactiveCommand` instances. The canExecute state
  properties added in Step 8 will be consumed via `WhenAnyValue` — commands
  will automatically enable/disable as state changes, eliminating the
  manual `RefreshAllCommandStates()` calls.

- **AXAML bindings** (`{Binding OpenCommand}`, `{Binding CenterStatusText}`,
  etc.) already resolve against the ViewModel, so Iteration 2's command
  migration won't require any AXAML changes.

- **The 51 pass-through `ICommand?` properties** on `MainViewModel` are
  temporary scaffolding. In Iteration 2, each one will be replaced with a
  proper `ReactiveCommand` instance — the pass-throughs are then deleted.

- **`MainWindow.axaml.cs` is significantly thinner** — it no longer owns
  any data state. It's on its way to becoming the "thin shell" that MVVM
  prescribes: pure UI plumbing (event handlers, control access, animations)
  with no business logic or data ownership.
