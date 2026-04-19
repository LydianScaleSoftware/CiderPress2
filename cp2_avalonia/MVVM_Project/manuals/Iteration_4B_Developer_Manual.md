# Iteration 4B Developer Manual: Remaining Dialog ViewModels

> **Iteration identifier:** 4B
>
> **Prerequisites:**
> - Iteration 4A is complete (all complex dialog ViewModels created and working).
> - The application builds and runs correctly.
>
> **Reference documents:**
> - `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §3.3, §6 Phase 4B, §7.14
> - `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md`
> - `cp2_avalonia/MVVM_Project/Iteration_4A_Blueprint.md` (for the conversion
>   pattern established there)

---

## Overview

This iteration converts all remaining dialogs — 6 medium-complexity and 8
simple — from code-behind to MVVM ViewModels. When this iteration is done,
**every dialog in the application** will follow the MVVM pattern, and the
`IDialogService` mapping dictionary will be complete. This also makes it
possible to retire the legacy `RelayCommand` class (provided Phase 2
already converted `MainWindow`'s commands).

### Dialogs covered

**Medium Complexity (100–500 lines):**

| Dialog | Lines | Modality | Purpose |
|---|---|---|---|
| `EditConvertOpts` | ~319 | Modal | Import/export converter option configuration |
| `EditAppSettings` | ~246 | Modal | Application-wide settings (tabs, theme, audio) |
| `WorkProgress` | ~248 | Modal | Cancellable async progress with overwrite queries |
| `LogViewer` (Tools) | ~247 | Modeless | Debug log viewer with auto-scroll, save, copy |
| `DropTarget` (Tools) | ~202 | Modeless | Debug clipboard/drag-drop inspector |
| `ReplacePartition` | ~238 | Modal | Replace disk partition from file |

**Simple Complexity (< 175 lines):**

| Dialog | Lines | Modality | Purpose |
|---|---|---|---|
| `CreateDirectory` | ~175 | Modal | Enter directory name with validation |
| `EditMetadata` | ~144 | Modal | Edit a single metadata key/value |
| `CreateFileArchive` | ~130 | Modal | Select archive format (Binary2, NuFX, Zip) |
| `FindFile` | ~135 | Modeless | Search for file by name pattern |
| `AboutBox` | ~113 | Modal | Version, legal, runtime info display |
| `AddMetadata` | ~103 | Modal | Add new metadata entry |
| `OverwriteQueryDialog` | ~93 | Modal | Confirm file overwrite with "apply to all" |
| `ShowText` (Tools) | ~77 | Modal/Modeless | Simple text display window |

---

## Key Concepts for This Iteration

Before diving into the per-dialog steps, here are the MVVM and ReactiveUI
concepts you will use repeatedly throughout this iteration. If you completed
Iteration 4A, these will be familiar — this section serves as a refresher
and quick reference.

### The Conversion Pattern

Every dialog follows the same transformation:

1. **Create a ViewModel class** in `ViewModels/` that extends `ReactiveObject`.
   `ReactiveObject` is ReactiveUI's base class — it replaces the hand-rolled
   `INotifyPropertyChanged` the dialogs currently implement.

2. **Move properties and business logic** out of the `.axaml.cs` code-behind
   into the new ViewModel. Properties use the explicit backing-field pattern:
   ```csharp
   private string _name = string.Empty;
   public string Name {
       get => _name;
       set => this.RaiseAndSetIfChanged(ref _name, value);
   }
   ```
   `RaiseAndSetIfChanged` is a ReactiveUI method that sets the backing field
   and fires `PropertyChanged` only if the value actually changed — no
   manual `OnPropertyChanged("Name")` calls needed.

3. **Add `ReactiveCommand` properties** for button actions. `ReactiveCommand`
   replaces `RelayCommand`. It has built-in async support and automatic
   `CanExecute` tracking via `WhenAnyValue`.

4. **Update the View base class.** This is a two-file change:
   - In the `.axaml.cs` file, change the class declaration:
     ```csharp
     public partial class MyDialog : ReactiveWindow<MyDialogViewModel>
     ```
   - In the `.axaml` file, replace the root `<Window>` / `</Window>` with:
     ```xml
     <rxui:ReactiveWindow x:TypeArguments="vm:MyDialogViewModel"
         xmlns:rxui="http://reactiveui.net"
         xmlns:vm="clr-namespace:cp2_avalonia.ViewModels"
         ...>
     ```
     and change the closing tag to `</rxui:ReactiveWindow>`.

   This enables `this.WhenActivated(...)` — a ReactiveUI lifecycle hook
   that lets you register disposable subscriptions scoped to the window's
   active lifetime.

5. **Strip the code-behind** to UI-only concerns (focus management,
   drag-drop, animation).

6. **Register the ViewModel→View mapping** in `RegisterDialogMappings()`
   so `IDialogService` knows which View to create for each ViewModel.

7. **Update callers** in `MainViewModel` to create the ViewModel and call
   `_dialogService.ShowDialogAsync(vm)` or `_dialogService.ShowModeless(vm)`.

8. **Build and test.**

### Modal Close Mechanism (CloseInteraction)

Modal dialogs need a way for the ViewModel to tell the View "close the
window now, with this result." Since ViewModels must never reference Views
directly (a core MVVM rule), we use ReactiveUI's `Interaction` mechanism.

**What is an `Interaction`?** Think of it as a request-response channel.
The ViewModel *raises* an interaction (makes a request), and the View
*handles* it (provides a response). This inverts control — the ViewModel
says "I want to close" and the View does the actual `Close()` call.

Every modal ViewModel declares:

```csharp
public Interaction<bool, Unit> CloseInteraction { get; } = new();
```

- `bool` is the input — `true` for OK/success, `false` for Cancel.
- `Unit` is the output — we don't need a return value, so we use
  ReactiveUI's `Unit` (equivalent to `void` for reactive types).

Commands that close the dialog call `Handle()`:

```csharp
OkCommand = ReactiveCommand.CreateFromTask(async () => {
    // validation or side-effects here
    await CloseInteraction.Handle(true);
});

CancelCommand = ReactiveCommand.CreateFromTask(async () =>
    await CloseInteraction.Handle(false));
```

**Important:** Because `Handle()` returns `Task<Unit>`, these commands
must be created with `ReactiveCommand.CreateFromTask`, **not**
`ReactiveCommand.Create`. If you use `Create` (the synchronous version),
the `Handle()` call won't be awaited and the dialog may not close.

The View registers a handler in `WhenActivated`:

```csharp
this.WhenActivated(d =>
    ViewModel!.CloseInteraction.RegisterHandler(ctx => {
        Close(ctx.Input);           // Close the window with true/false
        ctx.SetOutput(Unit.Default); // Complete the interaction
    }).DisposeWith(d));
```

`DisposeWith(d)` ensures the handler is automatically unregistered when
the window deactivates — no manual cleanup needed.

### Modeless Lifecycle Observables

Modeless dialogs (windows that stay open alongside the main window) need
a different mechanism. They need:

1. A way for `MainViewModel` to know when the user closes the window
   (so it can null its reference and update menu check marks).
2. A way for `MainViewModel` to programmatically close the window
   (e.g., when the workspace closes).

The pattern uses three members on the ViewModel:

```csharp
// Backing field — View fires this when the window closes
public Subject<Unit> ClosedSubject { get; } = new();

// Public observable — MainViewModel subscribes to this
public IObservable<Unit> Closed => ClosedSubject.AsObservable();

// Interaction — MainViewModel calls Handle() to request close
public Interaction<Unit, Unit> RequestCloseInteraction { get; } = new();
```

In the View's code-behind:
- `Window_Closed` fires `ViewModel!.ClosedSubject.OnNext(Unit.Default)`.
- `WhenActivated` registers a handler for `RequestCloseInteraction` that
  calls `Close()`.

In `MainViewModel`:
- On open: subscribe to `vm.Closed.Take(1)` to null the field and set the
  `IsXxxOpen` property to `false`.
- On close-request: call `await vm.RequestCloseInteraction.Handle(Unit.Default)`.

### IBrush Convention

Several existing dialogs use `IBrush` properties to control text color
(e.g., red for invalid input, gray for hints). ViewModels must not reference
Avalonia types like `IBrush`. The convention is:

- Replace `IBrush` properties with booleans (e.g., `IsSizeCompatible`,
  `IsNameValid`).
- In the AXAML, use a style trigger or value converter to select the
  brush color based on the boolean.

---

## Section 1: ShowText (~77 lines, Tools/)

### What we are going to accomplish

ShowText is the simplest dialog in the entire application — it displays a
read-only text string in a window. Converting it first serves as a warm-up
exercise to practice the conversion pattern with minimal risk.

ShowText has an interesting twist: it's used both modally (for System Info
display) and modelessly (for test failure reports). The same ViewModel class
serves both use cases — the modality is determined by whether the caller
uses `ShowDialogAsync` or `ShowModeless`.

**MVVM context:** This demonstrates that ViewModels are modality-agnostic.
The ViewModel doesn't know or care whether it's shown as a modal or modeless
window. That's a View/service concern.

### To do that, follow these steps

1. **Create** `ViewModels/ShowTextViewModel.cs`:
   ```csharp
   public class ShowTextViewModel : ReactiveObject
   ```
   - Constructor takes `string displayText`.
   - Expose a single property: `public string DisplayText { get; }` (read-only,
     set in constructor). The property name `DisplayText` matches the existing
     AXAML binding — do not rename it.
   - Add `CloseCommand = ReactiveCommand.CreateFromTask(async () => await CloseInteraction.Handle(false));`
   - Add the standard `CloseInteraction`:
     ```csharp
     public Interaction<bool, Unit> CloseInteraction { get; } = new();
     ```

2. **Update** `Tools/ShowText.axaml.cs`:
   - Change the class declaration to:
     ```csharp
     public partial class ShowText : ReactiveWindow<ShowTextViewModel>
     ```
   - Remove `INotifyPropertyChanged` implementation and `DataContext = this`.
   - Remove the `DisplayText` property (it's now on the ViewModel).
   - Add `WhenActivated` to register the `CloseInteraction` handler.
   - Keep `InitializeComponent()`.

3. **Update** `Tools/ShowText.axaml`:
   - Replace `<Window ...>` / `</Window>` with
     `<rxui:ReactiveWindow x:TypeArguments="vm:ShowTextViewModel" ...>` /
     `</rxui:ReactiveWindow>`.
   - Add the `rxui` and `vm` namespace declarations.
   - Remove any `d:DataContext` that references the old code-behind type.

4. **Register** in `RegisterDialogMappings()`:
   ```csharp
   ds.Register<ShowTextViewModel, Tools.ShowText>();
   ```

5. **Update callers** in `MainViewModel`:
   - `Debug_ShowSystemInfo` → `await _dialogService.ShowDialogAsync(new ShowTextViewModel(infoText))`.
   - Test failure reporting → `_dialogService.ShowModeless(new ShowTextViewModel(resultText))`.

6. **Build and test:**
   - `dotnet build`
   - Run the app → Debug → System Info → verify text displays, window closes on button click.

### Now that those are done, here's what changed

- **New file:** `ViewModels/ShowTextViewModel.cs`
- **Modified files:** `Tools/ShowText.axaml`, `Tools/ShowText.axaml.cs`,
  `RegisterDialogMappings()` in `App.axaml.cs`, caller(s) in `MainViewModel`
- **Behavior:** Identical — same text display, same close behavior.
- **New capability:** `ShowTextViewModel` is now unit-testable (you can
  instantiate it, check `DisplayText`, invoke `CloseCommand` — all without
  Avalonia).

---

## Section 2: AboutBox (~113 lines)

### What we are going to accomplish

The AboutBox displays version info, runtime details, and legal text. It's
mostly display-only, making it another low-risk conversion. The interesting
parts are:

- The constructor reads `LegalStuff.txt` from disk using path traversal
  logic (`GetRuntimeDataDir()`). This is pure logic with no Avalonia
  dependency, so it moves to the ViewModel.
- There's a `WebsiteLink_Tapped` handler that opens a URL. This uses a
  static utility (`CommonUtil.ShellCommand.OpenUrl`) and has no testable
  logic — it stays in code-behind.

### To do that, follow these steps

1. **Create** `ViewModels/AboutBoxViewModel.cs`:
   ```csharp
   public class AboutBoxViewModel : ReactiveObject
   ```
   - Move version strings, legal text, and runtime info properties.
   - Move `GetRuntimeDataDir()` and the `File.ReadAllText(LegalStuff.txt)`
     call into the constructor. Preserve the existing `try/catch(Exception)`
     that sets `LegalStuffText` to the error message on failure.
   - Add `CloseCommand`:
     ```csharp
     CloseCommand = ReactiveCommand.CreateFromTask(async () =>
         await CloseInteraction.Handle(false));
     ```
   - Add the standard `CloseInteraction`.

2. **Update** `AboutBox.axaml.cs`:
   - Change to `ReactiveWindow<AboutBoxViewModel>`.
   - Remove `INotifyPropertyChanged`, `DataContext = this`, and all moved
     properties.
   - Keep `WebsiteLink_Tapped` in code-behind — it calls
     `CommonUtil.ShellCommand.OpenUrl` with a hardcoded URL and has no
     testable logic.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `AboutBox.axaml`:
   - Replace root element with `<rxui:ReactiveWindow x:TypeArguments="vm:AboutBoxViewModel" ...>`.
   - Add `rxui` and `vm` namespaces.

4. **Register** in `RegisterDialogMappings()`:
   ```csharp
   ds.Register<AboutBoxViewModel, AboutBox>();
   ```

5. **Update callers** in `MainViewModel`:
   - Help → About → `await _dialogService.ShowDialogAsync(new AboutBoxViewModel())`.

6. **Build and test:**
   - `dotnet build`
   - Run → Help → About → verify version info, legal text, website link.

### Now that those are done, here's what changed

- **New file:** `ViewModels/AboutBoxViewModel.cs`
- **Modified files:** `AboutBox.axaml`, `AboutBox.axaml.cs`,
  `RegisterDialogMappings()`, caller in `MainViewModel`
- **Behavior:** Identical.
- **New capability:** The path-traversal logic and legal text loading are
  now unit-testable.

---

## Section 3: AddMetadata (~103 lines)

### What we are going to accomplish

AddMetadata lets the user add a new metadata entry to a disk image or
archive. It presents a list of available keys, lets the user pick one and
enter a value, and validates the input. This introduces the **IBrush
convention** — the existing dialog uses `IBrush` properties for
color-coding validity. We'll replace those with boolean properties.

**ReactiveUI concept: `canExecute` with `WhenAnyValue`.**
The `OkCommand` should only be enabled when the entered key is valid. In
ReactiveUI, you define this by passing a `canExecute` observable:

```csharp
var canOk = this.WhenAnyValue(x => x.IsKeyValid);
OkCommand = ReactiveCommand.CreateFromTask(async () => {
    await CloseInteraction.Handle(true);
}, canOk);
```

`WhenAnyValue` watches the `IsKeyValid` property and emits a new `bool`
every time it changes. `ReactiveCommand` automatically enables/disables
the button based on the latest value. No manual `CanExecuteChanged`
raising needed.

### To do that, follow these steps

1. **Create** `ViewModels/AddMetadataViewModel.cs`:
   ```csharp
   public class AddMetadataViewModel : ReactiveObject
   {
       public AddMetadataViewModel(IMetadata metadata)
   }
   ```
   - Move key/value input properties, available keys list, validation logic.
   - Replace `KeySyntaxForeground` and `ValueSyntaxForeground` (`IBrush`)
     with booleans: `IsKeyValid`, `IsValueValid`. In the AXAML, use a
     style trigger or value converter to pick the brush color.
   - Commands: `OkCommand` (with `canExecute` based on `IsKeyValid`),
     `CancelCommand`.
   - Output properties (read by the caller after OK): `KeyText`, `ValueText`.
   - Add the standard `CloseInteraction`.
   - `OkCommand` calls `await CloseInteraction.Handle(true)`.
   - `CancelCommand` calls `await CloseInteraction.Handle(false)`.

2. **Update** `AddMetadata.axaml.cs`:
   - Change to `ReactiveWindow<AddMetadataViewModel>`.
   - Remove `INotifyPropertyChanged`, `DataContext = this`, moved properties.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `AddMetadata.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Replace any `IBrush` bindings with boolean-based style triggers or
     value converters.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<AddMetadataViewModel, AddMetadata>();
   ```

5. **Update callers** in `MainViewModel`.

6. **Build and test:**
   - `dotnet build`
   - Run → open a disk image with metadata → Metadata panel → Add →
     verify key list populates, validation works, OK enables/disables.

### Now that those are done, here's what changed

- **New file:** `ViewModels/AddMetadataViewModel.cs`
- **Modified files:** `AddMetadata.axaml`, `AddMetadata.axaml.cs`,
  `RegisterDialogMappings()`, caller in `MainViewModel`
- **IBrush properties** replaced with boolean properties — the ViewModel
  no longer references any Avalonia UI types.
- **Behavior:** Identical.

---

## Section 4: EditMetadata (~144 lines)

### What we are going to accomplish

EditMetadata lets the user edit or delete an existing metadata entry. It
builds on the same patterns as AddMetadata, with one addition: a `CanEdit`
property that controls whether the value text box is editable (some metadata
entries are read-only).

This dialog also introduces a three-button modal pattern: OK, Delete, and
Cancel — each closing the dialog but with different semantic results.

### To do that, follow these steps

1. **Create** `ViewModels/EditMetadataViewModel.cs`:
   ```csharp
   public class EditMetadataViewModel : ReactiveObject
   {
       public EditMetadataViewModel(IMetadata metadata, string key)
   }
   ```
   - Move value property, validation, delete flag.
   - Expose `bool CanEdit`, initialized from
     `metadata.GetMetaEntry(key)!.CanEdit`. When `CanEdit` is false,
     set `ValueSyntaxText` to `"This entry can't be edited."` (otherwise
     set it to `entry.ValueSyntax`).
   - Replace `ValueSyntaxForeground` (`IBrush`) with `bool IsValueValid`.
     Use an AXAML style trigger or value converter for brush color.
   - Commands: `OkCommand`, `DeleteCommand`, `CancelCommand`.
   - Output properties (read by caller after OK): `DoDelete` (bool),
     `KeyText`, `ValueText`.
   - Add the standard `CloseInteraction`.
   - `OkCommand` → `await CloseInteraction.Handle(true)`.
   - `DeleteCommand` → set `DoDelete = true`, then
     `await CloseInteraction.Handle(true)`.
   - `CancelCommand` → `await CloseInteraction.Handle(false)`.

2. **Update** `EditMetadata.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `IsReadOnly="{Binding !CanEdit}"` to the `valueTextBox`.
     Avalonia supports `!` negation in bindings.
   - Replace `IBrush` bindings with boolean-based style triggers.
   - Add `rxui` and `vm` namespaces.

3. **Update** `EditMetadata.axaml.cs`:
   - Change to `ReactiveWindow<EditMetadataViewModel>`.
   - Remove moved properties.
   - The `Window_Opened` focus/select-all logic that checks `CanEdit`
     may remain in code-behind and read `ViewModel!.CanEdit`.
   - Add `WhenActivated` for `CloseInteraction`.

4. **Register:**
   ```csharp
   ds.Register<EditMetadataViewModel, EditMetadata>();
   ```

5. **Update callers** in `MainViewModel`.

6. **Build and test:**
   - `dotnet build`
   - Open a disk image with editable metadata → Edit an entry → verify
     value editing, validation.
   - Open a read-only metadata entry → verify text box is read-only.
   - Test Delete button.

### Now that those are done, here's what changed

- **New file:** `ViewModels/EditMetadataViewModel.cs`
- **Modified files:** `EditMetadata.axaml`, `EditMetadata.axaml.cs`,
  `RegisterDialogMappings()`, caller in `MainViewModel`
- **New pattern:** Three-button modal (OK / Delete / Cancel) with a
  `DoDelete` output property distinguishing the two "success" paths.
- **Behavior:** Identical.

---

## Section 5: CreateFileArchive (~130 lines)

### What we are going to accomplish

CreateFileArchive presents three radio buttons (Binary2, NuFX, Zip) and
remembers the last selection across invocations via `ISettingsService`. This
is a clean example of how ViewModels interact with settings.

**ReactiveUI concept: reading/writing settings in commands.**
The constructor reads the initial value from `_settingsService` to set which
radio button is pre-selected. The `OkCommand` writes the chosen value back
before closing. This keeps the ViewModel's relationship with persistence
explicit and testable.

### To do that, follow these steps

1. **Create** `ViewModels/CreateFileArchiveViewModel.cs`:
   ```csharp
   public class CreateFileArchiveViewModel : ReactiveObject
   {
       public CreateFileArchiveViewModel(ISettingsService settingsService)
   }
   ```
   - Move the three radio-button state properties (`IsChecked_Binary2`,
     `IsChecked_NuFX`, `IsChecked_Zip`) and the backing `FileKind Kind`
     enum value.
   - Constructor reads `NEW_ARC_MODE` from `_settingsService` to initialize
     the selected radio button.
   - `OkCommand` writes
     `_settingsService.SetEnum(AppSettings.NEW_ARC_MODE, Kind)` before
     calling `await CloseInteraction.Handle(true)`.
   - `CancelCommand` → `await CloseInteraction.Handle(false)`.
   - Output property: `Kind` (`FileKind` enum — the selected format).
     Keep the name `Kind` to minimize caller changes.

2. **Update** `CreateFileArchive.axaml.cs`:
   - Change to `ReactiveWindow<CreateFileArchiveViewModel>`.
   - Remove moved properties.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `CreateFileArchive.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<CreateFileArchiveViewModel, CreateFileArchive>();
   ```

5. **Update callers** in `MainViewModel`.

6. **Build and test:**
   - `dotnet build`
   - Run → File → New File Archive → select each format → OK → reopen →
     verify last selection is remembered.

### Now that those are done, here's what changed

- **New file:** `ViewModels/CreateFileArchiveViewModel.cs`
- **Modified files:** `CreateFileArchive.axaml`, `CreateFileArchive.axaml.cs`,
  `RegisterDialogMappings()`, caller in `MainViewModel`
- **Behavior:** Identical — format selection and persistence work the same.
- **New capability:** Settings read/write is now testable through a mock
  `ISettingsService`.

---

## Section 6: CreateDirectory (~175 lines)

### What we are going to accomplish

CreateDirectory lets the user type a directory name, validates it against
filesystem rules, and shows syntax hints. This dialog introduces a
**validation callback pattern**: the caller passes a `Func<string, bool>`
that the ViewModel uses to validate the name. This replaces the existing
inner delegate `IsValidDirNameFunc`.

### To do that, follow these steps

1. **Create** `ViewModels/CreateDirectoryViewModel.cs`:
   ```csharp
   public class CreateDirectoryViewModel : ReactiveObject
   {
       public CreateDirectoryViewModel(
           IFileSystem fileSystem,
           IFileEntry parentDir,
           Func<string, bool> isValidName,
           string syntaxRules)
   }
   ```
   - Move name input, validation, error message.
   - The source defines an inner delegate `IsValidDirNameFunc`. Replace it
     with `Func<string, bool>` for simplicity. **Update all call sites**
     that reference `CreateDirectory.IsValidDirNameFunc` to pass a
     `Func<string, bool>` instead.
   - Replace `SyntaxRulesForeground` and `UniqueNameForeground` (`IBrush`)
     with booleans (e.g., `IsNameValid`, `IsSyntaxHintVisible`). Use AXAML
     style triggers or value converters for brush colors.
   - Commands: `OkCommand` (with `canExecute` when name is valid),
     `CancelCommand`.
   - Output property: `NewFileName` — the validated directory name.
   - Add the standard `CloseInteraction`.

2. **Update** `CreateDirectory.axaml.cs`:
   - Change to `ReactiveWindow<CreateDirectoryViewModel>`.
   - Remove moved properties and the `IsValidDirNameFunc` delegate.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `CreateDirectory.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Replace `IBrush` bindings with boolean-based style triggers.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<CreateDirectoryViewModel, CreateDirectory>();
   ```

5. **Update callers** in `MainViewModel` to pass `Func<string, bool>`
   instead of `CreateDirectory.IsValidDirNameFunc`.

6. **Build and test:**
   - `dotnet build`
   - Run → open a disk image → Actions → Create Directory → type valid
     and invalid names → verify validation feedback and OK enable/disable.

### Now that those are done, here's what changed

- **New file:** `ViewModels/CreateDirectoryViewModel.cs`
- **Modified files:** `CreateDirectory.axaml`, `CreateDirectory.axaml.cs`,
  `RegisterDialogMappings()`, callers in `MainViewModel`
- **Removed:** `IsValidDirNameFunc` inner delegate type — replaced by
  standard `Func<string, bool>`.
- **Behavior:** Identical.

---

## Section 7: OverwriteQueryDialog (~93 lines, Actions/)

### What we are going to accomplish

OverwriteQueryDialog is shown during file operations (add, extract, paste)
when a file already exists at the destination. It lets the user choose
Overwrite, Skip, or Cancel, and optionally apply the choice to all remaining
conflicts.

This dialog has a **three-result pattern** that differs from the standard
OK/Cancel: the caller needs to know not just "did the user confirm?" but
*which* action they chose. The pattern uses a `Result` property that the
caller reads after `ShowDialogAsync` returns `true`.

### To do that, follow these steps

1. **Create** `ViewModels/OverwriteQueryViewModel.cs`:
   ```csharp
   public class OverwriteQueryViewModel : ReactiveObject
   {
       public OverwriteQueryViewModel(CallbackFacts facts)
   }
   ```
   - Move file detail display properties (name, size, dates).
   - Move the "apply to all" checkbox state (`UseForAll`).
   - Commands:
     - `OverwriteCommand` — sets `Result = CallbackFacts.Results.Overwrite`,
       then `await CloseInteraction.Handle(true)`.
     - `SkipCommand` — sets `Result = CallbackFacts.Results.Skip`,
       then `await CloseInteraction.Handle(true)`.
     - `CancelCommand` — `await CloseInteraction.Handle(false)`.
   - Both Overwrite and Skip close with `true` because they are "successful"
     user decisions. Cancel closes with `false`. The caller reads `Result`
     and `UseForAll` **only** when `ShowDialogAsync` returns `true`.
   - Add the standard `CloseInteraction`.

2. **Update** `Actions/OverwriteQueryDialog.axaml.cs`:
   - Change to `ReactiveWindow<OverwriteQueryViewModel>`.
   - Remove moved properties.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `Actions/OverwriteQueryDialog.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<OverwriteQueryViewModel, Actions.OverwriteQueryDialog>();
   ```

5. **Update callers.** This dialog is called from `WorkProgressViewModel`'s
   `ProgressChanged` handler (see Section 14 below). For now, register the
   mapping — the caller update will happen when WorkProgress is converted.

6. **Build and test:**
   - `dotnet build`
   - Trigger an overwrite conflict (e.g., add duplicate files) → verify
     Overwrite, Skip, and Cancel all work → verify "apply to all" checkbox.

### Now that those are done, here's what changed

- **New file:** `ViewModels/OverwriteQueryViewModel.cs`
- **Modified files:** `Actions/OverwriteQueryDialog.axaml`,
  `Actions/OverwriteQueryDialog.axaml.cs`, `RegisterDialogMappings()`
- **New pattern:** Three-result modal where the caller reads a `Result`
  property to distinguish between Overwrite and Skip.
- **Behavior:** Identical.

---

## Section 8: FindFile (~135 lines)

### What we are going to accomplish

FindFile searches for files by name pattern within the archive tree. This
dialog is more architecturally interesting than it looks:

1. **Modality change:** The existing code uses `ShowDialog` (modal). This
   intentionally changes to **modeless** via `_dialogService.ShowModeless()`.
   This means the user can keep the find window open while navigating results.

2. **Observable-based communication:** Instead of returning a result when
   the dialog closes, `FindFile` fires search requests as an
   `IObservable<FindFileReq>` that `MainViewModel` subscribes to. Each time
   the user clicks Find Next or Find Prev, the observable emits a new
   request with the search parameters.

3. **Persistent search state:** The current code preserves the last search
   configuration across dialog invocations using `static` fields. We
   replicate this with `static` fields on `FindFileViewModel`.

**ReactiveUI concept: `Subject<T>` as an event source.**
A `Subject<T>` is both an `IObservable<T>` (something you can subscribe to)
and an `IObserver<T>` (something you can push values into). It's the
reactive equivalent of a C# `event` — the ViewModel pushes values in,
and external subscribers react.

### To do that, follow these steps

1. **Create** `Models/FindFileReq.cs`:
   - Move the `FindFileReq` inner class here. This is a simple data class
     holding the search text, direction, and case-sensitivity flag.

2. **Create** `ViewModels/FindFileViewModel.cs`:
   ```csharp
   public class FindFileViewModel : ReactiveObject
   ```
   - Move search text, search options (forward/backward, case-sensitivity).
   - Create `Subject<FindFileReq> FindRequestedSubject` (private) and
     expose `IObservable<FindFileReq> FindRequested` (public).
   - `FindNextCommand` and `FindPrevCommand` push a `FindFileReq` onto
     the subject with the current search parameters.
   - `FindNextCommand` / `FindPrevCommand` handlers must **update** the
     static fields from the current property values before executing
     (equivalent to the existing `SaveConfig()` call).
   - Add `static` fields for `sLastSearch` and `sCurrentArchiveOnly` to
     preserve search configuration across invocations.
   - `CloseCommand` → `await CloseInteraction.Handle(false)`.
   - Add the standard `CloseInteraction`.
   - Add the modeless lifecycle observables: `ClosedSubject`, `Closed`,
     `RequestCloseInteraction`.

3. **Update** `FindFile.axaml.cs`:
   - Change to `ReactiveWindow<FindFileViewModel>`.
   - Remove moved properties.
   - Add `WhenActivated` for `CloseInteraction` and `RequestCloseInteraction`.
   - Add `Window_Closed` handler that fires
     `ViewModel!.ClosedSubject.OnNext(Unit.Default)`.

4. **Update** `FindFile.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `Closed="Window_Closed"` if not already present.
   - Add `rxui` and `vm` namespaces.

5. **Register:**
   ```csharp
   ds.Register<FindFileViewModel, FindFile>();
   ```

6. **Update `MainViewModel`:**
   - Hold a `FindFileViewModel?` field.
   - The "Find Files" action:
     - If null → create VM, call `_dialogService.ShowModeless(findFileVm)`.
       Subscribe to `FindRequested` to handle search navigation.
       Subscribe to `Closed.Take(1)` to null the field and dispose the
       `FindRequested` subscription.
     - If not null → bring the existing window to focus (or close/reopen).
   - When workspace closes, close FindFile via
     `await _findFileVm.RequestCloseInteraction.Handle(Unit.Default)` to
     avoid stale-selection errors on an empty archive tree.
   - The `FindRequested` subscriber replaces the existing `DoFindFiles` in
     `MainController`. This handler accesses `SelectedArchiveTreeItem`,
     `ArchiveTreeRoot`, `FileList`, and `PostNotification` — these must
     already be available as properties on `MainViewModel` (established in
     earlier phases). The `FindFileState` private class and `FindInTree`
     helper also migrate into `MainViewModel` (or a dedicated service).

7. **Build and test:**
   - `dotnet build`
   - Run → open an archive → Actions → Find Files → search → verify results
     navigate, window stays open, search state persists.
   - Close the workspace → verify FindFile window closes automatically.

### Now that those are done, here's what changed

- **New files:** `ViewModels/FindFileViewModel.cs`, `Models/FindFileReq.cs`
- **Modified files:** `FindFile.axaml`, `FindFile.axaml.cs`,
  `RegisterDialogMappings()`, `MainViewModel`
- **Modality change:** Modal → modeless.
- **New pattern:** Observable-based communication between a modeless dialog
  and `MainViewModel` via `Subject<FindFileReq>`.
- **Behavior:** Search works the same, but the window now stays open.

---

## Section 9: ReplacePartition (~238 lines)

### What we are going to accomplish

ReplacePartition lets the user replace a disk partition's contents from a
source file. It validates compatibility (size, format), shows a comparison,
and performs the actual disk copy. This dialog introduces two patterns:

1. **Delegate migration:** The source defines an `EnableWriteFunc` delegate
   type inside the dialog class. We move it to the ViewModel.

2. **`Task.Run` for I/O-bound work:** The OK action calls
   `DiskImageTypes.CopyDisk()`, which iterates every sector/block in a tight
   loop. We wrap this in `Task.Run(...)` to avoid blocking the UI thread.

### To do that, follow these steps

1. **Create** `ViewModels/ReplacePartitionViewModel.cs`:
   ```csharp
   public class ReplacePartitionViewModel : ReactiveObject
   {
       public ReplacePartitionViewModel(
           Partition destination,
           IChunkAccess sourceChunks,
           EnableWriteFunc writeFunc,
           Formatter formatter,
           AppHook appHook,
           IDialogService dialogService)
   }
   ```
   - Move `EnableWriteFunc` delegate definition here. Update all external
     references (e.g., `ReplacePartition.EnableWriteFunc` →
     `ReplacePartitionViewModel.EnableWriteFunc`).
   - Move compatibility validation, source/dest info display.
   - Replace `SizeDiffForeground` (`IBrush`) with `bool IsSizeCompatible`.
     Use an AXAML style trigger or value converter for brush color.
   - Replace `PlatformUtil.ShowMessageAsync(this, ...)` calls with
     `await _dialogService.ShowMessageAsync(message, caption)`.
   - `OkCommand` body: wrap `DiskImageTypes.CopyDisk` in `Task.Run`:
     ```csharp
     int errorCount = 0;
     await Task.Run(() => {
         DiskImageTypes.CopyDisk(_srcChunks, _dstChunks, out int ec);
         errorCount = ec;
     });
     ```
     **Note:** `CopyDisk` must be in `Models/DiskImageTypes.cs` as
     `internal static` (placed there during Phase 4A). Both
     `SaveAsDiskViewModel` and `ReplacePartitionViewModel` share this
     method.
   - Commands: `OkCommand` (with `canExecute` based on compatibility),
     `CancelCommand`.
   - Add the standard `CloseInteraction`.

2. **Update** `ReplacePartition.axaml.cs`:
   - Change to `ReactiveWindow<ReplacePartitionViewModel>`.
   - Remove moved properties, the `EnableWriteFunc` delegate definition.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `ReplacePartition.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Replace `IBrush` bindings with boolean-based style triggers.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<ReplacePartitionViewModel, ReplacePartition>();
   ```

5. **Update callers** in `MainViewModel`.

6. **Build and test:**
   - `dotnet build`
   - Open a multi-partition disk image → Actions → Replace Partition →
     verify compatibility display, copy operation.

### Now that those are done, here's what changed

- **New file:** `ViewModels/ReplacePartitionViewModel.cs`
- **Modified files:** `ReplacePartition.axaml`, `ReplacePartition.axaml.cs`,
  `RegisterDialogMappings()`, callers in `MainViewModel`
- **Moved:** `EnableWriteFunc` delegate from View to ViewModel.
- **`IBrush` replaced** with boolean `IsSizeCompatible`.
- **Threading:** `CopyDisk` now runs on a background thread via `Task.Run`.
- **Behavior:** Identical, but UI stays responsive during copy.

---

## Section 10: EditAppSettings (~246 lines)

### What we are going to accomplish

EditAppSettings is the application-wide settings dialog. It's more complex
than the simple dialogs because:

1. **Sub-dialog launching:** The "Configure Import Options" and "Configure
   Export Options" buttons open `EditConvertOpts` as a child dialog. This
   demonstrates **ViewModel-to-ViewModel dialog chaining** via
   `IDialogService`.

2. **Settings commit exception:** The source uses
   `AppSettings.Global.ReplaceSettings(mSettings)` — a bulk-replace. Since
   `ISettingsService` has no `ReplaceSettings` method, this ViewModel either
   accesses `AppSettings.Global` directly (documented exception) or writes
   individual `_settingsService.Set*()` calls.

3. **Retiring `SettingsAppliedHandler`:** The old `SettingsApplied` event is
   replaced by `ISettingsService.SettingChanged`. `MainViewModel` subscribes
   to `SettingChanged` to re-apply theme and other app-wide settings
   reactively.

### To do that, follow these steps

1. **Create** `ViewModels/EditAppSettingsViewModel.cs`:
   ```csharp
   public class EditAppSettingsViewModel : ReactiveObject
   {
       public EditAppSettingsViewModel(ISettingsService settingsService,
           IDialogService dialogService)
   }
   ```
   - Move all settings properties (theme mode, audio algorithm, feature
     toggles).
   - Move Apply logic.
   - Commands: `OkCommand`, `ApplyCommand`, `CancelCommand`,
     `ConfigureImportOptionsCommand`, `ConfigureExportOptionsCommand`.
   - `ConfigureImportOptionsCommand`:
     ```csharp
     ConfigureImportOptionsCommand = ReactiveCommand.CreateFromTask(async () => {
         var vm = new EditConvertOptsViewModel(false, _settingsService);
         bool? result = await _dialogService.ShowDialogAsync(vm);
         if (result == true)
             RefreshFromGlobal();
     });
     ```
     `RefreshFromGlobal()` re-reads properties that `EditConvertOpts.OkCommand`
     may have updated in `AppSettings.Global` (equivalent to the source's
     `OnSettingsApplied()`).
   - `ConfigureExportOptionsCommand` — same pattern with `true` for export.
   - **Settings commit:** Choose one approach:
     - **Option 1 (direct access):** `ApplyCommand` calls
       `AppSettings.Global.ReplaceSettings(mSettings)` directly. Document
       this as a deliberate exception alongside the `EditConvertOpts`
       exception.
     - **Option 2 (individual writes):** Write each setting via
       `_settingsService.Set*()`. Consider a guard flag in `MainViewModel`
       to suppress per-key reactions during batch writes.
   - On OK/Apply, writing through `_settingsService` raises `SettingChanged`
     for each key. `MainViewModel` subscribes and re-applies reactively —
     no additional notification event needed.
   - Add the standard `CloseInteraction`.

2. **Retire `SettingsAppliedHandler`:**
   - Remove the `SettingsAppliedHandler` delegate type.
   - Remove the `SettingsApplied` event from the old dialog.
   - Remove the controller's subscription to `SettingsApplied`.
   - These are replaced by `ISettingsService.SettingChanged`.

3. **Update** `EditAppSettings.axaml.cs`:
   - Change to `ReactiveWindow<EditAppSettingsViewModel>`.
   - Remove moved properties, the `SettingsApplied` event.
   - Add `WhenActivated` for `CloseInteraction`.

4. **Update** `EditAppSettings.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `rxui` and `vm` namespaces.

5. **Register:**
   ```csharp
   ds.Register<EditAppSettingsViewModel, EditAppSettings>();
   ```

6. **Update callers** in `MainViewModel`.

7. **Build and test:**
   - `dotnet build`
   - Run → Edit → Application Settings → change theme → Apply → verify
     theme changes.
   - Configure Import Options → make a change → OK → verify it persists.
   - Configure Export Options → same.
   - Close and reopen settings → verify persistence.

### Now that those are done, here's what changed

- **New file:** `ViewModels/EditAppSettingsViewModel.cs`
- **Modified files:** `EditAppSettings.axaml`, `EditAppSettings.axaml.cs`,
  `RegisterDialogMappings()`, caller in `MainViewModel`
- **Removed:** `SettingsAppliedHandler` delegate and `SettingsApplied` event.
- **New pattern:** Sub-dialog chaining via `IDialogService`.
- **Behavior:** Identical — settings changes apply the same way.

---

## Section 11: EditConvertOpts (~319 lines)

### What we are going to accomplish

EditConvertOpts configures import/export converter options. It's the most
complex medium dialog because of its dynamic control generation — the
available options change based on the selected converter.

**Key architectural decision — settings commit exception:**
This is the one ViewModel (besides `EditAppSettings`) that accesses
`AppSettings.Global` directly. The reason: the existing code accumulates
changes into an empty `SettingsHolder`, then merges them all at once via
`MergeSettings()`. `ISettingsService` has no equivalent for this
accumulated-merge pattern, and `SettingsHolder` doesn't expose its keys
for enumeration. So `OkCommand` calls
`AppSettings.Global.MergeSettings(mSettings)` directly. This is a
deliberate, documented exception.

### To do that, follow these steps

1. **Create** `ViewModels/EditConvertOptsViewModel.cs`:
   ```csharp
   public class EditConvertOptsViewModel : ReactiveObject
   {
       public EditConvertOptsViewModel(bool isExport, ISettingsService settingsService)
   }
   ```
   - The parameter is `isExport` (true = export, false = import). **Do not
     rename** to `isImport` — this matches the existing source convention.
   - Move converter option mapping logic. Dynamic control generation stays
     in code-behind (or uses DataTemplates).
   - Move `ConverterListItem` inner class to `EditConvertOptsViewModel.cs`
     (or `Models/ConverterListItem.cs`).
   - **Do not move** `ConfigOptCtrl` helper classes (`ControlMapItem`,
     `ToggleButtonMapItem`, `TextBoxMapItem`, `RadioButtonGroupItem` in
     `Tools/ConfigOptCtrl.cs`). These reference Avalonia control types and
     must remain in the View layer.
   - Read/write settings via `_settingsService` (not raw `SettingsHolder`)
     except for the `OkCommand` merge.
   - `OkCommand` calls `AppSettings.Global.MergeSettings(mSettings)`
     directly. Document as deliberate exception.
   - Commands: `OkCommand`, `CancelCommand`.
   - Add the standard `CloseInteraction`.

2. **Update** `EditConvertOpts.axaml.cs`:
   - Change to `ReactiveWindow<EditConvertOptsViewModel>`.
   - Remove moved properties.
   - Keep dynamic control generation logic in code-behind.
   - Add `WhenActivated` for `CloseInteraction`.

3. **Update** `EditConvertOpts.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<EditConvertOptsViewModel, EditConvertOpts>();
   ```

5. **Update callers.** `EditAppSettingsViewModel` already calls this via
   `_dialogService.ShowDialogAsync()` (established in Section 10).

6. **Build and test:**
   - `dotnet build`
   - Run → Edit → Application Settings → Configure Import Options →
     change settings → OK → verify persistence.
   - Same for Export Options.

### Now that those are done, here's what changed

- **New file:** `ViewModels/EditConvertOptsViewModel.cs`
- **Modified files:** `EditConvertOpts.axaml`, `EditConvertOpts.axaml.cs`,
  `RegisterDialogMappings()`, callers
- **Settings commit exception:** `OkCommand` accesses `AppSettings.Global`
  directly — documented alongside the `EditAppSettings` exception as the
  two ViewModels that bypass `ISettingsService` for bulk operations.
- **Behavior:** Identical.

---

## Section 12: LogViewer (~247 lines, Tools/)

### What we are going to accomplish

LogViewer is a modeless debug window that displays the application's debug
log with auto-scroll, save-to-file, and copy-to-clipboard features. This is
a richer modeless dialog than FindFile — it introduces:

1. **Event subscription with thread marshaling.** Log events fire on
   background threads. The ViewModel must marshal them to the UI thread
   before adding to its `ObservableCollection`.

2. **Save/Copy interactions.** The ViewModel can't directly access the
   filesystem or clipboard (those are View-level/platform concerns). It
   uses `Interaction<string, Unit>` to request these operations from the
   View's code-behind.

3. **Modeless toggle pattern.** `MainViewModel` tracks whether the window
   is open and toggles it on/off.

**ReactiveUI concept: `Dispatcher.UIThread.Post` for thread marshaling.**
When a background thread needs to update a UI-bound collection, it must
marshal the update to the UI thread. Avalonia's `Dispatcher.UIThread.Post()`
queues a delegate to run on the UI thread. This is necessary because
`ObservableCollection` is not thread-safe, and Avalonia (like all UI
frameworks) requires property changes to happen on the UI thread.

### To do that, follow these steps

1. **Create** `ViewModels/LogViewerViewModel.cs`:
   ```csharp
   public class LogViewerViewModel : ReactiveObject
   {
       public LogViewerViewModel(DebugMessageLog messageLog)
   }
   ```
   - Hold `ObservableCollection<LogEntry> LogEntries` and `bool AutoScroll`.
   - In the constructor, replay stored entries and subscribe to new log events:
     ```csharp
     foreach (var e in messageLog.GetLogs())
         LogEntries.Add(new LogEntry(e));
     messageLog.RaiseLogEvent += HandleLogEvent;
     ```
   - `HandleLogEvent` fires on background threads — marshal to the UI thread:
     ```csharp
     private void HandleLogEvent(object? s, DebugMessageLog.LogEventArgs e) =>
         Dispatcher.UIThread.Post(() => LogEntries.Add(new LogEntry(e.Entry)));
     ```
   - **Unsubscription method:** Provide a method (called from the View's
     `Window_Closed` handler) that unsubscribes:
     `_messageLog.RaiseLogEvent -= HandleLogEvent`.
   - Move `LogEntry` wrapper class to `Models/LogEntry.cs` (or nest it in
     `LogViewerViewModel.cs`). Update the AXAML namespace if needed.
   - `SaveCommand` raises `SaveInteraction` (`Interaction<string, Unit>`):
     ```csharp
     public Interaction<string, Unit> SaveInteraction { get; } = new();
     ```
     The ViewModel provides formatted log text as input. The View handler
     calls `StorageProvider.SaveFilePickerAsync(...)` and writes via
     `IStorageFile.OpenWriteAsync()`. **Do not** use
     `File.WriteAllText(path, ...)` — the `IStorageFile` stream may not
     correspond to an accessible file path (important on macOS App Sandbox).
   - `CopyCommand` raises `CopyInteraction` (`Interaction<string, Unit>`):
     ```csharp
     public Interaction<string, Unit> CopyInteraction { get; } = new();
     ```
     The View handler calls
     `TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(ctx.Input)`.
     `IClipboardService` is scoped to CP2's internal `ClipInfo` format and
     has no `SetTextAsync(string)` method, so plain-text clipboard access
     stays in code-behind.
   - Commands: `SaveCommand`, `CopyCommand`.
   - Add modeless lifecycle observables: `ClosedSubject`, `Closed`,
     `RequestCloseInteraction`.

2. **Update** `Tools/LogViewer.axaml.cs`:
   - Change to `ReactiveWindow<LogViewerViewModel>`.
   - Remove moved properties.
   - Keep the auto-scroll logic (`logListBox.AddHandler(ScrollViewer.ScrollChangedEvent, ...)`)
     in code-behind.
   - Add `WhenActivated` for `RequestCloseInteraction`, `SaveInteraction`,
     and `CopyInteraction`.
   - `Window_Closed` handler: call the ViewModel's unsubscription method,
     then fire `ViewModel!.ClosedSubject.OnNext(Unit.Default)`.

3. **Update** `Tools/LogViewer.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<LogViewerViewModel, Tools.LogViewer>();
   ```

5. **Update `MainViewModel`:**
   - Hold a `LogViewerViewModel?` field.
   - `Debug_ShowDebugLog()` toggles:
     - If null → create VM, call `_dialogService.ShowModeless(vm)`.
       Subscribe to `vm.Closed.Take(1)` to null the field and set
       `IsDebugLogOpen = false`.
     - If not null → call
       `await _logViewerVm.RequestCloseInteraction.Handle(Unit.Default)`.
   - Expose `bool IsDebugLogOpen` as a reactive property for the AXAML
     menu check-mark binding.

6. **Build and test:**
   - `dotnet build`
   - Run → Debug → Show Debug Log → verify log entries appear, auto-scroll
     works.
   - Save log to file, copy to clipboard — verify content.
   - Toggle the menu item → verify window opens and closes.
   - Close via the X button → verify `MainViewModel` knows it's closed.

### Now that those are done, here's what changed

- **New files:** `ViewModels/LogViewerViewModel.cs`,
  `Models/LogEntry.cs` (if extracted)
- **Modified files:** `Tools/LogViewer.axaml`, `Tools/LogViewer.axaml.cs`,
  `RegisterDialogMappings()`, `MainViewModel`
- **New patterns:** Thread-marshaled event subscription, Save/Copy
  interactions, modeless toggle management.
- **Behavior:** Identical.

---

## Section 13: DropTarget (~202 lines, Tools/)

### What we are going to accomplish

DropTarget is a debug-only modeless window that inspects clipboard and
drag-drop data. Its ViewModel is thin — it holds the formatted result text
as a reactive property. All the heavy lifting (processing `DragEventArgs`,
raw clipboard access via `TopLevel`) stays in code-behind because it
requires Avalonia types.

This dialog uses the same modeless toggle pattern as LogViewer.

### To do that, follow these steps

1. **Create** `ViewModels/DropTargetViewModel.cs`:
   ```csharp
   public class DropTargetViewModel : ReactiveObject
   ```
   - Expose `string TextArea` as a reactive property (set by View
     code-behind after processing drag/drop or paste events).
   - Add modeless lifecycle observables: `ClosedSubject`, `Closed`,
     `RequestCloseInteraction`.

2. **Update** `Tools/DropTarget.axaml.cs`:
   - Change to `ReactiveWindow<DropTargetViewModel>`.
   - Remove moved properties.
   - **Keep in code-behind:**
     - Programmatic `DragDrop.DropEvent` / `DragDrop.DragOverEvent` handler
       registration.
     - `DoPasteAsync()` — raw clipboard access via `TopLevel`.
     - `TextArea_Drop()` / `TextArea_DragOver()` — take Avalonia
       `DragEventArgs`.
     - `ShowDataObject(IDataObject)` — takes Avalonia `IDataObject`.
   - After processing, set `ViewModel!.TextArea = formattedText`.
   - Add `WhenActivated` for `RequestCloseInteraction`.
   - Add `Window_Closed` handler:
     ```csharp
     private void Window_Closed(object? sender, EventArgs e) {
         ViewModel!.ClosedSubject.OnNext(Unit.Default);
     }
     ```

3. **Update** `Tools/DropTarget.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `Closed="Window_Closed"` to the root element (the existing AXAML
     has no `Closed=` attribute).
   - Add `rxui` and `vm` namespaces.

4. **Register:**
   ```csharp
   ds.Register<DropTargetViewModel, Tools.DropTarget>();
   ```

5. **Update `MainViewModel`:**
   - Hold a `DropTargetViewModel?` field.
   - `Debug_ShowDropTarget()` toggles open/close using the same pattern as
     LogViewer.
   - Expose `bool IsDropTargetOpen` as a reactive property for the AXAML
     menu check-mark binding.

6. **Build and test:**
   - `dotnet build`
   - Run → Debug → Show Drop Target → drag files onto it → verify text
     displays.
   - Toggle the menu item → verify window opens and closes.

### Now that those are done, here's what changed

- **New file:** `ViewModels/DropTargetViewModel.cs`
- **Modified files:** `Tools/DropTarget.axaml`, `Tools/DropTarget.axaml.cs`,
  `RegisterDialogMappings()`, `MainViewModel`
- **Behavior:** Identical.
- **Pattern reuse:** Same modeless toggle management as LogViewer.

---

## Section 14: WorkProgress (~248 lines)

### What we are going to accomplish

WorkProgress is the most impactful dialog in this iteration — it's used by
nearly every file operation (add, extract, delete, test, defragment, copy,
paste, scan). It shows a cancellable progress bar and handles overwrite
queries as sub-dialogs.

This dialog introduces several advanced patterns:

1. **`BackgroundWorker` retention.** The existing code uses
   `BackgroundWorker` for async operations. We keep it rather than switching
   to `Task`+`IProgress<T>`, which would require changing the `IWorker`
   interface and all its callers.

2. **Post-construction `IDialogService` injection.** The ViewModel is
   created in `MainViewModel` before the `WorkProgress` View exists, so it
   can't receive a WorkProgress-owned `IDialogService` in its constructor.
   Instead, the View injects it after construction.

3. **Nested type migration.** `WorkProgress` defines three nested types
   (`IWorker`, `OverwriteQuery`, `MessageBoxQuery`) that other files
   reference. Moving them to `WorkProgressViewModel` requires updating all
   references.

4. **Static registration sharing.** The ad-hoc `DialogService` instance
   created for the WorkProgress window must share the static registration
   dictionary, or it won't know how to create `OverwriteQueryDialog` Views.

### To do that, follow these steps

1. **Create** `ViewModels/WorkProgressViewModel.cs`:
   ```csharp
   public class WorkProgressViewModel : ReactiveObject
   {
       public WorkProgressViewModel(IWorker worker, bool isIndeterminate)
   }
   ```
   - Move progress state, cancel flag, status text, progress percentage.
   - **Keep `BackgroundWorker`** inside the ViewModel. The
     `ProgressChanged` handler body moves here. Since `ProgressChanged`
     fires on the UI thread (via `SynchronizationContext`), no additional
     `Dispatcher` marshaling is needed for property updates.
   - Move nested types to this file:
     - `IWorker` — 7 classes in `Actions/` implement
       `WorkProgress.IWorker`; update them to
       `WorkProgressViewModel.IWorker`.
     - `OverwriteQuery` — `ProgressUtil.cs` creates
       `new WorkProgress.OverwriteQuery(...)`; update to
       `WorkProgressViewModel.OverwriteQuery`.
     - `MessageBoxQuery` — `ProgressUtil.cs` creates
       `new WorkProgress.MessageBoxQuery(...)` (4 call sites); update to
       `WorkProgressViewModel.MessageBoxQuery`.
   - **Post-construction `IDialogService` injection:**
     ```csharp
     internal void SetDialogService(IDialogService ds)
     ```
     The `BackgroundWorker` must not start until `SetDialogService` has
     been called. Guard this by having `RunWorkerAsync()` called from
     `SetDialogService` itself, or by checking a flag.
   - **OverwriteQuery handling** (inside `ProgressChanged`, already on UI
     thread):
     ```csharp
     var oqVm = new OverwriteQueryViewModel(oq.Facts);
     bool? ok = await _dialogService.ShowDialogAsync(oqVm);
     if (ok == true) {
         oq.SetResult(oqVm.Result, oqVm.UseForAll);
     } else {
         oq.SetResult(CallbackFacts.Results.Cancel, false);
     }
     ```
     Assign the ViewModel to a local variable before awaiting so result
     properties can be read afterwards.
   - **MessageBoxQuery handling:** Same pattern — show a message dialog
     via `_dialogService.ShowMessageAsync(...)`, pulse the waiting worker
     thread with the result.
   - **Closure mechanism:** Expose `CloseInteraction`
     (`Interaction<bool, Unit>`). `RunWorkerCompleted` raises it with the
     boolean result. The View registers a handler via `WhenActivated`.
   - **Caller result pattern:** Callers read the result from
     `ShowDialogAsync`'s `bool?` return value directly:
     ```csharp
     bool? ok = await _dialogService.ShowDialogAsync(progressVM);
     if (ok == true) { /* success path */ }
     ```
     Do **not** expose a separate `DialogResult` property. The existing
     6 call sites that read `workDialog.DialogResult` must be rewritten.
   - Commands: `CancelCommand`.

2. **Handle `DialogService` registration sharing:**
   - The `DialogService` registration dictionary must be **static** so
     all instances (including the ad-hoc one created for WorkProgress)
     share the mappings registered at startup.
   - Without this, `ShowDialogAsync<OverwriteQueryViewModel>()` would
     throw at runtime because the per-window instance has no registered
     mappings.
   - Add `internal static void ClearMappings()` for test fixture cleanup.

3. **Update** `Common/WorkProgress.axaml.cs`:
   - Change to `ReactiveWindow<WorkProgressViewModel>`.
   - Remove moved properties, nested types, `BackgroundWorker`.
   - In the `Activated` or `OnLoaded` handler:
     ```csharp
     ViewModel!.SetDialogService(new DialogService(this));
     ```
     where `this` (the `WorkProgress` window) implements `IDialogHost`.
     This creates a `DialogService` without DI — a deliberate exception
     since the owner window is transient.
   - Add `WhenActivated` for `CloseInteraction`.

4. **Update** `Common/WorkProgress.axaml`:
   - Replace root element with `<rxui:ReactiveWindow>`.
   - Add `rxui` and `vm` namespaces.

5. **Update all references to moved nested types:**
   - Search for `WorkProgress.IWorker` → replace with
     `WorkProgressViewModel.IWorker` (7 files in `Actions/`).
   - Search for `WorkProgress.OverwriteQuery` → replace with
     `WorkProgressViewModel.OverwriteQuery` (in `ProgressUtil.cs`).
   - Search for `WorkProgress.MessageBoxQuery` → replace with
     `WorkProgressViewModel.MessageBoxQuery` (4 call sites in
     `ProgressUtil.cs`).

6. **Register:**
   ```csharp
   ds.Register<WorkProgressViewModel, Common.WorkProgress>();
   ```

7. **Update callers in `MainViewModel`:**
   - All 6 call sites that create `WorkProgress` and read
     `workDialog.DialogResult` must be rewritten:
     ```csharp
     var progressVM = new WorkProgressViewModel(worker, isIndeterminate);
     bool? ok = await _dialogService.ShowDialogAsync(progressVM);
     if (ok == true) { /* success path */ }
     ```

8. **Build and test:**
   - `dotnet build`
   - Exercise every operation that uses WorkProgress:
     - Add files to an archive
     - Extract files from an archive
     - Delete files
     - Test archive integrity
     - Defragment
     - Copy/paste between archives
     - Scan disk
   - Trigger an overwrite conflict during add → verify the overwrite
     query appears correctly over the progress window.
   - Cancel a long operation → verify cancellation works.

### Now that those are done, here's what changed

- **New file:** `ViewModels/WorkProgressViewModel.cs`
- **Modified files:** `Common/WorkProgress.axaml`,
  `Common/WorkProgress.axaml.cs`, 7 files in `Actions/` (nested type
  references), `ProgressUtil.cs`, `RegisterDialogMappings()`,
  6 callers in `MainViewModel`
- **Nested types moved:** `IWorker`, `OverwriteQuery`, `MessageBoxQuery`
  now live on `WorkProgressViewModel`.
- **Static registration sharing:** `DialogService` registration dictionary
  is now static.
- **Behavior:** Identical across all operations.

---

## Section 15: Complete Dialog Mapping Registration

### What we are going to accomplish

After all individual dialog conversions are done, we verify that the
`RegisterDialogMappings()` method contains every ViewModel→View entry.
This is the single place where `IDialogService` learns which View class
to create for each ViewModel type.

### To do that, follow these steps

1. **Open** `App.axaml.cs` and find `RegisterDialogMappings()`.

2. **Verify** it contains all entries. The complete list (Phase 4A + 4B):

   ```csharp
   private void RegisterDialogMappings() {
       var ds = Services.GetRequiredService<IDialogService>();

       // Phase 4A (complex)
       ds.Register<EditSectorViewModel, EditSector>();
       ds.Register<FileViewerViewModel, Tools.FileViewer>();
       ds.Register<EditAttributesViewModel, EditAttributes>();
       ds.Register<CreateDiskImageViewModel, CreateDiskImage>();
       ds.Register<SaveAsDiskViewModel, SaveAsDisk>();
       ds.Register<TestManagerViewModel, LibTest.TestManager>();
       ds.Register<BulkCompressViewModel, LibTest.BulkCompress>();

       // Phase 4B (medium + simple)
       ds.Register<EditConvertOptsViewModel, EditConvertOpts>();
       ds.Register<EditAppSettingsViewModel, EditAppSettings>();
       ds.Register<WorkProgressViewModel, Common.WorkProgress>();
       ds.Register<LogViewerViewModel, Tools.LogViewer>();
       ds.Register<DropTargetViewModel, Tools.DropTarget>();
       ds.Register<ReplacePartitionViewModel, ReplacePartition>();
       ds.Register<CreateDirectoryViewModel, CreateDirectory>();
       ds.Register<EditMetadataViewModel, EditMetadata>();
       ds.Register<CreateFileArchiveViewModel, CreateFileArchive>();
       ds.Register<FindFileViewModel, FindFile>();
       ds.Register<AboutBoxViewModel, AboutBox>();
       ds.Register<AddMetadataViewModel, AddMetadata>();
       ds.Register<OverwriteQueryViewModel, Actions.OverwriteQueryDialog>();
       ds.Register<ShowTextViewModel, Tools.ShowText>();
   }
   ```

3. **Count:** 7 from Phase 4A + 14 from Phase 4B = 21 total dialog mappings.

4. **Build** to confirm all type references resolve.

### Now that those are done, here's what changed

- **Verified:** `RegisterDialogMappings()` is complete.
- **New capability:** `IDialogService` can now create any dialog View from
  any dialog ViewModel, uniformly.
- **What this enables:** All future dialog usage goes through
  `_dialogService.ShowDialogAsync(vm)` or `ShowModeless(vm)` — no more
  `new SomeDialog(mMainWin, ...)` anywhere.

---

## Section 16: Retire RelayCommand

### What we are going to accomplish

`RelayCommand` was the hand-rolled `ICommand` implementation used before
ReactiveUI was adopted. Phase 2 converted `MainWindow`'s 51 command
instances to `ReactiveCommand`. Phase 4B (this iteration) converted all
dialog commands. If both phases are complete, zero `RelayCommand` usages
should remain, and the class can be deleted.

**Prerequisite:** Phase 2 (Iteration 2: Command Migration) must be complete.
If it hasn't run yet, skip this step — `RelayCommand.cs` cannot be retired
until both Phase 2 and Phase 4B are done.

### To do that, follow these steps

1. **Search** for remaining `RelayCommand` references:
   ```
   grep -rn "RelayCommand" cp2_avalonia/
   ```

2. **Expected result:** Zero matches (other than the file itself).

3. If zero matches: **delete** `cp2_avalonia/Common/RelayCommand.cs`.

4. If matches remain: those commands must be converted to `ReactiveCommand`
   before `RelayCommand.cs` can be retired. Identify the files and convert.

5. **Build** to confirm no compile errors from the deletion.

### Now that those are done, here's what changed

- **Deleted file:** `Common/RelayCommand.cs`
- **What this means:** The project now uses a single, consistent command
  system: ReactiveUI's `ReactiveCommand`. No more legacy command
  infrastructure.

---

## Section 17: Final Build and Validation

### What we are going to accomplish

Comprehensive validation that every dialog in the application works
correctly after the MVVM conversion.

### To do that, follow these steps

1. **Build:**
   ```
   dotnet build
   ```
   Verify zero errors.

2. **Test simple dialogs:**
   - Help → About — verify version, legal text, website link
   - File → New File Archive — select each format, verify persistence
   - Actions → Create Directory — test valid/invalid names
   - Metadata panel → Add, Edit, Delete entries
   - Trigger overwrite conflict (add duplicate files) → verify query dialog
   - Debug → System Info (ShowText modal)

3. **Test medium dialogs:**
   - Edit → Application Settings — change theme, verify persistence
   - Configure Import/Export Options from within settings
   - Actions → Find Files — search, navigate results, close/reopen
   - Actions → Replace Partition
   - Debug → Show Debug Log — toggle on/off, save, copy
   - Debug → Drop Target — toggle on/off, drag files
   - Any operation with progress (add, extract, delete, test)

4. **Verify modeless windows:**
   - LogViewer toggles on/off correctly
   - DropTarget toggles on/off correctly
   - FindFile stays open, results navigate correctly
   - FileViewer (from 4A) supports multiple instances
   - Closing workspace closes all modeless dialogs

5. **Cross-check:** Every dialog uses a ViewModel (no dialog sets
   `DataContext = this`).

### Now that those are done, here's what changed

- Every dialog in `cp2_avalonia` now follows MVVM: ViewModel + thin
  code-behind.
- `RelayCommand` is retired — all commands use `ReactiveCommand`.
- `IDialogService` handles all dialog presentation uniformly.
- Dialog ViewModels are unit-testable in isolation.
- **This enables Phase 5:** extracting child ViewModels from
  `MainViewModel` (archive tree, directory tree, file list, options
  panel, center info, status bar).
