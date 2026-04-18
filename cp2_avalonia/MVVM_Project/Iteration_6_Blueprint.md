# Iteration 6 Blueprint: Polish, Multi-Viewer Evaluation & Optional Enhancements

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 Phase 6, §7.10,
> §7.11.

---

## Goal

Final cleanup, polish, and optional feature evaluation. This phase is
**exploratory** — items are evaluated and implemented only if they add
clear value. The core MVVM refactor is complete after Phase 5.

---

## Prerequisites

- Iteration 5 is complete (all child ViewModels extracted, `MainViewModel`
  is a manageable coordinator).
- The application builds, runs, and passes all functional tests.

---

## Workstreams

### Workstream A: Cleanup & Technical Debt

#### A1. Remove Dead Code

1. Search for any remaining references to `MainController`:
   ```
   grep -rn "MainController" cp2_avalonia/
   ```
   Delete any leftover `using` statements, comments, or stubs.

2. Search for `RelayCommand`:
   ```
   grep -rn "RelayCommand" cp2_avalonia/
   ```
   Delete `Common/RelayCommand.cs` if no references remain.

3. Search for `DataContext = this`:
   ```
   grep -rn "DataContext = this" cp2_avalonia/
   ```
   Should return zero results. Every view should receive its DataContext
   from the ViewModel via DI or DialogService.

4. Search for `INotifyPropertyChanged` implementations in ViewModels:
   ```
   grep -rn "INotifyPropertyChanged" cp2_avalonia/ViewModels/
   ```
   All ViewModels should use `ReactiveObject` instead.

#### A2. Consistent Error Handling

> **Note:** `ThrownExceptions` subscriptions should have been added in Phase 2
> when commands were converted to `ReactiveCommand`. This step is a
> **verification and remediation pass** — confirm every command already has
> a subscription, and add any that are missing.

Enumerate all `ReactiveCommand` instances:
```
grep -rn "ReactiveCommand" cp2_avalonia/ViewModels/
```

For each result, confirm a `ThrownExceptions.Subscribe(...)` call exists.
The correct pattern (per Pre-Iteration-Notes §6) is:

```csharp
// Pattern for all commands (synchronous callback, fire-and-forget discard):
SomeCommand.ThrownExceptions.Subscribe(ex =>
    _ = _dialogService.ShowMessageAsync(ex.Message, "Error"));
```

> **Do NOT use `async ex => { await ... }`** inside `Subscribe` — this
> generates CS4014 and diverges from the established convention. The `_ =`
> discard is intentional; see Pre-Iteration-Notes §6 for rationale.

#### A3. Finalize Message Box Dialog

> **Note:** `ShowMessageAsync` is part of the `IDialogService` interface
> created in Phase 3A. A placeholder implementation has been in use since
> then. This step replaces that placeholder with a fully functional
> message box. Consider relocating this to Phase 3A in future blueprint
> revisions (see MVVM_Notes.md Appendix A).

Replace the placeholder `ShowMessageAsync` in `DialogService` with a proper
message box implementation. The custom approach is preferred for full control
over styling.

**Implementation steps:**

1. **VERIFY** that `MBButton`, `MBIcon`, and `MBResult` enums already exist
   in `Services/IDialogService.cs` (created in Phase 3A). Do **not** redefine
   them. If they are missing (e.g., Phase 3A was not fully applied), add them
   to `IDialogService.cs` now; if present, skip to Step 2.

2. Ensure the `IDialogService.ShowMessageAsync` signature matches:
   ```csharp
   Task<MBResult> ShowMessageAsync(
       string message, string caption,
       MBButton buttons = MBButton.OK,
       MBIcon icon = MBIcon.None);
   ```

3. Create `MessageBoxViewModel` with properties for message, caption,
   buttons, icon, and a `ResultCommand` for each button option.

4. Create `MessageBoxView.axaml` in the root of `cp2_avalonia/` (alongside
   existing dialogs such as `EditSector.axaml`, `CreateDiskImage.axaml`, etc.).
   Include a configurable button panel
   (OK, OK/Cancel, Yes/No, Yes/No/Cancel) and icon display.

5. Register the View→ViewModel mapping on the `DialogService` instance
   at the point where it is constructed for each `MainViewModel` (e.g., in
   `MainViewModel`'s constructor or the factory method that wires up the
   window). This ensures the mapping is present before any command calls
   `ShowMessageAsync`:
   ```csharp
   dialogService.Register<MessageBoxViewModel, MessageBoxView>();
   ```

6. Implement `ShowMessageAsync` in `DialogService` to create a
   `MessageBoxViewModel`, call `ShowDialogAsync`, and return the
   `MBResult` indicating which button was clicked.

#### A4. Settings Persistence Audit

Verify all settings round-trip correctly:

1. Change every setting in EditAppSettings
2. Close and reopen the application
3. Verify all settings restored to changed values
4. Reset to defaults and verify

Ensure `_settingsService.Save()` is called in `MainViewModel.Shutdown()`.

5. Verify live updates: change the theme setting in EditAppSettings, confirm
   OK **without restarting**, and verify the theme updates immediately in the
   main window (validates that `SettingChanged` observable fires correctly).

#### A5. Subscription Lifecycle Cleanup

Verify all `WhenAnyValue` and `Subscribe` calls are properly disposed.
This is a **correctness** requirement — leaked subscriptions on long-lived
ViewModels cause memory leaks and stale reactive state.

Apply the correct pattern based on ViewModel type (per Pre-Iteration-Notes §4):

- **Top-level ViewModels with a View** (e.g., `MainViewModel`): implement
  `IActivatableViewModel` and use `WhenActivated` + `DisposeWith`:
  ```csharp
  public class MainViewModel : ReactiveObject, IActivatableViewModel {
      public ViewModelActivator Activator { get; } = new();

      public MainViewModel(...) {
          this.WhenActivated(disposables => {
              ArchiveTree.WhenAnyValue(x => x.SelectedItem)
                  .Subscribe(item => OnArchiveTreeSelectionChanged(item))
                  .DisposeWith(disposables);
              // ... other subscriptions
          });
      }
  }
  ```

- **Child ViewModels without a direct View** (e.g., `FileListViewModel`,
  `ArchiveTreeViewModel`): implement `IDisposable`. These VMs are long-lived
  sub-objects of `MainViewModel` — `WhenActivated` will **never fire** on
  them because they are not attached to an activating View. `MainViewModel`
  must call `Dispose()` on each child VM when replacing or tearing down.

> **Do NOT** apply `IActivatableViewModel` to child VMs — their subscriptions
> will silently never be set up.

Enumerate all subscriptions to audit:
```
grep -rn "\.Subscribe\|WhenAnyValue\|ObservableAsPropertyHelper" cp2_avalonia/ViewModels/
```
For each result, confirm it is either (a) inside a `WhenActivated` block
with `DisposeWith(disposables)`, (b) tracked in an `IDisposable`-implementing
child VM's disposal collection, or (c) in a dialog VM where the subscription
is safe to leave untracked (per Pre-Iteration-Notes §4).

---

### Workstream B: Multi-Instance FileViewer Enhancement

Evaluate and potentially implement enhanced multi-viewer support.

#### B1. Current State Assessment

After Phase 4A, `FileViewer` is modeless with a ViewModel. Multiple
instances can be opened. Evaluate:

- Are multiple FileViewer windows tracked correctly by `IViewerService`?
- When a file is closed, are associated viewers closed?
- Can the user open a viewer for a file already being viewed?

#### B2. Implementation (if needed)

If the current implementation is insufficient:

```csharp
// ViewerService.cs — full implementation:
public class ViewerService : IViewerService {
    private readonly List<FileViewerViewModel> _viewers = new();
    private readonly object _lock = new();

    public IReadOnlyList<FileViewerViewModel> ActiveViewers {
        get { lock (_lock) return _viewers.ToList(); }
    }

    public void Register(FileViewerViewModel viewer) {
        lock (_lock) _viewers.Add(viewer);
    }

    public void Unregister(FileViewerViewModel viewer) {
        lock (_lock) _viewers.Remove(viewer);
    }

    public void CloseViewersForSource(string workPathName) {
        List<FileViewerViewModel> toClose;
        lock (_lock) {
            toClose = _viewers
                .Where(v => v.WorkPathName == workPathName)
                .ToList();
        }
        // Closing windows must happen on the UI thread. If
        // CloseViewersForSource can be called from a background thread,
        // wrap in Dispatcher.UIThread.InvokeAsync().
        foreach (var vm in toClose) {
            vm.RequestClose();
        }
    }
}
```

> **Ordering requirement (MVVM_Notes §7.10 gotcha #1):**
> `CloseViewersForSource(...)` must complete **synchronously** before
> `WorkTree.Dispose()`. The `MainViewModel.Close...()` path must call
> `CloseViewersForSource`, confirm all viewers are closed, and only then
> proceed to `IWorkspaceService.Close()`.

> **Orphaned registrations (§7.10 gotcha #3):** If a viewer's
> `Dispose`/`WhenDeactivated` fails to fire, the service retains a dead
> reference. Verify that the Avalonia window `Closed` event reliably
> triggers `Unregister`. Consider a weak-reference fallback if it does not.

`FileViewerViewModel` must expose a source identifier for scoped cleanup,
and receive `IViewerService` via constructor injection (per §7.10):

```csharp
// Constructor parameter — injected by the caller (e.g., MainViewModel)
public FileViewerViewModel(IViewerService viewerService, string workPathName, ...) {
    WorkPathName = workPathName;
    _viewerService = viewerService;
    _viewerService.Register(this);
}

// Source identifier for scoped cleanup. Named to match
// IWorkspaceService.WorkPathName for consistency.
public string WorkPathName { get; }

// Called by IViewerService.CloseViewersForSource(). Uses an
// Interaction<Unit, Unit> so the View owns the window-close action.
public Interaction<Unit, Unit> RequestCloseInteraction { get; } = new();

// Tracked manually because Interaction<TInput,TOutput> does not expose
// a public HasObservers property.
private bool mCloseHandlerRegistered;
private bool mClosePending;
public bool IsClosePending => mClosePending;

public void RequestClose() {
    // Guard: if the View has not yet activated (no handler registered),
    // Handle() would throw UnhandledInteractionException. Set a flag
    // so WhenActivated can close immediately when it does activate.
    if (!mCloseHandlerRegistered) {
        mClosePending = true;
        return;
    }
    try {
        _ = RequestCloseInteraction.Handle(Unit.Default).Subscribe();
    } catch (UnhandledInteractionException) {
        mClosePending = true;
    }
}

// In FileViewer code-behind WhenActivated:
// d(ViewModel!.RequestCloseInteraction.RegisterHandler(ctx => {
//     Close();
//     ctx.SetOutput(Unit.Default);
// }));
// ViewModel!.mCloseHandlerRegistered = true;  // enable RequestClose path
// if (ViewModel!.IsClosePending) Close();  // handle race condition
```

#### B3. Viewer Lifecycle

Ensure viewer windows:
- Register with `IViewerService` on creation
- Unregister on close
- Are force-closed when the source file is closed
- Handle the case where the source archive has been modified:
  `FileViewerViewModel` subscribes to `IWorkspaceService.WorkspaceModified`
  (added to the interface in Phase 3A). When the observable fires, the
  viewer sets a `mSourceModified` flag and displays a warning banner
  (bind to `IsSourceModifiedWarningVisible`). The viewer does **not**
  auto-refresh — the user decides whether to close or continue viewing
  stale data

---

### Workstream C: Docking Evaluation (Optional)

#### C1. Assess Need

Evaluate whether dockable panels add value. Consider:

- Is the current fixed layout sufficient for users?
- Would docking add complexity without proportional benefit?
- Are there user requests for docking?

#### C2. Available Libraries

If docking is desired:

| Library | Maturity | License | Notes |
|---|---|---|---|
| Dock.Avalonia | Active | MIT | Best Avalonia docking option |

#### C3. Decision Gate

**If docking is NOT pursued (expected):** Skip this workstream. Record the
decision and rationale in the project's `KNOWN_ISSUES.md` docking entry.
The child ViewModels from Phase 5 make it straightforward to add later if
needed.

**If docking IS pursued:** Create a separate blueprint for the docking
integration. This would involve:
- Adding `Dock.Avalonia` NuGet package
- Replacing the current Grid/SplitView layout with dock panels
- Exposing child ViewModels as dockable documents/tools
- Saving/restoring dock layout in settings

---

### Workstream D: Unit Test Infrastructure (Optional)

#### D1. ViewModel Unit Tests

With all logic on ViewModels, create a test project:

```
cp2_avalonia_tests/
    cp2_avalonia_tests.csproj
    ViewModels/
        MainViewModelTests.cs
        EditSectorViewModelTests.cs
        ArchiveTreeViewModelTests.cs
        ...
```

After creating the project, add it to the solution:
```
dotnet sln CiderPress2.sln add cp2_avalonia_tests/cp2_avalonia_tests.csproj
```

**Test infrastructure:**
```xml
<!-- cp2_avalonia_tests.csproj -->
<ProjectReference Include="../cp2_avalonia/cp2_avalonia.csproj" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

**Mock services:**

> **Required `using` directives** for test files:
> `using System.Reactive.Linq;` (for `Observable.Empty`),
> `using System.Reactive.Concurrency;` (for `Scheduler.CurrentThread`,
> `DefaultScheduler.Instance`).

```csharp
var mockDialog = new Mock<IDialogService>();
var mockPicker = new Mock<IFilePickerService>();
var mockSettings = new Mock<ISettingsService>();
var mockClipboard = new Mock<IClipboardService>();
var mockWorkspace = new Mock<IWorkspaceService>();
var mockViewer = new Mock<IViewerService>();

// ISettingsService.SettingChanged must return a non-null observable
// to avoid NullReferenceException when VMs subscribe in their constructor.
mockSettings.Setup(s => s.SettingChanged)
    .Returns(Observable.Empty<string>());

// IWorkspaceService.WorkspaceModified must also return a non-null observable.
// Moq returns null by default for reference-type properties; MainViewModel
// subscribes to this in its constructor and will throw NullReferenceException.
mockWorkspace.Setup(w => w.WorkspaceModified)
    .Returns(Observable.Empty<Unit>());

var vm = new MainViewModel(
    mockDialog.Object, mockPicker.Object,
    mockSettings.Object, mockClipboard.Object,
    mockWorkspace.Object, mockViewer.Object);
```

**ReactiveUI test scheduler setup:**

ReactiveUI uses `RxApp.MainThreadScheduler` internally. In unit tests,
this must be set to a synchronous scheduler or reactive pipelines will
not fire predictably. Add a base class or xUnit fixture:

```csharp
public class ReactiveTestBase : IDisposable
{
    public ReactiveTestBase()
    {
        RxApp.MainThreadScheduler = Scheduler.CurrentThread;
    }

    public void Dispose()
    {
        RxApp.MainThreadScheduler = DefaultScheduler.Instance;
    }
}
```

All ViewModel test classes should inherit from `ReactiveTestBase`.

**Test naming convention:** Use `MethodName_StateUnderTest_ExpectedBehavior`.

**Complete test example:**

```csharp
public class MainViewModelTests : ReactiveTestBase
{
    [Fact]
    public void OpenCommand_WhenFileNotOpen_CanExecuteIsTrue()
    {
        // Arrange
        var mockDialog = new Mock<IDialogService>();
        var mockPicker = new Mock<IFilePickerService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.SettingChanged)
            .Returns(Observable.Empty<string>());
        var mockClipboard = new Mock<IClipboardService>();
        var mockWorkspace = new Mock<IWorkspaceService>();
        mockWorkspace.Setup(w => w.WorkspaceModified)
            .Returns(Observable.Empty<Unit>());
        var mockViewer = new Mock<IViewerService>();

        var vm = new MainViewModel(
            mockDialog.Object, mockPicker.Object,
            mockSettings.Object, mockClipboard.Object,
            mockWorkspace.Object, mockViewer.Object);

        // Act
        vm.IsFileOpen = false;

        // Assert — capture emitted value explicitly; if CanExecute
        // does not emit, canExec stays null and the assertion fails.
        bool? canExec = null;
        vm.OpenCommand.CanExecute.Take(1).Subscribe(v => canExec = v);
        canExec.Should().BeTrue();
    }
}
```

#### D2. Priority Test Cases

1. **Command canExecute:** `OpenCommand_WhenFileNotOpen_CanExecuteIsTrue`
   - *Arrange:* Construct `MainViewModel` with mocks, set `IsFileOpen = false`
   - *Act:* Subscribe to `OpenCommand.CanExecute`
   - *Assert:* Emitted value is `true`

2. **Settings reaction:** `MainViewModel_SettingChangedEmits_HandlerFires`
   - *Arrange:* Create `MainViewModel` with mocks; set up
     `mockSettings.SettingChanged` to return `new Subject<string>()`
   - *Act:* Emit a known setting key via `subject.OnNext("theme")`
   - *Assert:* Verify the ViewModel reacted (e.g., a property changed or
     a method was invoked). This tests the ViewModel's reactive subscription
     to `ISettingsService.SettingChanged` — the testable unit at the VM level.
     Do **not** test `SettingsService` directly here; it wraps the
     `AppSettings.Global` singleton and cannot be isolated without refactoring.

3. **Tree population:** `ArchiveTreeVM_PopulateFromWorkTree_MatchesStructure`
   - *Arrange:* Create mock `IWorkspaceService` returning a known tree
   - *Act:* Call the populate method on `ArchiveTreeViewModel`
   - *Assert:* `Items` collection matches the mock tree structure

4. **File selection:** `FileListVM_GetFileSelection_ReturnsSelectedEntries`
   - *Arrange:* Populate `FileListViewModel` with test items, mark some selected
   - *Act:* Call `GetFileSelection()`
   - *Assert:* Returned list contains exactly the selected items

5. **Dialog ViewModel state:** `EditAttributesVM_InvalidDate_IsValidIsFalse`
   - *Arrange:* Create `EditAttributesViewModel` with test data
   - *Act:* Set date field to an invalid value
   - *Assert:* `IsValid` property is `false`

---

### Workstream E: Performance Audit

#### E1. ObservableCollection Performance

If large archives (10,000+ entries) are slow:
- Consider virtualizing file list with `DataGrid` virtualization settings
- Batch `ObservableCollection` updates using `.AddRange()` extension

> **Note:** Migrating from `ObservableCollection` to `SourceList<T>`
> (DynamicData) is an architectural change that requires rewriting population,
> sort, and filter logic from Phase 5. Do not attempt within this phase's
> performance audit — create a dedicated blueprint if this migration is pursued.

#### E2. ~~Command Subscription Cleanup~~ — *Moved to Workstream A as A5*

See A5 above.

---

## Step-by-Step Instructions

### Step 1: Workstream A (Cleanup) — Required

Complete all A1–A5 items. Build and test.

### Step 2: Workstream B (Multi-Viewer) — Required Verification

Complete B1 assessment. If the current implementation is sufficient,
document the result and skip B2. If B1 identifies gaps, implement B2.
In either case, verify B3 lifecycle requirements. Build and test.

### Step 3: Workstream C (Docking) — Decision Only

Evaluate and document decision. No implementation expected.

### Step 4: Workstream D (Unit Tests) — Recommended

Create test project and priority test cases if time allows.

### Step 5: Workstream E (Performance) — As Needed

Profile with large archives. Optimize only if measurable issues exist.

### Step 6: Final Validation

Complete end-to-end test of the entire application:

1. **All menu commands** — every item in every menu. Refer to
   `MainWindow.axaml` for the complete menu structure (File, Edit, Actions,
   Tools, Help). Verify each `MenuItem` with a bound command is exercised
2. **All dialogs** — open, interact, close (OK and Cancel paths)
3. **File operations** — add, extract, delete, test, move, copy, paste
4. **Multiple file types** — .2mg, .po, .do, .woz, .shk, .zip, .bxy
5. **Edge cases** — read-only files, corrupt archives, empty archives
6. **Window lifecycle** — resize, panel resize, close and reopen,
   settings persistence
7. **macOS native menu** — About, Settings, Quit
8. **Drag and drop** — file drop on main window and launch panel
9. **Multi-viewer** — open multiple FileViewer windows, close source file

---

## Completion Criteria

The MVVM refactor is complete when:

- [ ] Zero `MainController` references remain
- [ ] Zero `RelayCommand` references remain
- [ ] Zero `DataContext = this` in any View
- [ ] All ViewModels extend `ReactiveObject`
  ```
  grep -rn "INotifyPropertyChanged\|: ViewModelBase\|: BindableBase" cp2_avalonia/ViewModels/
  ```
  Should return zero results.
- [ ] All commands are `ReactiveCommand`
  ```
  grep -rn "new RelayCommand\|RelayCommand<" cp2_avalonia/
  ```
  Should return zero results (covered by A1 RelayCommand check).
- [ ] All singleton services (`ISettingsService`, `IClipboardService`, `IViewerService`,
  `IWorkspaceService`) are registered in the DI container. `IDialogService` and
  `IFilePickerService` are constructed manually by `MainViewModel` per
  Pre-Iteration-Notes DI Service Lifetimes
- [ ] All dialogs use `IDialogService`
  ```
  grep -rn "new.*Window()\|new.*Dialog()" cp2_avalonia/ViewModels/
  grep -rn "\.ShowDialog[^A]" cp2_avalonia/ViewModels/
  ```
  The first grep catches direct View instantiation; the second catches
  synchronous `ShowDialog` calls (excluding correct `ShowDialogAsync` uses).
  Both should return zero results.
- [ ] All file pickers use `IFilePickerService`
  ```
  grep -rn "StorageProvider\|OpenFilePickerAsync\|SaveFilePickerAsync" cp2_avalonia/ViewModels/
  ```
  ViewModels should not access `StorageProvider` directly.
- [ ] `MainViewModel` is ≤ 1,200 lines
- [ ] All long-lived ViewModels implement `IActivatableViewModel` (if
  View-attached) or `IDisposable` (if child VM) and dispose subscriptions
  correctly
- [ ] All functional tests pass
- [ ] Settings persist correctly across restarts
