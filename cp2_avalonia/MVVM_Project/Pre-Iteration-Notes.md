# CiderPress II — MVVM Refactor: Pre-Iteration Notes

> **Read this file first** before starting any MVVM iteration blueprint. It contains the
> common context, technology choices, code conventions, and reference information shared
> by all MVVM refactor iterations.

---

## 1. Project Overview

CiderPress II is being refactored from a code-behind + controller architecture to a
proper MVVM architecture using **ReactiveUI** with **Avalonia**. All changes are confined
to the `cp2_avalonia/` project. The supporting libraries (`DiskArc`, `AppCommon`,
`CommonUtil`, `FileConv`, etc.) remain unchanged.

The full analysis and migration plan is in `cp2_avalonia/MVVM_Notes.md`. Refer to it for
the complete inventory of items to migrate, the target architecture diagram, and the
phased approach.

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
| MVVM framework | **ReactiveUI** (`Avalonia.ReactiveUI`) | `ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`, `Interaction<,>` |
| DI container | **Microsoft.Extensions.DependencyInjection** | Start basic; expand as needed for testing |
| Base class for VMs | `ReactiveObject` | Replaces hand-rolled `INotifyPropertyChanged` |
| Commands | `ReactiveCommand<TParam, TResult>` | Replaces `RelayCommand`; built-in async, observable `CanExecute` |
| File reorganization | Incremental | Move files as each phase touches them — keeps diffs manageable |

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
See `cp2_avalonia/guidance/Pre-Iteration-Notes.md` §4 for the full specification.

Key points that apply specifically to the MVVM refactor:

- **License header:** Every new `.cs` file begins with the Apache 2.0 dual-copyright
  header (faddenSoft first, then Lydian Scale Software). For brand-new files with no
  WPF counterpart, use `Copyright 2026 faddenSoft`.

- **Namespace:** `cp2_avalonia` for root, sub-namespaces follow directory names:
  `cp2_avalonia.ViewModels`, `cp2_avalonia.Services`, `cp2_avalonia.Models`.

- **Naming conventions:**
  - Private fields: `mCamelCase`
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

- **Dialog invocation via Interaction:**
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

---

## 5. Phased Migration Strategy (Summary)

Each phase has its own iteration blueprint in this directory. See
`cp2_avalonia/MVVM_Notes.md` §6 for full details.

| Phase | Scope | Key Changes |
|---|---|---|
| 0 — Preparation | Infrastructure, no behavior change | Add ReactiveUI + DI packages, wire up in App, extract inner classes, create service interfaces |
| 1 — MainViewModel Core | ~100 properties + AXAML rebinding | Create `MainViewModel : ReactiveObject`, move all bindable properties |
| 2 — Commands | ~50 commands | Convert `RelayCommand` → `ReactiveCommand`, eliminate `RefreshAllCommandStates()` |
| 3 — Dissolve Controller | Merge ~1,800 lines of logic | Merge controller into VM + services, delete `MainController*.cs` |
| 4 — Dialog VMs | ~20 dialogs | Create dialog ViewModels, register in `DialogService` |
| 5 — Sub-VMs & Panel Modularity | Decompose large VM | Extract self-contained child VMs, retire `RelayCommand.cs` |
| 6 — Multi-Viewer & Future (optional) | Modeless FileViewer, docking eval | Multiple concurrent viewers, docking framework evaluation |

**Each phase must be completed and tested before moving to the next.** The
application must remain fully functional at every step.

---

## 6. Validation Checklist (Per Iteration)

After completing each iteration, verify:

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

## 7. NuGet Packages to Add (Phase 0)

| Package | Purpose |
|---|---|
| `Avalonia.ReactiveUI` | ReactiveUI integration for Avalonia (pulls in `ReactiveUI` transitively) |
| `Microsoft.Extensions.DependencyInjection` | Service registration and resolution |

---

## 8. Decisions & Open Items

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

## 9. References

- Full MVVM analysis & plan: `cp2_avalonia/MVVM_Notes.md`
- Original porting conventions: `cp2_avalonia/guidance/Pre-Iteration-Notes.md`
- Original porting overview: `cp2_avalonia/guidance/PORTING_OVERVIEW.md`
- Known issues / future rework: `cp2_avalonia/KNOWN_ISSUES.md`
- ReactiveUI docs: https://www.reactiveui.net/docs/
- ReactiveUI + Avalonia: https://www.reactiveui.net/docs/getting-started/installation/avalonia
- Avalonia MVVM pattern: https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern
- MS DI: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection
