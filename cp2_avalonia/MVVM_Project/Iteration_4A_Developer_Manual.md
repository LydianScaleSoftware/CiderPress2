# Iteration 4A Developer Manual: Complex Dialog ViewModels

> **Iteration:** 4A
> **Source:** `Iteration_4A_Blueprint.md`
> **Authority:** `MVVM_Notes.md` §3.3, §6 Phase 4A, §7.14

---

## How to Use This Manual

This document is a teaching-oriented expansion of the Iteration 4A Blueprint.
Each major section explains **why** the work is done, **what** MVVM and
ReactiveUI concepts are involved, and **how** to perform the work step by step.

If you are new to MVVM and ReactiveUI, read each "What we are going to
accomplish" block carefully — it introduces terminology and patterns before
asking you to apply them.

---

## 1. Goal

### What we are going to accomplish

The goal of this iteration is to create ViewModels for the seven **complex**
dialogs in the application — those with 300+ lines and significant business
logic. By the end of this iteration, every complex dialog will follow the
MVVM pattern: a ViewModel class owns all state and logic, while the View
(the `.axaml.cs` code-behind) is reduced to a thin shell that only handles
purely visual concerns.

**Why this matters:**

In the old architecture, each dialog was a `Window` that set
`DataContext = this` — meaning the window *was* its own data source. All
properties, validation, commands, and business logic lived in the
code-behind. This made the dialog untestable (you can't create a `Window`
in a unit test) and tightly coupled to Avalonia.

In MVVM (Model-View-ViewModel), we split this into:
- **Model** — the domain data (e.g., disk sectors, file attributes). These
  already exist in the `DiskArc` and `AppCommon` libraries.
- **ViewModel** — a plain C# class that owns all the dialog's state and
  logic. It knows nothing about windows, buttons, or UI controls.
- **View** — the AXAML markup and thin code-behind that binds to the
  ViewModel.

**ReactiveUI** is the MVVM framework we use. It provides:
- `ReactiveObject` — a base class for ViewModels that implements property
  change notification.
- `ReactiveCommand` — a command type with built-in async support and
  automatic `CanExecute` reevaluation.
- `Interaction<TInput, TOutput>` — a mechanism for ViewModels to request
  something from the View (like closing a dialog) without holding a
  reference to the View.
- `ReactiveWindow<T>` — an Avalonia window base class that provides typed
  access to its ViewModel and `WhenActivated` lifecycle support.

This iteration converts seven dialogs. After it is complete, all complex
dialogs are testable in isolation, and `IDialogService` handles all dialog
presentation uniformly.

### To do that, follow these steps

No action required for this section — it sets the stage. Proceed to the
Prerequisites section.

### Now that those are done, here's what changed

Nothing yet — this is the overview.

---

## 2. Prerequisites

### What we are going to accomplish

Before starting any dialog conversion, we need to confirm that the
foundational infrastructure from earlier iterations is in place. This
iteration builds on:

- **Iteration 3B** (controller dissolved): `MainViewModel` now uses
  services directly. There is no more `MainController`.
- **Service interfaces**: `IDialogService`, `IFilePickerService`,
  `ISettingsService`, `IClipboardService`, and `IWorkspaceService` are
  all defined and registered in the DI container.
- **DialogService**: The `DialogService` class is operational with its
  ViewModel→View registration dictionary.

One specific prerequisite deserves attention:

**`IClipboardService.SetTextAsync(string text)`**: Two of the dialogs in
this iteration (`EditSector` and `FileViewer`) need to copy plain text to
the system clipboard. The `IClipboardService` interface defined in Phase 3A
may or may not include a `SetTextAsync(string)` method — the original
interface focused on the structured `ClipInfo`-based clipboard operations.
If the method is missing, you must add it before starting this iteration.

This method wraps Avalonia's `IClipboard.SetTextAsync` behind the service
abstraction, keeping the ViewModel free of any Avalonia dependency.

### To do that, follow these steps

1. Open `cp2_avalonia/Services/IClipboardService.cs`.
2. Search for `SetTextAsync`. If a method with signature
   `Task SetTextAsync(string text)` exists, you are done.
3. If it does not exist, add the following to the interface:
   ```csharp
   Task SetTextAsync(string text);
   ```
4. Open `cp2_avalonia/Services/ClipboardService.cs` (the implementation).
5. Add the implementation:
   ```csharp
   public async Task SetTextAsync(string text) {
       var clipboard = TopLevel.GetTopLevel(...)?.Clipboard;
       if (clipboard != null) {
           await clipboard.SetTextAsync(text);
       }
   }
   ```
   The exact way to obtain the clipboard reference depends on how
   `ClipboardService` was implemented in Phase 3A. The point is: wrap the
   Avalonia clipboard call so that ViewModels call `_clipboardService.SetTextAsync(text)`
   instead of touching Avalonia APIs directly.
6. Build to verify: `dotnet build cp2_avalonia/`

### Now that those are done, here's what changed

- `IClipboardService` now (if it didn't already) includes `SetTextAsync(string)`.
- The application builds and runs identically to before — no behavioral change.

---

## 3. Target Dialogs

### What we are going to accomplish

This section identifies the seven dialogs that will be converted in this
iteration. They are called "complex" because they each have hundreds of
lines of code with significant business logic — not just simple
OK/Cancel prompts.

Here is the list with a brief description of what each dialog does:

| Dialog | Approx. Lines | What It Does |
|---|---|---|
| `EditSector` | ~1,194 | A hex editor for disk sectors/blocks. Has a grid, navigation, read/write operations. |
| `FileViewer` | ~1,081 | Displays file contents in text, hex, or graphics format. Supports format conversion, magnification, export, and find. |
| `EditAttributes` | ~1,050 | Edits file metadata: ProDOS/HFS types, timestamps, access flags. |
| `CreateDiskImage` | ~580 | Creates new disk images with chosen filesystem, size, and format. |
| `SaveAsDisk` | ~728 | Saves an existing disk in a different file format. |
| `TestManager` | ~351 | Debug-only: runs library test suites and displays results. |
| `BulkCompress` | ~308 | Debug-only: benchmarks compression algorithms. |

Each of these currently follows the old pattern: `Window : INotifyPropertyChanged`
with `DataContext = this`. By the end of this iteration, each will have a
separate ViewModel class in `ViewModels/`, and the code-behind will be reduced
to purely visual concerns.

### To do that, follow these steps

No action for this section — it is informational. The actual conversion
work is described in subsequent sections.

### Now that those are done, here's what changed

Nothing yet — this describes the scope.

---

## 4. Conversion Pattern

### What we are going to accomplish

Every dialog in this iteration follows the same conversion pattern. This
section explains the pattern in detail, with the reasoning behind each step.
The pattern has six parts (A through F). Understanding this pattern is
essential — you will repeat it seven times.

#### Key Concepts for MVVM Newcomers

**`ReactiveObject`**: The base class for all ViewModels. When you inherit
from `ReactiveObject`, you get access to `RaiseAndSetIfChanged`, which
automatically fires property change notifications. In the old code, each
dialog implemented `INotifyPropertyChanged` manually and called
`OnPropertyChanged("PropertyName")`. With ReactiveUI, you write:

```csharp
private string mSomeValue = string.Empty;
public string SomeValue {
    get => mSomeValue;
    set => this.RaiseAndSetIfChanged(ref mSomeValue, value);
}
```

The `this.RaiseAndSetIfChanged(...)` call does three things: checks if the
value actually changed, updates the backing field, and fires the property
change notification. This replaces the entire hand-rolled notification
pattern.

**`ReactiveCommand`**: Replaces `RelayCommand` and inline button click
handlers. A `ReactiveCommand` is created with a `canExecute` observable
that automatically re-evaluates when the properties it depends on change.
No more manual `CommandManager.InvalidateRequerySuggested()` or
`RefreshAllCommandStates()` calls.

```csharp
// A command that is enabled only when IsDirty is true
var canSave = this.WhenAnyValue(x => x.IsDirty);
SaveCommand = ReactiveCommand.CreateFromTask(() => SaveAsync(), canSave);
```

**`ReactiveWindow<TViewModel>`**: An Avalonia window base class provided
by `ReactiveUI.Avalonia`. When your dialog inherits from
`ReactiveWindow<EditSectorViewModel>` instead of plain `Window`, you get:
- A strongly typed `ViewModel` property (no casting `DataContext`)
- `WhenActivated` support for managing subscription lifetimes

**`Interaction<TInput, TOutput>`**: A ReactiveUI mechanism that lets a
ViewModel "ask" the View to do something without holding a reference to
the View. The ViewModel exposes an `Interaction` property. The View
registers a handler for it. When the ViewModel calls `.Handle(input)`,
the handler runs and returns output.

We use this specifically for **closing dialogs**. Since the ViewModel must
not call `Window.Close()` (that would be a View reference), instead:
1. The ViewModel exposes `Interaction<bool, Unit> CloseInteraction`.
2. When the user clicks OK, the ViewModel calls
   `await CloseInteraction.Handle(true)`.
3. The View's handler receives `true`, calls `this.Close(true)`, and
   returns `Unit.Default`.

This is called the **Modal Close Protocol**.

**`IDialogService`**: A service (defined in Phase 3A) that maps ViewModel
types to View types. When `MainViewModel` wants to show a dialog, it
creates the ViewModel, then calls:
```csharp
var result = await _dialogService.ShowDialogAsync(vm);
```
The `DialogService` looks up which View class corresponds to that ViewModel
type, creates the View, sets `DataContext = vm`, and calls `ShowDialog`.
This eliminates all `new SomeDialog(mMainWin, ...)` code from ViewModels.

---

### Part A: Create the ViewModel Class

### What we are going to accomplish

For each dialog, create a new class file at
`cp2_avalonia/ViewModels/{DialogName}ViewModel.cs`. This class holds all
the dialog's state and logic.

### To do that, follow these steps

1. Create a new file `cp2_avalonia/ViewModels/{DialogName}ViewModel.cs`.
2. The class must extend `ReactiveObject`:
   ```csharp
   public class EditSectorViewModel : ReactiveObject {
       // ...
   }
   ```
3. Move all **bindable properties** from the `.axaml.cs` code-behind into
   the ViewModel. Convert each property from the old
   `INotifyPropertyChanged` style to `RaiseAndSetIfChanged`:
   ```csharp
   // Old (in code-behind):
   private bool mIsDirty;
   public bool IsDirty {
       get => mIsDirty;
       set { mIsDirty = value; OnPropertyChanged(); }
   }

   // New (in ViewModel):
   private bool mIsDirty;
   public bool IsDirty {
       get => mIsDirty;
       set => this.RaiseAndSetIfChanged(ref mIsDirty, value);
   }
   ```
4. Move all **business logic** (validation, computation, data
   transformation) into the ViewModel. The code-behind should not contain
   logic about what the dialog's data means — only about how it looks.
5. Move **inner data classes**, enums, and delegates into the ViewModel
   file (or a companion file if they are shared). If an inner class (like
   `SectorRow`) implements `INotifyPropertyChanged`, convert it to use
   `ReactiveObject` as well.
6. Create **`ReactiveCommand` properties** for button actions. Replace
   `Click="Handler"` event handlers with commands.
7. The constructor should accept **domain objects** (data the dialog needs
   to work with) and **services** (for showing messages, picking files,
   etc.) — never View references like `Window` or `TopLevel`.
8. **Validation colors**: Do not put `IBrush`-typed properties in the
   ViewModel. `IBrush` is an Avalonia type and belongs in the View layer.
   Instead, expose a `bool` property (e.g., `IsTrackBlockValid`) and use
   an AXAML style trigger or value converter in the View to select the
   appropriate brush color based on the boolean.
9. **Imperative command invocation from code-behind**: When code-behind
   needs to invoke a `ReactiveCommand` (e.g., in a keyboard shortcut
   handler), cast the command to `ICommand` and call `Execute(null)`:
   ```csharp
   ((ICommand)ViewModel!.SomeCommand).Execute(null);
   ```
   Do **not** call `SomeCommand.Execute()` directly. `ReactiveCommand.Execute()`
   returns an `IObservable<T>` that has no effect without a subscription.
   The `ICommand.Execute(null)` path triggers the command synchronously.

### Now that those are done, here's what changed

- A new `{DialogName}ViewModel.cs` file exists in `ViewModels/`.
- All state and logic that was in the code-behind is now in the ViewModel.
- The ViewModel has no Avalonia dependencies (no `Window`, `Control`,
  `IBrush`, `TopLevel`, etc.).

---

### Part B: Update the View (.axaml.cs)

### What we are going to accomplish

The dialog's code-behind file must be thinned down. It should no longer own
any data or business logic — only visual plumbing.

### To do that, follow these steps

1. Change the base class from `Window` to
   `ReactiveWindow<{DialogName}ViewModel>`:
   ```csharp
   // Before:
   public partial class EditSector : Window, INotifyPropertyChanged

   // After:
   public partial class EditSector : ReactiveWindow<EditSectorViewModel>
   ```
   `ReactiveWindow<T>` comes from the `ReactiveUI.Avalonia` namespace.
   It provides a strongly typed `ViewModel` property and `WhenActivated`
   lifecycle management.

2. **Remove** `DataContext = this;` from the constructor. The `DataContext`
   will now be set by `DialogService` when it creates the View.

3. **Remove** the `INotifyPropertyChanged` implementation (the
   `OnPropertyChanged` method, the `PropertyChanged` event, etc.).

4. **Register the `CloseInteraction` handler** in the constructor or
   `WhenActivated`. This is how the View responds when the ViewModel asks
   to close:
   ```csharp
   this.WhenActivated(d => {
       ViewModel!.CloseInteraction.RegisterHandler(ctx => {
           Close(ctx.Input);            // ctx.Input is true (OK) or false (Cancel)
           ctx.SetOutput(Unit.Default);  // Required by Interaction<,>
       }).DisposeWith(d);
   });
   ```
   - `WhenActivated` ensures the handler is registered when the window
     opens and disposed when it closes.
   - `DisposeWith(d)` ties the handler's lifetime to the activation scope.
   - `ctx.Input` is the `bool` value passed by the ViewModel (true for OK,
     false for Cancel).
   - `ctx.SetOutput(Unit.Default)` is required — `Interaction` is a
     two-way protocol, and the handler must provide an output even if it's
     meaningless.

5. **Keep only** these kinds of code in code-behind:
   - `InitializeComponent()`
   - Event handlers that are purely visual (scroll position, focus
     management, drag-drop gesture start)
   - Window lifecycle handlers (`Opened`, `Closing`) that delegate to the
     ViewModel for logic but perform View-only actions (like calling
     `Focus()` on a control)

### Now that those are done, here's what changed

- The dialog code-behind no longer implements `INotifyPropertyChanged`.
- It inherits from `ReactiveWindow<T>` instead of `Window`.
- `DataContext` is not set in the constructor — `DialogService` sets it.
- The `CloseInteraction` handler is registered and tied to activation.

---

### Part C: Update the AXAML

### What we are going to accomplish

The AXAML markup needs minor updates to reflect the fact that `DataContext`
is now the ViewModel (not the Window itself). In practice, many bindings
remain unchanged because the property names are the same.

### To do that, follow these steps

1. **Review each `Binding`** in the AXAML. Since the old code used
   `DataContext = this`, bindings like `{Binding IsDirty}` pointed to the
   Window's `IsDirty` property. Now they point to the ViewModel's
   `IsDirty` property. As long as the property names are the same, **no
   binding changes are needed**.

2. **Replace `Click="Handler"` on buttons** with
   `Command="{Binding SomeCommand}"`:
   ```xml
   <!-- Before: -->
   <Button Content="Read" Click="ReadButton_Click" />

   <!-- After: -->
   <Button Content="Read" Command="{Binding ReadSectorCommand}" />
   ```

3. **Remove `x:Name` references** that were used only for code-behind
   access to controls. If a control was named solely so that code-behind
   could read its value (e.g., `x:Name="trackTextBox"` used in
   `trackTextBox.Text`), remove the name and use a binding instead.
   Keep `x:Name` only for controls that code-behind still needs for
   purely visual operations (e.g., calling `Focus()` or
   `ScrollIntoView()`).

### Now that those are done, here's what changed

- Button click handlers are replaced with command bindings.
- Unnecessary `x:Name` attributes are removed.
- Bindings continue to work because property names are preserved.

---

### Part D: Register the ViewModel→View Mapping

### What we are going to accomplish

`DialogService` needs to know which View class to create for each ViewModel
type. This is done through an explicit registration.

### To do that, follow these steps

1. Open `cp2_avalonia/App.axaml.cs`.
2. Find the method `RegisterDialogMappings()` (or wherever dialog
   registrations are configured — this was set up in Phase 3A).
3. Add one line for the dialog you just converted:
   ```csharp
   ds.Register<EditSectorViewModel, EditSector>();
   ```
   This tells `DialogService`: "When someone calls
   `ShowDialogAsync(editSectorViewModel)`, create an `EditSector` window,
   set its `DataContext` to the ViewModel, and show it as a modal dialog."

### Now that those are done, here's what changed

- `DialogService` can now create and show the converted dialog.
- The mapping is explicit and easy to find — one line per dialog in a
  central location.

---

### Part E: Update Callers

### What we are going to accomplish

The code that *launches* the dialog (typically in `MainViewModel`) must
change from directly creating a `Window` to creating a ViewModel and
passing it to `IDialogService`.

### To do that, follow these steps

1. Find the call site in `MainViewModel` (or wherever the dialog is
   launched).
2. Replace the old pattern with the new one:
   ```csharp
   // Before (legacy):
   var dlg = new EditSector(chunks, mode, writeFunc, formatter);
   await dlg.ShowDialog(host.GetOwnerWindow());

   // After:
   var vm = new EditSectorViewModel(chunks, mode, writeFunc, formatter,
       _dialogService, _clipboardService);
   var result = await _dialogService.ShowDialogAsync(vm);
   ```
3. After `ShowDialogAsync` returns, read any output from the ViewModel.
   For example, if the dialog modified data, check a property like
   `vm.WritesEnabled` to decide what to do next.
4. **Verify the `ShowDialogAsync` signature** against the actual
   `Services/DialogService.cs` implementation. Per Phase 3A, it accepts
   a pre-constructed ViewModel instance, creates the View, assigns
   `DataContext = vm`, and calls `ShowDialog`. Make sure your calling
   code matches.

### Now that those are done, here's what changed

- The caller no longer creates a `Window` directly.
- The caller creates only the ViewModel, passes it to `DialogService`,
  and reads results from the ViewModel after the dialog closes.
- The caller never touches any View type.

---

### Part F: Modal Close Protocol

### What we are going to accomplish

This part explains the full close-dialog flow in detail. It ties together
the `Interaction` concept with the View handler registration from Part B.

In the old code, a dialog could simply call `this.Close(true)` because
it *was* the Window. Now the ViewModel cannot call `Close()` — it does not
have a Window reference. So we use ReactiveUI's `Interaction` mechanism.

### To do that, follow these steps

1. In the **ViewModel**, add:
   ```csharp
   public Interaction<bool, Unit> CloseInteraction { get; } = new();
   ```
   This declares an interaction that sends a `bool` (the dialog result)
   and receives `Unit` (nothing meaningful — it's a one-way signal).

2. In `OkCommand`, call:
   ```csharp
   await CloseInteraction.Handle(true);
   ```
   This sends `true` to whatever handler is registered — the View will
   receive it and call `Close(true)`.

3. In `CancelCommand`, call:
   ```csharp
   await CloseInteraction.Handle(false);
   ```
   Same mechanism, but sends `false`.

4. In the **View** (code-behind), register the handler as shown in Part B:
   ```csharp
   this.WhenActivated(d => {
       ViewModel!.CloseInteraction.RegisterHandler(ctx => {
           Close(ctx.Input);
           ctx.SetOutput(Unit.Default);
       }).DisposeWith(d);
   });
   ```

5. `DialogService.ShowDialogAsync` returns the `bool?` result from
   `Window.ShowDialog`. The caller reads this to determine whether the
   user accepted or cancelled.

**Close-guard pattern (for dialogs with dirty-check logic):**

Some dialogs (like `EditSector`) ask "Discard unsaved changes?" when the
user clicks the window's X button. This is handled as follows:

- The `Window_Closing` handler in code-behind reads
  `ViewModel.IsDirty`.
- If dirty, it cancels the close event (`e.Cancel = true`) and calls
  `ViewModel.ConfirmDiscardChangesAsync()`.
- `ConfirmDiscardChangesAsync()` uses `IDialogService.ShowConfirmAsync`
  to ask the user.
- If confirmed, code-behind sets a local `mUserConfirmedClose` guard
  flag and calls `Close()` again.
- The guard flag stays in code-behind (it's a View-level concern about
  the close lifecycle).

### Now that those are done, here's what changed

- The ViewModel can request dialog closure without knowing about windows.
- The View handles the actual `Close()` call.
- The protocol is async-safe and works with the `DialogService` plumbing.

---

## 5. Dialog-Specific Instructions: EditSector (~1,194 lines)

### What we are going to accomplish

`EditSector` is a hex editor for reading and writing individual
sectors/blocks on a disk image. It is the most complex dialog in this
iteration.

The current `EditSector.axaml.cs` contains:
- A hex grid displayed via a `DataGrid` bound to `ObservableCollection<SectorRow>`
- Navigation state (current track, sector, block number)
- Read/write operations on the underlying `IChunkAccess`
- An inner `SectorRow` class that represents one row of the hex grid
- Multiple enums and delegate types
- Copy-to-clipboard functionality
- Dirty tracking and a close-guard

All of this moves to `EditSectorViewModel`, except for the purely visual
DataGrid cell editing and keyboard navigation.

**Key ReactiveUI concepts in this dialog:**
- `ReactiveCommand` for navigation (prev/next sector/block) and
  read/write operations
- `ObservableCollection<SectorRow>` for the hex grid data
- `Interaction<bool, Unit>` for dialog close
- `IClipboardService` for the copy button
- `IDialogService` for confirmation dialogs (discard changes, enable writes)

**Why validation-color booleans matter here:**
The old code used `IBrush` properties (`TrackBlockLabelForeground`,
`SectorLabelForeground`) to color-code validation state. Brushes are
Avalonia types and must not appear in the ViewModel. Instead, the
ViewModel exposes `bool IsTrackBlockValid` and `bool IsSectorValid`. The
AXAML uses a style trigger or value converter to select red or default
foreground based on the boolean.

**Static display-base fields:** The three `sTrackNumBase`, `sSectorNumBase`,
`sBlockNumBase` fields must remain `static` on `EditSectorViewModel`.
They preserve the user's decimal/hex display choice across multiple
invocations of the dialog — a static field persists for the process
lifetime.

### To do that, follow these steps

1. **Create** `cp2_avalonia/ViewModels/EditSectorViewModel.cs`.

2. **Define the constructor:**
   ```csharp
   public EditSectorViewModel(
       IChunkAccess chunks,
       SectorEditMode editMode,
       EnableWriteFunc? writeFunc,
       Formatter formatter,
       IDialogService dialogService,
       IClipboardService clipboardService)
   ```

3. **Move inner types** into the ViewModel file:
   - `SectorRow` class — convert from `INotifyPropertyChanged` to
     `ReactiveObject`. **Note:** `SectorRow` does not use
     `RaiseAndSetIfChanged` in the normal way because its column
     properties (`C0`–`Cf`) write into a shared byte buffer via
     `Set(col, value)`. Replace each `OnPropertyChanged()` call with
     `this.RaisePropertyChanged(nameof(C0))` (etc.) in each setter.
   - `SectorEditMode` enum
   - `EnableWriteFunc` delegate
   - `TxtConvMode` enum
   - `BlockOrderItem` class
   - `SectorOrderItem` class

4. **Update all external references** to the migrated types. Search the
   workspace for `EditSector.SectorEditMode`, `EditSector.EnableWriteFunc`,
   etc. and change them to `EditSectorViewModel.SectorEditMode`,
   `EditSectorViewModel.EnableWriteFunc`, etc.

5. **Move all bindable properties** from code-behind to ViewModel:
   - Sector/block navigation state (current track, sector, block number)
   - Grid data: `ObservableCollection<SectorRow>`
   - Read-only flag, dirty flag
   - Text encoding mode
   - Status text
   - Replace `TrackBlockLabelForeground` and `SectorLabelForeground`
     (`IBrush`) with `bool IsTrackBlockValid` and `bool IsSectorValid`
   - Keep `sTrackNumBase`, `sSectorNumBase`, `sBlockNumBase` as `static`

6. **Move all business logic** — validation, sector reading, sector
   writing, data transformation — into the ViewModel.

7. **Replace dialog/message calls** with service calls:
   - Discard-changes confirmation → `_dialogService.ShowConfirmAsync(...)`
   - Write-error messages → `_dialogService.ShowMessageAsync(...)`
   - Enable-write-access confirmation → `_dialogService.ShowConfirmAsync(...)`

8. **Create commands:**
   - `ReadSectorCommand` / `ReadBlockCommand`
   - `WriteSectorCommand` / `WriteBlockCommand`
   - `PrevSectorCommand` / `NextSectorCommand`
   - `PrevBlockCommand` / `NextBlockCommand`
   - `CopyToClipboardCommand` — formats `mBuffer` as a hex dump via
     `_formatter.FormatHexDump(mBuffer)` and calls
     `_clipboardService.SetTextAsync(dumpText)`

9. **Add CloseInteraction** and `OkCommand`/`CancelCommand` (or handle
   close through the close-guard pattern).

10. **Add output property:** `bool WritesEnabled { get; private set; }`
    — set to `true` inside `TryEnableWrites()`. The caller reads this
    after `ShowDialogAsync` returns.

11. **Update the View** (`EditSector.axaml.cs`):
    - Change base class to `ReactiveWindow<EditSectorViewModel>`
    - Remove `DataContext = this` and `INotifyPropertyChanged`
    - Register `CloseInteraction` handler in `WhenActivated`
    - Keep DataGrid cell editing commit/cancel (pure UI)
    - Keep keyboard navigation within hex grid
    - For the Ctrl+C keyboard handler, invoke the clipboard command:
      `((ICommand)ViewModel!.CopyToClipboardCommand).Execute(null)`
    - The `Window_Closing` close-guard reads `ViewModel.IsDirty`

12. **Update the AXAML** (`EditSector.axaml`):
    - Replace button `Click` handlers with `Command` bindings
    - Add style triggers for validation colors (bind brush to boolean)
    - Remove unnecessary `x:Name` attributes

13. **Register the mapping** in `App.axaml.cs`:
    ```csharp
    ds.Register<EditSectorViewModel, EditSector>();
    ```

14. **Update the caller** in `MainViewModel.EditBlocksSectors()`:
    ```csharp
    var vm = new EditSectorViewModel(chunks, mode, writeFunc, formatter,
        _dialogService, _clipboardService);
    var result = await _dialogService.ShowDialogAsync(vm);
    if (vm.WritesEnabled) {
        // reprocess the disk image
    }
    ```

15. **Build and test:**
    - `dotnet build cp2_avalonia/`
    - Open a disk image → Actions → Edit Sectors
    - Test navigation (prev/next sector/block)
    - Test read and write operations
    - Test copy to clipboard (Ctrl+C and button)
    - Test the dirty-check close guard
    - Test hex/decimal display toggle

### Now that those are done, here's what changed

- **New file:** `ViewModels/EditSectorViewModel.cs`
- **Modified files:** `EditSector.axaml.cs`, `EditSector.axaml`,
  `App.axaml.cs`, `MainViewModel.cs`, and any files that referenced
  `EditSector.SectorEditMode` or other migrated types
- **New capabilities:** EditSector's state and logic are testable without
  Avalonia. The dialog is created and shown via `IDialogService`.
- **Same behavior:** All sector editing functionality works identically
  from the user's perspective.
- **Enables:** Unit tests for sector navigation validation, read/write
  logic, and dirty tracking.

---

## 6. Dialog-Specific Instructions: FileViewer (~1,081 lines)

### What we are going to accomplish

`FileViewer` (located in `Tools/`) is a modal dialog that displays the
contents of selected files in text, hex, or graphics format. It supports:
- Browsing through multiple selected files (prev/next)
- Multiple format converters (chosen via a combo box)
- A tabbed interface for Data, Resource, and Note forks
- Magnification for graphics display
- Find functionality across text content
- Export to file
- Copy to clipboard

This is the second-most complex dialog. A key design consideration is that
in a future iteration (Phase 6), FileViewer will become **modeless**
(allowing multiple viewers open simultaneously). The ViewModel we create
here must be designed as a self-contained, independent unit — no singleton
state, no static mutable fields. This ensures it can later support multiple
concurrent instances.

**Key concepts specific to this dialog:**

- **`IDisposable`**: Unlike most dialog ViewModels, `FileViewerViewModel`
  **must** implement `IDisposable`. It holds file data, potentially open
  streams, and temp files that must be cleaned up. The code-behind's
  `OnClosed` handler calls `ViewModel.Dispose()`.

- **`IConvOutput` delivery**: The ViewModel stores the conversion output
  (`IConvOutput`) after formatting a file. It exposes this as an observable
  property. The code-behind subscribes (via `WhenAnyValue`) and applies
  the content to the AvaloniaEdit `TextEditor` control — because
  `TextEditor.Document` assignment requires a control reference, which
  belongs in the View.

- **Settings access**: Properties like `IsDOSRaw` and `MAC_ZIP_ENABLED`
  must use `ISettingsService` (not `AppSettings.Global` directly) for
  reads and writes. This keeps the ViewModel testable and consistent with
  the DI pattern.

- **Source-archive-modified notification**: The constructor takes
  `IWorkspaceService` and subscribes to its `WorkspaceModified` observable.
  If the underlying archive changes while the viewer is open, a warning
  banner becomes visible (driven by `IsSourceModifiedWarningVisible`).

### To do that, follow these steps

1. **Create** `cp2_avalonia/ViewModels/FileViewerViewModel.cs`.

2. **Define the constructor (Init pattern):**
   ```csharp
   public FileViewerViewModel(
       object archiveOrFileSystem,
       List<IFileEntry> entries,
       int initialIndex,
       AppHook appHook,
       ISettingsService settingsService,
       IFilePickerService filePickerService,
       IClipboardService clipboardService,
       IWorkspaceService workspaceService)
   ```

3. **Move inner types** into the ViewModel file:
   - `ConverterComboItem` class
   - `DisplayItemType` enum
   - `Tab` enum (`Unknown`, `Data`, `Rsrc`, `Note`)

4. **Move all bindable properties** from code-behind to ViewModel:
   - Current file entry, file index
   - Conversion format list (`ObservableCollection<ConverterComboItem>`)
     and `SelectedConverterIndex`
   - Display content (text, hex dump, bitmap)
   - `MagnificationTick` (observable `double`, bound to slider value),
     `PreviewImageWidth`, `PreviewImageHeight` (observable, bound to
     image dimensions)
   - Find text, find state
   - `IsDOSRaw` — getter/setter must use `ISettingsService`
   - `MAC_ZIP_ENABLED` — read from `ISettingsService`
   - `IsOptionsBoxEnabled` — derived from selected fork tab
   - `IsDOSRawEnabled` — set based on filesystem type
   - `IsDataTabEnabled`, `IsRsrcTabEnabled`, `IsNoteTabEnabled` —
     observable bools bound to `TabItem.IsEnabled` in AXAML
   - `SelectedForkTab` — observable `Tab` enum, replaces `ShowTab()`
   - `NoteText` — observable string for the note text editor
   - `IsSaveDefaultsEnabled` — observable, updated by `ShowFile()` and
     `SaveDefaultsCommand`
   - `IsSourceModifiedWarningVisible` — observable bool for warning banner

5. **Move conversion output delivery:**
   - Store `IConvOutput? DataOutput` and `IConvOutput? RsrcOutput` as
     public observable properties
   - Code-behind subscribes via `WhenAnyValue(x => x.DataOutput)` and
     applies content to the `TextEditor` control

6. **Move find subsystem:**
   - `DoFind(bool forward)` searches within pre-computed text content
   - For Note fork, searches `_noteText`
   - For Data/Rsrc fork, extracts text from `_curDataOutput`/`_curRsrcOutput`
   - Returns found character offset (or -1)
   - Expose `FindResultOffset` (observable) for code-behind to subscribe
     to and call `editor.Select(offset, length)` / `editor.ScrollTo(...)`

7. **Move fork-tab selection logic** (`SelectEnabledTab()`) to the ViewModel.
   The ViewModel sets `SelectedForkTab`, bound to `tabControl.SelectedIndex`
   in AXAML.

8. **Move magnification logic:** `ConfigureMagnification()` moves to the
   ViewModel. It reads `MagnificationTick` and writes `PreviewImageWidth`
   / `PreviewImageHeight`. The slider's AXAML binding on `MagnificationTick`
   drives recalculation via `WhenAnyValue`. The code-behind handler
   `MagnificationSlider_ValueChanged` is eliminated.

9. **Create commands:**
   - `PrevFileCommand` / `NextFileCommand`
   - `ExportCommand` — calls `_filePickerService.SaveFileAsync(…)`
   - `FindNextCommand` / `FindPrevCommand`
   - `CopyTextCommand` — calls `_clipboardService.SetTextAsync(…)`.
     When `SelectedForkTab == Tab.Note`, copies `_noteText`
   - `SaveDefaultsCommand` — calls `_settingsService.SetString(...)` and
     sets `IsSaveDefaultsEnabled = false`

10. **Implement `IDisposable`:**
    - `Dispose()` closes open streams, deletes temp files
    - Disposes the `WorkspaceModified` subscription
    - `FindStaleTempFiles()` becomes a static utility on the ViewModel

11. **Subscribe to workspace modification:**
    ```csharp
    mWorkspaceModifiedSub = workspaceService.WorkspaceModified
        .ObserveOn(RxApp.MainThreadScheduler)
        .Subscribe(_ => IsSourceModifiedWarningVisible = true);
    ```

12. **Update the View** (`Tools/FileViewer.axaml.cs`):
    - Change base class to `ReactiveWindow<FileViewerViewModel>`
    - Register `CloseInteraction` handler
    - Keep image rendering (DrawingContext operations)
    - Keep scroll synchronization
    - Keep keyboard shortcuts — Enter in search box invokes
      `((ICommand)ViewModel!.FindNextCommand).Execute(null)`
    - Subscribe to `ViewModel.WhenAnyValue(x => x.DataOutput)` and apply
      to `TextEditor`
    - Subscribe to `ViewModel.WhenAnyValue(x => x.NoteText)` and assign
      to `noteTextEditor.Document`
    - Subscribe to `FindResultOffset` and call `editor.Select(...)`
    - Keep `ConfigOptCtrl` dynamic options panel (`CreateControlMap()`,
      `ConfigureControls()`) — these take direct control references
    - `OnClosed` calls `ViewModel!.Dispose()`

13. **Register the mapping** in `App.axaml.cs`:
    ```csharp
    ds.Register<FileViewerViewModel, FileViewer>();
    ```

14. **Update the caller** in `MainViewModel.ViewFiles()`.

15. **Build and test:**
    - Select files → Actions → View Files
    - Test prev/next file navigation
    - Test format conversion (switch between converters)
    - Test all fork tabs (Data, Rsrc, Note)
    - Test magnification slider
    - Test Find (forward and backward)
    - Test Export
    - Test Copy to clipboard
    - Test the source-modified warning (modify archive while viewer is open)

### Now that those are done, here's what changed

- **New file:** `ViewModels/FileViewerViewModel.cs`
- **Modified files:** `Tools/FileViewer.axaml.cs`, `Tools/FileViewer.axaml`,
  `App.axaml.cs`, `MainViewModel.cs`
- **New capabilities:** FileViewer logic is testable. The ViewModel is
  self-contained and ready for future multi-instance support (Phase 6).
- **Same behavior:** All viewing functionality works identically.
- **Enables:** Future modeless viewer conversion, unit testing of format
  selection and find logic.

---

## 7. Dialog-Specific Instructions: EditAttributes (~1,050 lines)

### What we are going to accomplish

`EditAttributes` is a modal dialog for editing file metadata: file name,
ProDOS file type/aux type, HFS type/creator, timestamps, access flags,
and comments. It includes validation (is the filename valid? is the
ProDOS type a known value?) with color-coded feedback.

**Key concept: Output via ViewModel properties**

The dialog does not modify the file directly. Instead, it populates a
`NewAttribs` object with the user's changes. When the dialog closes with
OK, the caller reads `vm.NewAttribs` to apply the changes. This is a
common MVVM pattern: the dialog ViewModel is a "proposed edit" that the
caller either commits or discards based on the dialog result.

**Filename validation function:** The ViewModel stores a
`Func<string, bool> _isValidFunc` initialized from the archive/filesystem's
`IsValidFileName` or `IsValidVolumeName` method. This avoids holding a
reference to the archive/filesystem object beyond construction.

**ProDOS type combobox binding:** The ViewModel exposes
`SelectedProTypeItem` (observable `ProTypeListItem?`), initialized in
the constructor by scanning `ProTypeList` for the matching entry. The
setter updates `NewAttribs.FileType` and triggers validation. This
eliminates the code-behind's `Loaded_FileType()` and
`ProTypeCombo_SelectionChanged` handlers — the AXAML binding handles
everything.

### To do that, follow these steps

1. **Create** `cp2_avalonia/ViewModels/EditAttributesViewModel.cs`.

2. **Define the constructor:**
   ```csharp
   public EditAttributesViewModel(
       object archiveOrFileSystem,
       IFileEntry entry,
       IFileEntry adfEntry,
       FileAttribs initialAttribs,
       bool isReadOnly)
   ```
   No service injections are needed here — this dialog has no file pickers,
   clipboard use, or nested dialogs (the OK/Cancel commands use
   `CloseInteraction`, not `IDialogService`).

3. **Move inner types:**
   - `ProTypeListItem` class

4. **Move all bindable properties:**
   - File name, file type, aux type
   - ProDOS type combo items (`ProTypeList`)
   - `SelectedProTypeItem` — observable, setter updates `NewAttribs.FileType`
   - HFS file type, creator
   - Timestamps (create, modify, access)
   - Access flags
   - Comment text
   - Validation error messages
   - Validation-color booleans: replace `SyntaxRulesForeground`,
     `UniqueNameForeground`, `ProAuxForeground`, `HFSTypeForeground`,
     `HFSCreatorForeground`, `CreateWhenForeground`, `ModWhenForeground`
     (`IBrush`) with corresponding `bool` properties (e.g.,
     `IsSyntaxValid`, `IsUniqueNameValid`, etc.)

5. **Store the validation function:**
   ```csharp
   private readonly Func<string, bool> _isValidFunc;
   ```
   Initialize from `arc.IsValidFileName` / `fs.IsValidFileName` /
   `fs.IsValidVolumeName` in the constructor (same logic as the current
   View constructor).

6. **Create commands:**
   - `OkCommand` (with `canExecute` based on validation state) — calls
     `await CloseInteraction.Handle(true)`
   - `CancelCommand` — calls `await CloseInteraction.Handle(false)`

7. **Update the View** (`EditAttributes.axaml.cs`):
   - Change base class to `ReactiveWindow<EditAttributesViewModel>`
   - Register `CloseInteraction` handler
   - Keep `OnOpened`: calls `fileNameTextBox.SelectAll()` and
     `fileNameTextBox.Focus()` — these are UI-only
   - Remove `Loaded_FileType()` — replaced by `SelectedProTypeItem` binding
   - Remove `ProTypeCombo_SelectionChanged` — replaced by binding

8. **Update the AXAML:**
   - Bind `proTypeCombo.SelectedItem` to `SelectedProTypeItem`
   - Replace `IBrush` bindings with boolean-driven style triggers
   - Replace button click handlers with command bindings

9. **Register the mapping:**
   ```csharp
   ds.Register<EditAttributesViewModel, EditAttributes>();
   ```

10. **Update callers** in `MainViewModel.EditAttributes()` and
    `MainViewModel.EditDirAttributes()`:
    ```csharp
    var vm = new EditAttributesViewModel(arcOrFs, entry, adfEntry,
        initialAttribs, isReadOnly);
    var result = await _dialogService.ShowDialogAsync(vm);
    if (result == true) {
        // apply vm.NewAttribs
    }
    ```

11. **Build and test:**
    - Select a file → Actions → Edit Attributes
    - Test filename validation (type invalid name, check color feedback)
    - Test ProDOS type selection from combobox
    - Test HFS type/creator editing
    - Test timestamp editing
    - Test access flag toggles
    - Test OK and Cancel
    - Test read-only mode (verify fields are disabled)

### Now that those are done, here's what changed

- **New file:** `ViewModels/EditAttributesViewModel.cs`
- **Modified files:** `EditAttributes.axaml.cs`, `EditAttributes.axaml`,
  `App.axaml.cs`, `MainViewModel.cs`
- **New capabilities:** Attribute validation logic is testable in isolation.
- **Same behavior:** All attribute editing works identically.
- **Enables:** Unit tests for validation rules, ProDOS type lookup, and
  timestamp parsing.

---

## 8. Dialog-Specific Instructions: CreateDiskImage (~580 lines)

### What we are going to accomplish

`CreateDiskImage` is a modal dialog that creates a new disk image file.
The user selects a disk size, filesystem, file format, and volume
parameters. The dialog validates the combination and creates the file.

**Key concept: Shared enums**

`CreateDiskImage` defines two enums — `DiskSizeValue` and `FileTypeValue` —
that are also referenced by `SaveAsDisk`. Rather than leaving them as
inner types of one dialog, the blueprint moves them to a shared location:
`Models/DiskImageTypes.cs`. This prevents circular dependencies and makes
them accessible to both ViewModels.

**Settings migration:** All `AppSettings.Global.GetEnum/SetEnum/GetString/…`
calls must be replaced with the corresponding `ISettingsService` methods.
This is part of the gradual migration described in MVVM_Notes.md §7.17.

**Cursor management:** The old code changed the cursor to a wait cursor
during disk creation. In MVVM, `ReactiveCommand` has a built-in
`IsExecuting` observable (an `IObservable<bool>`) that emits `true` while
the command is running and `false` when it completes. The code-behind
subscribes to this and changes `this.Cursor` accordingly. The ViewModel
never touches cursor types.

### To do that, follow these steps

1. **Create** `cp2_avalonia/Models/DiskImageTypes.cs` (if not already
   present).
2. **Move** `DiskSizeValue` and `FileTypeValue` enums into this file.
3. **Update all references** to these enums. Search for
   `CreateDiskImage.DiskSizeValue`, `CreateDiskImage.FileTypeValue`, and
   any using aliases (like `SaveAsDisk`'s `using ... = CreateDiskImage.FileTypeValue`).
   Point them to `cp2_avalonia.Models.DiskImageTypes`.

4. **Create** `cp2_avalonia/ViewModels/CreateDiskImageViewModel.cs`.

5. **Define the constructor:**
   ```csharp
   public CreateDiskImageViewModel(
       AppHook appHook,
       IFilePickerService filePickerService,
       ISettingsService settingsService,
       IDialogService dialogService)
   ```

6. **Move all bindable properties:**
   - Disk size selection (list of size options)
   - Filesystem selection (list of supported filesystems per size)
   - File type / container format
   - Volume name, volume number
   - Custom size parameters
   - Reserve boot tracks flag
   - Validation state
   - Validation-color booleans: replace `SizeDescForeground` and
     `SizeLimitForeground` (`IBrush`) with `bool` properties

7. **Replace `AppSettings.Global` calls** with `_settingsService` calls.

8. **Replace `ShowErrorAsync(…)` calls** with
   `await _dialogService.ShowMessageAsync(message, "Error")`.

9. **Create commands:**
   - `OkCommand` — the file-type/extension logic moves into the ViewModel.
     Calls `_filePickerService.SaveFileAsync(…)` instead of
     `SelectOutputFile(TopLevel, …)`. On success, calls
     `await CloseInteraction.Handle(true)`.
   - `CancelCommand` — calls `await CloseInteraction.Handle(false)`

10. **Add output property:** `string? CreatedDiskPath` — set after
    successful creation.

11. **Update the View** (`CreateDiskImage.axaml.cs`):
    - Change base class to `ReactiveWindow<CreateDiskImageViewModel>`
    - Register `CloseInteraction` handler
    - Subscribe to `OkCommand.IsExecuting` and set cursor accordingly

12. **Register the mapping:**
    ```csharp
    ds.Register<CreateDiskImageViewModel, CreateDiskImage>();
    ```

13. **Update the caller** in `MainViewModel.NewDiskImage()`:
    ```csharp
    var vm = new CreateDiskImageViewModel(appHook, _filePickerService,
        _settingsService, _dialogService);
    var result = await _dialogService.ShowDialogAsync(vm);
    if (result == true) {
        // open vm.CreatedDiskPath
    }
    ```

14. **Build and test:**
    - File → New Disk Image
    - Test all disk sizes
    - Test all filesystem types
    - Test all file formats
    - Test custom size parameters
    - Test validation feedback (invalid combinations)
    - Test actual disk creation

### Now that those are done, here's what changed

- **New files:** `ViewModels/CreateDiskImageViewModel.cs`,
  `Models/DiskImageTypes.cs`
- **Modified files:** `CreateDiskImage.axaml.cs`, `CreateDiskImage.axaml`,
  `App.axaml.cs`, `MainViewModel.cs`, `SaveAsDisk.axaml.cs` (using alias
  update), and any other files referencing the moved enums
- **New capabilities:** Disk creation logic testable without UI.
  `DiskSizeValue` and `FileTypeValue` are now shared types.
- **Same behavior:** Disk creation works identically.

---

## 9. Dialog-Specific Instructions: SaveAsDisk (~728 lines)

### What we are going to accomplish

`SaveAsDisk` is a modal dialog that saves an existing disk image in a
different file format. It presents available output formats based on the
current disk's capabilities.

This dialog is closely related to `CreateDiskImage` — they share the
`DiskSizeValue` and `FileTypeValue` enums (now in
`Models/DiskImageTypes.cs`), and `SaveAsDisk` previously used
`CreateDiskImage.SelectOutputFile(this, …)` for the file picker.

**Key concept: `CopyDisk()` static method**

The existing `CopyDisk()` method (`internal static`, ~45 lines) performs
the actual byte-copy from one disk format to another. It is only called
from `CreateImage()` / the OK button handler. It moves to
`SaveAsDiskViewModel` as a `private static` helper. No external callers
need updating.

### To do that, follow these steps

1. **Create** `cp2_avalonia/ViewModels/SaveAsDiskViewModel.cs`.

2. **Define the constructor:**
   ```csharp
   public SaveAsDiskViewModel(
       object currentWorkObject,
       IChunkAccess chunks,
       Formatter formatter,
       AppHook appHook,
       IFilePickerService filePickerService,
       ISettingsService settingsService,
       IDialogService dialogService)
   ```

3. **Move all bindable properties:**
   - File format selection (list of available output formats)
   - Chunk access info (sector count, block count)
   - Estimated output size
   - Selected format's capabilities

4. **Replace `AppSettings.Global` calls** with `_settingsService` methods.

5. **Replace `PlatformUtil.ShowMessageAsync(this, ...)` calls** with
   `await _dialogService.ShowMessageAsync(message, title)`.

6. **Move `CopyDisk()`** to `SaveAsDiskViewModel` as `private static`
   (or to `Models/DiskImageTypes.cs` as a shared utility).

7. **Replace the file picker call**: Instead of
   `CreateDiskImage.SelectOutputFile(this, …)`, call
   `_filePickerService.SaveFileAsync(…)`. Duplicate or refactor the
   file-type/extension logic into the ViewModel (or the shared helper).

8. **Create commands:**
   - `OkCommand` — performs the save. On success, calls
     `await CloseInteraction.Handle(true)`.
   - `CancelCommand` — calls `await CloseInteraction.Handle(false)`

9. **Add output property:** `string? PathName` — set after successful
   creation.

10. **Update the View** (`SaveAsDisk.axaml.cs`):
    - Change base class to `ReactiveWindow<SaveAsDiskViewModel>`
    - Register `CloseInteraction` handler
    - Subscribe to `OkCommand.IsExecuting` for cursor management

11. **Register the mapping:**
    ```csharp
    ds.Register<SaveAsDiskViewModel, SaveAsDisk>();
    ```

12. **Update the caller** in `MainViewModel.SaveAsDiskImage()`.

13. **Build and test:**
    - Actions → Save As Disk Image
    - Test all format options
    - Test actual save operation
    - Verify saved file opens correctly

### Now that those are done, here's what changed

- **New file:** `ViewModels/SaveAsDiskViewModel.cs`
- **Modified files:** `SaveAsDisk.axaml.cs`, `SaveAsDisk.axaml`,
  `App.axaml.cs`, `MainViewModel.cs`
- **New capabilities:** Save-as-disk logic testable without UI.
- **Same behavior:** Save functionality works identically.

---

## 10. Dialog-Specific Instructions: TestManager (~351 lines)

### What we are going to accomplish

`TestManager` (in `LibTest/`) is a **debug-only** dialog that runs library
test suites and displays results with colored progress text and a failure
browser. It is lower priority but follows the same conversion pattern.

**Key concept: Retaining `BackgroundWorker`**

The current code uses `BackgroundWorker` with `WorkerReportsProgress` and
`WorkerSupportsCancellation`. The blueprint specifies **retaining
`BackgroundWorker`** inside the ViewModel rather than converting to
`async ReactiveCommand`. This is a pragmatic choice — `BackgroundWorker`
is well-suited for the progress-reporting pattern here, and converting it
would add complexity without clear benefit for a debug-only dialog.

The worker's `DoWork`, `ProgressChanged`, and `RunWorkerCompleted` handlers
move into the ViewModel. `ProgressChanged` updates observable properties
that the View binds to.

**Colored progress delivery:** The ViewModel exposes an
`IObservable<(string text, Color color)> ProgressAppended` that the
`ProgressChanged` handler pushes colored text into. Code-behind subscribes
and calls `AppendColoredText(text, color)` on the AvaloniaEdit control.
The text colorizer (`DocumentColorizingTransformer`) and `AddSpan()` logic
stay in code-behind — they require direct control manipulation.

### To do that, follow these steps

1. **Create** `cp2_avalonia/ViewModels/TestManagerViewModel.cs`.

2. **Define the constructor:**
   ```csharp
   public TestManagerViewModel(string testLibName, string testIfaceName)
   ```
   No service injections needed.

3. **Move all bindable properties:**
   - `bool IsNotRunning` — enables/disables Run button
   - `string RunButtonLabel` — toggles "Run Test" / "Cancel"
   - `ObservableCollection<TestRunner.TestResult> OutputItems` — failure list
   - `bool IsOutputSelectEnabled` — enables ComboBox
   - `bool IsOutputRetained` — retain-output checkbox
   - Progress text
   - `string SelectedOutputText` — observable, displays exception chain
     for selected failure

4. **Move `BackgroundWorker`** and its handlers (`DoWork`,
   `ProgressChanged`, `RunWorkerCompleted`) into the ViewModel.
   `ProgressChanged` updates observable properties and pushes colored
   text into `ProgressAppended`.

5. **Create the `ProgressAppended` observable** — use a `Subject<(string, Color)>`
   or an event that code-behind subscribes to.

6. **Create `OnOutputSelectChanged(int index)`** — reads `mLastResults[index]`,
   formats the exception chain, sets `SelectedOutputText`.

7. **Create `ResetRequested` event** — code-behind subscribes and clears
   the progress editor and colorizer.

8. **Create commands:**
   - `RunCancelCommand` — starts or cancels the `BackgroundWorker`
   - `CloseCommand` — calls `await CloseInteraction.Handle(false)`

9. **Update the View** (`LibTest/TestManager.axaml.cs`):
    - Change base class to `ReactiveWindow<TestManagerViewModel>`
    - Register `CloseInteraction` handler
    - Keep `Window_Closing` guard (cancels worker if busy)
    - Keep `AppendColoredText()` — subscribes to `ProgressAppended`
    - Keep `OutputSelectComboBox_SelectedIndexChanged` — forwards to
      `ViewModel.OnOutputSelectChanged(index)`
    - Subscribe to `ResetRequested` — clears editor and transformer

10. **Register the mapping:**
    ```csharp
    ds.Register<TestManagerViewModel, TestManager>();
    ```

11. **Update callers** in the debug menu commands.

12. **Build and test:**
    - Debug menu → Run Tests (if available)
    - Verify progress text appears with correct colors
    - Verify failure browser works
    - Test cancel during run
    - Test retain-output checkbox

### Now that those are done, here's what changed

- **New file:** `ViewModels/TestManagerViewModel.cs`
- **Modified files:** `LibTest/TestManager.axaml.cs`, `App.axaml.cs`,
  debug command callers
- **New capabilities:** Test runner logic in ViewModel; progress
  observable for View consumption.
- **Same behavior:** Test runner works identically.

---

## 11. Dialog-Specific Instructions: BulkCompress (~308 lines)

### What we are going to accomplish

`BulkCompress` (in `LibTest/`) is a **debug-only** dialog that benchmarks
compression algorithms. It follows the same `BackgroundWorker` pattern as
`TestManager`.

**Key concepts:**

- **Radio button binding with `EnumToBoolConverter`**: The ViewModel
  exposes `CompressionFormat SelectedCompressionFormat` as a public
  observable property. Each `RadioButton.IsChecked` in the AXAML is
  bound via a value converter (`EnumToBoolConverter`) so the radio group
  drives the property directly — no code-behind forwarding needed.

- **`IFilePickerService`**: The "Choose File" button uses this service
  to select the file to compress. This is the only service injection
  needed.

### To do that, follow these steps

1. **Create** `cp2_avalonia/ViewModels/BulkCompressViewModel.cs`.

2. **Define the constructor:**
   ```csharp
   public BulkCompressViewModel(AppHook appHook, IFilePickerService filePickerService)
   ```

3. **Move all bindable properties:**
   - `bool CanStartRunning` — enables Run button (true when `PathName` is set)
   - `string PathName` — file/directory path, bound to TextBox
   - `string RunButtonLabel` — "Run Test" / "Cancel"
   - `string ProgressMsg` — progress status text
   - `string LogText` — accumulates progress messages. Code-behind subscribes
     via `WhenAnyValue(x => x.LogText)` and sets `logTextBox.Text` and
     `logTextBox.CaretIndex`
   - `bool CanChooseFile` — false during run, true otherwise
   - `CompressionFormat SelectedCompressionFormat` — observable, initialized to
     `CompressionFormat.NuLZW2`

4. **Move `BackgroundWorker`** and handlers into the ViewModel.

5. **Create commands:**
   - `ChooseFileCommand` — calls `_filePickerService.OpenFileAsync(…)`,
     sets `PathName`
   - `RunCancelCommand` — starts or cancels the `BackgroundWorker`
   - `CloseCommand` — calls `await CloseInteraction.Handle(false)`

6. **Update the View** (`LibTest/BulkCompress.axaml.cs`):
    - Change base class to `ReactiveWindow<BulkCompressViewModel>`
    - Register `CloseInteraction` handler
    - Keep `Window_Closing` guard (cancels worker if busy)
    - Subscribe to `WhenAnyValue(x => x.ViewModel.LogText)` for log updates

7. **Update the AXAML:**
    - Bind radio buttons to `SelectedCompressionFormat` via
      `EnumToBoolConverter`
    - Bind `chooseFileButton.IsEnabled` to `CanChooseFile`

8. **Register the mapping:**
    ```csharp
    ds.Register<BulkCompressViewModel, BulkCompress>();
    ```

9. **Update callers** in the debug menu commands.

10. **Build and test:**
    - Debug menu → Bulk Compress (if available)
    - Test Choose File button
    - Test each compression format radio button
    - Test run and cancel
    - Verify progress text and log output

### Now that those are done, here's what changed

- **New file:** `ViewModels/BulkCompressViewModel.cs`
- **Modified files:** `LibTest/BulkCompress.axaml.cs`,
  `LibTest/BulkCompress.axaml`, `App.axaml.cs`, debug command callers
- **New capabilities:** Compression benchmark logic in ViewModel.
- **Same behavior:** Benchmark works identically.

---

## 12. Execution Order

### What we are going to accomplish

This section provides the recommended order for performing all the
conversions. Each step includes a build/test checkpoint.

The order is deliberate:
1. **EditSector first** — it is the most complex. Getting this right
   establishes the pattern and catches any integration issues early.
2. **FileViewer second** — the second-most complex, with unique concerns
   (IDisposable, IConvOutput delivery, magnification).
3. **EditAttributes third** — complex validation logic, but simpler
   than the first two.
4. **CreateDiskImage fourth** — introduces the shared enum extraction.
5. **SaveAsDisk fifth** — depends on the shared enums from step 4.
6. **TestManager and BulkCompress last** — debug-only, lowest risk.

### To do that, follow these steps

**Step 1: Create the `ViewModels/` Directory**

If `cp2_avalonia/ViewModels/` does not already exist (it should from
Iteration 1A where `MainViewModel.cs` was placed there), create it.

**Step 2: Convert EditSector**

Follow the instructions in Section 5 above.
- Build and test: Open a disk image → Actions → Edit Sectors

**Step 3: Convert FileViewer**

Follow the instructions in Section 6 above.
- Build and test: Select files → Actions → View Files

**Step 4: Convert EditAttributes**

Follow the instructions in Section 7 above.
- Build and test: Select file → Actions → Edit Attributes

**Step 5: Convert CreateDiskImage**

Follow the instructions in Section 8 above.
- Build and test: File → New Disk Image

**Step 6: Convert SaveAsDisk**

Follow the instructions in Section 9 above.
- Build and test: Actions → Save As Disk Image

**Step 7: Convert TestManager and BulkCompress**

Follow the instructions in Sections 10 and 11 above.
- Build and test: Debug menu items

**Step 8: Final Build and Validation**

1. Run `dotnet build` — verify zero errors.
2. Systematically test every converted dialog:
   - **EditSector:** navigate sectors, read/write, hex editing, copy,
     dirty-check close guard
   - **FileViewer:** browse files, switch formats, all fork tabs,
     magnify, export, find, copy, source-modified warning
   - **EditAttributes:** change name/type/dates/flags, validation colors,
     save, verify, read-only mode
   - **CreateDiskImage:** all filesystem types, all sizes, custom sizes,
     validation feedback
   - **SaveAsDisk:** all format options, actual save, verify result
   - **TestManager:** run tests, verify colored progress, failure browser,
     cancel
   - **BulkCompress:** choose file, run benchmark, cancel

### Now that those are done, here's what changed

All seven complex dialogs are converted. The application behavior is
identical to before this iteration, but the architecture is fundamentally
improved.

---

## 13. Summary: What This Iteration Accomplished

### Files Created

| File | Purpose |
|---|---|
| `ViewModels/EditSectorViewModel.cs` | ViewModel for hex sector/block editor |
| `ViewModels/FileViewerViewModel.cs` | ViewModel for file content viewer |
| `ViewModels/EditAttributesViewModel.cs` | ViewModel for file attribute editor |
| `ViewModels/CreateDiskImageViewModel.cs` | ViewModel for disk image creation |
| `ViewModels/SaveAsDiskViewModel.cs` | ViewModel for save-as-disk-image |
| `ViewModels/TestManagerViewModel.cs` | ViewModel for test runner (debug) |
| `ViewModels/BulkCompressViewModel.cs` | ViewModel for compression benchmark (debug) |
| `Models/DiskImageTypes.cs` | Shared enums (`DiskSizeValue`, `FileTypeValue`) |

### Files Modified

- Seven dialog `.axaml.cs` files (thinned code-behind)
- Seven dialog `.axaml` files (command bindings, style triggers)
- `App.axaml.cs` (seven new dialog registrations)
- `MainViewModel.cs` (updated dialog callers)
- Possibly `IClipboardService.cs` and `ClipboardService.cs`
  (if `SetTextAsync` was added)
- Various files with updated type references for migrated inner types

### New Capabilities

- All complex dialog logic is testable in isolation
  (ViewModel unit tests can verify validation, navigation, format
  selection, etc. without Avalonia)
- `IDialogService` handles all dialog presentation uniformly
- Dialog ViewModels are independent of View types — no `Window`, `TopLevel`,
  or `IBrush` references in ViewModels
- `FileViewerViewModel` is self-contained and ready for future
  multi-instance support (Phase 6)

### What Stayed the Same

- All user-visible behavior is identical
- The existing libraries (`DiskArc`, `AppCommon`, etc.) are unchanged
- `MainViewModel` continues to orchestrate dialog workflows

### What This Enables Next

- **Phase 4B** converts the remaining ~14 dialogs (medium and simple
  complexity), completing the dialog layer migration
- After Phase 4B, `RelayCommand` can be fully retired
- The self-contained dialog ViewModel pattern established here applies
  directly to all remaining dialogs
