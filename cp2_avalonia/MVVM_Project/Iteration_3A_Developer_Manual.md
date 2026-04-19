# Iteration 3A Developer Manual: Service Interfaces, Implementations & DI Container

> **Iteration:** 3A
> **Blueprint:** `cp2_avalonia/MVVM_Project/Iteration_3A_Blueprint.md`
> **Architecture reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §4, §7.14,
> §7.15, §7.17, §7.19
> **Conventions:** `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md`

---

## Overview

This iteration creates the **service layer** — the set of abstractions that sit
between ViewModels and platform-specific Avalonia APIs. After this iteration,
all service interfaces exist, most have concrete implementations, and the DI
container is wired up in `App.axaml.cs`. However, no command bodies are
changed — they still call through `mController`. Phase 3B will dissolve the
controller and route commands through services.

### Why services?

In the pre-MVVM architecture, `MainController` directly uses Avalonia APIs to
show dialogs (`new EditSector(mMainWin, ...)`), open file pickers
(`StorageProvider.OpenFilePickerAsync`), read settings
(`AppSettings.Global.GetBool(...)`), and manage the clipboard. This makes the
controller untestable without a running Avalonia application and tightly
couples business logic to the UI framework.

Services solve this by wrapping each platform dependency behind an interface.
ViewModels receive these interfaces through their constructor parameters
(called **constructor injection**). In tests, you can substitute mock
implementations. In production, the real implementations talk to Avalonia.

### What is Dependency Injection (DI)?

DI is a pattern where an object receives its dependencies from outside rather
than creating them itself. Instead of `new DialogService()` inside a ViewModel,
the ViewModel declares "I need an `IDialogService`" in its constructor, and
something else provides it.

In this project, we use Microsoft's `Microsoft.Extensions.DependencyInjection`
package. You register services in a `ServiceCollection`, build a
`ServiceProvider`, and then resolve services from it. The registrations happen
once at application startup in `App.axaml.cs`.

### What is `IDialogHost`?

Avalonia's `ShowDialog()` and `StorageProvider` APIs require an owner `Window`
reference. Services are typically singletons (one instance for the whole app)
and don't know about windows. `IDialogHost` bridges this gap — it's a tiny
interface with one method (`GetOwnerWindow()`) that `MainWindow` implements.
The ViewModel passes `IDialogHost` to services that need a window reference,
keeping the ViewModel decoupled from the actual `Window` type.

---

## Prerequisites

Before starting this iteration, confirm:

- **Iteration 2 is complete.** All 51 commands are `ReactiveCommand` instances
  on `MainViewModel`. No `RelayCommand` instances remain on `MainWindow`.
- **The application builds and runs correctly** (`dotnet build` succeeds,
  the app launches, all menu items and toolbar buttons function).

---

## Step 1: Create the `Services/` Directory

### What we are going to accomplish

Create the physical directory where all service interfaces and their
implementations will live. This follows the folder structure defined in
MVVM_Notes.md §5, which places services in `cp2_avalonia/Services/` with
the namespace `cp2_avalonia.Services`.

Separating services into their own directory keeps the project organized and
makes it clear which files are "platform abstraction" code versus ViewModels
or Views. This is standard practice in MVVM projects.

### To do that, follow these steps

1. In your file explorer or terminal, create the directory:
   ```
   cp2_avalonia/Services/
   ```
2. The directory will be populated by the following steps.

**Important:** Every new `.cs` file created in this iteration must include the
Apache 2.0 license header with dual copyright (faddenSoft + Lydian Scale
Software) as specified in Pre-Iteration-Notes §4. The blueprint's code
snippets omit this header for brevity, but you must add it to every file.

### Now that those are done, here's what changed

- A new `Services/` directory exists under `cp2_avalonia/`.
- No code changes; no build impact.

---

## Step 2: Create `IDialogHost`

### What we are going to accomplish

Create the `IDialogHost` interface and implement it on `MainWindow`. This is
the **owner-window abstraction** described in MVVM_Notes.md §7.15.

**Why this exists:** Avalonia's `ShowDialog()` method requires a parent `Window`
object. But ViewModels must never reference Avalonia `Window` types directly
(that would couple them to the UI framework). `IDialogHost` provides an
abstraction: ViewModels know about `IDialogHost` (which is just an interface),
and `MainWindow` implements it (because `MainWindow` *is* the owner window).

This is the **only** ViewModel→View coupling in the architecture, and it goes
through an abstraction. In a future multi-window scenario, each `MainWindow`
would be its own `IDialogHost`, and each `MainViewModel` would get the correct
one.

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/IDialogHost.cs`:**

   ```csharp
   // (license header)
   namespace cp2_avalonia.Services;

   using Avalonia.Controls;

   /// <summary>
   /// Provides the owner Window for dialogs and file pickers.
   /// Implemented by MainWindow (or any future top-level window).
   /// </summary>
   public interface IDialogHost {
       Window GetOwnerWindow();
   }
   ```

   This interface has a single method. Any class that implements it promises
   to return a `Window` that can serve as the owner for dialogs and pickers.

2. **Open `MainWindow.axaml.cs`** and add `IDialogHost` to the class
   declaration. `MainWindow` already implements `INotifyPropertyChanged` —
   add `IDialogHost` alongside it:

   ```csharp
   public partial class MainWindow : Window, INotifyPropertyChanged, IDialogHost {
       public Window GetOwnerWindow() => this;
       // ... existing code unchanged ...
   }
   ```

   Add the using directive at the top of the file:
   ```csharp
   using cp2_avalonia.Services;
   ```

3. **Do not** change any other code in `MainWindow.axaml.cs`. The existing
   constructor, properties, and event handlers remain untouched.

4. **Build checkpoint:** Run `dotnet build`. The project should compile with
   zero errors. `IDialogHost` is defined but not yet consumed by anything.

### Now that those are done, here's what changed

- **New file:** `Services/IDialogHost.cs` — defines the owner-window
  abstraction.
- **Modified file:** `MainWindow.axaml.cs` — added `IDialogHost`
  implementation (one-liner) and `using` directive.
- **Behavior:** Unchanged. The interface exists but nothing calls it yet.

---

## Step 3: Create `IDialogService` / `DialogService`

### What we are going to accomplish

Create the dialog service — the abstraction that lets ViewModels show modal
and modeless dialogs without directly creating `Window` objects.

This addresses MVVM_Notes.md §4.1 and §7.14. The key concepts:

- **`IDialogService`** is the interface that ViewModels depend on. It has
  methods like `ShowDialogAsync<TViewModel>(vm)` (show a modal dialog),
  `ShowMessageAsync(...)` (show a message box), `ShowConfirmAsync(...)`
  (yes/no prompt), and `ShowModeless<TViewModel>(vm)` (non-blocking window).

- **`DialogService`** is the concrete implementation. It maintains a
  **registration dictionary** — a mapping from ViewModel types to View types.
  When you call `ShowDialogAsync<EditSectorViewModel>(vm)`, it looks up
  `EditSectorViewModel` in the dictionary, finds the corresponding
  `EditSector` window class, creates an instance, sets `DataContext = vm`,
  and calls `ShowDialog()`.

- **Why a dictionary, not a convention?** ReactiveUI has a built-in
  `ViewLocator` that maps ViewModels to Views by naming convention. We
  don't use it for dialog windows because `ViewLocator` is designed for
  `UserControl`-based resolution, not top-level `Window` creation. The
  explicit dictionary is simpler and more debuggable (MVVM_Notes §7.14).

- **`MBButton`, `MBIcon`, `MBResult`** are simple enums that replace
  WPF-style `MessageBox` button/icon/result enums. They're defined alongside
  the interface because they're part of its API surface.

**ReactiveUI concept — What is a "modal" vs "modeless" dialog?**
A *modal* dialog blocks interaction with its parent window until it's closed
(e.g., a settings dialog). A *modeless* dialog allows the user to interact
with the parent while it's open (e.g., a file viewer you can keep open while
browsing). `ShowDialogAsync` is modal; `ShowModeless` is modeless.

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/IDialogService.cs`** with the interface
   definition. The interface declares:
   - `ShowDialogAsync<TViewModel>(TViewModel viewModel)` — shows a modal
     dialog and returns `true` if accepted, `false`/`null` otherwise.
   - `ShowMessageAsync(text, caption, buttons, icon)` — shows a standard
     message box. Returns which button the user clicked.
   - `ShowConfirmAsync(text, caption)` — convenience wrapper for yes/no.
   - `ShowModeless<TViewModel>(TViewModel viewModel)` — shows a non-blocking
     window. Used for `FileViewerViewModel` (multiple concurrent viewers).
   - `Register<TViewModel, TView>()` — registers a ViewModel→View mapping.
     Called once at startup.

   Copy the interface exactly from the blueprint's Step 3 "Interface" section.

2. **Create `cp2_avalonia/Services/DialogService.cs`** with the concrete
   implementation. Key implementation details:
   - The constructor takes `IDialogHost` — this is how it gets the owner
     window for `ShowDialog()`.
   - `mMap` is a `Dictionary<Type, Func<Window>>` — each entry maps a
     ViewModel type to a factory that creates the View.
   - `ShowDialogAsync` looks up the factory, creates the View, sets
     `DataContext = viewModel`, and calls `view.ShowDialog<bool?>(owner)`.
   - `ShowMessageAsync` builds a simple dialog window programmatically
     (buttons, text, layout) since Avalonia doesn't have a built-in
     `MessageBox` API.
   - `ShowModeless` creates a new View per call (never caches/reuses) —
     this is essential for multiple concurrent FileViewer instances
     (MVVM_Notes §7.10).

   Copy the implementation exactly from the blueprint's Step 3
   "Implementation" section.

3. **Build checkpoint:** Run `dotnet build`. Should compile cleanly. The
   `DialogService` depends on `IDialogHost` (Step 2) but nothing uses
   it yet.

### Now that those are done, here's what changed

- **New files:** `Services/IDialogService.cs`, `Services/DialogService.cs`
- **New types:** `MBButton`, `MBIcon`, `MBResult` enums (in
  `IDialogService.cs`)
- **Capabilities:** The infrastructure to show dialogs from ViewModels now
  exists. No ViewModel→View mappings are registered yet — those will be added
  in Phases 4A/4B as dialog ViewModels are created.
- **Behavior:** No runtime changes. Nothing calls these services yet.

---

## Step 4: Create `IFilePickerService` / `FilePickerService`

### What we are going to accomplish

Create the file-picker service — the abstraction that lets ViewModels ask the
user to select files or folders without directly using Avalonia's
`StorageProvider` API.

This addresses MVVM_Notes.md §4.2. Currently, `MainController` calls
`TopLevel.GetTopLevel(mMainWin)!.StorageProvider.OpenFilePickerAsync(...)`.
That requires a direct reference to the window. With `IFilePickerService`,
the ViewModel just calls `await _filePickerService.OpenFileAsync("title")`.

**Avalonia concept — `StorageProvider`:** Avalonia provides cross-platform
file dialogs through the `IStorageProvider` interface, available via
`TopLevel.GetTopLevel(window).StorageProvider`. The `FilePickerService`
wraps this so ViewModels never need to call `TopLevel.GetTopLevel()`.

**`FilePickerFileType`:** This is Avalonia's type for file-type filters in
open/save dialogs (e.g., "Disk Images (*.2mg, *.po)"). The service passes
these through — ViewModels construct filter lists and the service forwards
them to Avalonia.

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/IFilePickerService.cs`** with four methods:
   - `OpenFileAsync` — single file selection, returns path or `null`
   - `OpenFilesAsync` — multiple file selection, returns list of paths
   - `SaveFileAsync` — save dialog, returns chosen path or `null`
   - `OpenFolderAsync` — folder selection, returns path or `null`

   Copy from the blueprint's Step 4 "Interface" section.

2. **Create `cp2_avalonia/Services/FilePickerService.cs`** with the
   implementation:
   - Constructor takes `IDialogHost` to get the owner window.
   - `GetProvider()` calls `TopLevel.GetTopLevel(host.GetOwnerWindow())!.StorageProvider`.
   - `ResolveStartDir()` converts an `initialDir` string path to an
     `IStorageFolder` using `TryGetFolderFromPathAsync`.
   - Each method creates the appropriate `FilePickerOpenOptions` /
     `FilePickerSaveOptions` / `FolderPickerOpenOptions` and calls the
     `StorageProvider`.
   - Results are converted from Avalonia's `IStorageFile` objects to plain
     `string` paths (`r.Path.LocalPath`).

   Copy from the blueprint's Step 4 "Implementation" section.

3. **Build checkpoint:** Run `dotnet build`. Should compile cleanly.

### Now that those are done, here's what changed

- **New files:** `Services/IFilePickerService.cs`,
  `Services/FilePickerService.cs`
- **Capabilities:** ViewModels can now request file/folder selection without
  touching Avalonia's `StorageProvider` directly.
- **Behavior:** No runtime changes.

---

## Step 5: Create `ISettingsService` / `SettingsService`

### What we are going to accomplish

Create the settings service — a thin wrapper around the existing
`AppSettings.Global` / `SettingsHolder` that makes settings accessible through
an injectable interface.

This addresses MVVM_Notes.md §4.3 and the coexistence migration strategy in
§7.17. The key insight: **we're not replacing `AppSettings.Global`**, we're
wrapping it. Both the service and direct `AppSettings.Global` access read/write
the same underlying `SettingsHolder` instance. This means:

- ViewModel code uses `ISettingsService` (injected, testable).
- View code-behind can continue using `AppSettings.Global` directly for pure
  view concerns (window placement, column widths) — those never move to VMs.
- No data is duplicated. Changes through either path are visible everywhere.

**`IObservable<string> SettingChanged`:** This is a reactive stream (an
RxNET concept used by ReactiveUI) that emits the key name whenever a setting
is changed through the service. ViewModels subscribe to this observable to
react to settings changes in real time. For example, if
`EditAppSettingsViewModel` changes the theme, `MainViewModel` can subscribe
to `SettingChanged` and apply the new theme immediately.

**What is a `Subject<T>`?** It's the simplest way to create an observable
that you can push values into. `mSettingChanged.OnNext(key)` pushes a new
value to all subscribers. Think of it like a C# event, but composable with
LINQ-like operators.

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/ISettingsService.cs`** with typed
   get/set methods for `bool`, `int`, `string`, and `Enum`, plus
   `SettingChanged`, `Load()`, and `Save()`.

   Copy from the blueprint's Step 5 "Interface" section.

2. **Create `cp2_avalonia/Services/SettingsService.cs`** with the
   implementation:
   - Constructor grabs `AppSettings.Global` into `mHolder`.
   - Get methods delegate to `mHolder.GetBool(...)`, etc.
   - Set methods delegate to `mHolder.SetBool(...)` and then call
     `mSettingChanged.OnNext(key)` to notify subscribers.
   - `Load()` reads the JSON settings file from disk using
     `PlatformUtil.GetRuntimeDataDir()` and `SettingsHolder.Deserialize()`,
     then merges into the current holder.
   - `Save()` checks `mHolder.IsDirty`, serializes to the JSON file.
   - The `Load()` and `Save()` comments note that some controller-managed
     settings (like `AUTO_OPEN_DEPTH` defaults and window placement) will
     migrate in Phase 3B.

   Copy from the blueprint's Step 5 "Implementation" section.

3. **Build checkpoint:** Run `dotnet build`. Verify `SettingsHolder`,
   `AppSettings`, and `PlatformUtil` resolve correctly (they're in existing
   code).

### Now that those are done, here's what changed

- **New files:** `Services/ISettingsService.cs`,
  `Services/SettingsService.cs`
- **Capabilities:** ViewModels can read/write settings through
  `ISettingsService` and subscribe to `SettingChanged` for reactive updates.
- **Coexistence:** `AppSettings.Global` continues to work everywhere. Both
  paths access the same data.
- **Behavior:** No runtime changes.

---

## Step 6: Create `IClipboardService` / `ClipboardService`

### What we are going to accomplish

Create the clipboard service — the abstraction for all clipboard operations
(copy/paste within the app and between processes).

This addresses MVVM_Notes.md §4.4. The clipboard is more complex than the
other services because CiderPress II supports three paste scenarios:

1. **Same-process paste:** Copy files within the same app instance. The
   service caches `ClipInfo` and `ClipFileEntry` objects in memory.
2. **Cross-process CP2 paste:** Copy from one CiderPress II process, paste
   into another. Uses the system clipboard's text format with CP2 JSON data.
3. **External file-manager paste:** Paste files copied from a file manager
   (KDE Dolphin, GNOME Nautilus, macOS Finder). Uses `text/uri-list` format.

**Why lazy clipboard resolution?** The implementation calls `GetClipboard()`
at the point of use, not at construction time. This is because in a future
multi-window architecture, the "active window" (and thus the correct
clipboard context) may change. MVVM_Notes §4.4 specifies this: "resolves
the system clipboard lazily at call time (not captured at construction) to
support multi-window."

**`ClipInfo` accessibility change:** The blueprint notes that `ClipInfo` is
currently `internal class ClipInfo`. Since the public `IClipboardService`
interface uses `ClipInfo` in its method signatures, it must be changed to
`public class ClipInfo` to avoid a C# compiler error (CS0051: inconsistent
accessibility).

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/IClipboardService.cs`** with the
   interface. Methods include `SetClipAsync`, `GetClipAsync`,
   `GetRawClipTextAsync`, `GetUriListAsync`, `ClearIfPendingAsync`, and
   the `HasPendingContent` property.

   Copy from the blueprint's Step 6 "Interface" section.

2. **Open `cp2_avalonia/ClipInfo.cs`** and change the class accessibility
   from `internal` to `public`:
   ```csharp
   // Before:
   internal class ClipInfo { ... }
   // After:
   public class ClipInfo { ... }
   ```
   This is required because `IClipboardService` (which is public) exposes
   `ClipInfo` in its method signatures.

3. **Create `cp2_avalonia/Services/ClipboardService.cs`** with the
   implementation. Key details:
   - `mPendingClipInfo`, `mCachedClipEntries`, `mClipTempDir` — in-memory
     state for same-process paste.
   - `GetClipboard()` is a static helper that resolves the system clipboard
     from the currently active top-level window. It navigates through
     `Application.Current.ApplicationLifetime` → `MainWindow` →
     `TopLevel.GetTopLevel(window).Clipboard`.
   - `SetClipAsync` stores cached state, builds a `DataObject` with CP2
     JSON text and optionally `text/uri-list` for external paste.
   - `GetUriListAsync` tries three clipboard formats in order:
     `text/uri-list` (standard X11/freedesktop), `x-special/gnome-copied-files`
     (GNOME/KDE), and plain text fallback (bare `file://` URIs or absolute
     paths).
   - `ClearIfPendingAsync` clears both the cached state and the system
     clipboard, and deletes the temp directory if it exists.

   Copy from the blueprint's Step 6 "Implementation" section (combining the
   code from the blueprint's Step 6 block and the continuation in the file).

4. **Build checkpoint:** Run `dotnet build`. Verify that `ClipInfo`,
   `ClipFileEntry` (from `AppCommon`), and Avalonia clipboard types resolve.

### Now that those are done, here's what changed

- **New files:** `Services/IClipboardService.cs`,
  `Services/ClipboardService.cs`
- **Modified file:** `ClipInfo.cs` — changed from `internal` to `public`.
- **Capabilities:** Clipboard operations are now abstracted behind an
  interface, supporting same-process, cross-process, and external-app paste.
- **Behavior:** No runtime changes.

---

## Step 7a: Promote `AutoOpenDepth` to a Standalone Type

### What we are going to accomplish

Move the `AutoOpenDepth` enum from inside `MainController` to its own file
under `Models/`. This is necessary because `IWorkspaceService` (created in
Step 7b) references `AutoOpenDepth` in its `OpenAsync` method signature, and
we don't want the service interface to depend on the controller.

**Why `Models/`?** `AutoOpenDepth` is a domain concept (how deeply to auto-open
nested archives), not a service or a ViewModel. It's a simple enum used by
multiple layers, so `Models/` is the appropriate home. The namespace will be
`cp2_avalonia.Models`.

### To do that, follow these steps

1. **Create the directory** `cp2_avalonia/Models/` if it doesn't already exist.
   (It may already exist from Phase 0 if inner classes were extracted.)

2. **Create `cp2_avalonia/Models/AutoOpenDepth.cs`:**

   ```csharp
   // (license header)
   namespace cp2_avalonia.Models;

   /// <summary>
   /// How deeply to auto-open nested archives/disk images when opening
   /// a work file.
   /// </summary>
   public enum AutoOpenDepth {
       Unknown = 0,
       Shallow,
       SubVol,
       Max
   }
   ```

3. **Open `MainController.cs`** and find the nested `AutoOpenDepth` enum
   definition. Remove (or comment out) the nested enum. Add a using at the
   top:
   ```csharp
   using cp2_avalonia.Models;
   ```
   All existing code that references `AutoOpenDepth` without the
   `MainController.` prefix will continue to compile because the `using`
   brings it into scope.

4. **Open `EditAppSettings.axaml.cs`** — this file qualifies every reference
   as `MainController.AutoOpenDepth`. Add the using:
   ```csharp
   using cp2_avalonia.Models;
   ```
   Then replace all occurrences of `MainController.AutoOpenDepth` with just
   `AutoOpenDepth`. The blueprint identifies approximately 8 occurrences
   (around lines 99, 106, 108–109, 112–113, 116–117, but verify in your
   source since line numbers may differ after prior iterations).

5. **Search the project** for any other files that reference
   `MainController.AutoOpenDepth` and update them similarly:
   ```
   grep -rn "MainController.AutoOpenDepth" cp2_avalonia/
   ```
   Each match needs `using cp2_avalonia.Models;` and removal of the
   `MainController.` qualifier.

6. **Build checkpoint:** Run `dotnet build`. Zero errors expected — the enum
   values and type name are unchanged; only the location moved.

### Now that those are done, here's what changed

- **New file:** `Models/AutoOpenDepth.cs`
- **Modified files:** `MainController.cs` (removed nested enum, added using),
  `EditAppSettings.axaml.cs` (changed qualified references), and potentially
  other files that referenced `MainController.AutoOpenDepth`.
- **Behavior:** Completely unchanged. The enum has the same name, same values,
  same usage — it's just in a standalone file now.
- **This enables:** `IWorkspaceService` (Step 7b) can reference
  `AutoOpenDepth` without depending on `MainController`.

---

## Step 7b: Create `IWorkspaceService` (Interface Only)

### What we are going to accomplish

Define the `IWorkspaceService` interface — the abstraction for workspace
lifecycle (opening/closing files, managing the `WorkTree`, recent files, etc.).

This addresses MVVM_Notes.md §4.5. **Only the interface** is created in this
iteration. The concrete `WorkspaceService` implementation is built in Phase 3B
when the controller is dissolved. This is because the implementation needs to
absorb logic currently scattered across `MainController`, and that's a complex
task that belongs in its own iteration.

**Key concepts from the interface:**

- **`WorkTree`**: The core data structure from the `AppCommon` library that
  represents an open disk image or file archive (potentially with nested
  sub-archives). The workspace service owns the `WorkTree` lifecycle.

- **`IObservable<Unit> WorkspaceModified`**: A reactive stream that fires
  after any operation modifies the workspace (add, delete, move files, etc.).
  ViewModels subscribe to this to refresh their state. `Unit` is ReactiveUI's
  "void" type for observables — it means "something happened" without
  carrying data.

- **`CanWrite` is NOT on this interface.** Per MVVM_Notes.md §4.5, `CanWrite`
  is a computed ViewModel property that depends on both workspace state and
  UI context. It belongs on `MainViewModel`, not on the service.

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/IWorkspaceService.cs`** with the interface
   definition. It includes properties for `WorkTree`, `IsFileOpen`,
   `WorkPathName`, `DebugLog`, `Formatter`, `AppHook`, `RecentFilePaths`,
   and methods `OpenAsync()`, `Close()`, plus the `WorkspaceModified`
   observable.

   Copy from the blueprint's Step 7b code block. Note the usings:
   ```csharp
   using System.Reactive;         // for Unit
   using cp2_avalonia.Models;     // for AutoOpenDepth
   using AppCommon;               // for WorkTree
   using CommonUtil;              // for Formatter
   ```

2. **Do NOT create an implementation file.** The implementation is Phase 3B.
   The interface exists now so that `MainViewModel`'s constructor signature
   can be prepared (even though it won't resolve `IWorkspaceService` from
   the container yet in this iteration).

3. **Build checkpoint:** Run `dotnet build`. The interface references types
   from `AppCommon` and `CommonUtil` — verify they resolve.

### Now that those are done, here's what changed

- **New file:** `Services/IWorkspaceService.cs`
- **No implementation** — just the contract.
- **This enables:** Phase 3B knows the exact interface to implement when it
  moves `WorkTree` lifecycle out of the controller.

---

## Step 8: Create `IViewerService` (Interface Only) + `FileViewerViewModel` Stub

### What we are going to accomplish

Define the `IViewerService` interface — the centralized registry for active
`FileViewerViewModel` instances — and create a minimal stub for
`FileViewerViewModel` so the interface can compile.

This addresses MVVM_Notes.md §4.6 and §7.10. The viewer service is important
for a future goal: **multiple concurrent file viewers**. Each viewer registers
itself with the service on construction and unregisters on disposal. When a
file is closed, `MainViewModel` calls `CloseViewersForSource(...)` to tear
down all viewers associated with that file *before* the `WorkTree` is disposed
(preventing stale data references — see MVVM_Notes §7.10 gotcha #1).

**Why a stub `FileViewerViewModel`?** The `IViewerService` interface
references `FileViewerViewModel` in its method signatures. That class doesn't
fully exist yet (it's built in Phase 4A). We create a minimal stub now so the
interface compiles. The stub extends `ReactiveObject` and implements
`IDisposable` (because `FileViewerViewModel` must self-deregister on disposal
— this is a critical architectural constraint from Pre-Iteration-Notes §4).

### To do that, follow these steps

1. **Create `cp2_avalonia/Services/IViewerService.cs`:**

   ```csharp
   // (license header)
   namespace cp2_avalonia.Services;

   using System.Collections.Generic;
   using cp2_avalonia.ViewModels;

   public interface IViewerService {
       IReadOnlyList<FileViewerViewModel> ActiveViewers { get; }
       void Register(FileViewerViewModel viewer);
       void Unregister(FileViewerViewModel viewer);

       /// <summary>
       /// Close all viewers associated with the given work path (called on
       /// file close).
       /// </summary>
       void CloseViewersForSource(string workPathName);
   }
   ```

2. **Create the directory** `cp2_avalonia/ViewModels/` if it doesn't already
   exist. (It should exist from Phase 1A when `MainViewModel` was created.)

3. **Create `cp2_avalonia/ViewModels/FileViewerViewModel.cs`** as a minimal
   stub:

   ```csharp
   // (license header)
   namespace cp2_avalonia.ViewModels;

   using System;
   using ReactiveUI;

   /// <summary>
   /// Stub — Phase 4A will flesh this out with full file viewer logic.
   /// Implements IDisposable because it holds file data/handles and must
   /// self-deregister from IViewerService (see Pre-Iteration-Notes §4).
   /// </summary>
   public class FileViewerViewModel : ReactiveObject, IDisposable {
       // Phase 4A: Dispose() must call _viewerService.Unregister(this).
       public void Dispose() { }
   }
   ```

   The comment documents the future requirement so Phase 4A doesn't miss it.

4. **Build checkpoint:** Run `dotnet build`. The interface references the
   stub class; both should compile.

### Now that those are done, here's what changed

- **New files:** `Services/IViewerService.cs`,
  `ViewModels/FileViewerViewModel.cs` (stub)
- **No implementation** of the service — just the interface and a
  compilation stub.
- **This enables:** Phase 4A/6 will implement `ViewerService` and flesh out
  `FileViewerViewModel`. The interface exists now so the full service layer
  architecture is visible.

---

## Step 9: Configure the DI Container in `App.axaml.cs`

### What we are going to accomplish

Wire up the Dependency Injection container in `App.axaml.cs`. This is where
all services are registered so they can be resolved later. After this step,
`App.Services` is a global `IServiceProvider` that any code can use to get
service instances.

**How DI registration works:** You create a `ServiceCollection`, register
services in it (specifying their interface, implementation, and lifetime),
then call `BuildServiceProvider()` to create the actual resolver. The three
common lifetimes are:

- **Singleton:** One instance for the entire app lifetime. Good for services
  that manage global state (settings, clipboard).
- **Transient:** A new instance every time you request one. Good for services
  that need per-consumer state.
- **Scoped:** One instance per "scope" (usually per HTTP request in web apps).
  Not used here.

**What gets registered in 3A:** Only `ISettingsService` and
`IClipboardService` are registered in the container as singletons. Per
MVVM_Notes §7.19 and Pre-Iteration-Notes §4, `IDialogService` and
`IFilePickerService` are **NOT** registered in the container — they're
manually constructed by `MainViewModel` with its specific `IDialogHost`. This
is because each window needs its own dialog/picker service bound to its own
owner window, and registering them as singletons would bind them to a single
window.

**What is NOT registered yet:** `IWorkspaceService` (no implementation until
Phase 3B) and `IViewerService` (no implementation until Phase 4A/6). Do not
attempt to resolve these from the container in this iteration.

### To do that, follow these steps

1. **Open `App.axaml.cs`**.

2. **Add using directives** at the top of the file:
   ```csharp
   using Microsoft.Extensions.DependencyInjection;
   using cp2_avalonia.Services;
   ```

3. **Add a static `Services` property** to the `App` class:
   ```csharp
   /// <summary>Application-wide service provider.</summary>
   public static IServiceProvider Services { get; private set; } = null!;
   ```

4. **In the `OnFrameworkInitializationCompleted()` method**, add the DI
   container setup **before** `MainWindow` is constructed. This is important
   because `MainWindow`'s constructor will resolve services from
   `App.Services`. The method should look like this (merge into existing
   code — do NOT replace the entire method):

   ```csharp
   public override void OnFrameworkInitializationCompleted() {
       if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
           // Build the DI container FIRST — before MainWindow() is
           // constructed, because its constructor resolves services
           // from App.Services.
           var sc = new ServiceCollection();

           // Singletons (container-managed)
           sc.AddSingleton<ISettingsService, SettingsService>();
           sc.AddSingleton<IClipboardService, ClipboardService>();

           // IDialogHost is NOT registered — MainWindow passes itself
           // directly as IDialogHost to MainViewModel's constructor.

           // IDialogService and IFilePickerService are NOT registered
           // in the container. MainViewModel constructs its own instances
           // passing IDialogHost (see Step 10 and Pre-Iteration-Notes §4).

           Services = sc.BuildServiceProvider();

           // Now construct MainWindow — App.Services is valid.
           var mainWindow = new MainWindow();
           desktop.MainWindow = mainWindow;
       }
       base.OnFrameworkInitializationCompleted();
   }
   ```

5. **Preserve all existing code** in `App.axaml.cs` (theme handling, native
   menu handlers, etc.). Only add the DI setup and the `Services` property.
   The existing `MainWindow` construction may need to be moved after
   `Services = sc.BuildServiceProvider()` if it's currently inline.

6. **Build checkpoint:** Run `dotnet build`. Verify zero errors. The
   container is built but nothing resolves from it yet at runtime.

### Now that those are done, here's what changed

- **Modified file:** `App.axaml.cs` — added `Services` property, DI
  container setup with `ISettingsService` and `IClipboardService`
  registrations.
- **Capabilities:** `App.Services.GetRequiredService<ISettingsService>()`
  and `App.Services.GetRequiredService<IClipboardService>()` now work.
- **Behavior:** No functional changes. The container exists but services
  aren't consumed by any command logic yet.

---

## Step 10: Inject Services into MainViewModel

### What we are going to accomplish

Update `MainViewModel`'s constructor to accept service dependencies. This
step connects the services created in Steps 2–8 to the ViewModel that will
eventually use them (in Phase 3B).

**Two categories of services:**

1. **Container-managed singletons** (`ISettingsService`, `IClipboardService`):
   Resolved from `App.Services` in `MainWindow`'s constructor and passed to
   `MainViewModel`. These use the `_camelCase` field naming convention (per
   Pre-Iteration-Notes §4 — the exception for DI service interfaces).

2. **Manually constructed services** (`IDialogService`, `IFilePickerService`):
   Created by `MainViewModel` itself, passing the `IDialogHost` it receives.
   These use the `mCamelCase` field naming convention (standard for non-DI
   fields). Each `MainViewModel` instance gets its own dialog/picker service
   bound to its own window — essential for future multi-window support.

**The `RegisterDialogMappings()` method:** This is where ViewModel→View
mappings will be registered for `IDialogService`. In Phase 3A, the method
body is empty — dialog ViewModels don't exist yet. In Phases 4A/4B, lines
like `mDialogService.Register<EditSectorViewModel, EditSector>()` will be
added here.

**Updating `MainWindow` constructor:** `MainWindow` constructs
`MainViewModel`, passing `this` as `IDialogHost` and resolving singletons
from `App.Services`. The existing `mMainCtrl` (controller) is wired to the
ViewModel via `vm.SetController(mMainCtrl)` — this is the temporary Phase 1–2
coupling that Phase 3B will remove (MVVM_Notes §7.13).

### To do that, follow these steps

1. **Open `MainViewModel.cs`** (in `ViewModels/`).

2. **Add a using directive:**
   ```csharp
   using cp2_avalonia.Services;
   ```

3. **Add service fields** at the top of the class:
   ```csharp
   // Manually constructed (not DI-injected) — use mCamelCase per convention.
   private readonly IDialogService mDialogService;
   private readonly IFilePickerService mFilePickerService;

   // Container-managed singletons — use _camelCase per convention.
   private readonly ISettingsService _settingsService;
   private readonly IClipboardService _clipboardService;
   ```

4. **Update the constructor** to accept and store services:
   ```csharp
   public MainViewModel(
       IDialogHost dialogHost,
       ISettingsService settingsService,
       IClipboardService clipboardService) {
       // Manually construct host-dependent services (not container-managed).
       mDialogService = new DialogService(dialogHost);
       mFilePickerService = new FilePickerService(dialogHost);

       // Container-managed singletons.
       _settingsService = settingsService;
       _clipboardService = clipboardService;

       // Register ViewModel→View mappings on our DialogService instance.
       RegisterDialogMappings();

       // ... existing command initialization ...
   }
   ```

   **Important:** Do not remove the existing command initialization code. Add
   the service setup **before** the existing command creation code. The
   existing `SetController()` method (if present from Phase 2) stays.

5. **Add the `RegisterDialogMappings` method** (empty for now):
   ```csharp
   /// <summary>
   /// One-time registration of all ViewModel→View pairs used by
   /// IDialogService.ShowDialogAsync. Add entries as dialog ViewModels
   /// are created in Phases 4A/4B.
   /// </summary>
   private void RegisterDialogMappings() {
       // Dialog registrations will be added in Phase 4A/4B, e.g.:
       // mDialogService.Register<EditSectorViewModel, EditSector>();
       // mDialogService.Register<EditAppSettingsViewModel, EditAppSettings>();
   }
   ```

6. **Open `MainWindow.axaml.cs`** and update the constructor to pass
   services to `MainViewModel`:

   Add usings:
   ```csharp
   using Microsoft.Extensions.DependencyInjection;
   using cp2_avalonia.Services;
   ```

   Update the constructor where `MainViewModel` is created:
   ```csharp
   var vm = new MainViewModel(
       this,   // IDialogHost
       App.Services.GetRequiredService<ISettingsService>(),
       App.Services.GetRequiredService<IClipboardService>());

   DataContext = vm;
   ```

   The existing `mMainCtrl = new MainController(this)` and
   `vm.SetController(mMainCtrl)` lines stay — they're the temporary wiring
   from Phases 1–2 that Phase 3B will remove.

7. **Build checkpoint:** Run `dotnet build`. Verify zero errors.

### Now that those are done, here's what changed

- **Modified files:** `ViewModels/MainViewModel.cs` (constructor updated,
  service fields added, `RegisterDialogMappings` added),
  `MainWindow.axaml.cs` (constructor updated to pass services)
- **Capabilities:** `MainViewModel` now holds references to all four
  services. Commands don't use them yet — they still delegate to
  `mController`.
- **Behavior:** No functional changes. The services are wired but dormant.

---

## Step 11: Build and Validate

### What we are going to accomplish

Perform a final build-and-run check to confirm that all new files compile
correctly, the DI container resolves successfully, and the application
behaves identically to before this iteration.

This is the "no functional changes" validation that MVVM_Notes §6 Phase 3A
specifies: "Build, run; no functional changes (services exist but are
unused)."

### To do that, follow these steps

1. **Run `dotnet build`** from the solution root:
   ```
   dotnet build CiderPress2.sln
   ```
   Verify zero errors and zero warnings related to the new files.

2. **Launch the application:**
   ```
   dotnet run --project cp2_avalonia
   ```
   Verify it starts normally — the main window appears with the launch panel.

3. **Smoke test:** Open a disk image or file archive. Verify that:
   - The archive tree populates
   - The file list shows entries
   - Menu items enable/disable correctly
   - You can close the file
   - The application doesn't crash

4. **Optional DI verification:** To confirm the container is working, you can
   temporarily add a debug assertion in `MainWindow`'s constructor (remove it
   after testing):
   ```csharp
   var ss = App.Services.GetRequiredService<ISettingsService>();
   System.Diagnostics.Debug.Assert(ss != null, "SettingsService not resolved");
   ```

5. **Verify:** Commands still work through `mController`. No command body has
   changed. The services exist but are dormant — they'll be activated in
   Phase 3B.

### Now that those are done, here's what changed

- **Nothing new.** This step is purely validation.
- **Confirmed:** All 13 new files compile, the DI container resolves
  correctly, and the application behavior is unchanged.

---

## Summary of All Changes in Iteration 3A

### Files Created

| File | Purpose |
|---|---|
| `Services/IDialogHost.cs` | Owner-window abstraction (§7.15) |
| `Services/IDialogService.cs` | Modal/modeless dialog interface + `MBButton`/`MBIcon`/`MBResult` enums |
| `Services/DialogService.cs` | Dialog service with ViewModel→View registration dictionary |
| `Services/IFilePickerService.cs` | File open/save/folder picker interface |
| `Services/FilePickerService.cs` | Wraps Avalonia `StorageProvider` |
| `Services/ISettingsService.cs` | Settings read/write interface with `SettingChanged` observable |
| `Services/SettingsService.cs` | Wraps `AppSettings.Global` / `SettingsHolder` |
| `Services/IClipboardService.cs` | Clipboard abstraction (same-process + cross-process) |
| `Services/ClipboardService.cs` | Full clipboard implementation with `text/uri-list` support |
| `Models/AutoOpenDepth.cs` | Promoted enum (was nested in `MainController`) |
| `Services/IWorkspaceService.cs` | Interface only — implementation in Phase 3B |
| `Services/IViewerService.cs` | Interface only — implementation in Phase 4A/6 |
| `ViewModels/FileViewerViewModel.cs` | Stub class — fleshed out in Phase 4A |

### Files Modified

| File | Change |
|---|---|
| `MainWindow.axaml.cs` | Added `IDialogHost` implementation, updated constructor to pass services to `MainViewModel` |
| `MainController.cs` | Removed nested `AutoOpenDepth` enum, added `using cp2_avalonia.Models` |
| `EditAppSettings.axaml.cs` | Updated `MainController.AutoOpenDepth` references to unqualified `AutoOpenDepth` |
| `App.axaml.cs` | Added `Services` property, DI container setup |
| `ViewModels/MainViewModel.cs` | Added service fields and constructor parameters, added `RegisterDialogMappings()` |
| `ClipInfo.cs` | Changed from `internal` to `public` |

### What's New

- The complete service interface layer exists.
- Four services have concrete implementations (`DialogService`,
  `FilePickerService`, `SettingsService`, `ClipboardService`).
- Two services have interfaces only (`IWorkspaceService`, `IViewerService`).
- The DI container is configured and builds successfully.
- `MainViewModel` holds references to all services.

### What Stayed the Same

- All command bodies still delegate to `mController`.
- All UI behavior is identical.
- `AppSettings.Global` continues to work alongside `ISettingsService`.
- No ViewModel→View mappings are registered in `DialogService` yet.

### What This Enables in Future Iterations

- **Phase 3B** can now inject services into command bodies, replacing all
  direct `mMainWin.StorageProvider`, `new DialogName(mMainWin, ...)`, and
  `AppSettings.Global` calls in the controller. The controller is dissolved
  and deleted.
- **Phases 4A/4B** will register dialog ViewModel→View mappings in
  `RegisterDialogMappings()` and use `IDialogService.ShowDialogAsync<TVM>()`
  from command handlers.
- **Phase 4A/6** will implement `ViewerService` and flesh out
  `FileViewerViewModel`.
