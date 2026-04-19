# CiderPress II — MVVM Refactor: Pre-Iteration Notes

> **Read this file first** before starting any MVVM iteration blueprint. It contains the
> common context, technology choices, code conventions, and reference information shared
> by all MVVM refactor iterations.

> **Agent priming:** You are implementing an MVVM refactor of a desktop application.
> The application is CiderPress II, an Apple II disk image and file archive utility.
> You are working **only** in the `cp2_avalonia/` project (Avalonia UI, .NET 8, C#).
> The supporting libraries (`DiskArc`, `AppCommon`, `CommonUtil`, `FileConv`) are
> unchanged and consumed through their existing public APIs.
>
> **You must NOT run any git commands** (`git add`, `git commit`, `git push`,
> `git checkout`, `git merge`, `git reset`, etc.). The user handles all version control.
> Your role is limited to editing source files and running build/test commands.
>
> Before making changes, **read the source files** you intend to modify. Do not guess
> at existing code structure — verify it. Follow the coding conventions in §4 exactly.

---

## 1. Project Overview

CiderPress II is being refactored from a code-behind + controller architecture to a
proper MVVM architecture using **ReactiveUI** with **Avalonia**. All changes are confined
to the `cp2_avalonia/` project. The supporting libraries (`DiskArc`, `AppCommon`,
`CommonUtil`, `FileConv`, etc.) remain unchanged.

The full analysis and migration plan is in `cp2_avalonia/MVVM_Project/MVVM_Notes.md`.
Refer to it for the complete inventory of items to migrate, the target architecture
diagram, and the phased approach.

All work happens on the **`avalonia_mvvm`** git branch.

### 1.1 Future Goals This Refactor Enables

The MVVM refactor is the **foundational prerequisite** for several future goals
documented in `KNOWN_ISSUES.md` § "Future Major Rework":

- **Multiple concurrent viewers/editors** (multi-document workflow)
- **Dynamic windowing/paneling** (VS/Code-style docking, splits, tabs via Avalonia Dock)
- **Single-process, multi-window architecture** ("File → New Window",
  shared workspace, coordinated clipboard/undo)
- **Unit testing** with xUnit for ViewModel and service code

Design decisions in every iteration should **accommodate** these futures. The
practical test: "Would this still work if there were two FileViewers open,
or if this panel were in a separate docked window?"

---

## 2. Technology Choices

| Decision | Choice | Notes |
|---|---|---|
| MVVM framework | **ReactiveUI** (`ReactiveUI.Avalonia`) | `ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`, `Interaction<,>` |
| DI container | **Microsoft.Extensions.DependencyInjection** | Start basic; expand as needed for testing |
| Base class for VMs | `ReactiveObject` | Replaces hand-rolled `INotifyPropertyChanged` |
| Commands | `ReactiveCommand<TParam, TResult>` | Replaces `RelayCommand`; built-in async, observable `CanExecute` |
| File reorganization | Incremental | Move files as each phase touches them — keeps diffs manageable |

**`Locator` vs MS DI:** ReactiveUI ships with `Splat.Locator`, a built-in
service locator. This project uses **Microsoft.Extensions.DependencyInjection**
as the primary DI container. Do **not** register application services in
`Locator.Current` — use `IServiceProvider` / `IServiceCollection` exclusively.
`Locator` may still be used internally by ReactiveUI for its own registrations
(view-model mapping, logging); do not fight that. If ReactiveUI requires a
registration in `Locator`, add it in `App.axaml.cs` startup alongside the
MS DI container setup, and document it clearly.

---

## 3. Architecture: Before & After

### Before (Current)

```
MainWindow.axaml.cs (DataContext=this, INotifyPropertyChanged, ~50 commands, ~60+ properties)
    ↕ circular reference
MainController.cs + MainController_Panels.cs (mMainWin back-reference, all logic)
```

### After (Target)

```
Views (thin code-behind, no INotifyPropertyChanged)
    ↓ DataContext binding
ViewModels (ReactiveObject, ReactiveCommand, WhenAnyValue)
    ↓ constructor injection
Services (IDialogService, IFilePickerService, ISettingsService, etc.)
    ↓ uses
Existing Libraries (DiskArc, AppCommon, CommonUtil, FileConv — unchanged)
```

---

## 4. Code Conventions & Style

Follow the existing faddenSoft coding style used throughout CiderPress II.
The Avalonia porting guide at `cp2_avalonia/guidance/Pre-Iteration-Notes.md`
contains the base faddenSoft coding style (brace placement, field naming, comment
style, etc.). This section **augments** that guide with MVVM-specific rules — read
both. If a rule here conflicts with the porting guide, this document takes precedence.

Key points that apply specifically to the MVVM refactor:

- **License header:** Every new `.cs` file begins with the Apache 2.0 dual-copyright
  header (faddenSoft first, then Lydian Scale Software). For brand-new files with no
  WPF counterpart, use the same double copyright pattern, with `Copyright 2026 faddenSoft` followed by `Copyright 2026 Lydian Scale Software`.

- **Namespace:** `cp2_avalonia` for root, sub-namespaces follow directory names:
  `cp2_avalonia.ViewModels`, `cp2_avalonia.Services`, `cp2_avalonia.Models`.

- **Naming conventions:**
  - Private fields: `mCamelCase`
  - `IXxxService`-typed `readonly` fields set via constructor injection:
    `_camelCase` (e.g., `_dialogService`, `_settingsService`). This exception
    applies only to DI service interfaces — all other constructor-injected
    or `readonly` fields (e.g., `Formatter`, `AppHook`, domain objects) use
    the standard `mCamelCase` prefix.
  - Constants: `UPPER_SNAKE_CASE`
  - Properties: `PascalCase`
  - Methods: `PascalCase`
  - Local variables: `camelCase`
  - Boolean properties for UI binding: `IsXxxEnabled`, `ShowXxxYyy`

- **Braces & formatting:**
  - Opening brace on same line as declaration (K&R style)
  - 4-space indentation
  - XML doc comments (`/// <summary>`) on public/internal members

### ReactiveUI-Specific Conventions

- **Property change notification:** Use `this.RaiseAndSetIfChanged(ref field, value)`
  from `ReactiveObject` instead of the hand-rolled `OnPropertyChanged()` pattern.
  Always use the explicit backing-field pattern shown below. Do **not** use the
  `[Reactive]` attribute (`ReactiveUI.Fody`) — it requires an IL-weaving package
  with known build fragility and makes setter behavior invisible in source code.

- **Reactive properties pattern:**
  ```csharp
  private bool mIsFileOpen;
  public bool IsFileOpen {
      get => mIsFileOpen;
      set => this.RaiseAndSetIfChanged(ref mIsFileOpen, value);
  }
  ```

- **Command creation:**
  ```csharp
  // Simple synchronous command with canExecute
  var canOpen = this.WhenAnyValue(x => x.IsFileOpen, open => !open);
  OpenCommand = ReactiveCommand.Create(() => DoOpen(), canOpen);

  // Async command
  ExtractCommand = ReactiveCommand.CreateFromTask(
      () => ExtractFilesAsync(),
      this.WhenAnyValue(x => x.IsFileOpen, x => x.AreFileEntriesSelected,
          (open, sel) => open && sel));
  ```

- **Derived state with `ObservableAsPropertyHelper`:**
  ```csharp
  private readonly ObservableAsPropertyHelper<string> mWindowTitle;
  public string WindowTitle => mWindowTitle.Value;

  // In constructor:
  mWindowTitle = this.WhenAnyValue(x => x.WorkPathName, x => x.IsReadOnly,
      (path, ro) => FormatTitle(path, ro))
      .ToProperty(this, x => x.WindowTitle);
  ```
  If the upstream observable can fire on a background thread, add
  `.ObserveOn(RxApp.MainThreadScheduler)` before `.ToProperty()`:
  ```csharp
  mSomeProperty = someBackgroundObservable
      .ObserveOn(RxApp.MainThreadScheduler)
      .ToProperty(this, x => x.SomeProperty);
  ```

- **Dialog invocation — `IDialogService` vs `Interaction<,>`:**

  **Default: use `IDialogService`** for all modal and modeless dialogs. The
  ViewModel→View mapping is registered at startup and resolved automatically:
  ```csharp
  // In ViewModel:
  var vm = new EditSectorViewModel(chunks, mode, writeFunc, formatter);
  var result = await _dialogService.ShowDialogAsync(vm);
  ```

  Each dialog ViewModel **must** have a ViewModel→View mapping registered in
  `App.axaml.cs` (or wherever `DialogService` is configured) before
  `ShowDialogAsync` can resolve it. Registration form:
  `dialogService.Register<EditSectorViewModel, EditSector>()`
  (exact API finalized when `DialogService` is implemented in Phase 3A).

  **Exception: use `Interaction<TInput, TOutput>`** only when a dialog cannot
  be registered at DI startup (e.g., context-sensitive dialogs with no fixed
  owner, or dialogs that need View-side setup beyond `DataContext` assignment).
  In all other cases, prefer `IDialogService`.

  `Interaction` example (for the rare exception case):
  ```csharp
  // In ViewModel:
  public Interaction<EditSectorViewModel, bool?> ShowEditSector { get; } = new();

  // In View code-behind:
  this.WhenActivated(d => {
      d(ViewModel!.ShowEditSector.RegisterHandler(async interaction => {
          var dialog = new EditSector { DataContext = interaction.Input };
          var result = await dialog.ShowDialog<bool?>(this);
          interaction.SetOutput(result);
      }));
  });
  ```

- **`WhenActivated` pattern:**

  `WhenActivated` manages subscription lifetimes — subscriptions registered
  inside it are disposed when the View/ViewModel is deactivated.

  - **Views:** Inherit from `ReactiveWindow<TViewModel>` (or
    `ReactiveUserControl<TViewModel>`) to get `this.WhenActivated(d => {...})`.
    The parameter `d` is an `Action<IDisposable>` — pass it each subscription's
    `IDisposable` to auto-dispose on deactivation.
  - **ViewModels:** Implement `IActivatableViewModel` and add
    `public ViewModelActivator Activator { get; } = new();` to use
    `this.WhenActivated(disposables => {...})` with the `DisposeWith` extension.
  - **Dialog ViewModels** with short lifecycles (created, shown, closed) do not
    need `IActivatableViewModel` — their subscriptions die with the window.
    Use `WhenActivated` in dialog VMs only if they create long-lived subscriptions
    that must be cleaned up explicitly.
  - **Child ViewModels** (e.g., `FileListViewModel`, `ArchiveTreeViewModel`) are
    long-lived sub-objects of `MainViewModel`. They should implement `IDisposable`
    if they create subscriptions in their constructor. `MainViewModel` disposes
    each child VM when replacing or tearing down (e.g., after
    `IWorkspaceService.Close()` rebuilds child VMs). This prevents subscription
    leaks where an old child VM continues receiving notifications from the parent.

  **Enforcement rule:** These patterns must be applied at the point of
  creation — when a ViewModel or subscription is first introduced — not
  deferred to a later cleanup pass. Any code that creates a `WhenAnyValue`,
  `Subscribe`, `ObservableAsPropertyHelper`, or `Interaction.RegisterHandler`
  call must wire up the corresponding disposal (`DisposeWith`, `IDisposable`,
  or `WhenActivated`) in the same step.

- **`IDialogHost` — owner window resolution:**

  `IDialogHost` (implemented by `MainWindow`) provides the owner `Window`
  reference needed by `IDialogService` and `IFilePickerService` for
  `ShowDialog()` and `StorageProvider` calls. Never inject `MainWindow`
  directly into services or ViewModels — use `IDialogHost` instead.
  See MVVM_Notes.md §7.15.

### DI Service Lifetimes

Not every service is resolved from the DI container in the same way:

| Service | Lifetime | Registration |
|---|---|---|
| `ISettingsService` | Singleton | Registered in container, resolved normally |
| `IClipboardService` | Singleton | Registered in container, resolved normally |
| `IViewerService` | Singleton | Registered in container, resolved normally |
| `IWorkspaceService` | Singleton | Registered in container, resolved normally |
| `IDialogService` | **Manual (not container-managed)** | Each `MainViewModel` constructs its own instance, passing its `IDialogHost`. Do not register in the container. |
| `IFilePickerService` | **Manual (not container-managed)** | Same as `IDialogService` — constructed by `MainViewModel` with `IDialogHost`. |

`IDialogService` and `IFilePickerService` require an owner `Window` reference
(`IDialogHost`), which varies per top-level window. Registering them as
container singletons would bind them to a single window and break the
multi-window future goal. See MVVM_Notes.md §7.19.

### File Placement Rules

| File Type | Folder | Namespace |
|---|---|---|
| ViewModels | `ViewModels/` | `cp2_avalonia.ViewModels` |
| Service interfaces & implementations | `Services/` | `cp2_avalonia.Services` |
| Model / data classes (extracted inner classes) | `Models/` | `cp2_avalonia.Models` |
| View code-behind & AXAML | Root or existing subfolder | `cp2_avalonia` (or existing) |

### Critical Architectural Constraints

- **`FileViewerViewModel` must be non-singleton and fully independent per
  instance.** Each instantiation is a separate viewer with no shared mutable
  state. Never store `FileViewerViewModel` state in static fields or DI
  singletons. `FileViewerViewModel` **must implement `IDisposable`** because
  it holds file data/handles and must self-deregister from `IViewerService`.
  Each instance **self-registers** with `IViewerService` on construction;
  `Dispose()` calls `IViewerService.Unregister(this)` and releases all file
  data and handles. Do not track active viewers in `MainViewModel` or any
  other class — `IViewerService` is the single source of truth for the active
  viewer registry. This is the prerequisite for multiple concurrent viewers
  (see MVVM_Notes.md §2.3, §4.6, §7.10).

- **`ISettingsService` must expose a change-notification mechanism** (e.g.,
  `IObservable<string> SettingChanged`) so ViewModels can react to settings
  written by other ViewModels (e.g., `EditAppSettingsViewModel` changes the
  theme → `MainViewModel` applies it). ViewModels subscribe to this observable
  rather than polling `AppSettings.Global`.

- **View code-behind may continue using `AppSettings.Global` directly** for
  pure-view concerns (window placement, column widths, etc.) — these never
  move to ViewModels. All ViewModel code uses `ISettingsService` exclusively.
  See MVVM_Notes.md §7.17.

---

## 5. MVVM-Specific Naming Conventions

| Item | Convention | Example |
|---|---|---|
| `ReactiveCommand` properties | `VerbNounCommand` | `OpenFileCommand`, `DeleteFilesCommand` |
| `Interaction` properties | `ShowDialogName` | `ShowEditSector`, `ShowFileViewer` |
| ViewModel classes | `NounViewModel` | `MainViewModel`, `FileListViewModel` |
| Service interfaces | `INounService` | `IDialogService`, `ISettingsService` |
| Child VM properties on parent VM | `Noun` (drop `ViewModel`) | `ArchiveTree`, `FileList`, `StatusBar` |
| Boolean binding props | `IsXxxEnabled`, `ShowXxxYyy` | `IsFileOpen`, `ShowOptionsPanel` |

---

## 6. Error Handling Pattern

Errors during command execution use a consistent two-tier approach:

1. **`ReactiveCommand.ThrownExceptions`** — subscribe on every command to
   catch unhandled exceptions and show them via `IDialogService.ShowMessageAsync()`.
2. **Explicit error results** — operations with expected failure modes return
   result objects or throw domain exceptions, caught in the command handler.

```csharp
// text (body), caption (title)
SomeCommand.ThrownExceptions.Subscribe(ex =>
    _ = _dialogService.ShowMessageAsync(ex.Message, "Error"));
```

> **Note on `_ =`:** `Subscribe` takes a synchronous callback, so we cannot
> `await` the async dialog call. The explicit discard (`_ =`) suppresses
> compiler warning CS4014 and documents intent. The dialog will still display;
> if it throws internally, the exception surfaces through the TaskScheduler
> unobserved-exception handler.

In ViewModels that implement `IActivatableViewModel`, dispose these subscriptions
with `WhenActivated` + `DisposeWith`:
```csharp
this.WhenActivated(disposables => {
    SomeCommand.ThrownExceptions
        .Subscribe(ex => _ = _dialogService.ShowMessageAsync(ex.Message, "Error"))
        .DisposeWith(disposables);
});
```

**Dialog ViewModels** (which do not implement `IActivatableViewModel`) use the
naked `Subscribe` pattern (without `DisposeWith`). This is safe because the VM
is garbage-collected when the dialog window closes, which drops the subscription
automatically. Do not add `IActivatableViewModel` to dialog VMs purely for
disposal purposes.

---

## 7. Threading Model

- **UI-bound VM properties** must be set on the UI thread.
- **`ReactiveCommand.CreateFromTask()`** automatically marshals results to the
  UI thread.
- **Background workers** (`IWorker` implementations) report progress via
  `Dispatcher.UIThread.InvokeAsync(...)` or ReactiveUI's
  `ObserveOn(RxApp.MainThreadScheduler)`.
- **Reactive pipelines** (`WhenAnyValue`, `Select`, `Where`, etc.) that
  originate from a background observable and feed a UI-bound property must
  include `.ObserveOn(RxApp.MainThreadScheduler)` before the terminal
  operator (`.ToProperty()`, `.BindTo()`, `.Subscribe()`). Pipelines that
  originate from UI properties (which already fire on the UI thread) do not
  need this.

---

## 8. Version Control Policy

**The user (not the agent) performs all git operations.** Agents must never run
`git add`, `git commit`, `git push`, `git checkout`, `git merge`, or any other
git command. The agent's role is limited to editing files and running build/test
commands (`dotnet build`, `dotnet run`, etc.).

---

## 9. Phased Migration Strategy (Summary)

Each phase has its own iteration blueprint in this directory. See
`cp2_avalonia/MVVM_Project/MVVM_Notes.md` §6 for full details. Some phases are
split into sub-iterations (e.g., 1A/1B, 3A/3B, 4A/4B) for manageability.

| Phase | Blueprint(s) | Scope |
|---|---|---|
| 0 | `Iteration_0_Blueprint.md` | Infrastructure, no behavior change |
| 1A | `Iteration_1A_Blueprint.md` | Create MainViewModel, move properties |
| 1B | `Iteration_1B_Blueprint.md` | Update AXAML bindings, wire interim controller |
| 2 | `Iteration_2_Blueprint.md` | Convert all commands to ReactiveCommand |
| 3A | `Iteration_3A_Blueprint.md` | Create services (WorkspaceService, etc.) |
| 3B | `Iteration_3B_Blueprint.md` | Merge remaining controller logic, delete controller |
| 4A | `Iteration_4A_Blueprint.md` | Dialog VMs: complex dialogs (EditSector, FileViewer, EditAttributes, CreateDiskImage, SaveAsDisk, TestManager, BulkCompress) |
| 4B | `Iteration_4B_Blueprint.md` | Dialog VMs: remaining dialogs |
| 5 | `Iteration_5_Blueprint.md` | Sub-ViewModels & panel modularity |
| 6 | `Iteration_6_Blueprint.md` | Multi-viewer & future preparation (optional) |

**Each iteration must be completed and tested before moving to the next.** The
application must remain fully functional at every step.

---

## 10. Validation Checklist (Per Iteration)

This is the **minimum bar** — every iteration must pass all items below.
Individual iteration blueprints add phase-specific checks on top of this list.

- [ ] Solution builds without errors or new warnings
- [ ] Application launches and displays correctly
- [ ] All menu items, toolbar buttons, and keyboard shortcuts work
- [ ] File open/close lifecycle works
- [ ] Archive/directory tree navigation works
- [ ] File list population and selection works
- [ ] All Actions menu operations work (add, extract, delete, move, etc.)
- [ ] Dialog windows open and close correctly
- [ ] Settings persist across sessions
- [ ] No regressions in existing functionality

---

## 11. NuGet Packages (Added in Phase 0)

| Package | Purpose |
|---|---|
| `ReactiveUI.Avalonia` | ReactiveUI integration for Avalonia (pulls in `ReactiveUI` transitively) |
| `Microsoft.Extensions.DependencyInjection` | Service registration and resolution |

---

## 12. Decisions & Open Items

### Resolved

1. **ArchiveTreeItem / DirectoryTreeItem identity** → **Keep as single classes;
   upgrade to `ReactiveObject`; extract UI-coupled static methods.** They are
   reactive tree-node models placed in `Models/`. Pure data methods stay;
   static methods taking `MainWindow` / `TreeView` move to ViewModels; icon
   resolution moves out of constructors. (See `MVVM_Notes.md` §3.4)

2. **Column sort state ownership** → **ViewModel owns sort state.** The View
   forwards column-click events and applies visual indicators only. Sort column
   is represented as a `ColumnId` enum (no `DataGridColumn` reference in VM).
   `mSuppressSort` stays in View code-behind. (See `MVVM_Notes.md` §7.4)

### Deferred

3. **Docking framework timing** — Deferred until after MVVM Phases 0–5 are
   complete and stable. The self-contained child VM pattern ensures a docking
   framework can be introduced without re-architecting.
   (See `MVVM_Notes.md` §7.11)

4. **FileViewer side panel / toolbar** — Deferred. Part of a broader FileViewer
   redesign. The MVVM refactor ensures `FileViewerViewModel` is flexible enough
   to accommodate it later. (See `MVVM_Notes.md` §7.10)

### Resolved (details to be refined during implementation)

5. **Multi-viewer lifecycle management** → **`IViewerService` (DI singleton)
   owns the viewer registry.** Viewers self-register/deregister. The service
   provides `CloseViewersForSource(workPathName)` for cleanup when a file is
   closed. Global scope, source-file tagging for scoped cleanup. Watch for
   gotchas documented in `MVVM_Notes.md` §7.10 (stale viewers after file
   close, disposal race conditions, orphaned registrations, re-open of same
   file, cross-window ownership). (See `MVVM_Notes.md` §4.6, §7.10)

---

## 13. References

- Full MVVM analysis & plan: `cp2_avalonia/MVVM_Project/MVVM_Notes.md`
- Original porting conventions: `cp2_avalonia/guidance/Pre-Iteration-Notes.md`
- Original porting overview: `cp2_avalonia/guidance/PORTING_OVERVIEW.md`
- Known issues / future rework: `cp2_avalonia/KNOWN_ISSUES.md`
- ReactiveUI docs: https://www.reactiveui.net/docs/
- ReactiveUI + Avalonia: https://www.reactiveui.net/docs/getting-started/installation/avalonia
- Avalonia MVVM pattern: https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern
- MS DI: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection
