# Iteration 2 Developer Manual: Commands → MainViewModel

> **Iteration Identifier:** 2
>
> **Prerequisite Reading:**
> - `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` — conventions and coding rules
> - `cp2_avalonia/MVVM_Project/MVVM_Notes.md` — the architectural source of truth (§6 Phase 2, §7.13, §7.16)
> - `cp2_avalonia/MVVM_Project/Iteration_2_Blueprint.md` — the detailed technical blueprint this manual expands

---

## Overview and Context

In Iterations 1A and 1B you created `MainViewModel`, moved all bindable
properties into it, switched `DataContext` to point at the ViewModel, and gave
`MainController` a reference to the new ViewModel. The application builds and
runs, but all 51 commands still live on `MainWindow.axaml.cs` as `RelayCommand`
instances — they're passed through to the ViewModel via temporary `ICommand?`
properties.

This iteration finishes the command migration. When it's done, every command
will be a `ReactiveCommand` owned by the ViewModel, the manual command
invalidation infrastructure (`RefreshAllCommandStates` /
`InvalidateCommands`) will be gone, and `CanExecute` state will update
automatically via ReactiveUI's `WhenAnyValue`.

### Key MVVM and ReactiveUI Concepts for This Iteration

If you're new to MVVM and ReactiveUI, here are the core concepts you'll
encounter throughout this iteration:

- **`ReactiveCommand<TParam, TResult>`** — ReactiveUI's replacement for
  `RelayCommand`. Instead of manually calling `RaiseCanExecuteChanged()` to
  tell the UI "this button's enabled state may have changed," a
  `ReactiveCommand` takes a `canExecute` observable that fires automatically
  whenever the underlying properties change. The type parameters are
  `<TParam, TResult>` — for commands that take no parameter and return nothing,
  you use `ReactiveCommand<Unit, Unit>` (where `Unit` is the reactive
  equivalent of `void`).

- **`WhenAnyValue`** — A ReactiveUI method on `ReactiveObject` that creates
  an `IObservable` stream from one or more properties. Every time any of the
  listed properties changes, `WhenAnyValue` emits a new value. When you pass
  this stream as the `canExecute` parameter to `ReactiveCommand.Create(...)`,
  the command's enabled/disabled state automatically re-evaluates — no manual
  invalidation needed.

- **`ReactiveCommand.Create(...)` vs. `ReactiveCommand.CreateFromTask(...)`** —
  Use `Create` for synchronous command bodies (lambdas that return immediately).
  Use `CreateFromTask` for async command bodies (lambdas that return `Task`).
  Using the wrong one can cause unobserved exceptions and silent failures.

- **`ThrownExceptions`** — Every `ReactiveCommand` has a `ThrownExceptions`
  observable. If an exception is thrown inside the command body *and* you
  haven't subscribed to `ThrownExceptions`, the exception is silently
  swallowed. You must subscribe to it for every command to avoid silent
  failures.

- **`Observable.Return(false)`** — Creates an observable that emits `false`
  once and completes. Used to create a permanently-disabled command.

- **`Unit` and `Unit.Default`** — ReactiveUI uses `Unit` (from
  `System.Reactive`) as a stand-in for `void` in observable sequences. When
  you need to execute a `ReactiveCommand<Unit, Unit>` programmatically, you
  pass `Unit.Default` (not `null` — it's a value type and won't compile with
  `null`).

---

## Step 1: Add Required Usings to MainViewModel

### What we are going to accomplish

Before you can use any ReactiveUI command types or reactive LINQ operators,
the ViewModel file needs the right `using` directives. These three namespaces
give you access to:

- `System.Reactive` — provides the `Unit` type (the reactive equivalent of
  `void`), used as the type parameter for commands that take no input and
  produce no output.
- `System.Reactive.Linq` — provides LINQ-style operators over observables,
  including `Observable.Return(...)` which you'll use to create a
  permanently-disabled command.
- `ReactiveUI` — provides `ReactiveCommand`, `WhenAnyValue`, and the rest
  of the ReactiveUI infrastructure.

These usings may already be partially present from Iteration 1. You're adding
anything that's missing.

### To do that, follow these steps

1. Open `cp2_avalonia/MainViewModel.cs`.
2. Look at the existing `using` block at the top of the file.
3. If any of the following are missing, add them:
   ```csharp
   using System.Reactive;
   using System.Reactive.Linq;
   using ReactiveUI;
   ```
4. Do **not** remove any existing `using` statements.
5. Build (`dotnet build`) to confirm no errors are introduced.

### Now that those are done, here's what changed

- **Modified file:** `MainViewModel.cs` (added usings only)
- **New capabilities:** The ViewModel can now declare `ReactiveCommand`
  properties and use `Observable.Return(...)`.
- **No behavioral change** — the application runs identically.

---

## Step 2: Create Command Properties on MainViewModel

### What we are going to accomplish

In Iteration 1B, you added 51 temporary `ICommand?` pass-through properties
to `MainViewModel` — mutable properties with `{ get; set; }` that the
`MainWindow` constructor assigned into. These were a bridge so AXAML bindings
would resolve against the ViewModel's `DataContext`.

Now you replace those 51 temporary properties with proper
`ReactiveCommand<Unit, Unit>` declarations. These are read-only properties
(no `set;` — they're initialized once in the constructor and never
reassigned). The property *names* stay exactly the same (`OpenCommand`,
`CloseCommand`, etc.), so every AXAML binding, every `KeyBinding`, and every
menu item binding continues to work without changes.

**Why `ReactiveCommand<Unit, Unit>`?** The two type parameters mean:
- First `Unit`: the command takes no input parameter (no `CommandParameter` needed)
- Second `Unit`: the command produces no output value

This is the most common command signature — "do something, take nothing,
return nothing."

### To do that, follow these steps

1. Open `cp2_avalonia/MainViewModel.cs`.
2. Find the 51 temporary `ICommand?` properties that were added in
   Iteration 1B. They look like this:
   ```csharp
   public ICommand? OpenCommand { get; set; }
   public ICommand? CloseCommand { get; set; }
   // ... etc.
   ```
   They're identifiable by their `ICommand?` type and `{ get; set; }`
   mutability.

3. **Delete all 51** of these temporary properties.

4. **In their place**, add the following `ReactiveCommand<Unit, Unit>`
   property declarations. These are read-only (get-only) properties —
   they will be initialized in the constructor (Step 3):

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

5. **Do not** build yet — these properties have no initializer and the
   constructor doesn't set them yet. The compiler will warn about
   uninitialized non-nullable fields. That's expected and will be fixed in
   Step 3.

### Now that those are done, here's what changed

- **Modified file:** `MainViewModel.cs`
- **Removed:** 51 temporary `ICommand?` pass-through properties
- **Added:** 51 `ReactiveCommand<Unit, Unit>` get-only property declarations
- **Not yet compilable** — the properties need constructor initialization
  (Step 3)

---

## Step 2A: Cross-Reference Key Bindings

### What we are going to accomplish

`MainWindow.axaml` contains 18 `KeyBinding` entries that bind keyboard
shortcuts to command properties by name. Avalonia resolves these by
looking up the property name on the `DataContext` — which is now
`MainViewModel`. If a property name in the AXAML doesn't exactly match
a property on the ViewModel, the key binding silently breaks with **no
build error** and **no runtime error**. It just stops working.

This step is a verification checkpoint — you're confirming that every
key binding name matches one of the property declarations you just added.

### To do that, follow these steps

1. Open `cp2_avalonia/MainWindow.axaml`.
2. Search for `KeyBinding` entries.
3. For each one, confirm the `Command` binding path matches a property
   name from Step 2. Here is the expected mapping:

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

4. If any name doesn't match, fix the AXAML binding path or the ViewModel
   property name so they agree. A single typo here means a dead keyboard
   shortcut with no diagnostic.

5. Do **not** modify anything else in the AXAML file.

### Now that those are done, here's what changed

- **Verified:** All 18 key binding names match ViewModel property names.
- **Possibly modified:** `MainWindow.axaml` if any mismatches were found
  (unlikely if property names were copied from the existing code-behind).
- **No behavioral change yet** — commands aren't initialized until Step 3.

---

## Step 3: Initialize Commands in the Constructor

### What we are going to accomplish

This is the largest and most important step in the iteration. You'll
initialize all 51 `ReactiveCommand` properties inside the `MainViewModel`
constructor. Each command needs two things:

1. **An execute body** — what happens when the user clicks/presses the
   command. During this iteration, most commands delegate to
   `mController.SomeMethod()`. This is a temporary coupling that will be
   removed in Phase 3 when the controller is dissolved. It's intentional —
   we're moving commands to the ViewModel *without* rewriting all the
   business logic at once.

2. **A `canExecute` observable** — a reactive stream that tells the UI
   when the command should be enabled or disabled. Instead of manually
   calling `RaiseCanExecuteChanged()` (which you had to remember to do in
   the right places with `RelayCommand`), `ReactiveCommand` watches the
   observable and automatically updates enabled state whenever the
   underlying properties change.

**Why this approach works:** The controller already sets ViewModel
properties like `IsFileOpen`, `CanWrite`, `AreFileEntriesSelected`, etc.
(from Iteration 1B). When the controller sets `IsFileOpen = true`, the
`WhenAnyValue` observable attached to any command that depends on
`IsFileOpen` fires automatically, and the command becomes enabled. No
`RefreshAllCommandStates()` needed.

This step has several sub-sections because different commands have
different patterns:
- Sub-step 3.0: Add interim helper methods to `MainController`
- Sub-step 3.1: Async vs. Sync classification
- Sub-step 3.2: `canExecute` observable patterns
- Sub-step 3.3: Standard controller-delegation examples
- Sub-step 3.4: Special-case commands
- Sub-step 3.5: `ShowCenterFileList` and `IsMultiFileItemSelected`

### To do that, follow these steps

#### Part A: Store the Controller Reference

The ViewModel needs a way to call the controller during this interim period.

1. Open `cp2_avalonia/MainViewModel.cs`.
2. Add a private field and a setter method:

   ```csharp
   private MainController? mController;

   /// <summary>
   /// Set by MainWindow after construction. Temporary coupling removed in Phase 3.
   /// </summary>
   public void SetController(MainController controller) {
       mController = controller;
   }
   ```

3. Open `cp2_avalonia/MainWindow.axaml.cs`.
4. Find where `mMainCtrl` is constructed (the `new MainController(...)` call).
5. Immediately after that line, add:
   ```csharp
   ViewModel.SetController(mMainCtrl);
   ```
   (Where `ViewModel` is the `MainViewModel` reference — adjust the name to
   match your code. It may be accessed via `DataContext as MainViewModel` or
   a stored field, depending on how Iteration 1B was implemented.)

#### Part B: Add Interim Helper Methods to MainController

Several commands need to do things that currently require a `MainWindow`
reference (closing the window, showing a dialog, selecting all items in the
DataGrid, etc.). The ViewModel can't hold a window reference (that would
violate MVVM), so instead we add thin helper methods to the controller. These
are all temporary — they'll be removed in Phase 3.

1. Open `cp2_avalonia/MainController.cs`.
2. Add these methods:

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

3. Open `cp2_avalonia/MainWindow.axaml.cs`.
4. Add these helper methods (they're called by the controller methods above):

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

5. Build to verify these compile. They won't be called yet.

#### Part C: Add the `HasInfoOnly` Property and `SetShowCenterInfo` Helper

Four center-panel commands (`ShowFullListCommand`, `ShowDirListCommand`,
`ShowInfoCommand`, `ToggleInfoCommand`) need a guard: when only the info
panel is available (e.g., when the archive tree root has no file system),
you can't switch away from it. This guard was previously a private property
on `MainWindow`. It needs to move to the ViewModel.

1. In `MainViewModel.cs`, add:
   ```csharp
   private bool mHasInfoOnly;
   public bool HasInfoOnly {
       get => mHasInfoOnly;
       set => this.RaiseAndSetIfChanged(ref mHasInfoOnly, value);
   }
   ```

2. Add a private helper method to `MainViewModel`:
   ```csharp
   private void SetShowCenterInfo(bool showInfo) {
       if (HasInfoOnly && !showInfo) {
           return;  // guard: can't switch away from info when it's the only option
       }
       ShowCenterInfo = showInfo;
   }
   ```

3. In `MainWindow.axaml.cs`, find `ConfigureCenterPanel(...)`. Replace the
   section that sets `HasInfoOnly` and calls `SetShowCenterInfo(...)`:
   ```csharp
   // Replace this pattern:
   HasInfoOnly = isInfoOnly;
   if (HasInfoOnly) { SetShowCenterInfo(CenterPanelChange.Info); }
   else             { SetShowCenterInfo(CenterPanelChange.Files); }

   // With:
   mViewModel.HasInfoOnly = isInfoOnly;
   mViewModel.ShowCenterInfo = isInfoOnly;
   ```
   (Where `mViewModel` is however you access the ViewModel from code-behind.
   Adjust to match your code.)

4. Delete `MainWindow.SetShowCenterInfo(CenterPanelChange req)` entirely
   from `MainWindow.axaml.cs`. After the changes in this iteration remove
   all its call sites, this method will have zero callers and will fail to
   compile if left (it references the removed `HasInfoOnly` field).

5. Delete the private `HasInfoOnly` property and its backing field
   (`mHasInfoOnly`) from `MainWindow.axaml.cs` — ownership has moved to
   `MainViewModel`.

6. **Note:** The toolbar border brush updates (`InfoBorderBrush`,
   `FullListBorderBrush`, `DirListBorderBrush`) that were previously done
   inside `SetShowCenterInfo` are temporarily lost. The toolbar buttons
   won't visually highlight the active panel. This is an acceptable interim
   regression — it will be fixed in Phase 5 with AXAML data triggers or
   reactive subscriptions.

#### Part D: Understand Async vs. Sync Classification

Before writing the command initialization code, you need to know which
commands are synchronous and which are asynchronous. Getting this wrong causes
real problems:

- Wrapping an `async` lambda in `ReactiveCommand.Create(...)` (the sync
  version) causes the `Task` return value to be discarded. If the async
  operation throws, the exception becomes unobserved and may crash the
  application or silently disappear. `ThrownExceptions` won't catch it.

- Using `ReactiveCommand.CreateFromTask(...)` for a synchronous body is
  harmless but adds unnecessary overhead.

**Rule of thumb:** If the method you're calling returns `Task`, use
`CreateFromTask`. If it returns `void`, use `Create`.

Here is the classification:

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

#### Part E: Define Shared `canExecute` Observables

Many commands share the same enabled/disabled conditions. Define shared
observable variables at the top of the constructor to avoid duplication.

**What is a `canExecute` observable?** It's an `IObservable<bool>` that the
`ReactiveCommand` watches. Every time it emits a new `bool` value, the
command updates its enabled/disabled state. `WhenAnyValue` creates one of
these from ViewModel properties — whenever any of the listed properties
change via `RaiseAndSetIfChanged`, the observable fires.

Add these at the top of the `MainViewModel` constructor:

```csharp
var canWhenOpen = this.WhenAnyValue(x => x.IsFileOpen);

var canWrite = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.CanWrite,
    (open, write) => open && write);
```

You'll define additional command-specific observables inline as needed.

Here is the complete reference table of which commands need which conditions:

| canExecute Condition | Commands |
|---|---|
| Always enabled (no canExecute parameter) | `OpenCommand`, `NewDiskImageCommand`, `NewFileArchiveCommand`, `RecentFile1–6`, `EditAppSettingsCommand`, `Debug_DiskArcLibTestCommand`, `Debug_FileConvLibTestCommand`, `Debug_BulkCompressTestCommand`, `Debug_ShowSystemInfoCommand`, `Debug_ShowDebugLogCommand`, `Debug_ShowDropTargetCommand`, `ExitCommand`, `HelpCommand`, `AboutCommand` |
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
| Commands that also require `ShowCenterFileList` | `CopyCommand`, `ViewFilesCommand`, `AddFilesCommand`, `ImportFilesCommand`, `ExtractFilesCommand`, `ExportFilesCommand`, `DeleteFilesCommand`, `TestFilesCommand` |
| Commands that use `IsMultiFileItemSelected` | `PasteCommand`, `AddFilesCommand`, `ImportFilesCommand`, `DeleteFilesCommand` |

#### Part F: Initialize Standard Commands

Now write the command initialization code in the constructor. Here are the
patterns with examples. You'll need to apply the appropriate pattern to each
of the 51 commands using the async/sync classification from Part D and the
canExecute table from Part E.

**Pattern 1: Sync command, with canExecute:**
```csharp
CloseCommand = ReactiveCommand.Create(
    () => mController!.CloseWorkFile(), canWhenOpen);
```

**Pattern 2: Async command, always enabled (no canExecute):**
```csharp
OpenCommand = ReactiveCommand.CreateFromTask(
    () => mController!.OpenWorkFile());
```

**Pattern 3: Async command, with canExecute:**
```csharp
var canSingleSelect = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.IsSingleEntrySelected,
    (open, single) => open && single);

EditAttributesCommand = ReactiveCommand.CreateFromTask(
    () => mController!.EditAttributes(), canSingleSelect);
```

**Pattern 4: Async command, multi-property canExecute:**
```csharp
var canSaveAsDisk = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.IsDiskOrPartitionSelected, x => x.HasChunks,
    (open, diskPart, chunks) => open && diskPart && chunks);

SaveAsDiskImageCommand = ReactiveCommand.CreateFromTask(
    () => mController!.SaveAsDiskImage(), canSaveAsDisk);
```

**Pattern 5: Always disabled (not yet implemented):**
```csharp
ScanForBadBlocksCommand = ReactiveCommand.Create(
    () => { /* Not yet implemented */ },
    Observable.Return(false));
```

**Pattern 6: Always enabled stub (shows "not implemented" dialog):**
```csharp
OpenPhysicalDriveCommand = ReactiveCommand.CreateFromTask(
    () => mController!.NotImplemented("Open Physical Drive"));
```

Apply these patterns to all 51 commands. For each command, look up:
1. Its async/sync classification (Part D)
2. Its canExecute condition (Part E)
3. The controller method it delegates to (same method name the old
   `RelayCommand` lambda called)

#### Part G: Initialize Special-Case Commands

Several commands don't follow the simple delegation pattern. Handle each
as follows:

**`ExitCommand`** — Closes the window. Delegates to the interim
`RequestClose()` helper:
```csharp
ExitCommand = ReactiveCommand.Create(() => mController!.RequestClose());
```

**`HelpCommand`** — Opens a URL in the browser. No controller dependency;
inline the body:
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

**`AboutCommand`** — Shows a dialog requiring a parent window. Delegates to
the controller's interim helper:
```csharp
AboutCommand = ReactiveCommand.CreateFromTask(
    () => mController!.ShowAboutDialog());
```

**`SelectAllCommand`** — Selects all DataGrid items. Delegates to controller:
```csharp
SelectAllCommand = ReactiveCommand.Create(
    () => mController!.SelectAll(), canWhenOpen);
```

**`ResetSortCommand`** — Clears column sort state. Delegates to controller:
```csharp
ResetSortCommand = ReactiveCommand.Create(
    () => mController!.ResetSort(),
    this.WhenAnyValue(x => x.IsResetSortEnabled));
```

**`Debug_ShowDebugLogCommand` / `Debug_ShowDropTargetCommand`** — These write
back to VM properties after the controller call:
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

**`Debug_ConvertANICommand`** — Uses `IsANISelected`:
```csharp
Debug_ConvertANICommand = ReactiveCommand.CreateFromTask(
    () => mController!.Debug_ConvertANI(),
    this.WhenAnyValue(x => x.IsANISelected));
```

**Center-panel commands** — These four commands modify ViewModel state
directly (calling the `SetShowCenterInfo` helper from Part C):

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

#### Part H: Handle `ShowCenterFileList` and `IsMultiFileItemSelected`

Eight commands include `ShowCenterFileList` in their `canExecute`, and four
use `IsMultiFileItemSelected`. Both must be reactive ViewModel properties.

**`ShowCenterFileList`** is the inverse of `ShowCenterInfo`. If
`ShowCenterInfo` is already on the ViewModel (from Iteration 1), you can
either add `ShowCenterFileList` as a derived property or use `!ShowCenterInfo`
directly in `WhenAnyValue` expressions.

The five-property `WhenAnyValue` pattern looks like this (example for
`DeleteFilesCommand`):
```csharp
var canDeleteFiles = this.WhenAnyValue(
    x => x.IsFileOpen, x => x.CanWrite,
    x => x.IsMultiFileItemSelected, x => x.AreFileEntriesSelected,
    x => x.ShowCenterFileList,
    (open, write, multi, sel, fileList) => open && write && multi && sel && fileList);

DeleteFilesCommand = ReactiveCommand.CreateFromTask(
    () => mController!.DeleteFiles(), canDeleteFiles);
```

**Specific canExecute combinations:**

| Command | canExecute |
|---|---|
| `CopyCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `ViewFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `ExtractFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `ExportFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `TestFilesCommand` | `IsFileOpen && AreFileEntriesSelected && ShowCenterFileList` |
| `PasteCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected` |
| `AddFilesCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected && ShowCenterFileList` |
| `ImportFilesCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected && ShowCenterFileList` |
| `DeleteFilesCommand` | `IsFileOpen && CanWrite && IsMultiFileItemSelected && AreFileEntriesSelected && ShowCenterFileList` |

Ensure `IsMultiFileItemSelected` and `ShowCenterFileList` are reactive
properties on `MainViewModel` (confirm they exist from Iteration 1, or
add them now with `this.RaiseAndSetIfChanged`).

#### Build checkpoint

After initializing all 51 commands, **build** (`dotnet build`). You may get
errors about the old command properties still being referenced in
`MainWindow.axaml.cs` — that's expected. The next steps remove those
references. For now, confirm the ViewModel side compiles cleanly by
temporarily commenting out conflicting code in `MainWindow` if needed, then
restore it before proceeding.

### Now that those are done, here's what changed

- **Modified files:** `MainViewModel.cs`, `MainController.cs`,
  `MainWindow.axaml.cs`
- **New capabilities:** All 51 commands now live on the ViewModel with
  reactive `canExecute` observables. Command enabled/disabled state updates
  automatically when the underlying properties change.
- **Interim coupling:** Commands delegate to `mController` — this is
  intentional and temporary (removed in Phase 3).
- **Interim regression:** Toolbar panel-highlight brushes are temporarily
  non-functional (fixed in Phase 5).

---

## Step 4: Subscribe to ThrownExceptions

### What we are going to accomplish

`ReactiveCommand` has a safety feature that's also a trap: if a command body
throws an exception, the exception is routed to the command's
`ThrownExceptions` observable. If nothing is subscribed to
`ThrownExceptions`, the exception is **silently swallowed** — no crash, no
error dialog, no log entry. The command just stops working and you have no
idea why.

To prevent silent failures, every command must have a `ThrownExceptions`
subscriber. In this iteration we use a simple `Debug.WriteLine` because the
`IDialogService` doesn't exist yet (it comes in Phase 3). The subscriber
will be upgraded to show a user-visible error dialog at that point.

### To do that, follow these steps

1. In `MainViewModel.cs`, add a private helper method:
   ```csharp
   private void SubscribeErrors(ReactiveCommand<Unit, Unit> cmd) {
       cmd.ThrownExceptions.Subscribe(ex => {
           // TODO: Replace with IDialogService.ShowMessageAsync in Phase 3
           System.Diagnostics.Debug.WriteLine($"Command error: {ex.Message}");
       });
   }
   ```

2. At the end of the constructor (after all 51 commands are initialized),
   iterate over every command and subscribe:
   ```csharp
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

3. **Verify the array contains exactly 51 entries** — one for each command.
   A missing entry means a command's errors will be silently swallowed.

### Now that those are done, here's what changed

- **Modified file:** `MainViewModel.cs`
- **New capability:** All 51 commands now log exceptions to the debug output
  instead of silently swallowing them.
- **Future improvement:** In Phase 3, the `SubscribeErrors` body will be
  upgraded to call `IDialogService.ShowMessageAsync()` so the user sees a
  proper error dialog.

---

## Step 5: Remove Commands from MainWindow.axaml.cs

### What we are going to accomplish

Now that the ViewModel owns all 51 commands, you need to remove the old
`RelayCommand` declarations and initialization code from `MainWindow`. This
includes:

1. The `ICommand` property declarations on `MainWindow`
2. The `new RelayCommand(...)` initialization block in the `MainWindow`
   constructor
3. The Iteration 1B pass-through assignments (`ViewModel.OpenCommand =
   OpenCommand;`)

After this step, `MainWindow.axaml.cs` has zero command properties — they
all live on `MainViewModel`.

### To do that, follow these steps

1. Open `cp2_avalonia/MainWindow.axaml.cs`.

2. **Delete all 51 `ICommand` property declarations.** These are the
   properties like:
   ```csharp
   public ICommand OpenCommand { get; private set; }
   public ICommand CloseCommand { get; private set; }
   // etc.
   ```

3. **Delete the entire command initialization block** from the constructor.
   This is the large block of `new RelayCommand(...)` calls that looks like:
   ```csharp
   OpenCommand = new RelayCommand(async () => await mMainCtrl.OpenWorkFile(),
       () => true);
   CloseCommand = new RelayCommand(() => mMainCtrl.CloseWorkFile(),
       () => mMainCtrl.IsFileOpen);
   // ... etc. for all 51 commands
   ```

4. **Delete the Iteration 1B pass-through assignment block.** These are the
   lines like:
   ```csharp
   ViewModel.OpenCommand = OpenCommand;
   ViewModel.CloseCommand = CloseCommand;
   // etc.
   ```
   These temporary assignments are no longer needed because the ViewModel
   creates the `ReactiveCommand` instances itself.

5. **Do not** delete any non-command code from the constructor (window
   placement, event handler subscriptions, etc.).

6. **Do not** delete any non-command properties from `MainWindow` (panel
   visibility, status text, etc. were already moved in Iteration 1).

### Now that those are done, here's what changed

- **Modified file:** `MainWindow.axaml.cs`
- **Removed:** All `ICommand` property declarations, all `RelayCommand`
  initialization code, all pass-through assignments
- **AXAML bindings remain unchanged** — they still say
  `{Binding OpenCommand}`, which now resolves to `MainViewModel.OpenCommand`
  (a `ReactiveCommand`) via the `DataContext`

---

## Step 5A: Update PopulateRecentFilesMenu

### What we are going to accomplish

`MainWindow.PopulateRecentFilesMenu()` builds an array of `ICommand`
references for the recent-file menu items. It previously referenced
Window-owned command properties like `RecentFile1Command`. After Step 5
removed those, this method needs to read from the ViewModel instead.

The good news: `ReactiveCommand<Unit, Unit>` implements `ICommand`, so the
rest of the method (which builds `MenuItem` objects and native macOS
`NativeMenuItem` objects) works without any changes.

### To do that, follow these steps

1. Open `cp2_avalonia/MainWindow.axaml.cs`.
2. Find the `PopulateRecentFilesMenu()` method.
3. Find the line that builds the `ICommand[]` array (it references
   `RecentFile1Command`, `RecentFile2Command`, etc.).
4. Replace it to read from the ViewModel:
   ```csharp
   var vm = DataContext as MainViewModel;
   ICommand[] commands = {
       vm!.RecentFile1Command, vm.RecentFile2Command, vm.RecentFile3Command,
       vm.RecentFile4Command, vm.RecentFile5Command, vm.RecentFile6Command
   };
   ```
5. Leave the rest of the method unchanged.
6. Build to confirm.

### Now that those are done, here's what changed

- **Modified file:** `MainWindow.axaml.cs`
- **Changed:** `PopulateRecentFilesMenu` now reads commands from ViewModel
  instead of Window properties
- **Behavior unchanged** — recent file menu items work the same way

---

## Step 5B: Remove Orphaned ResetSortCommand References

### What we are going to accomplish

After removing the `ResetSortCommand` property from `MainWindow` (Step 5),
two lines in the code-behind still reference it as a Window property. Both
are manual `RaiseCanExecuteChanged()` calls — which are unnecessary with
`ReactiveCommand` because `WhenAnyValue(x => x.IsResetSortEnabled)` handles
it automatically.

### To do that, follow these steps

1. In `MainWindow.axaml.cs`, find `FileListDataGrid_Sorting` (approximately
   line 1582).
2. Find and **delete** this line:
   ```csharp
   ((RelayCommand)ResetSortCommand).RaiseCanExecuteChanged();
   ```
   The line above it (`IsResetSortEnabled = true;`) — which should now be
   writing to the ViewModel — already triggers the reactive `canExecute`
   observable. No manual invalidation needed.

3. If there is a `RaiseCanExecuteChanged()` call inside the old
   `ResetSortCommand` initialization lambda (approximately line 1174), it
   should already be gone from Step 5 (it was part of the deleted command
   initialization block). But if it was migrated to the controller's
   `ResetSort()` method, verify it was **not** carried over. Setting
   `IsResetSortEnabled = false` in `ResetSort()` is sufficient.

4. Build to confirm no remaining references to `ResetSortCommand` on
   `MainWindow`.

### Now that those are done, here's what changed

- **Modified file:** `MainWindow.axaml.cs`
- **Removed:** Manual `RaiseCanExecuteChanged()` calls that are now
  unnecessary
- **How it works now:** `IsResetSortEnabled = true` fires the reactive
  `WhenAnyValue` observable, which automatically enables the
  `ResetSortCommand`. No manual invalidation needed — ever.

---

## Step 6: Remove Command Invalidation Infrastructure

### What we are going to accomplish

With `RelayCommand`, you had to manually call `RaiseCanExecuteChanged()`
every time a property changed that might affect a command's enabled state.
The codebase had two mechanisms for this:

1. **`InvalidateCommands()`** on `MainWindow` — a reflection-based method
   that iterates *all* `ICommand` properties and calls
   `RaiseCanExecuteChanged()` on each one. A sledgehammer approach.

2. **`RefreshAllCommandStates()`** on `MainController_Panels` — a more
   targeted method that explicitly calls `RaiseCanExecuteChanged()` on ~30
   individual `RelayCommand` references. Called after tree selection changes
   and file list selection changes.

Both are now completely unnecessary. `ReactiveCommand`'s `canExecute`
observables (from `WhenAnyValue`) fire automatically whenever the underlying
ViewModel properties change. The controller already sets those properties
(from Iteration 1B), so enabled/disabled state updates are automatic.

### To do that, follow these steps

1. **Delete `InvalidateCommands()`** from `MainWindow.axaml.cs`. Search for
   the method by name and remove the entire method body and declaration.

2. **Delete `RefreshAllCommandStates()`** from `MainController_Panels.cs`.
   Remove the entire method.

3. **Remove the two calls** to `RefreshAllCommandStates()` in
   `MainController_Panels.cs`:
   - Inside `ArchiveTree_SelectionChanged` (approximately line 429)
   - Inside `DirectoryTree_SelectionChanged` (approximately line 496)
   
   Delete just the call lines (`RefreshAllCommandStates();`), not the
   surrounding method code.

4. **Remove the call** to `mMainCtrl.RefreshAllCommandStates()` in
   `MainWindow.axaml.cs` inside `FileListDataGrid_SelectionChanged`
   (approximately line 1537). Delete just this one line.

5. **Remove the two `mMainWin.InvalidateCommands()` calls** in
   `MainController.cs` — one in the file-open path and one in the file-close
   path. Delete just these call lines.

6. Build to verify zero errors.

### Now that those are done, here's what changed

- **Modified files:** `MainWindow.axaml.cs`, `MainController.cs`,
  `MainController_Panels.cs`
- **Removed:** `InvalidateCommands()` method, `RefreshAllCommandStates()`
  method, and all 5 call sites
- **How it works now:** When the controller sets `IsFileOpen = true` on the
  ViewModel, every `ReactiveCommand` whose `canExecute` observable includes
  `IsFileOpen` automatically re-evaluates. No manual "refresh" step needed.
  This is one of the core benefits of reactive programming with ReactiveUI.

---

## Step 7: Update App.axaml.cs Native Menu Handlers

### What we are going to accomplish

On macOS, the application has native menu items (About, Preferences/Settings,
Quit) that are handled in `App.axaml.cs`. These handlers currently execute
commands directly on `MainWindow`:

```csharp
GetMainWindow()?.AboutCommand?.Execute(null);
```

Two things need to change:
1. Commands now live on `MainViewModel`, not `MainWindow`.
2. `ReactiveCommand<Unit, Unit>.Execute()` takes a `Unit` parameter (not
   `object?`), so passing `null` won't compile. You must pass
   `Unit.Default`.

Additionally, `ReactiveCommand.Execute()` returns an `IObservable<Unit>` —
the command doesn't actually execute until something subscribes to that
observable. You need `.Subscribe()` at the end for fire-and-forget execution.

### To do that, follow these steps

1. Open `cp2_avalonia/App.axaml.cs`.

2. Add a helper method to get the ViewModel:
   ```csharp
   private MainViewModel? GetMainViewModel() =>
       (GetMainWindow()?.DataContext) as MainViewModel;
   ```

3. Update the native menu handlers:
   ```csharp
   private void OnNativeAboutClick(object? sender, EventArgs e) =>
       GetMainViewModel()?.AboutCommand.Execute(Unit.Default).Subscribe();

   private void OnNativeSettingsClick(object? sender, EventArgs e) =>
       GetMainViewModel()?.EditAppSettingsCommand.Execute(Unit.Default).Subscribe();

   private void OnNativeQuitClick(object? sender, EventArgs e) =>
       GetMainViewModel()?.ExitCommand.Execute(Unit.Default).Subscribe();
   ```

4. Add `using System.Reactive;` at the top of `App.axaml.cs` if not already
   present (for the `Unit` type).

5. **Note the subtle difference:** The old code used `?.` on the command
   (`AboutCommand?.Execute(null)`) because the command could be null. The
   new code uses `?.` on `GetMainViewModel()` — if the ViewModel is null,
   the entire expression short-circuits. But if it's non-null, the command
   is guaranteed to exist (it's initialized in the constructor), so no `?.`
   is needed on the command itself.

6. Build and verify.

### Now that those are done, here's what changed

- **Modified file:** `App.axaml.cs`
- **Changed:** Native macOS menu handlers now execute commands via the
  ViewModel instead of the Window
- **New helper:** `GetMainViewModel()` method
- **Behavioral parity:** About, Settings, and Quit work identically on macOS

---

## Step 8: Build and Validate

### What we are going to accomplish

This is the final verification step. You need to confirm that every command
works exactly as it did before, just through the ViewModel instead of the
Window. The application should have zero behavioral regressions (except the
known interim toolbar highlight loss noted in Step 3 Part C).

### To do that, follow these steps

1. **Build the solution:**
   ```
   dotnet build
   ```
   Verify zero errors. Warnings about unused `RelayCommand` usings or
   similar are expected and can be cleaned up.

2. **Launch the application** and work through the following verification
   checklist.

3. **File menu:**
   - File → New Disk Image — dialog opens
   - File → New File Archive — dialog opens
   - File → Open — open a disk image or file archive
   - File → Close — file closes
   - File → Recent Files — if any are listed, they open
   - File → Exit — application closes

4. **Edit menu:**
   - Edit → Copy (with files selected in the file list)
   - Edit → Paste
   - Edit → Find
   - Edit → Select All
   - Edit → Application Settings — settings dialog opens

5. **Actions menu** (with a file open and entries selected):
   - View Files, Add Files, Import Files
   - Extract Files, Export Files
   - Delete Files, Test Files
   - Edit Attributes, Create Directory
   - Edit Sectors / Edit Blocks (with an appropriate disk image)
   - Save As Disk Image, Replace Partition (with an appropriate image)

6. **View menu:**
   - Full List / Directory List / Info toggles — panel switches correctly
   - Navigate to Parent Dir, Navigate to Parent

7. **Help menu:**
   - Help — browser opens to documentation
   - About — about dialog appears

8. **Toolbar:**
   - Reset Sort (after sorting a column)
   - Toggle Info

9. **Verify canExecute (enabled/disabled) state:**
   - With no file open: only Open, New, Settings, Help, About, Exit,
     and Debug commands should be enabled. Everything else should be
     grayed out.
   - Open a file in **read-only** mode: write commands (Add, Delete,
     Paste, Create Directory, etc.) should be disabled.
   - **Select entries** in the file list: selection-dependent commands
     (View, Copy, Extract, Export, Test, Edit Attributes, Delete) should
     enable.
   - **Deselect** all entries: those commands should disable again.

10. **Verify keyboard shortcuts** (spot-check a few):
    - Ctrl+C — Copy
    - Ctrl+V — Paste
    - Enter — View Files
    - Delete — Delete Files
    - Alt+Up — Navigate to Parent

11. **Verify macOS native menu** (if testing on macOS):
    - Apple menu → About CiderPress II
    - CiderPress II → Settings
    - CiderPress II → Quit

12. **Expected known regression:** Toolbar buttons (Full List, Dir List,
    Info) won't visually highlight the active panel. This is the interim
    brush regression noted in Step 3 Part C — it will be fixed in Phase 5.

13. **Expected result:** All commands work identically through the ViewModel.
    No `RelayCommand` instances remain on `MainWindow`. The manual command
    invalidation infrastructure is gone.

### Now that those are done, here's what changed

- **All 51 commands** now live on `MainViewModel` as `ReactiveCommand`
  instances with reactive `canExecute` observables.
- **`RefreshAllCommandStates()`** and **`InvalidateCommands()`** are deleted.
  Command enabled/disabled state updates automatically via `WhenAnyValue`.
- **`RelayCommand`** is no longer used by `MainWindow` (it may still be used
  by dialog code-behind until Phase 4).
- **Macros native menu handlers** route through the ViewModel.
- **AXAML bindings are unchanged** — `{Binding OpenCommand}` resolves to
  `MainViewModel.OpenCommand` via `DataContext`.

---

## Summary: What This Iteration Enables

With Iteration 2 complete, the MVVM command migration is done:

| Before (Iteration 1B) | After (Iteration 2) |
|---|---|
| 51 `RelayCommand` instances on `MainWindow` | 51 `ReactiveCommand` instances on `MainViewModel` |
| Manual `RaiseCanExecuteChanged()` calls | Automatic via `WhenAnyValue` observables |
| `RefreshAllCommandStates()` sledgehammer | Deleted — no longer needed |
| `InvalidateCommands()` reflection-based sweep | Deleted — no longer needed |
| Temporary `ICommand?` pass-throughs on VM | Replaced with proper `ReactiveCommand<Unit, Unit>` |

**What comes next:**
- **Phase 3A** will create the service interfaces (`IDialogService`,
  `IFilePickerService`, `ISettingsService`, etc.) and configure the DI
  container. The `mController!.DoSomething()` calls inside the command
  bodies will remain unchanged during Phase 3A.
- **Phase 3B** will dissolve `MainController`, inlining command bodies into
  the ViewModel and replacing controller method calls with service calls.
  The temporary `mController` field and `SetController()` method will be
  deleted at that point.

### Files Modified in This Iteration

| File | Changes |
|---|---|
| `MainViewModel.cs` | Replaced 51 `ICommand?` pass-throughs with `ReactiveCommand<Unit, Unit>` declarations; added constructor initialization with `canExecute` observables; added `mController` field and `SetController()`; added `HasInfoOnly` property and `SetShowCenterInfo()` helper; added `SubscribeErrors()` helper |
| `MainController.cs` | Added interim helpers: `RequestClose()`, `ShowAboutDialog()`, `SelectAll()`, `ResetSort()`, `NotImplemented()`; removed `InvalidateCommands()` calls |
| `MainController_Panels.cs` | Deleted `RefreshAllCommandStates()`; removed its call sites in selection-changed handlers |
| `MainWindow.axaml.cs` | Deleted all 51 `ICommand` properties and `RelayCommand` initialization; deleted pass-through assignments; deleted `InvalidateCommands()`; updated `PopulateRecentFilesMenu()` to read from ViewModel; removed orphaned `RaiseCanExecuteChanged()` calls; added `SelectAllFileListItems()`, `ClearColumnSortTags()`, `ClearSortColumn()` helpers; moved `HasInfoOnly` to ViewModel; deleted `SetShowCenterInfo()` |
| `App.axaml.cs` | Added `GetMainViewModel()` helper; updated native menu handlers to use ViewModel commands with `Unit.Default` and `.Subscribe()` |
| `MainWindow.axaml` | Verified key bindings (no changes expected unless name mismatches were found) |
