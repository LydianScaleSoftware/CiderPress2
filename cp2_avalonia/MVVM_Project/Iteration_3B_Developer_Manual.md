# Iteration 3B Developer Manual: Dissolve MainController into ViewModel + Services

> **Iteration identifier:** 3B
>
> **Source blueprint:** `cp2_avalonia/MVVM_Project/Iteration_3B_Blueprint.md`
>
> **Authoritative design document:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md`
>
> **Prerequisite reading:** `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md`

---

## Overview

This is the largest and most complex phase of the MVVM refactor. You will
take the ~2,700-line `MainController.cs` and ~1,186-line
`MainController_Panels.cs` and distribute their contents across
`MainViewModel`, `WorkspaceService`, and other services that were created
as empty shells in Iteration 3A. When you are done, both controller files
will be deleted, and `MainWindow` will be a thin view with no controller
reference.

**Work method-by-method, building and testing frequently.** Do not attempt
to move all methods at once.

---

## Goal

### What we are going to accomplish

The goal of Iteration 3B is to eliminate the `MainController` class entirely.
Currently, `MainController` sits between `MainWindow` (the View) and the
application's domain logic. It holds a direct reference to `MainWindow`
(`mMainWin`) and freely reads/writes UI properties and manipulates controls
on the window. This is the opposite of MVVM: in a proper MVVM architecture,
business logic lives in ViewModels and services, and Views are passive shells
that bind to ViewModel properties.

After this iteration:

- All business logic (add, extract, delete, move, test, edit attributes,
  sector editing, etc.) lives in `MainViewModel` methods.
- WorkTree lifecycle (open, close, recent files) lives in `WorkspaceService`.
- Dialog creation uses `IDialogService`, file pickers use `IFilePickerService`,
  clipboard uses `IClipboardService`, and settings use `ISettingsService`.
- No code references `mMainWin` directly — the ViewModel communicates with
  the View only through data binding and the `IViewActions` interface (for
  operations like scroll-into-view and focus that cannot be achieved through
  binding).
- `MainController.cs` and `MainController_Panels.cs` are deleted.

**Why this step appears in this iteration:** Phases 0–2 created the
ViewModel and moved properties and commands to it. Phase 3A created the
service interfaces and DI container. But commands still delegate to the
controller via `mController.DoSomething()`. Phase 3B is where that delegation
chain is replaced with actual logic in the ViewModel and services.

### ReactiveUI / MVVM concepts involved

- **ReactiveCommand auto-reevaluation:** Once properties like `IsFileOpen`
  and `CanWrite` are set directly by ViewModel methods (rather than by a
  controller through an intermediary), `ReactiveCommand` instances that
  observe these properties via `WhenAnyValue` automatically update their
  `CanExecute` state. No manual `RefreshAllCommandStates()` call is needed.

- **Service injection:** The ViewModel receives service interfaces through
  its constructor (constructor injection). It calls `_filePickerService`,
  `_dialogService`, `_clipboardService`, `_settingsService`, and
  `_workspaceService` instead of directly using platform APIs.

- **IViewActions pattern:** Some operations are inherently view-level — you
  cannot scroll a DataGrid to a specific row or set keyboard focus through
  data binding alone. The `IViewActions` interface provides a narrow,
  well-defined contract for these operations. `MainWindow` implements it,
  and the ViewModel calls it through the interface (never through a direct
  `MainWindow` reference). This preserves testability: in a unit test, you
  can provide a mock `IViewActions`.

### To do that, follow these steps

Follow the **Incremental Migration Strategy** at the end of this manual.
Each section below corresponds to a blueprint step. Work through them in
the order specified by the migration strategy, building and testing after
each group.

### Now that those are done, here's what changed

- `MainController.cs` and `MainController_Panels.cs` are deleted.
- `MainViewModel` is self-contained with injected services.
- `WorkspaceService` manages WorkTree lifecycle, recent files, and the
  `Formatter`/`AppHook` instances.
- Phase 4 can create dialog ViewModels with `IDialogService` integration.
- Phase 5 can extract child ViewModels from `MainViewModel` methods.

---

## Prerequisites

- Iteration 3A is complete: all service interfaces exist, concrete
  implementations exist (even if some methods throw
  `NotImplementedException`), the DI container is configured in
  `App.axaml.cs`, and services are injected into `MainViewModel`.
- All commands on `MainViewModel` currently delegate to `mController`.
- The application builds and runs correctly.

---

## Strategy: Migration Destination Rules

### What we are going to accomplish

Before diving into individual steps, you need to understand *where* each
category of controller code goes. The controller is a grab-bag of different
responsibilities. MVVM separates these responsibilities into distinct layers:

| Responsibility | Where it goes | Why |
|---|---|---|
| Business logic (add, extract, delete, test, edit, move) | `MainViewModel` private/internal methods | These are command implementations — they belong on the ViewModel that owns the commands. |
| WorkTree lifecycle (open, close, populate trees) | `IWorkspaceService` → `WorkspaceService` | WorkTree management is a shared service concern — multiple ViewModels (or future windows) may need it. |
| Dialog creation (`new DialogName(mMainWin, ...)`) | Replace with `_dialogService.ShowDialogAsync<TVM>(vm)` | ViewModels must not reference View types. The dialog service handles View creation. |
| File picker calls (`StorageProvider.*`) | Replace with `_filePickerService.OpenFileAsync(...)` etc. | Platform API access is a service concern, not a ViewModel concern. |
| Clipboard operations | Replace with `_clipboardService.*` | Same reasoning — platform abstraction. |
| Settings load/save/apply | Replace with `_settingsService.*` | Centralizes settings access and enables change notification. |
| State query properties (`CanWrite`, `IsFileOpen`, ...) | Already on `MainViewModel` (from Iteration 1A) | These were moved in Phase 1. |
| UI wiring (populate trees, lists, info panel) | `MainViewModel` methods (Phase 5 extracts to child VMs) | These populate ViewModel-owned collections that the View binds to. |
| Navigation (tree selection handlers) | `MainViewModel` methods | Selection state is ViewModel state. |
| Window lifecycle (`WindowLoaded`, `WindowClosing`) | Split: init → `MainViewModel.Initialize()`, cleanup → `MainViewModel.Shutdown()` | The ViewModel owns initialization and teardown logic; the View's event handlers just delegate. |

### To do that, follow these steps

When you encounter a controller method, consult this table to determine its
destination. If a method mixes responsibilities (e.g., business logic *and*
dialog creation), split it: the business logic goes to the ViewModel, the
dialog call becomes a `_dialogService` call.

### Now that those are done, here's what changed

You now have a mental model for where every line of controller code should
land. The remaining sections walk through the specifics.

---

## Step 0a: Extract `AutoOpenDepth` Enum

### What we are going to accomplish

`AutoOpenDepth` is currently a nested enum inside `MainController`
(`MainController.AutoOpenDepth`). It controls how deeply the application
automatically opens sub-archives when a file is first opened. This enum is
referenced by `IWorkspaceService.OpenAsync()` and `WorkspaceService.OpenAsync()`.

**The problem:** If `AutoOpenDepth` stays inside `MainController`, the service
classes would need to reference `MainController` to use the enum — which
defeats the purpose of decoupling. The enum must be extracted to a standalone
file before `WorkspaceService` can be implemented.

**MVVM concept:** In MVVM, *model types* (data classes, enums, value objects)
should live independently of any ViewModel or View so that services and
ViewModels can reference them freely. The `Models/` folder is the conventional
home for these types.

### To do that, follow these steps

1. **Create the file** `cp2_avalonia/Models/AutoOpenDepth.cs`:

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

2. **Add `using cp2_avalonia.Models;`** to every file that currently
   references `MainController.AutoOpenDepth`:
   - `IWorkspaceService.cs`
   - `WorkspaceService.cs`
   - `MainViewModel.cs`
   - Any other file that uses `AutoOpenDepth`

3. **In `MainController.cs`**, replace the nested enum definition with a
   comment:
   ```csharp
   // AutoOpenDepth enum extracted to cp2_avalonia/Models/AutoOpenDepth.cs
   ```
   Add `using cp2_avalonia.Models;` at the top of `MainController.cs` so
   that existing controller code still compiles. (The controller will be
   deleted later in Step 7, but it must keep building until then.)

4. **Build and verify zero errors.** Run `dotnet build` from the solution
   root.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/Models/AutoOpenDepth.cs`
- **Modified:** `MainController.cs` (nested enum removed, using added),
  plus any files that referenced `MainController.AutoOpenDepth` (now
  use the standalone enum).
- **Behavior:** Unchanged — this is a pure code-organization change.
- **Enables:** `WorkspaceService` can now reference `AutoOpenDepth`
  without depending on `MainController`.

---

## Step 0b: WorkProgress Dialog Strategy for Phase 3B

### What we are going to accomplish

Many controller methods (adding files, extracting, deleting, etc.) show a
`WorkProgress` dialog — a modal progress window that runs a background
worker and shows a progress bar. In the MVVM target architecture, this
dialog would be shown via `_dialogService.ShowDialogAsync<WorkProgressViewModel>(vm)`.
However, `WorkProgressViewModel` **does not exist yet** — it is a Phase 4B
deliverable.

This step establishes the **Phase 3B rule for progress dialogs:** continue
using the existing `WorkProgress` class directly. Do not attempt to use
`_dialogService` for progress dialogs in this iteration.

**Why not create WorkProgressViewModel now?** Phase 4B is specifically
designed to convert dialog windows to ViewModels. Creating one prematurely
would mix concerns and make the already-complex Phase 3B even larger.

### To do that, follow these steps

1. **Understand the rule:** When migrating controller methods that show a
   `WorkProgress` dialog, keep the existing `new WorkProgress(parentWindow,
   prog, isIndeterminate)` call pattern.

2. **Obtain the parent window:** The ViewModel cannot reference
   `MainWindow` directly. Instead, use `IDialogHost.GetParentWindow()` —
   this interface is already available from Phase 3A. The ViewModel holds
   a reference to `IDialogHost` (see `mDialogHost` field).

3. **Do NOT use** `_dialogService.ShowDialogAsync<WorkProgressViewModel>(...)`
   anywhere in Phase 3B. That type does not exist yet.

4. **Leave a breadcrumb:** When you write a `new WorkProgress(...)` call in
   a ViewModel method, add a comment:
   ```csharp
   // TODO(Phase 4B): Replace with _dialogService.ShowDialogAsync<WorkProgressViewModel>(vm)
   ```

5. No code changes are required for this step — it is a strategy decision
   that affects how you write code in Steps 1–6.

### Now that those are done, here's what changed

- **No files modified.** This is a strategy step.
- **New capability:** You have a clear rule for handling progress dialogs
  during migration.
- **Future:** Phase 4B will revisit every `new WorkProgress(...)` call site
  in `MainViewModel` and replace them with `_dialogService` calls.

---

## Step 0c: Define `IViewActions` Interface

### What we are going to accomplish

Some view-level operations cannot be accomplished through data binding:
scrolling a `DataGrid` to a specific row, setting keyboard focus on a
control, changing the mouse cursor, or populating a native platform menu.
These operations require imperative calls on Avalonia controls.

In MVVM, ViewModels must never reference View types directly. The solution
is an **interface contract** — `IViewActions` — that defines the narrow set
of imperative operations the ViewModel may request. `MainWindow` implements
this interface, and the ViewModel calls it through the abstraction.

**ReactiveUI concept — Interactions vs. IViewActions:** ReactiveUI provides
`Interaction<TInput, TOutput>` for ViewModel-to-View communication (e.g.,
requesting a dialog). `IViewActions` serves a different purpose: it is for
**synchronous, fire-and-forget view operations** (scroll, focus, cursor)
that don't return a result. Using `Interaction` for these would add
unnecessary complexity.

**Why this must be done before Steps 3–6:** Many migrated controller methods
call operations like `mMainWin.fileListDataGrid.ScrollIntoView(...)` or
`mMainWin.Cursor = new Cursor(...)`. Those calls must be replaced with
`mViewActions.ScrollFileListTo(...)` or `mViewActions.SetCursorBusy(true)`.
The interface must exist before the methods are migrated.

### To do that, follow these steps

1. **Create `cp2_avalonia/IViewActions.cs`:**

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
       // --- Scroll/Focus ---
       void ScrollFileListTo(object item);
       void ScrollFileListToTop();
       void ScrollDirectoryTreeToTop();
       void FocusFileList();
       void SetFileListSelectionFocus(int index);

       // --- Multi-select (DataGrid multi-select is not bindable in Avalonia) ---
       void SelectAllFileListItems();
       void SetFileListSelection(IList<FileListItem> items);

       // --- Toast/Notification ---
       void ShowToast(string message, bool success);

       // --- Cursor ---
       void SetCursorBusy(bool busy);

       // --- Recent files menu (native platform menu construction) ---
       void PopulateRecentFilesMenu();
   }
   ```

   **Key design decisions:**
   - `ScrollFileListTo(object item)` takes `object` rather than
     `FileListItem` to keep the interface generic.
   - `SetFileListSelection(IList<FileListItem> items)` is needed by
     `MoveFiles()` — after moving entries, the method clears the DataGrid
     selection and re-selects the moved items. DataGrid multi-select is
     not bindable in Avalonia, so this must be imperative.
   - `ShowToast(string, bool)` replaces the old `PostNotification()` calls.
     The `DispatcherTimer` animation that hides the toast after a delay
     stays in `MainWindow` code-behind.
   - `PopulateRecentFilesMenu()` is needed because native platform menus
     cannot be driven by data binding.

2. **Make `MainWindow` implement `IViewActions`:**

   Open `MainWindow.axaml.cs`. Change the class declaration:
   ```csharp
   public partial class MainWindow : Window, IDialogHost, IViewActions {
   ```

   Implement each method. For example:
   ```csharp
   public void ScrollFileListTo(object item) {
       fileListDataGrid.ScrollIntoView(item, null);
   }

   public void FocusFileList() {
       fileListDataGrid.Focus();
   }

   public void SetCursorBusy(bool busy) {
       Cursor = busy ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
   }
   ```

   The implementations are straightforward wrappers around existing control
   access patterns that are already in the code-behind or controller.

3. **Add `IViewActions` to the `MainViewModel` constructor:**

   Open `MainViewModel.cs`. Add a field and constructor parameter:
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
       // ... existing initialization ...
   }
   ```

   **Note:** `IViewActions` is NOT a DI service. It is a view-side object
   passed directly by `MainWindow` at construction time. Each `MainWindow`
   instance provides its own `IViewActions` implementation.

4. **Update `MainWindow` to pass `this` for `IViewActions`:**

   In `MainWindow.axaml.cs`, where the ViewModel is constructed:
   ```csharp
   var vm = new MainViewModel(
       this,    // IDialogHost
       this,    // IViewActions
       App.Services.GetRequiredService<IWorkspaceService>(),
       App.Services.GetRequiredService<ISettingsService>(),
       App.Services.GetRequiredService<IClipboardService>(),
       App.Services.GetRequiredService<IFilePickerService>());
   DataContext = vm;
   ```

5. **Build and verify zero errors.**

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/IViewActions.cs`
- **Modified:** `MainWindow.axaml.cs` (implements `IViewActions`, passes
  `this` to ViewModel), `MainViewModel.cs` (accepts `IViewActions` in
  constructor).
- **Behavior:** Unchanged — the interface exists but is not yet called.
- **Enables:** Migrated controller methods can call `mViewActions.*`
  instead of `mMainWin.someControl.*`.

---

## Step 1: Implement WorkspaceService

### What we are going to accomplish

`WorkspaceService` encapsulates the WorkTree lifecycle — opening and closing
disk images and file archives, managing the recent-files list, and holding
the `Formatter` and `AppHook` instances. This logic currently lives in
`MainController` methods like `DoOpenWorkFile`, `CloseWorkFile`,
`UpdateRecentFilesList`, and `UnpackRecentFileList`.

**MVVM concept — Service vs. ViewModel:** The WorkTree lifecycle is a
*shared concern*. In the future multi-window architecture (MVVM_Notes §7.12),
multiple `MainViewModel` instances would share a single `WorkspaceService`.
This is why it's a DI singleton, not a ViewModel method.

**What stays in the ViewModel:** Tree/list population, title updates, recent
file link properties, and clipboard cleanup all remain on `MainViewModel`.
Only the core WorkTree open/close mechanics move to the service.

### To do that, follow these steps

1. **Open `cp2_avalonia/Services/WorkspaceService.cs`** (created as a stub
   in Phase 3A).

2. **Flesh out the class** with the following structure:

   ```csharp
   public class WorkspaceService : IWorkspaceService {
       private readonly ISettingsService _settings;

       public WorkTree? WorkTree { get; private set; }
       public bool IsFileOpen => WorkTree != null;
       public string WorkPathName { get; private set; } = string.Empty;
       public Formatter Formatter { get; private set; }
       public AppHook AppHook { get; }
       public DebugMessageLog DebugLog { get; }
       public ObservableCollection<string> RecentFilePaths { get; } = new();

       public WorkspaceService(ISettingsService settings) {
           _settings = settings;
           DebugLog = new DebugMessageLog();
           AppHook = new AppHook(DebugLog);
           Formatter = new Formatter(new Formatter.FormatConfig());
       }
   ```

3. **Move `DoOpenWorkFile` core logic** (MC lines ~139–189) →
   `WorkspaceService.OpenAsync()`:

   - The WorkTree is constructed inside `OpenProgress.DoWork()` which runs
     on a background thread managed by `WorkProgress`. Two viable patterns:

     **Pattern A (preferred):** `OpenAsync` calls `OpenProgress.DoWork()`
     on `Task.Run()`, returning the completed `WorkTree`. The ViewModel
     command body handles the `WorkProgress` dialog wrapper.

     **Pattern B:** The ViewModel command body creates the WorkTree via
     `WorkProgress`/`OpenProgress` and calls `AttachWorkTree(WorkTree)`
     to register state.

   - Keep: path recording, depth limiting
   - Keep: `DepthLimit()` as a private static helper inside `WorkspaceService`

4. **Move `CloseWorkFile` core logic** (MC lines ~190–215) →
   `WorkspaceService.Close()`:

   - Strip UI cleanup (clearing trees/lists stays in ViewModel)
   - Keep: WorkTree disposal
   - **Important:** Clipboard cleanup stays in the ViewModel.
     `WorkspaceService` has no access to `IClipboardService`. The ViewModel's
     own `CloseWorkFile()` must call
     `await _clipboardService.ClearIfPendingAsync()` immediately after
     `_workspaceService.Close()` returns.

5. **Move `UpdateRecentFilesList`** (MC line ~289) → `WorkspaceService`
   private method.

6. **Move `UnpackRecentFileList`** (MC line ~314) → `WorkspaceService`
   constructor/Load method.

7. **Move UI-facing methods to the ViewModel** (not the service):

   - `UpdateRecentLinks()` → `MainViewModel`. Create VM properties
     (`RecentFilePath1`, `RecentFileName1`, etc.), then call
     `mViewActions.PopulateRecentFilesMenu()` at the end for the native
     menu update.
   - `UpdateTitle` (MC line ~297) → `MainViewModel` (sets `WindowTitle`
     property for AXAML binding).
   - `OpenRecentFile(int)` (MC line ~376) → `MainViewModel` (reads from
     `_workspaceService.RecentFilePaths`).
   - `DropOpenWorkFile(string)` (MC line ~142) → `MainViewModel`
     (drag-drop open path).
   - `ShowFileError(string)` (MC line ~498) → Replace with
     `_dialogService.ShowMessageAsync()`.

8. **Fix `IWorkspaceService` interface:**

   - **Remove `CanWrite`** from `IWorkspaceService`. The controller's
     `CanWrite` (MCP line 112) returns whether the *currently selected
     archive tree node* is writable, which depends on UI selection state.
     That makes it a ViewModel concern, not a service concern. Keep
     `CanWrite` as a computed property on `MainViewModel` that reads
     from `SelectedArchiveTreeItem.WorkTreeNode.IsReadOnly`.

   - **Add `DebugLog`** to `IWorkspaceService`:
     ```csharp
     DebugMessageLog DebugLog { get; }
     ```
     This allows `Debug_ShowDebugLog()` (Group F) to access the log via
     `_workspaceService.DebugLog`.

9. **Register in DI container** (`App.axaml.cs`):
   ```csharp
   sc.AddSingleton<IWorkspaceService>(
       sp => new WorkspaceService(sp.GetRequiredService<ISettingsService>()));
   ```

10. **Build and verify zero errors.**

### Now that those are done, here's what changed

- **Modified:** `WorkspaceService.cs` (fleshed out from stub),
  `IWorkspaceService.cs` (removed `CanWrite`, added `DebugLog`),
  `App.axaml.cs` (DI registration), `MainViewModel.cs` (new lifecycle
  methods).
- **New capabilities:** `WorkspaceService` can open and close files,
  manage recent files, and provide the `Formatter`/`AppHook` instances
  that the rest of the application needs.
- **Behavior:** Commands still delegate to the controller. The service
  exists and is wired but not yet called from commands.

---

## Step 2: Move Settings Logic

### What we are going to accomplish

The controller currently handles loading, saving, and applying application
settings. These calls need to move: loading/saving go to
`MainViewModel.Initialize()` and `MainViewModel.Shutdown()` (calling
`_settingsService`), and applying settings becomes a ViewModel method.

**MVVM concept — Settings flow:** In the MVVM architecture, settings are
accessed through `ISettingsService` (a wrapper around `AppSettings.Global`).
The ViewModel reads settings via the service, translates them into ViewModel
properties (panel sizes, column widths, etc.), and the View binds to those
properties. The View never reads `AppSettings.Global` directly (except for
pure view concerns like window placement).

### To do that, follow these steps

1. **Move `LoadAppSettings()`** → call `_settingsService` methods in
   `MainViewModel.Initialize()`.

2. **Move `SaveAppSettings()`** → call `_settingsService.Save()` in
   `MainViewModel.Shutdown()`.

   **Save-side `mMainWin` accesses:** `SaveAppSettings()` (MC line 424)
   reads `WindowPlacement.Save(mMainWin)` and `mMainWin.LeftPanelWidth`.
   These reads stay in code-behind — the `Window_Closing` handler must:
   - Read `WindowPlacement.Save(this)` and `LeftPanelWidth`
   - Store them via `_settingsService.Set*(...)` (accessible through the
     ViewModel or directly if the window has a service reference)
   - Then call `vm.Shutdown()` which invokes `_settingsService.Save()`

3. **Move `ApplyAppSettings()`** → `MainViewModel.ApplySettings()`:

   - This method reads settings and sets ViewModel properties for panel
     sizes, column widths, recent files, etc.
   - **`WindowPlacement.Restore(mMainWin, ...)`** stays in code-behind.
     `MainWindow.Window_Loaded` calls it directly. This is a pure view
     concern.
   - **`mIsFirstApplySettings`** (MC line 451): This is a one-shot boolean
     guard. The window-placement restore and `LeftPanelWidth` restoration
     run only on the first call, then the flag is set to false. Without
     it, every subsequent `ApplySettings()` call would snap the window
     back to its saved position. Carry to the ViewModel as
     `private bool mIsFirstApplySettings = true;`.

4. **Eliminate `PublishSideOptions()`:** This method on `MainWindow` raises
   `PropertyChanged` for all options-panel properties. After migration,
   this is redundant — the ViewModel subscribes to
   `_settingsService.SettingChanged` and raises `PropertyChanged` for each
   affected property directly via `this.RaiseAndSetIfChanged(...)`. Delete
   `PublishSideOptions()` when `ApplySettings()` is migrated.

5. **Move `EditAppSettings()` (MC line 2240)** → `MainViewModel`:

   This method has a special pattern: the `EditAppSettings` dialog has an
   **Apply** button that fires a `SettingsApplied` event each time it's
   clicked, without closing the dialog. The generic
   `_dialogService.ShowDialogAsync<T>()` pattern doesn't cover this.

   Solution: `EditAppSettingsViewModel` must expose an `ApplyRequested`
   observable (or `Action` callback). `MainViewModel.EditAppSettings()`
   subscribes before showing the dialog and calls `ApplySettings()` on
   each emission.

6. **Build and verify zero errors.** Test that settings persist across
   application restarts.

### Now that those are done, here's what changed

- **Modified:** `MainViewModel.cs` (new `Initialize()`, `Shutdown()`,
  `ApplySettings()`, `EditAppSettings()` methods), `MainWindow.axaml.cs`
  (removed `PublishSideOptions()`, added save-side reads in
  `Window_Closing`).
- **New capabilities:** Settings are now loaded, saved, and applied
  through the ViewModel and `ISettingsService`.
- **Behavior:** Settings round-trip correctly.

---

## Step 3: Move Business-Logic Methods into MainViewModel

### What we are going to accomplish

This is the largest step. You will move all the "Actions" menu operations,
clipboard operations, debug commands, UI population methods, and
selection/state handlers from `MainController` and `MainController_Panels`
into `MainViewModel`.

**Pattern for each method migration:**

1. Copy the method body from the controller to `MainViewModel`.
2. Replace `mMainWin.PropertyName` → `this.PropertyName` (properties are
   already on the ViewModel from Iteration 1A).
3. Replace `new DialogName(mMainWin, ...) → await ShowDialog(mMainWin)`
   with `await _dialogService.ShowDialogAsync<TViewModel>(vm)`.
4. Replace `TopLevel.GetTopLevel(mMainWin).StorageProvider.*` with
   `await _filePickerService.*`.
5. Replace `AppSettings.Global.Get/Set*(...)` with
   `_settingsService.Get/Set*(...)`.
6. Remove `mMainWin.` prefix from property accesses that are now `this.`.
7. Keep `async Task` signatures; convert `async void` to `async Task`.

**VM-owned collection properties:** The following `ObservableCollection<T>`
properties must live on `MainViewModel` (not `MainWindow`) so that the
population methods can use `this.FileList`, etc., and AXAML bindings resolve
against the `DataContext`:
- `FileList` — `ObservableCollection<FileListItem>`
- `ArchiveTreeRoot` — `ObservableCollection<ArchiveTreeItem>`
- `DirectoryTreeRoot` — `ObservableCollection<DirectoryTreeItem>`

If these already moved in Iteration 1A, confirm they are on the ViewModel.
If not, move them now.

**Private dialog helpers:** `ShowMessageAsync(string, string)` (MC line 2644)
and `ShowConfirmAsync(string, string)` (MC line 2678) are private ad-hoc
dialog implementations called ~10 times. Delete each helper when its
consuming method is migrated; replace calls with
`await _dialogService.ShowMessageAsync(...)` and
`await _dialogService.ShowConfirmAsync(...)`.

**Toast notifications:** `mMainWin.PostNotification(msg, success)` is called
from ~8 places. Replace with `mViewActions.ShowToast(msg, success)` (the
`IViewActions` method defined in Step 0c).

The methods are organized into eight groups (A–H). The incremental migration
order at the end of this manual specifies which groups to tackle first.

---

### Group A — File Operations (add, extract, delete, test, move)

#### What we are going to accomplish

These are the core file-manipulation operations from the Actions menu.
Each method follows a similar pattern: validate preconditions → optionally
show a file picker → configure options → run a `WorkProgress` dialog with
a background worker → refresh the UI.

#### To do that, follow these steps

Migrate each method individually. Here are the methods and their key notes:

| Method | Key migration notes |
|---|---|
| `AddFiles()` | File picker → `_filePickerService.OpenFilesAsync()` |
| `ImportFiles()` | Same as AddFiles but with import converter spec |
| `HandleAddImport()` | Core add/import logic; calls `AddPaths()` |
| `AddPaths()` | Bulk add with WorkProgress dialog (keep `new WorkProgress(...)` per Step 0b) |
| `ConfigureAddOpts()` | Settings → `_settingsService` |
| `GetImportSpec()` | Settings lookup via `_settingsService` |
| `ExtractFiles()` | Folder picker → `_filePickerService.OpenFolderAsync()` |
| `ExportFiles()` | Same with export spec |
| `HandleExtractExport()` | Core extract/export logic with WorkProgress |
| `GetExportSpec()` | Settings lookup |
| `GetDefaultExportSpecs()` | Static helper — copy as-is |
| `DeleteFiles()` | WorkProgress dialog |
| `TestFiles()` | WorkProgress, ShowText for report |
| `MoveFiles()` | WorkProgress dialog. **Multi-select rebuild:** After moving entries, the method clears the DataGrid selection and re-selects the moved items. Call `mViewActions.SetFileListSelection(rebuiltItems)` after reconstructing the moved `FileListItem` objects. |
| `TryOpenNewSubVolumes()` | Called at end of `AddPaths()` and `PasteOrDrop()` — scans for newly-added entries that can be opened as sub-volumes. Belongs on the ViewModel alongside Group G population methods. |
| `AddDirEntries(...)` | Moves with `GetFileSelection` |
| `ShiftDirectories(...)` | Moves with `MoveFiles` |
| `GetCommonPathPrefix(...)` | Static helper — moves with `AddPaths` |

#### Now that those are done, here's what changed

- All file operations execute through `MainViewModel` methods and services.
- No controller reference needed for add/extract/delete/test/move.

---

### Group B — Edit/Attributes

#### What we are going to accomplish

These methods handle editing file attributes (ProDOS types, HFS types,
timestamps, access flags) and creating directories. They show dialogs
and apply the results.

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `EditAttributes()` | → `_dialogService.ShowDialogAsync<EditAttributesViewModel>(...)`. Note: `EditAttributesViewModel` does not exist until Phase 4A. For now, call the existing dialog class directly, getting the parent window via `IDialogHost`. |
| `EditDirAttributes()` | Same pattern |
| `EditAttributesImpl()` | Core logic (MacZip handling) — copy to ViewModel |
| `FinishEditAttributes()` | Post-edit UI refresh — ViewModel method |
| `CreateDirectory()` | → `_dialogService.ShowDialogAsync<CreateDirectoryViewModel>(...)` (or existing dialog class for now) |

#### Now that those are done, here's what changed

- Attribute editing and directory creation execute through the ViewModel.

---

### Group C — Disk/Sector Operations

#### What we are going to accomplish

These methods handle sector/block editing, saving disk images, replacing
partitions, defragmenting, and scanning for sub-volumes. Some are complex
(sector editing), others are simple (scan for sub-volumes).

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `EditBlocksSectors()` | → `_dialogService.ShowDialogAsync<EditSectorViewModel>(...)` (or existing dialog for now). **Note:** `EditSector.SectorEditMode` enum must be promoted to a shared location (e.g., `DiskArcNode.cs` or a dedicated enum file) before or as part of Phase 3B, so the ViewModel can reference it without depending on the dialog class. |
| `SaveAsDiskImage()` | → `_dialogService` + file picker |
| `ReplacePartition()` | File picker + dialog |
| `Defragment()` | WorkProgress dialog |
| `ScanForSubVol()` | Direct WorkTree operation |
| `CloseSubTree()` | Direct tree manipulation |
| `ScanForBadBlocks()` | Stub — `CanExecute` = false until implemented. Ensure the ViewModel exposes a `ReactiveCommand` with `canExecute: Observable.Return(false)` so the AXAML binding doesn't break. |

#### Now that those are done, here's what changed

- All disk/sector operations execute through the ViewModel.

---

### Group D — View/Navigation

#### What we are going to accomplish

These methods handle viewing files, navigating the archive tree, and
responding to double-click events on the file list and partition layout.

**Important note about `ViewFiles()`:** `FileViewerViewModel` does not
exist until Phase 4A. For Phase 3B, relocate the current `FileViewer`
dialog call from the controller to the ViewModel, calling the dialog
directly with its existing constructor. It will be replaced with
`_dialogService.ShowModeless<FileViewerViewModel>(...)` in Phase 4A.

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `ViewFiles()` | Move to ViewModel; call existing `FileViewer` dialog directly for now. Replace in Phase 4A. |
| `NavToParent()` | Tree navigation — direct property manipulation on the ViewModel |
| `HandleFileListDoubleClick()` | Navigation + open sub-archive |
| `HandlePartitionLayoutDoubleClick()` | Navigation |
| `FindFiles()` | **Modal** dialog with event callback. The source uses `await dialog.ShowDialog<bool?>(mMainWin)` with a `FindRequested` event subscription. `FindFileViewModel` (Phase 4B) must expose a `FindRequested` observable; for now, use the existing dialog. Note: `UpdateFindState()` calls `ArchiveTreeItem.SelectItem(mMainWin, ...)` — this needs `IViewActions` treatment (see Step 6). |
| `DoFindFiles()`, `FindInTree()`, etc. | Static search helpers — copy as-is |
| `HandleMetadataDoubleClick()` | MCP line 1132. Calls `UpdateMetadata()`, `RemoveMetadata()`. These become ViewModel methods that mutate the ViewModel's own `MetadataList` collection. |
| `HandleMetadataAddEntry()` | MCP line 1161. Calls `AddMetadata()`, `SetMetadataList()`. Same treatment. |

#### Now that those are done, here's what changed

- Navigation and viewing execute through the ViewModel.
- Metadata operations are ViewModel methods operating on ViewModel-owned
  collections.

---

### Group E — Clipboard

#### What we are going to accomplish

These methods handle copy, paste, and drag-drop of file entries within
and between CiderPress2 instances. They use `IClipboardService` for the
actual clipboard operations.

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `CopyToClipboard()` | → `_clipboardService.SetFilesAsync()` + WorkProgress |
| `PasteOrDrop()` | → `_clipboardService.GetFilesAsync()` + WorkProgress |
| `PasteExternalFiles()` | URI parsing + `AddPaths()` |
| `ClearClipboardIfPending()` | → `_clipboardService.ClearIfPendingAsync()`. **Note:** Currently `async void` — called from three synchronous property setters in MainWindow: `IsChecked_AddExtract.set`, `IsChecked_ImportExport.set`, `SelectedDDCPModeIndex.set`. Either keep as `async void` (fire-and-forget from setter semantics) or convert to `async Task` with `_ =` discard at call sites. Document your chosen approach. These three setters must be updated in Step 5 to call the ViewModel instead of `mMainCtrl`. |
| `CleanupClipTemp()` | Temp dir cleanup |

#### Now that those are done, here's what changed

- All clipboard operations go through `IClipboardService` via the ViewModel.

---

### Group F — Debug Commands

#### What we are going to accomplish

These are the Debug menu commands. They're the least critical to the
application's core functionality, which is why they're migrated last.

Several debug commands use **modeless toggle windows** — a window that
stays open alongside the main window and can be opened/closed independently.
The ViewModel keeps a reference to the window instance and subscribes to its
`Closed` event to reset the reference.

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `Debug_ShowDebugLog()` | Modeless toggle — keep reference on ViewModel. Get `DebugMessageLog` via `_workspaceService.DebugLog`. Subscribe to `Closed` event to reset reference and update `IsDebugLogOpen`: `mDebugLogViewer.Closed += (_, _) => { mDebugLogViewer = null; this.RaisePropertyChanged(nameof(IsDebugLogOpen)); };` |
| `Debug_DiskArcLibTests()` | → `_dialogService.ShowDialogAsync(...)` |
| `Debug_FileConvLibTests()` | Same |
| `Debug_BulkCompressTest()` | Same |
| `Debug_ShowSystemInfo()` | → `_dialogService.ShowDialogAsync(...)` — this is **modal**, not modeless. |
| `Debug_ShowDropTarget()` | Modeless toggle — same `Closed` event pattern as `Debug_ShowDebugLog()` for `mDebugDropTarget` / `IsDropTargetOpen`. |
| `Debug_ConvertANI()` | File picker + export |

#### Now that those are done, here's what changed

- All debug commands execute through the ViewModel.
- Modeless window lifecycle is managed by ViewModel references with
  `Closed` event subscriptions.

---

### Group G — UI Population

#### What we are going to accomplish

These methods populate the archive tree, directory tree, file list, and
center information panel after a file is opened, after navigation changes,
or after modifications. They transform `WorkTree` data into the
`ObservableCollection` items that the View binds to.

These methods are central to the application — nearly every other group
depends on them.

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `PopulateArchiveTree()` | → `MainViewModel.PopulateArchiveTree()`. Reads from `_workspaceService.WorkTree`. |
| `PopulateDirectoryTree()` | → `MainViewModel.PopulateDirectoryTree()` |
| `PopulateFileList()` | → `MainViewModel.PopulateFileList()` |
| `PopulateEntriesFromArchive()` | Helper — stays with `PopulateFileList` |
| `PopulateEntriesFromSingleDir()` | Same |
| `PopulateEntriesFromFullDisk()` | Same |
| `RefreshDirAndFileList()` | → `MainViewModel.RefreshDirAndFileList()` |
| `ConfigureCenterInfo()` | → `MainViewModel.ConfigureCenterInfo()` |
| `SetEntryCounts()` | → `MainViewModel.SetEntryCounts()` |
| `VerifyDirectoryTree()` | Static helper — copy as-is |
| `VerifyFileList()` (overloads) | Static helpers — copy as-is |

**`ShowFullListCommand` / `ShowDirListCommand`** (MW lines 1107–1130):
These command bodies currently mix view-level properties with a controller
call. After `PopulateFileList()` moves to the ViewModel:
- `PreferSingleDirList` and `ShowSingleDirFileList` migrate to ViewModel
  properties (they are app-state, not pure-view concerns).
- `ShowFullListCommand` and `ShowDirListCommand` move to the ViewModel and
  call `PopulateFileList()` directly.
- `SetShowCenterInfo()` is replaced by setting the ViewModel's
  `ShowCenterInfo` property.

#### Now that those are done, here's what changed

- All UI population logic runs in the ViewModel, operating on
  ViewModel-owned collections.
- The View simply binds to these collections and displays them.

---

### Group H — Selection/State

#### What we are going to accomplish

These methods handle selection changes in the archive tree, directory tree,
and file list. They also compute derived state properties (what's selected,
what's writable, what operations are available). Many other groups depend
on these methods, which is why they must be migrated early.

**Key VM fields to carry from the controller:**

- **`mSyncingSelection`** (MCP line 46): A boolean re-entrancy guard.
  `SyncDirectoryTreeToFileSelection()` programmatically changes the tree
  selection, which would otherwise trigger another `SelectionChanged`
  event, creating an infinite loop. Check this field at the top of the
  migrated `DirectoryTree_SelectionChanged()`.

- **`mSwitchFocusToFileList`** (MCP line 41): Controls whether
  `DirectoryTree_SelectionChanged()` ends by focusing the file list
  (via `mViewActions.FocusFileList()`). Set in `RefreshDirAndFileList()`
  and `HandleFileListDoubleClick()` to coordinate a two-step sequence.

- **`CurrentWorkObject`** (MCP line 65): `private object? mCurrentWorkObject` —
  the currently-selected DA object (`IDiskImage`, `IFileSystem`,
  `IArchive`, `Partition`, etc.). Set in `ArchiveTree_SelectionChanged()`.
  All computed state properties (`IsDiskImageSelected`, `CanEditBlocks`,
  `CanWrite`, `HasChunks`, etc.) read from it.

- **`CachedArchiveTreeSelection`** (MCP line 58): Tracks the
  last-confirmed archive tree selection. Set in
  `ArchiveTree_SelectionChanged()`, read by `NavToParent()`, cleared in
  `CloseWorkFile()`.

- **`CachedDirectoryTreeSelection`** (MCP line 53): Tracks the
  last-confirmed directory tree selection. Set in
  `DirectoryTree_SelectionChanged()` and
  `SyncDirectoryTreeToFileSelection()`.

#### To do that, follow these steps

| Method | Key migration notes |
|---|---|
| `GetSelectedArcDir()` | → `MainViewModel.GetSelectedArcDir()` |
| `GetFileSelection()` | → `MainViewModel.GetFileSelection()` |
| `ArchiveTree_SelectionChanged()` | → `MainViewModel.OnArchiveTreeSelectionChanged(ArchiveTreeItem?)` — the View's event handler extracts the selected item and passes it to this method. |
| `DirectoryTree_SelectionChanged()` | → `MainViewModel.OnDirectoryTreeSelectionChanged(DirectoryTreeItem?)` — same pattern. |
| `SyncDirectoryTreeToFileSelection()` | → `MainViewModel` method — uses `mSyncingSelection` guard. |
| `CheckPasteDropOkay()` | → `MainViewModel` method |

Also migrate all computed state properties that are set by these handlers
(`IsDiskImageSelected`, `CanEditBlocks`, `CanWrite`, `HasChunks`, etc.)
if they haven't already moved in Iteration 1A.

#### Now that those are done, here's what changed

- All selection/state logic runs in the ViewModel.
- The View's selection-changed event handlers are thin pass-throughs that
  extract the selected item and call a ViewModel method.
- Computed state properties react to selection changes, and
  `ReactiveCommand` `CanExecute` observables automatically update.

---

## Step 4: Wire Up Lifecycle Methods

### What we are going to accomplish

The controller's `WindowLoaded()` and `WindowClosing()` methods handle
application startup and shutdown. These must be replaced with
`MainViewModel.Initialize()` and `MainViewModel.Shutdown()` methods,
called from thin code-behind event handlers.

### To do that, follow these steps

1. **Replace the `Window_Loaded` handler** in `MainWindow.axaml.cs`:

   **Before:**
   ```csharp
   private void Window_Loaded(object sender, RoutedEventArgs e) {
       mMainCtrl.WindowLoaded();
   }
   ```

   **After:**
   ```csharp
   private void Window_Loaded(object sender, RoutedEventArgs e) {
       if (DataContext is MainViewModel vm) {
           vm.Initialize();
       }
   }
   ```

2. **Replace the `Window_Closing` handler:**

   **Before:**
   ```csharp
   private void Window_Closing(object sender, WindowClosingEventArgs e) {
       mMainCtrl.WindowClosing();
   }
   ```

   **After:**
   ```csharp
   private void Window_Closing(object sender, WindowClosingEventArgs e) {
       if (DataContext is MainViewModel vm) {
           // Read view-only state before shutdown
           // (window placement, panel widths — see Step 2 notes)
           vm.Shutdown();
       }
   }
   ```

3. **Implement `MainViewModel.Initialize()`:**

   Move from `MainController.WindowLoaded()`:
   - Run startup self-tests (copy verbatim — `Debug.Assert(RangeSet.Test())`,
     `CommonUtil.Version.Test()`, `CircularBitBuffer.DebugTest()`, etc.)
   - Load settings via `_settingsService.Load()`
   - Apply settings (column widths, recent files, panel state)
   - Open command-line file if provided

4. **Implement `MainViewModel.Shutdown()`:**

   Move from `MainController.WindowClosing()`:
   - Close debug log window (if open)
   - Cleanup clipboard temp via `_clipboardService`
   - Save settings via `_settingsService.Save()`

5. **Implement the VM command body for file open** (post-open sequence):

   After `_workspaceService.OpenAsync(...)` returns, the ViewModel must
   perform the following UI-state updates:

   ```csharp
   public async Task OpenWorkFileAsync() {
       if (!CloseWorkFile()) return;
       string? path = await _filePickerService.OpenFileAsync(...);
       if (path == null) return;
       mViewActions.SetCursorBusy(true);
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
           mViewActions.SetCursorBusy(false);
       }
   }
   ```

6. **Build and verify.** Test opening and closing a file, then closing
   the application and reopening to verify settings persist.

### Now that those are done, here's what changed

- **Modified:** `MainWindow.axaml.cs` (simplified lifecycle handlers),
  `MainViewModel.cs` (new `Initialize()`, `Shutdown()` methods).
- **New capabilities:** Application startup and shutdown are driven by
  the ViewModel.
- **Behavior:** Same as before — startup self-tests run, settings are
  loaded and saved.

---

## Step 5: Wire View-Only Event Handlers

### What we are going to accomplish

Some controller methods handle pure View events (selection changed,
double-tap, button click). After migration, these events still fire in the
View, but the View's code-behind handlers become thin pass-throughs that
delegate to ViewModel methods.

**MVVM concept — Why keep event handlers in code-behind?** Avalonia's
`SelectionChanged`, `DoubleTapped`, and similar events are View-level
concerns. The View is responsible for extracting the relevant data from
the event args (e.g., which item was selected) and passing it to the
ViewModel as a typed parameter. The ViewModel never sees event args
or control types.

### To do that, follow these steps

1. **Update `ArchiveTree_SelectionChanged`** in `MainWindow.axaml.cs`:
   ```csharp
   private void ArchiveTree_SelectionChanged(object sender,
       SelectionChangedEventArgs e) {
       if (DataContext is MainViewModel vm) {
           var sel = archiveTree.SelectedItem as ArchiveTreeItem;
           vm.OnArchiveTreeSelectionChanged(sel);
       }
   }
   ```

2. **Update `DirectoryTree_SelectionChanged`:**
   ```csharp
   private void DirectoryTree_SelectionChanged(object sender,
       SelectionChangedEventArgs e) {
       if (DataContext is MainViewModel vm) {
           var sel = directoryTree.SelectedItem as DirectoryTreeItem;
           vm.OnDirectoryTreeSelectionChanged(sel);
       }
   }
   ```

3. **Update `FileList_DoubleTapped`:**
   ```csharp
   private void FileList_DoubleTapped(object sender, TappedEventArgs e) {
       if (DataContext is MainViewModel vm) {
           vm.HandleFileListDoubleClick();
       }
   }
   ```

4. **Update `PartitionLayout_DoubleTapped`:**
   ```csharp
   private void PartitionLayout_DoubleTapped(object sender,
       TappedEventArgs e) {
       // Column-header auto-size logic stays here — copy verbatim from MW
       if (DataContext is MainViewModel vm &&
           (sender as DataGrid)?.SelectedItem is PartitionListItem pli) {
           var arcTreeSel = archiveTree.SelectedItem as ArchiveTreeItem;
           vm.HandlePartitionLayoutDoubleClick(pli, arcTreeSel);
       }
   }
   ```

5. **Update `MetadataList_DoubleTapped`:**
   ```csharp
   private async void MetadataList_DoubleTapped(object sender,
       TappedEventArgs e) {
       // Column-header auto-size logic stays here
       if (DataContext is MainViewModel vm &&
           (sender as DataGrid)?.SelectedItem is MetadataItem item) {
           await vm.HandleMetadataDoubleClick(item, 0, 0);
       }
   }
   ```

6. **Update `Metadata_AddEntryButtonClick`:**
   ```csharp
   private async void Metadata_AddEntryButtonClick(object sender,
       RoutedEventArgs e) {
       if (DataContext is MainViewModel vm) {
           await vm.HandleMetadataAddEntry();
       }
   }
   ```

7. **Build and verify.** Test archive tree navigation, directory tree
   navigation, file list double-click, partition double-click, and
   metadata editing.

### Now that those are done, here's what changed

- **Modified:** `MainWindow.axaml.cs` (event handlers simplified to
  thin pass-throughs).
- **Behavior:** Unchanged — all events still work, but the logic now
  runs in the ViewModel.

---

## Step 6: Handle Direct Control Access

### What we are going to accomplish

The controller has several methods that access View controls directly
(e.g., `mMainWin.fileListDataGrid.SelectedItem`,
`mMainWin.fileListDataGrid.ScrollIntoView(...)`,
`mMainWin.Cursor = new Cursor(...)`). These must be replaced with either
ViewModel properties (for bindable state) or `IViewActions` calls (for
imperative operations).

This step also migrates several property groups from `MainWindow` to
`MainViewModel`.

### To do that, follow these steps

#### 6a. Panel visibility and debug menu properties

The following properties are set by controller methods that are migrating
to the ViewModel. They must become ViewModel properties bound in AXAML:

- `LaunchPanelVisible` (bool) — controls launch/drop panel visibility
- `MainPanelVisible` (bool) — controls main content panel visibility
- `ShowDebugMenu` (bool) — controls debug menu visibility

Update AXAML bindings from `MainWindow`-property references to
`DataContext`-property bindings:
```xml
<Panel IsVisible="{Binding LaunchPanelVisible}">
```

#### 6b. Options-panel properties migration

The ~15 `IsChecked_*` properties (e.g., `IsChecked_AddCompress`,
`IsChecked_ExtPreserveAS`) and `SelectedDDCPModeIndex` currently live on
`MainWindow` and read/write `AppSettings.Global.*` directly. In the
ViewModel, they read/write via `_settingsService.Get/SetBool(...)`, and
their AXAML bindings change from implicit Window context to
`{Binding IsChecked_AddCompress}` resolved against `MainViewModel`.

#### 6c. Import/export converter properties migration

Four additional properties must migrate alongside the options-panel
properties:

- `ImportConverters` (`List<ConvItem>`) — ComboBox `ItemsSource`
- `ExportConverters` (`List<ConvItem>`) — ComboBox `ItemsSource`
- `SelectedImportConverter` (`ConvItem?`) — ComboBox `SelectedItem`; setter
  writes chosen tag via `_settingsService.SetString(...)`
- `SelectedExportConverter` (`ConvItem?`) — same

`InitImportExportConfig()` (MW line 483) populates both lists and restores
saved selection. Move to `MainViewModel` and call from `Initialize()`. Read
saved tags via `_settingsService.GetString(...)`.

#### 6d. SelectedFileListItem binding

Expose a `SelectedFileListItem` property on the ViewModel and bind it in
AXAML:

```csharp
// MainViewModel:
private FileListItem? mSelectedFileListItem;
public FileListItem? SelectedFileListItem {
    get => mSelectedFileListItem;
    set => this.RaiseAndSetIfChanged(ref mSelectedFileListItem, value);
}
```

```xml
<DataGrid SelectedItem="{Binding SelectedFileListItem}">
```

#### 6e. Scroll/focus operations via IViewActions

Methods that need scroll-to or focus stay in code-behind and are exposed
through `IViewActions` (defined in Step 0c). The ViewModel calls these
through `mViewActions`:

```csharp
// In a ViewModel method:
mViewActions.ScrollFileListTo(item);
mViewActions.FocusFileList();
mViewActions.SetCursorBusy(true);
```

#### 6f. Methods that do NOT belong on IViewActions

The following are data/state operations that should be `MainViewModel`
methods or properties with AXAML bindings — **not** imperative view calls:

- `ConfigureCenterPanel(...)` → set ViewModel boolean properties
  (`HasInfoOnly`, `IsFullListEnabled`, `IsDirListEnabled`,
  `ShowCol_Format`, etc.)
- `ClearCenterInfo()` → ViewModel clears its own collections directly
- `SetNotesList(...)` → ViewModel populates `NotesList` collection +
  `ShowNotes` property
- `SetPartitionList(...)` → ViewModel populates `PartitionList` collection +
  `ShowPartitionLayout` property
- `SetMetadataList(...)` → ViewModel populates `MetadataList` collection +
  `ShowMetadata`, `CanAddMetadataEntry` properties
- `UpdateMetadata(...)`, `AddMetadata(...)`, `RemoveMetadata(...)` →
  ViewModel mutates its own `MetadataList` collection
- `ReapplyFileListSort()` → ViewModel method (sorts the bound `FileList`
  collection)

#### 6g. SelectAllCommand and ResetSortCommand stay in code-behind

**`SelectAllCommand`** (MW line 1018) calls
`fileListDataGrid.SelectAll()` — a purely view-level action with no business
logic. It can remain in `MainWindow` code-behind. Alternatively, the
ViewModel can call `mViewActions.SelectAllFileListItems()`.

**AXAML binding fix required:** After `DataContext = vm`,
`{Binding SelectAllCommand}` would resolve against the ViewModel (which
lacks this command). Update to:
```xml
{Binding SelectAllCommand, RelativeSource={RelativeSource AncestorType=Window}}
```

**`ResetSortCommand`** (MW line 1164) is also purely view-level. Same
binding fix:
```xml
{Binding ResetSortCommand, RelativeSource={RelativeSource AncestorType=Window}}
```

#### 6h. Static helper methods refactoring

The following static methods currently take `MainWindow` as a parameter
and cannot be called from a ViewModel:

- `ArchiveTreeItem.SelectItem(MainWindow, item)` — refactor to take
  `IViewActions` (or the relevant tree collection) instead
- `ArchiveTreeItem.SelectBestFrom(mainWin.archiveTree, ...)` — refactor
  to accept the tree collection directly
- `DirectoryTreeItem.SelectItemByEntry(MainWindow, ...)` — refactor to
  take `IViewActions`
- `FileListItem.SetSelectionFocusByEntry(fileList, mainWin.fileListDataGrid, ...)`
  — refactor to take `IViewActions`

These must be refactored before or during the Group G/H migration so that
ViewModel code can call them without a `MainWindow` reference.

#### Build checkpoint

Build and verify zero errors. Test all panel visibility, options panel
toggles, import/export converter selection, file list selection, and
sort behavior.

### Now that those are done, here's what changed

- **Modified:** `MainViewModel.cs` (new properties and methods),
  `MainWindow.axaml.cs` (removed migrated properties, updated command
  bindings), `MainWindow.axaml` (updated bindings), `ArchiveTreeItem.cs`,
  `DirectoryTreeItem.cs`, `FileListItem.cs` (static method signatures
  changed).
- **New capabilities:** The ViewModel owns all state that the View displays.
  Direct control access is fully mediated through `IViewActions`.

---

## Step 7: Delete Controller Files

### What we are going to accomplish

Once all methods have been moved and the build passes, the controller
files serve no purpose. Deleting them completes the MVVM separation for
the main window.

### To do that, follow these steps

1. **Delete `cp2_avalonia/MainController.cs`**
2. **Delete `cp2_avalonia/MainController_Panels.cs`**
3. **Remove the `mMainCtrl` field** and all references from
   `MainWindow.axaml.cs`
4. **Remove `SetController()`** from `MainViewModel` (the temporary
   coupling method from Phase 1)
5. **Remove the `mMainWin` field type** from anywhere it's referenced.
   The ViewModel no longer uses `MainWindow` directly — it uses
   `IDialogHost` + `IViewActions`.
6. **Build and verify zero errors.** Fix any remaining `mMainWin.` or
   `mController.` references that the compiler reports.

### Now that those are done, here's what changed

- **Deleted:** `MainController.cs`, `MainController_Panels.cs`
- **Modified:** `MainWindow.axaml.cs` (removed controller field and
  references), `MainViewModel.cs` (removed `SetController()` and
  `mController` field).
- **Behavior:** The application works exactly as before, but the
  controller layer no longer exists.

---

## Step 8: Build and Validate

### What we are going to accomplish

This is the final validation step. You will build the application and
perform a comprehensive functional test to verify that every feature
still works correctly after the controller has been dissolved.

### To do that, follow these steps

1. **Run `dotnet build`** — verify zero errors. Fix any remaining
   `mMainWin.` or `mController.` references.

2. **Launch the application.**

3. **Complete functional test** — exercise every feature:

   - [ ] Open/close files (various formats)
   - [ ] All menu commands
   - [ ] Add/extract/delete/test files
   - [ ] Edit attributes, create directory
   - [ ] Copy/paste (same-process)
   - [ ] Edit sectors/blocks
   - [ ] New disk image, new file archive
   - [ ] Save as disk image, replace partition
   - [ ] Find files
   - [ ] View selected files
   - [ ] Debug menu items (debug log, tests, system info, drop target)
   - [ ] Settings dialog (edit, apply, persist on restart)
   - [ ] Recent files list
   - [ ] Drag-and-drop file open
   - [ ] Window resize / panel resize (persisted across restart)
   - [ ] macOS native menu (About, Settings, Quit)

### Now that those are done, here's what changed

- The application is fully validated with the controller removed.
- All commands execute through `MainViewModel` and services.

---

## Incremental Migration Strategy

### What we are going to accomplish

This section specifies the **exact order** in which to migrate method
groups. The order is designed to maintain a compiling, runnable
application at every step. Migrating in the wrong order will cause
build failures because methods depend on each other.

**Do NOT attempt to move all methods at once.** Follow this order:

### To do that, follow these steps

1. **Extract `AutoOpenDepth`** (Step 0a) — prerequisite for Step 1
2. **Understand WorkProgress strategy** (Step 0b) — strategy for progress
   dialogs
3. **Define `IViewActions`** (Step 0c) — prerequisite for Steps 3–5
4. **Settings** (Step 2) — small, no dependencies
5. **State query properties** (Group H — `GetFileSelection()`,
   `GetSelectedArcDir()`, `CheckPasteDropOkay()`, `mSyncingSelection`,
   `mSwitchFocusToFileList`, `mCurrentWorkObject`,
   `CachedArchiveTreeSelection`, `CachedDirectoryTreeSelection`, and all
   computed state properties) — migrate these **before** Groups A–E
   because those groups depend on them
6. **UI population** (Group G) — needed by everything else
7. **Lifecycle** (Step 4) — connects init/shutdown
8. **Navigation** (Group D nav subset) — tree selection handlers
9. **File operations** (Group A) — largest group, do one method at a time
10. **Edit/Attributes** (Group B)
11. **Disk operations** (Group C)
12. **Clipboard** (Group E)
13. **Debug** (Group F) — least critical
14. **Delete controller files** (Step 7) — only after everything else
    compiles
15. **Build and validate** (Step 8)

**Build and test after moving each group.** This is essential — if you
wait until the end to test, debugging will be much harder because you
won't know which migration introduced the problem.

### Now that those are done, here's what changed

- `MainController.cs` and `MainController_Panels.cs` are deleted.
- `MainViewModel` is self-contained with injected services.
- Phase 4 can create dialog ViewModels with `IDialogService` integration.
- Phase 5 can extract child ViewModels from `MainViewModel` methods.

---

## Summary of What This Iteration Enables

| Before Iteration 3B | After Iteration 3B |
|---|---|
| Commands delegate to `mController` | Commands execute directly in `MainViewModel` |
| Controller holds `mMainWin` (circular dependency) | ViewModel uses `IDialogHost` + `IViewActions` (no View reference) |
| Dialog creation: `new Dialog(mMainWin, ...)` | Dialog creation: `_dialogService.ShowDialogAsync<TVM>(vm)` |
| File pickers: `StorageProvider` directly | File pickers: `_filePickerService.*` |
| Settings: `AppSettings.Global.*` directly | Settings: `_settingsService.*` |
| WorkTree lifecycle in controller | WorkTree lifecycle in `WorkspaceService` |
| ~3,900 lines in two controller files | Zero controller files |
| Cannot unit test business logic | Business logic is testable (inject mock services) |
