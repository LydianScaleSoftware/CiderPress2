# Iteration 6 Developer Manual: Polish, Multi-Viewer Evaluation & Optional Enhancements

> **Iteration identifier:** 6
>
> **Prerequisites:** Iteration 5 is complete. All child ViewModels have been
> extracted, `MainViewModel` is a manageable coordinator, the application
> builds, runs, and passes all functional tests.
>
> **Reference documents:**
> - `cp2_avalonia/MVVM_Project/Iteration_6_Blueprint.md` (the blueprint this manual expands)
> - `cp2_avalonia/MVVM_Project/MVVM_Notes.md` (the authoritative design document)
> - `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` (conventions and coding rules)

---

## How to Use This Manual

This manual is organized around the blueprint's workstreams. Each section
explains the *why* before the *how*, so you understand the MVVM reasoning
behind every change. If you are new to ReactiveUI and MVVM, read the
explanations carefully — they introduce concepts and patterns as they
become relevant.

Phase 6 is different from Phases 0–5. Those earlier phases built the MVVM
architecture. This phase **polishes** it: cleaning up leftover artifacts,
verifying correctness, and evaluating optional enhancements. The core MVVM
refactor is already complete. Think of Phase 6 as the quality pass that
ensures everything is tight, well-tested, and ready for future development.

---

## Workstream A: Cleanup & Technical Debt

---

### A1. Remove Dead Code

#### What we are going to accomplish

During Phases 0–5, the codebase transitioned from the old code-behind +
controller architecture to the new MVVM architecture. That transition was
incremental — old code was redirected, bypassed, and replaced piece by piece.
It is very common for artifacts of the old architecture to survive the
transition: a stale `using` statement, a comment referencing a deleted class,
or an entire file that nothing references anymore.

This step is a **sweep** to find and remove those artifacts. Specifically,
we are looking for four categories of dead code:

1. **`MainController` references** — `MainController` was the central business
   logic class in the old architecture (see MVVM_Notes §1.1). It was dissolved
   in Phase 3B; all its logic was merged into `MainViewModel` and services.
   Any remaining references are stale.

2. **`RelayCommand` references** — `RelayCommand` was the old command
   implementation. All commands were converted to `ReactiveCommand` in Phase 2.
   The file `Common/RelayCommand.cs` should have been retired in Phase 4B.
   If any references remain, they indicate an incomplete conversion.

3. **`DataContext = this`** — In the old architecture, each view set
   `DataContext = this`, meaning the view *was* its own ViewModel. This is the
   anti-pattern that the entire MVVM refactor eliminates. Every view should now
   receive its DataContext from a separate ViewModel. Zero results expected.

4. **`INotifyPropertyChanged` in ViewModels** — All ViewModels now inherit from
   `ReactiveObject`, which provides property change notification via
   `RaiseAndSetIfChanged`. Hand-rolled `INotifyPropertyChanged` implementations
   in the `ViewModels/` folder are stale.

#### To do that, follow these steps

1. Open a terminal at the project root.

2. **Search for `MainController`:**
   ```
   grep -rn "MainController" cp2_avalonia/
   ```
   - If results appear, examine each one. Delete stale `using` statements,
     comments, and stubs.
   - Do **not** touch files outside `cp2_avalonia/`.
   - If you find a *functional* reference (something that would cause a build
     error if removed), that indicates Phase 3B was not fully applied — resolve
     it before continuing.

3. **Search for `RelayCommand`:**
   ```
   grep -rn "RelayCommand" cp2_avalonia/
   ```
   - The only acceptable result is the file `Common/RelayCommand.cs` itself
     (which should already be deleted). If other files reference it, the command
     conversion is incomplete.
   - Delete `Common/RelayCommand.cs` if it still exists and no references
     remain.

4. **Search for `DataContext = this`:**
   ```
   grep -rn "DataContext = this" cp2_avalonia/
   ```
   - Should return zero results. If it doesn't, the affected view still uses the
     old pattern — its ViewModel was not created or wired correctly.

5. **Search for `INotifyPropertyChanged` in ViewModels:**
   ```
   grep -rn "INotifyPropertyChanged" cp2_avalonia/ViewModels/
   ```
   - Should return zero results. All ViewModels use `ReactiveObject` (which
     implements `INotifyPropertyChanged` internally, but you should never see
     an explicit `INotifyPropertyChanged` declaration in your ViewModel code).

6. **Build the project** to confirm nothing broke:
   ```
   dotnet build cp2_avalonia/
   ```

#### Now that those are done, here's what changed

- Stale references to the old architecture were removed.
- The codebase is clean of legacy artifacts.
- No behavior changed — this is purely a housekeeping step.
- This makes the codebase easier to navigate for future developers who won't
  encounter confusing references to deleted classes.

---

### A2. Consistent Error Handling

#### What we are going to accomplish

In ReactiveUI, every `ReactiveCommand` has a built-in observable called
`ThrownExceptions`. Here's the concept: when a command's execute logic throws
an exception, ReactiveUI does **not** let it propagate up the call stack and
crash the application. Instead, it catches the exception and pushes it onto
the `ThrownExceptions` observable. If nothing is subscribed to that observable,
the exception is silently swallowed — the user sees nothing, and you get a
silent failure that is very hard to debug.

That's why **every** `ReactiveCommand` must have a `ThrownExceptions`
subscription. This was supposed to happen in Phase 2 when commands were first
converted. This step is a **verification and remediation pass** — you are
confirming that every command already has a subscription, and adding any that
are missing.

The established pattern (from Pre-Iteration-Notes §6) is:

```csharp
SomeCommand.ThrownExceptions.Subscribe(ex =>
    _ = _dialogService.ShowMessageAsync(ex.Message, "Error"));
```

**Why `_ =` instead of `await`?** The `Subscribe` callback is synchronous
(it takes an `Action<Exception>`, not a `Func<Exception, Task>`). You cannot
use `await` inside it. The `_ =` discard tells the compiler "I know this
returns a Task and I'm intentionally not awaiting it." The dialog will still
display correctly — it just runs fire-and-forget. This suppresses compiler
warning CS4014.

**Do NOT** write `async ex => { await _dialogService.ShowMessageAsync(...); }`
— this creates an `async void` lambda, which has different exception-propagation
semantics and diverges from the project convention.

#### To do that, follow these steps

1. **Enumerate all `ReactiveCommand` instances:**
   ```
   grep -rn "ReactiveCommand" cp2_avalonia/ViewModels/
   ```

2. For each file in the results, open the file and locate every
   `ReactiveCommand` property.

3. For each command, search the same file for a corresponding
   `ThrownExceptions.Subscribe` call. The subscription should reference the
   same command name.

4. If a command is **missing** its `ThrownExceptions` subscription, add one.
   The correct pattern is:
   ```csharp
   SomeCommand.ThrownExceptions.Subscribe(ex =>
       _ = _dialogService.ShowMessageAsync(ex.Message, "Error"));
   ```
   Place it immediately after the command's creation, in the same constructor
   or `WhenActivated` block.

5. **For ViewModels that implement `IActivatableViewModel`** (like
   `MainViewModel`), the subscription should be inside `WhenActivated` and
   chained with `.DisposeWith(disposables)`:
   ```csharp
   this.WhenActivated(disposables => {
       SomeCommand.ThrownExceptions
           .Subscribe(ex => _ = _dialogService.ShowMessageAsync(ex.Message, "Error"))
           .DisposeWith(disposables);
   });
   ```

6. **For dialog ViewModels** (which do NOT use `IActivatableViewModel`), the
   naked `Subscribe` pattern (without `DisposeWith`) is correct. The VM is
   garbage-collected when the dialog closes, which drops the subscription
   automatically.

7. Build and verify:
   ```
   dotnet build cp2_avalonia/
   ```

#### Now that those are done, here's what changed

- Every `ReactiveCommand` in the application now has a `ThrownExceptions`
  subscription.
- Unhandled exceptions in command execution will display a user-visible error
  dialog instead of being silently swallowed.
- No functional behavior changed for commands that already had subscriptions.
- This completes the error-handling safety net across the entire application.

---

### A3. Finalize Message Box Dialog

#### What we are going to accomplish

Since Phase 3A, `IDialogService` has included a `ShowMessageAsync` method for
displaying simple message boxes (alerts, confirmations, yes/no questions). Up
to now, this has been a placeholder implementation — possibly just logging the
message or showing a basic system dialog. This step replaces that placeholder
with a fully custom message box that gives us control over styling, button
layout, and icon display.

**Why a custom message box?** Platform-native message boxes vary widely across
operating systems (Windows, macOS, Linux). A custom implementation ensures
consistent appearance and behavior. It also follows the same ViewModel→View
pattern as every other dialog in the application, reinforcing the MVVM
architecture.

This involves creating two new things:
- **`MessageBoxViewModel`** — a ViewModel that holds the message text, caption,
  which buttons to show, which icon to display, and a command for each button.
- **`MessageBoxView.axaml`** — a simple dialog window that binds to the
  ViewModel and renders the message, icon, and button panel.

Then, `DialogService.ShowMessageAsync` is updated to create a
`MessageBoxViewModel`, show it via `ShowDialogAsync`, and return the result.

**Key MVVM concepts at play:**
- The ViewModel owns all state (message, buttons, result).
- The View is a declarative shell that binds to the ViewModel.
- The `DialogService` orchestrates the ViewModel→View lifecycle using its
  registration dictionary (see MVVM_Notes §7.14).

#### To do that, follow these steps

1. **Verify that the enum types already exist.** Open
   `cp2_avalonia/Services/IDialogService.cs` and search for `MBButton`,
   `MBIcon`, and `MBResult`. These enums should have been defined in Phase 3A.
   - `MBButton` — values like `OK`, `OKCancel`, `YesNo`, `YesNoCancel`
   - `MBIcon` — values like `None`, `Info`, `Warning`, `Error`
   - `MBResult` — values like `OK`, `Cancel`, `Yes`, `No`

   If they are **present**, skip to step 2. If they are **missing**, add them
   to `IDialogService.cs` now (they are part of the service's public contract).

2. **Verify the `ShowMessageAsync` signature.** It should match:
   ```csharp
   Task<MBResult> ShowMessageAsync(
       string message, string caption,
       MBButton buttons = MBButton.OK,
       MBIcon icon = MBIcon.None);
   ```
   If it doesn't match (e.g., it has a simpler signature from Phase 3A), update
   it now. Update both the interface and the implementation.

3. **Create `MessageBoxViewModel`.** Create a new file at
   `cp2_avalonia/ViewModels/MessageBoxViewModel.cs`. This is a dialog ViewModel,
   so it inherits from `ReactiveObject` but does **not** need
   `IActivatableViewModel` (its lifecycle is short — created, shown, closed).

   The ViewModel should have:
   - Properties: `Message` (string), `Caption` (string), `Buttons` (MBButton),
     `Icon` (MBIcon), `Result` (MBResult)
   - Computed boolean properties that the View binds to for button visibility:
     `ShowOkButton`, `ShowCancelButton`, `ShowYesButton`, `ShowNoButton`
     (derived from the `Buttons` enum value)
   - A `ReactiveCommand` for each button (e.g., `OkCommand`, `CancelCommand`,
     `YesCommand`, `NoCommand`) that sets `Result` and closes the dialog
   - The close action uses the dialog's `Close()` method via an
     `Interaction<Unit, Unit>` or by having the View subscribe to a
     `CloseRequested` observable

4. **Create `MessageBoxView.axaml`.** Place it in the root of `cp2_avalonia/`
   (alongside existing dialogs like `EditSector.axaml`, `CreateDiskImage.axaml`).

   The AXAML should include:
   - A `TextBlock` bound to `{Binding Message}`
   - An icon area (bound to `{Binding Icon}`, possibly using a converter or
     DataTemplate to map enum values to images)
   - A button panel with buttons bound to the commands, with visibility
     controlled by the `ShowXxxButton` properties
   - The window title bound to `{Binding Caption}`

   The code-behind (`MessageBoxView.axaml.cs`) should be minimal: just
   `InitializeComponent()` and any close-handling wiring.

5. **Register the ViewModel→View mapping.** Find where `DialogService`
   registrations are set up (likely in `MainViewModel`'s constructor or the
   factory method that creates the `DialogService` for each `MainViewModel`).
   Add:
   ```csharp
   dialogService.Register<MessageBoxViewModel, MessageBoxView>();
   ```
   This must be registered **before** any command can call `ShowMessageAsync`,
   because `ShowMessageAsync` will internally create a `MessageBoxViewModel`
   and call `ShowDialogAsync`, which uses the registration to find the View.

6. **Implement `ShowMessageAsync` in `DialogService`.** Replace the placeholder
   with:
   ```csharp
   public async Task<MBResult> ShowMessageAsync(
       string message, string caption,
       MBButton buttons = MBButton.OK,
       MBIcon icon = MBIcon.None) {
       var vm = new MessageBoxViewModel(message, caption, buttons, icon);
       await ShowDialogAsync(vm);
       return vm.Result;
   }
   ```

7. **Build and test:**
   ```
   dotnet build cp2_avalonia/
   ```
   Then run the application and trigger an error condition that calls
   `ShowMessageAsync` — verify the custom dialog appears with the correct
   message, icon, and buttons.

#### Now that those are done, here's what changed

- **New files:** `ViewModels/MessageBoxViewModel.cs`, `MessageBoxView.axaml`,
  `MessageBoxView.axaml.cs`
- **Modified files:** `Services/DialogService.cs` (replaced placeholder
  `ShowMessageAsync` implementation)
- The application now shows a consistent, custom-styled message box for all
  error messages, confirmations, and alerts.
- All existing `ThrownExceptions` subscriptions and explicit
  `ShowMessageAsync` calls benefit automatically — they were already calling
  the interface method; now the implementation behind it is complete.
- This is a prerequisite for the A2 error handling verification — if commands
  surface exceptions, users will now see a proper dialog.

---

### A4. Settings Persistence Audit

#### What we are going to accomplish

Throughout the MVVM refactor, settings access was migrated from direct
`AppSettings.Global` calls scattered across the codebase to a centralized
`ISettingsService`. This step verifies that the migration is complete and
correct — that settings are saved, loaded, and applied properly.

There are two aspects to verify:

1. **Persistence** — settings survive application restart. When you change a
   setting and close the app, the setting should be restored when you reopen.
2. **Live updates** — some settings (like the UI theme) should take effect
   immediately when changed, without requiring a restart. This works through
   `ISettingsService.SettingChanged`, an `IObservable<string>` that ViewModels
   subscribe to. When a setting changes, the observable fires with the setting
   key, and subscribed ViewModels react accordingly.

#### To do that, follow these steps

1. **Launch the application.**

2. **Open EditAppSettings** (the settings dialog). Change every available
   setting to a non-default value. Note what you changed.

3. **Click OK** to save the settings.

4. **Close the application entirely.**

5. **Verify `Save()` is called during shutdown.** Open `MainViewModel` and
   search for the `Shutdown()` method (or equivalent window-closing handler).
   Confirm it calls `_settingsService.Save()`. If it doesn't, add the call.

6. **Reopen the application.**

7. **Open EditAppSettings again.** Verify that every setting you changed in
   step 2 is still set to the changed value. If any setting reverted to
   default, there is a persistence bug — trace the setting through
   `ISettingsService` to find where it is being lost.

8. **Reset to defaults** (if a "Reset" button exists in the settings dialog).
   Verify all settings return to their default values.

9. **Verify live updates.** This tests the reactive pipeline:
   - Open EditAppSettings.
   - Change the theme setting (or another visually obvious setting).
   - Click OK (do **not** restart the application).
   - Verify the theme updates immediately in the main window.
   - This confirms that the `SettingChanged` observable fired correctly
     and `MainViewModel` (or the appropriate child VM) reacted to it.

#### Now that those are done, here's what changed

- No files were modified (unless you found and fixed a missing `Save()` call
  or a broken setting path).
- You have verified that the settings infrastructure works end-to-end:
  write → persist → restore → live-update.
- This ensures that the `ISettingsService` abstraction introduced in Phase 3A
  is functioning correctly with real data.

---

### A5. Subscription Lifecycle Cleanup

#### What we are going to accomplish

This is a **correctness** step that addresses a subtle but important aspect of
reactive programming: **subscription disposal**.

**The problem:** When you call `WhenAnyValue`, `Subscribe`, or create an
`ObservableAsPropertyHelper`, you are creating a *subscription* — a live
connection between an observable source and a callback. If the source outlives
the subscriber (e.g., a service outlives a ViewModel), the subscription keeps
the subscriber alive in memory (preventing garbage collection) and continues
firing callbacks on a potentially stale object. This causes memory leaks and
can produce incorrect behavior.

**The solution in ReactiveUI:** Different ViewModel types use different
disposal strategies, as defined in Pre-Iteration-Notes §4:

1. **Top-level ViewModels with a View** (e.g., `MainViewModel`):
   - Implement the `IActivatableViewModel` interface
   - Add `public ViewModelActivator Activator { get; } = new();`
   - Put subscriptions inside `this.WhenActivated(disposables => { ... })`
   - Chain each subscription with `.DisposeWith(disposables)`
   - When the View deactivates, all subscriptions are automatically disposed

   ```csharp
   public class MainViewModel : ReactiveObject, IActivatableViewModel {
       public ViewModelActivator Activator { get; } = new();

       public MainViewModel(...) {
           this.WhenActivated(disposables => {
               ArchiveTree.WhenAnyValue(x => x.SelectedItem)
                   .Subscribe(item => OnArchiveTreeSelectionChanged(item))
                   .DisposeWith(disposables);
           });
       }
   }
   ```

2. **Child ViewModels without a direct View** (e.g., `FileListViewModel`,
   `ArchiveTreeViewModel`):
   - Implement `IDisposable`
   - Track subscriptions in a `CompositeDisposable` or similar
   - `MainViewModel` calls `Dispose()` on each child VM when replacing or
     tearing down

   **Critical:** Do NOT apply `IActivatableViewModel` to child VMs!
   `WhenActivated` only fires when a View activates — child VMs are not
   directly attached to a View, so `WhenActivated` would **never fire** on
   them. Their subscriptions would silently never be set up, and you'd have
   a very confusing bug where reactive chains just... don't work.

3. **Dialog ViewModels** (e.g., `EditSectorViewModel`):
   - Subscriptions are safe to leave untracked
   - The VM is short-lived — created, shown in a dialog, and garbage-collected
     when the dialog closes

#### To do that, follow these steps

1. **Enumerate all subscriptions:**
   ```
   grep -rn "\.Subscribe\|WhenAnyValue\|ObservableAsPropertyHelper" cp2_avalonia/ViewModels/
   ```

2. For each result, determine which category the ViewModel falls into:
   - Is it a top-level VM with a View? → Should use `IActivatableViewModel` +
     `WhenActivated` + `DisposeWith`
   - Is it a child VM? → Should implement `IDisposable` and track
     subscriptions
   - Is it a dialog VM? → Subscriptions are safe without tracking

3. For top-level VMs (`MainViewModel`):
   - Confirm it implements `IActivatableViewModel`
   - Confirm it has `public ViewModelActivator Activator { get; } = new();`
   - Confirm all `WhenAnyValue`, `Subscribe`, and `ObservableAsPropertyHelper`
     calls are inside `WhenActivated` and use `.DisposeWith(disposables)`

4. For child VMs (`FileListViewModel`, `ArchiveTreeViewModel`,
   `DirectoryTreeViewModel`, `OptionsPanelViewModel`, `CenterInfoViewModel`,
   `StatusBarViewModel`):
   - Confirm the class implements `IDisposable`
   - Confirm subscriptions created in the constructor (or elsewhere) are
     tracked in a disposal collection
   - Confirm `MainViewModel` calls `Dispose()` on each child VM when
     appropriate (e.g., during shutdown or when replacing child VMs after
     a file close/reopen)

5. For dialog VMs:
   - Confirm they do NOT implement `IActivatableViewModel` (unless they have
     a specific reason)
   - Confirm subscriptions are simple and will die naturally when the dialog
     closes

6. **Build and test:**
   ```
   dotnet build cp2_avalonia/
   ```

#### Now that those are done, here's what changed

- All reactive subscriptions in the application now have proper lifecycle
  management.
- Memory leaks from leaked subscriptions are eliminated.
- Stale callbacks on disposed ViewModels are prevented.
- This is a correctness and stability improvement — the application behaves
  the same, but is now more robust during long-running sessions with many
  file open/close cycles.

---

## Workstream B: Multi-Instance FileViewer Enhancement

---

### B1. Current State Assessment

#### What we are going to accomplish

After Phase 4A, `FileViewer` was converted from a tightly coupled dialog to a
modeless window with its own ViewModel (`FileViewerViewModel`). The design goal
(from MVVM_Notes §2.3, §7.10) is to support **multiple concurrent FileViewer
instances** — the user should be able to open several viewers at once, each
showing a different file (or even the same file from a different archive).

This step is an **assessment** — you are examining the current state of the
multi-viewer implementation to determine if it is complete and correct.
Depending on what you find, you may skip to B3 (lifecycle verification) or
need to implement B2 (fill in gaps).

**Key MVVM concept — `IViewerService`:**

`IViewerService` is a DI singleton that acts as a **registry** for all active
`FileViewerViewModel` instances across the entire application. It is the
single source of truth for "which viewers are currently open." The service
provides three core operations:

- `Register(viewer)` — called by each `FileViewerViewModel` when it is
  constructed
- `Unregister(viewer)` — called when a viewer is closed/disposed
- `CloseViewersForSource(workPathName)` — called when a source file is closed,
  to force-close all viewers associated with that file

This centralized approach (rather than tracking viewers in `MainViewModel`)
was chosen to support future multi-window architecture — if two `MainWindow`
instances are open, the viewer registry must be global, not per-window.

#### To do that, follow these steps

1. **Check whether `IViewerService` exists and is implemented.** Search for:
   ```
   grep -rn "IViewerService" cp2_avalonia/Services/
   ```
   Verify that:
   - The interface exists with `Register`, `Unregister`,
     `CloseViewersForSource`, and `ActiveViewers` members
   - A concrete `ViewerService` implementation exists

2. **Check `FileViewerViewModel`:**
   ```
   grep -rn "IViewerService\|_viewerService" cp2_avalonia/ViewModels/FileViewerViewModel.cs
   ```
   Verify that:
   - The constructor accepts `IViewerService` via injection
   - The constructor calls `_viewerService.Register(this)`
   - `Dispose()` calls `_viewerService.Unregister(this)`
   - The VM has a `WorkPathName` property for source identification

3. **Test multiple instances manually:**
   - Open an archive
   - Open a FileViewer for one file
   - Open a FileViewer for a different file
   - Verify both viewers are open simultaneously and display correctly

4. **Test source-file scoped cleanup:**
   - Open an archive and open one or more FileViewers
   - Close the archive
   - Verify all associated FileViewers close automatically

5. **Test re-opening the same file:**
   - Open a viewer for a file
   - Close the viewer
   - Open a viewer for the same file again
   - Verify it works correctly (no stale state)

6. **Document your findings.** If everything works, note it and proceed to B3.
   If there are gaps (missing `IViewerService`, incomplete registration, viewers
   not closing), proceed to B2.

#### Now that those are done, here's what changed

- You have a clear picture of the multi-viewer implementation status.
- No code was modified in this step — it is pure assessment.
- Your findings determine whether B2 is needed.

---

### B2. Implementation (if needed)

#### What we are going to accomplish

If B1 revealed gaps in the multi-viewer implementation, this step fills them.
The goal is a complete `IViewerService` with proper viewer registration,
deregistration, and source-scoped cleanup.

**Key concepts:**

- **Thread safety:** Multiple viewers might be opening and closing concurrently.
  The viewer list must be protected with a lock (or marshaled to the UI thread).
- **`RequestClose` interaction:** When `CloseViewersForSource` needs to close a
  viewer, the ViewModel cannot directly call `Window.Close()` — that would
  violate the MVVM principle of ViewModel→View independence. Instead, the
  ViewModel exposes a `RequestCloseInteraction` (a ReactiveUI `Interaction`),
  and the View registers a handler that calls `Close()`.
- **Race condition handling:** There is a timing edge case where
  `CloseViewersForSource` is called before the View has activated (and
  registered its close handler). The blueprint handles this with a
  `mClosePending` flag that the View checks during activation.

#### To do that, follow these steps

1. **Implement `ViewerService`** if it does not exist. Create
   `cp2_avalonia/Services/ViewerService.cs`:

   ```csharp
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
           foreach (var vm in toClose) {
               vm.RequestClose();
           }
       }
   }
   ```

   **Important ordering requirement** (MVVM_Notes §7.10 gotcha #1):
   `CloseViewersForSource(...)` must complete **synchronously** before
   `WorkTree.Dispose()`. The `MainViewModel`'s close path must call
   `CloseViewersForSource`, wait for all viewers to close, and only then
   proceed to `IWorkspaceService.Close()`.

2. **Wire up `FileViewerViewModel`** if not already done. The constructor
   should:
   - Accept `IViewerService` and `workPathName` as parameters
   - Call `_viewerService.Register(this)` in the constructor
   - Expose `public string WorkPathName { get; }`
   - Expose `public Interaction<Unit, Unit> RequestCloseInteraction { get; } = new()`
   - Track whether the close handler is registered:
     ```csharp
     private bool mCloseHandlerRegistered;
     private bool mClosePending;
     public bool IsClosePending => mClosePending;
     ```
   - Implement `RequestClose()`:
     ```csharp
     public void RequestClose() {
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
     ```

3. **Wire up the View's close handler** in `FileViewer.axaml.cs`:
   ```csharp
   this.WhenActivated(d => {
       d(ViewModel!.RequestCloseInteraction.RegisterHandler(ctx => {
           Close();
           ctx.SetOutput(Unit.Default);
       }));
       ViewModel!.mCloseHandlerRegistered = true;
       if (ViewModel!.IsClosePending) Close();
   });
   ```

4. **Implement `Dispose()`** in `FileViewerViewModel`:
   ```csharp
   public void Dispose() {
       _viewerService.Unregister(this);
       // Release any file data / handles
   }
   ```

5. **Register `IViewerService` in the DI container** if not already done
   (in `App.axaml.cs`):
   ```csharp
   services.AddSingleton<IViewerService, ViewerService>();
   ```

6. Build and test:
   ```
   dotnet build cp2_avalonia/
   ```
   Then run the application and repeat the B1 manual tests.

#### Now that those are done, here's what changed

- **New or modified files:** `Services/ViewerService.cs`,
  `ViewModels/FileViewerViewModel.cs`, `FileViewer.axaml.cs`, `App.axaml.cs`
- Multiple FileViewer instances can now be opened simultaneously.
- Viewers are tracked centrally by `IViewerService`.
- Closing a source file automatically closes all associated viewers.
- This enables the future goals of multi-document workflow and multi-window
  architecture (MVVM_Notes §2.4).

---

### B3. Viewer Lifecycle

#### What we are going to accomplish

Even if B2 was not needed (the implementation was already complete), you still
need to verify the viewer lifecycle requirements. This is a correctness check
that ensures viewers behave correctly in all edge cases.

There is also a new behavior to implement: **workspace modification
notification**. When the user modifies the source archive (adds a file, deletes
an entry, etc.), any open viewers showing data from that archive are now
displaying **stale** data. Rather than auto-refreshing (which could be
expensive and disruptive), the viewer should show a warning banner so the user
can decide whether to close the viewer or continue viewing stale data.

**How this works reactively:**

`IWorkspaceService` exposes `WorkspaceModified`, an `IObservable<Unit>` that
fires after any modifying operation (add, delete, move, etc.). Each
`FileViewerViewModel` subscribes to this observable. When it fires, the VM
sets a flag (`mSourceModified`) and a bindable property
(`IsSourceModifiedWarningVisible`) that the View binds to a warning banner.

#### To do that, follow these steps

1. **Verify viewer registration on creation:**
   - Set a breakpoint (or add a log statement) in `ViewerService.Register()`.
   - Open a FileViewer. Confirm `Register` is called.

2. **Verify viewer deregistration on close:**
   - Set a breakpoint in `ViewerService.Unregister()`.
   - Close the FileViewer. Confirm `Unregister` is called.
   - Confirm the `Closed` event on the Avalonia window reliably triggers
     `Unregister`. If it doesn't, consider a weak-reference fallback
     (see MVVM_Notes §7.10 gotcha #3).

3. **Verify force-close when source file is closed:**
   - Open an archive and one or more FileViewers.
   - Close the archive (File → Close).
   - Confirm all viewers close before the `WorkTree` is disposed.

4. **Implement workspace modification notification:**
   - In `FileViewerViewModel`, add:
     ```csharp
     private bool mSourceModified;
     private bool mIsSourceModifiedWarningVisible;
     public bool IsSourceModifiedWarningVisible {
         get => mIsSourceModifiedWarningVisible;
         set => this.RaiseAndSetIfChanged(ref mIsSourceModifiedWarningVisible, value);
     }
     ```
   - In the constructor (or `WhenActivated` if appropriate), subscribe to
     `IWorkspaceService.WorkspaceModified`:
     ```csharp
     _workspaceService.WorkspaceModified.Subscribe(_ => {
         mSourceModified = true;
         IsSourceModifiedWarningVisible = true;
     });
     ```
   - In `FileViewer.axaml`, add a warning banner (e.g., an `InfoBar` or a
     styled `Border` with a `TextBlock`) bound to
     `{Binding IsSourceModifiedWarningVisible}`. Text should say something
     like "The source archive has been modified. This view may show stale data."

5. **Test the modification warning:**
   - Open an archive and open a FileViewer.
   - Perform a modifying action (e.g., add a file, delete a file).
   - Verify the warning banner appears in the FileViewer.
   - Close the FileViewer and reopen it. The warning should not appear until
     another modification is made.

6. Build:
   ```
   dotnet build cp2_avalonia/
   ```

#### Now that those are done, here's what changed

- **Modified files:** `ViewModels/FileViewerViewModel.cs`,
  `FileViewer.axaml`
- Viewer lifecycle is verified and correct: register on create, unregister on
  close, force-close when source is closed.
- Viewers now display a warning when their source data has been modified,
  giving the user informed control over whether to close or continue viewing.
- The viewer does NOT auto-refresh — this is intentional and by design.

---

## Workstream C: Docking Evaluation (Optional)

---

### C1–C3. Assess, Evaluate Libraries, and Decide

#### What we are going to accomplish

MVVM_Notes §7.11 discusses panel modularity and composability. The current
`MainWindow` has a fixed layout: archive tree on the left, directory tree
below it, file list or info panel in the center, options panel on the right,
status bar at the bottom. A docking framework (like Avalonia Dock) could allow
users to rearrange, detach, and re-dock these panels to suit their workflow.

**This workstream is a decision gate, not an implementation task.** You are
evaluating whether docking adds value, examining available libraries, and
making a go/no-go decision. Implementation — if it happens — would get its
own separate blueprint.

The good news: because Phase 5 extracted each panel into its own child
ViewModel (`ArchiveTreeViewModel`, `FileListViewModel`, `CenterInfoViewModel`,
etc.), the architecture is already **ready** for docking. Each panel ViewModel
is self-contained, communicates through the parent or services (never through
direct View references), and makes no layout assumptions. Introducing a
docking framework later would be a View-layer change, not an architecture
change.

#### To do that, follow these steps

1. **Assess need.** Consider:
   - Is the current fixed layout sufficient for the application's users?
   - Would docking add complexity (both in development and user experience)
     without proportional benefit?
   - Are there user requests for docking?
   - Is CiderPress II a utility application where users work in a
     straightforward workflow, or a power-user tool where layout
     customization is expected?

2. **Evaluate libraries** (if docking is potentially desired):

   | Library | Maturity | License | Notes |
   |---|---|---|---|
   | Dock.Avalonia | Active development | MIT | Best available docking option for Avalonia |

3. **Make the decision:**

   **If docking is NOT pursued (this is the expected outcome):** Record the
   decision and rationale in the project's `KNOWN_ISSUES.md` docking entry.
   The child ViewModels from Phase 5 make it straightforward to add later
   if needed. Skip to Workstream D.

   **If docking IS pursued:** Do NOT implement it in this iteration. Create
   a separate blueprint covering:
   - Adding the `Dock.Avalonia` NuGet package
   - Replacing the current Grid/SplitView layout with dock panels
   - Exposing child ViewModels as dockable documents/tools
   - Saving/restoring dock layout in settings

#### Now that those are done, here's what changed

- A decision was made and documented. No code was modified.
- If the decision was "not now," the child VM architecture from Phase 5
  ensures docking can be revisited at any time without re-architecting.
- If the decision was "yes," a separate blueprint will be created to guide
  the implementation.

---

## Workstream D: Unit Test Infrastructure (Optional)

---

### D1. ViewModel Unit Tests

#### What we are going to accomplish

One of the primary motivations for the entire MVVM refactor was testability
(MVVM_Notes §8). Before the refactor, all logic lived in code-behind and the
controller — testing any of it required instantiating the full Avalonia UI
runtime. Now that logic lives in ViewModels and services, it can be tested
with simple unit tests using mock dependencies.

This step creates the test project and establishes the test infrastructure.

**Key concepts for testing ReactiveUI ViewModels:**

1. **Mock services:** Every service (`IDialogService`, `ISettingsService`,
   etc.) is an interface. In tests, you create mock implementations using a
   library like Moq. The ViewModel receives these mocks via constructor
   injection, just as it would receive real services in production.

2. **Observable mocks:** Some service interfaces expose `IObservable<T>`
   properties (like `ISettingsService.SettingChanged` and
   `IWorkspaceService.WorkspaceModified`). Moq returns `null` by default for
   reference types. If a ViewModel subscribes to one of these observables in
   its constructor, a `null` value will cause a `NullReferenceException`.
   You must set up the mock to return `Observable.Empty<T>()` (which is a
   valid observable that simply never emits any values).

3. **Scheduler setup:** ReactiveUI internally uses `RxApp.MainThreadScheduler`
   to schedule work on the UI thread. In unit tests, there is no UI thread.
   You must set `RxApp.MainThreadScheduler = Scheduler.CurrentThread` so that
   reactive pipelines fire synchronously and predictably. The blueprint provides
   a `ReactiveTestBase` class for this.

#### To do that, follow these steps

1. **Create the test project directory:**
   ```
   mkdir -p cp2_avalonia_tests/ViewModels
   ```

2. **Create the project file** at `cp2_avalonia_tests/cp2_avalonia_tests.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <IsPackable>false</IsPackable>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="../cp2_avalonia/cp2_avalonia.csproj" />
       <PackageReference Include="xunit" Version="2.*" />
       <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
       <PackageReference Include="Moq" Version="4.*" />
       <PackageReference Include="FluentAssertions" Version="6.*" />
     </ItemGroup>
   </Project>
   ```

3. **Add the project to the solution:**
   ```
   dotnet sln CiderPress2.sln add cp2_avalonia_tests/cp2_avalonia_tests.csproj
   ```

4. **Create the `ReactiveTestBase` class** at
   `cp2_avalonia_tests/ReactiveTestBase.cs`:
   ```csharp
   using System.Reactive.Concurrency;
   using ReactiveUI;

   public class ReactiveTestBase : IDisposable {
       public ReactiveTestBase() {
           RxApp.MainThreadScheduler = Scheduler.CurrentThread;
       }

       public void Dispose() {
           RxApp.MainThreadScheduler = DefaultScheduler.Instance;
       }
   }
   ```
   All ViewModel test classes should inherit from this base class. This ensures
   reactive pipelines fire immediately during tests rather than trying to
   dispatch to a non-existent UI thread.

5. **Create a sample test** at
   `cp2_avalonia_tests/ViewModels/MainViewModelTests.cs`. This verifies the
   test infrastructure works. The required `using` directives are:
   ```csharp
   using System.Reactive.Linq;    // for Observable.Empty
   using System.Reactive.Concurrency; // for Scheduler.CurrentThread
   ```

   The test creates `MainViewModel` with all mock services:
   ```csharp
   public class MainViewModelTests : ReactiveTestBase {
       [Fact]
       public void OpenCommand_WhenFileNotOpen_CanExecuteIsTrue() {
           // Arrange — create mocks
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

           // Assert
           bool? canExec = null;
           vm.OpenCommand.CanExecute.Take(1).Subscribe(v => canExec = v);
           canExec.Should().BeTrue();
       }
   }
   ```

6. **Run the tests:**
   ```
   dotnet test cp2_avalonia_tests/
   ```

#### Now that those are done, here's what changed

- **New files:** `cp2_avalonia_tests/cp2_avalonia_tests.csproj`,
  `cp2_avalonia_tests/ReactiveTestBase.cs`,
  `cp2_avalonia_tests/ViewModels/MainViewModelTests.cs`
- **Modified files:** `CiderPress2.sln` (new project reference)
- A test infrastructure is now in place. Developers can write unit tests for
  any ViewModel by inheriting `ReactiveTestBase`, mocking services, and testing
  command behavior and state transitions.
- This is the foundation for ongoing test development. The priority test cases
  in D2 can be implemented next.

---

### D2. Priority Test Cases

#### What we are going to accomplish

With the test infrastructure in place, this step implements the highest-priority
test cases. These tests cover the most important reactive behaviors in the
application: command `canExecute` logic, settings reaction, tree population,
file selection, and dialog ViewModel validation.

These tests serve as both regression protection and documentation — they
demonstrate how each ViewModel is expected to behave.

**Test naming convention:** Use `MethodName_StateUnderTest_ExpectedBehavior`.
This makes test names self-documenting.

#### To do that, follow these steps

1. **Command canExecute test** (already created in D1 as the sample test):
   `OpenCommand_WhenFileNotOpen_CanExecuteIsTrue`

2. **Settings reaction test** — create a test that verifies `MainViewModel`
   reacts when a setting changes:
   - Use `new Subject<string>()` (from `System.Reactive.Subjects`) instead
     of `Observable.Empty<string>()` for the `SettingChanged` mock.
   - After constructing the VM, emit a known setting key via
     `subject.OnNext("theme")`.
   - Assert that the VM reacted (e.g., a property changed or a handler was
     invoked).

3. **Tree population test** — verify that `ArchiveTreeViewModel` correctly
   populates its `Items` collection from a mock `IWorkspaceService`:
   - Create a mock that returns a known tree structure.
   - Call the populate method.
   - Assert the `Items` collection matches the expected structure.

4. **File selection test** — verify that `FileListViewModel.GetFileSelection()`
   returns the correct entries:
   - Populate the VM with test items.
   - Mark some as selected.
   - Assert the returned list contains exactly the selected items.

5. **Dialog ViewModel validation test** — verify that
   `EditAttributesViewModel` reports invalid state:
   - Create the VM with test data.
   - Set a date field to an invalid value.
   - Assert `IsValid` is `false`.

6. **Run all tests:**
   ```
   dotnet test cp2_avalonia_tests/
   ```

#### Now that those are done, here's what changed

- **New files:** Additional test files in `cp2_avalonia_tests/ViewModels/`
- The test suite now covers the five highest-priority scenarios.
- These tests will catch regressions in command behavior, settings
  propagation, tree population, file selection, and validation logic.
- Additional tests can be added following the same patterns.

---

## Workstream E: Performance Audit

---

### E1. ObservableCollection Performance

#### What we are going to accomplish

Some archives handled by CiderPress II can contain 10,000+ entries. When the
file list is populated, each entry is added to an `ObservableCollection<T>`.
By default, `ObservableCollection` fires a `CollectionChanged` event for
**every single add** — which means the UI tries to re-render 10,000+ times
during population. This can cause noticeable lag.

This step is an **as-needed** optimization. You only need to act if you
observe measurable performance issues with large archives.

**What NOT to do:** The blueprint explicitly warns against migrating from
`ObservableCollection` to `SourceList<T>` (from the DynamicData library)
during this phase. That would require rewriting the population, sort, and
filter logic from Phase 5 — a major architectural change. If that migration
is desired, it should get its own dedicated blueprint.

#### To do that, follow these steps

1. **Profile with a large archive.** Open an archive with 10,000+ entries
   (if available) and measure how long the file list takes to populate.

2. **If performance is acceptable:** Document the result and skip further
   optimization.

3. **If performance is poor, consider these optimizations:**
   - **DataGrid virtualization:** Verify that `VirtualizationMode` is set
     appropriately on the `DataGrid` in `MainWindow.axaml`. Avalonia's
     DataGrid supports UI virtualization by default, but ensure it is not
     accidentally disabled.
   - **Batch updates:** Use an `.AddRange()` extension method to add items
     in bulk rather than one-at-a-time. Alternatively, build the full list
     first, then replace the `ObservableCollection` wholesale:
     ```csharp
     var items = BuildFileListItems(entries);
     FileList = new ObservableCollection<FileListItem>(items);
     ```
     This fires a single `Reset` notification instead of N `Add`
     notifications.

4. Build and re-profile to verify improvement:
   ```
   dotnet build cp2_avalonia/
   ```

#### Now that those are done, here's what changed

- If optimization was applied: file list population is faster for large
  archives.
- If no optimization was needed: the performance baseline is documented.
- No architectural changes were made — this is a targeted, local
  optimization.

---

## Step-by-Step Execution Order

The blueprint defines a specific execution order for the workstreams.
Follow this sequence:

---

### Step 1: Workstream A (Cleanup) — Required

#### What we are going to accomplish

Complete all cleanup items (A1 through A5) before moving to feature work.
This ensures the codebase is clean before you build on it.

#### To do that, follow these steps

1. Complete A1 (Remove Dead Code).
2. Complete A2 (Consistent Error Handling).
3. Complete A3 (Finalize Message Box Dialog).
4. Complete A4 (Settings Persistence Audit).
5. Complete A5 (Subscription Lifecycle Cleanup).
6. Build and run a full test pass:
   ```
   dotnet build cp2_avalonia/
   ```
   Launch the application and verify basic functionality (open file, navigate,
   close).

#### Now that those are done, here's what changed

- All technical debt from the MVVM migration has been addressed.
- Error handling is consistent and complete.
- Message boxes are fully functional.
- Settings persist correctly.
- Subscription lifecycles are properly managed.

---

### Step 2: Workstream B (Multi-Viewer) — Required Verification

#### What we are going to accomplish

Verify (and if necessary implement) the multi-viewer infrastructure. Even if
you determine the current implementation is sufficient (and skip B2), you must
still verify the lifecycle requirements in B3.

#### To do that, follow these steps

1. Complete B1 (Assessment).
2. If B1 identifies gaps, complete B2 (Implementation).
3. In either case, complete B3 (Lifecycle Verification).
4. Build and test:
   ```
   dotnet build cp2_avalonia/
   ```

#### Now that those are done, here's what changed

- Multi-viewer support is verified or implemented.
- Viewer lifecycle is correct: register, unregister, force-close, and
  workspace modification warning all work.

---

### Step 3: Workstream C (Docking) — Decision Only

#### What we are going to accomplish

Evaluate and document the docking decision. No implementation is expected.

#### To do that, follow these steps

1. Complete the C1–C3 assessment.
2. Record the decision in `KNOWN_ISSUES.md`.

#### Now that those are done, here's what changed

- The docking decision is documented for future reference.

---

### Step 4: Workstream D (Unit Tests) — Recommended

#### What we are going to accomplish

Create the test project and implement priority test cases. This is strongly
recommended but not blocking for the MVVM refactor completion.

#### To do that, follow these steps

1. Complete D1 (Test Project Setup).
2. Complete D2 (Priority Test Cases).
3. Run all tests:
   ```
   dotnet test cp2_avalonia_tests/
   ```

#### Now that those are done, here's what changed

- A test project exists with infrastructure and priority tests.
- ViewModel behavior is covered by automated tests.

---

### Step 5: Workstream E (Performance) — As Needed

#### What we are going to accomplish

Profile the application with large archives and optimize only if measurable
issues exist.

#### To do that, follow these steps

1. Complete E1 (Profile and optimize if needed).

#### Now that those are done, here's what changed

- Performance is profiled and documented, optimized if necessary.

---

### Step 6: Final Validation

#### What we are going to accomplish

This is the comprehensive end-to-end test that confirms the entire MVVM
refactor is complete and the application is fully functional. This is not
a quick smoke test — it is a systematic verification of every feature.

#### To do that, follow these steps

1. **All menu commands.** Open `MainWindow.axaml` and find the complete menu
   structure (File, Edit, Actions, Tools, Help). Exercise every `MenuItem`
   that has a bound command. Verify each one works correctly.

2. **All dialogs.** Open every dialog in the application. Interact with it
   (change values, validate input). Close it via OK and via Cancel. Verify
   both paths work.

3. **File operations.** Test add, extract, delete, test, move, copy, and
   paste operations.

4. **Multiple file types.** Open archives of different types:
   `.2mg`, `.po`, `.do`, `.woz`, `.shk`, `.zip`, `.bxy`. Verify each opens
   and displays correctly.

5. **Edge cases.** Test with:
   - Read-only files
   - Corrupt archives
   - Empty archives

6. **Window lifecycle.** Test:
   - Resize the main window
   - Resize panels (splitters)
   - Close and reopen the application (verify window position/size persists)
   - Verify settings persist across restart

7. **macOS native menu.** If on macOS, verify the native menu items:
   About, Settings/Preferences, Quit.

8. **Drag and drop.** Test:
   - File drop on the main window
   - File drop on the launch panel

9. **Multi-viewer.** Test:
   - Open multiple FileViewer windows simultaneously
   - Close the source file and verify all viewers close

#### Now that those are done, here's what changed

- The entire application has been validated end-to-end.
- All MVVM refactor work across Phases 0–6 has been confirmed functional.

---

## Completion Criteria

The MVVM refactor is complete when all of the following are true:

- [ ] Zero `MainController` references remain:
  ```
  grep -rn "MainController" cp2_avalonia/
  ```

- [ ] Zero `RelayCommand` references remain:
  ```
  grep -rn "new RelayCommand\|RelayCommand<" cp2_avalonia/
  ```

- [ ] Zero `DataContext = this` in any View:
  ```
  grep -rn "DataContext = this" cp2_avalonia/
  ```

- [ ] All ViewModels extend `ReactiveObject` (no hand-rolled
  `INotifyPropertyChanged`):
  ```
  grep -rn "INotifyPropertyChanged\|: ViewModelBase\|: BindableBase" cp2_avalonia/ViewModels/
  ```

- [ ] All commands are `ReactiveCommand` (no `RelayCommand` usage):
  ```
  grep -rn "new RelayCommand\|RelayCommand<" cp2_avalonia/
  ```

- [ ] All singleton services are registered in the DI container:
  `ISettingsService`, `IClipboardService`, `IViewerService`,
  `IWorkspaceService`. `IDialogService` and `IFilePickerService` are
  constructed manually by `MainViewModel` (per Pre-Iteration-Notes DI
  Service Lifetimes).

- [ ] All dialogs use `IDialogService` (no direct View instantiation in
  ViewModels):
  ```
  grep -rn "new.*Window()\|new.*Dialog()" cp2_avalonia/ViewModels/
  grep -rn "\.ShowDialog[^A]" cp2_avalonia/ViewModels/
  ```

- [ ] All file pickers use `IFilePickerService` (no direct `StorageProvider`
  in ViewModels):
  ```
  grep -rn "StorageProvider\|OpenFilePickerAsync\|SaveFilePickerAsync" cp2_avalonia/ViewModels/
  ```

- [ ] `MainViewModel` is ≤ 1,200 lines

- [ ] All long-lived ViewModels implement `IActivatableViewModel` (if
  View-attached) or `IDisposable` (if child VM) and dispose subscriptions
  correctly

- [ ] All functional tests pass

- [ ] Settings persist correctly across restarts

---

## Summary of All New Files Created in Iteration 6

| File | Purpose |
|---|---|
| `ViewModels/MessageBoxViewModel.cs` | ViewModel for custom message box dialog (A3) |
| `MessageBoxView.axaml` + `.axaml.cs` | View for custom message box dialog (A3) |
| `cp2_avalonia_tests/cp2_avalonia_tests.csproj` | Test project (D1) |
| `cp2_avalonia_tests/ReactiveTestBase.cs` | Base class for reactive tests (D1) |
| `cp2_avalonia_tests/ViewModels/MainViewModelTests.cs` | Priority tests (D2) |

Files may also be modified: `DialogService.cs`, `FileViewerViewModel.cs`,
`FileViewer.axaml`, `App.axaml.cs`, `KNOWN_ISSUES.md`, `CiderPress2.sln`.
