# Iteration 0 Developer Manual: MVVM Infrastructure — ReactiveUI & DI Setup

> **Iteration:** 0
> **Blueprint:** `Iteration_0_Blueprint.md`
> **Architecture Reference:** `MVVM_Notes.md`

---

## Introduction

This is the very first iteration of the MVVM refactor. Its purpose is to lay the
**foundation** — installing the packages and plumbing that every subsequent iteration
will build on — without changing anything the user can see or do in the application.

If you are new to MVVM (Model-View-ViewModel) or ReactiveUI, this iteration is an
excellent starting point because the changes are small, self-contained, and produce
zero behavioral difference. You will learn what the tools are and where they plug in
before you need to use them for real work.

### Key Terms You Will Encounter

| Term | What It Means |
|---|---|
| **MVVM** | A design pattern that separates UI (View) from state and logic (ViewModel) from data (Model). The View binds to the ViewModel declaratively; the ViewModel never knows the View exists. |
| **ReactiveUI** | An MVVM framework that provides `ReactiveObject` (base class for ViewModels), `ReactiveCommand` (replaces `RelayCommand`), and `WhenAnyValue` (reactive property observation). It has first-class Avalonia integration. |
| **`ReactiveObject`** | The base class you will use for all ViewModels in later iterations. It implements `INotifyPropertyChanged` for you and adds reactive features. Not used in this iteration, but installed here. |
| **Dependency Injection (DI)** | A pattern where objects receive their dependencies (services) through constructor parameters rather than creating them directly. This makes code testable and loosely coupled. |
| **`ServiceCollection` / `IServiceProvider`** | The Microsoft DI container. You register services in a `ServiceCollection`, then call `BuildServiceProvider()` to get an `IServiceProvider` that can resolve those services. In this iteration we set up the container but register nothing in it yet. |
| **Splat / `Locator`** | ReactiveUI's built-in service locator. Calling `.UseReactiveUI()` registers ReactiveUI's internal services into Splat. We do **not** put our own application services there — we use the Microsoft DI container exclusively. |
| **Inner class** | A class defined inside another class. The four model classes currently live inside `MainWindow` — this makes them tightly coupled to the Window. We extract them to standalone files. |

---

## Prerequisites

Before starting this iteration, confirm:

- You are on the **`avalonia_mvvm`** git branch.
- The workspace root is the CiderPress2 solution directory.
- The `cp2_avalonia/` project builds and runs successfully (`dotnet build cp2_avalonia/cp2_avalonia.csproj` — zero errors).

---

## Step 1: Add NuGet Packages

### What we are going to accomplish

We need two NuGet packages that the entire MVVM refactor depends on:

1. **`ReactiveUI.Avalonia`** — This is the Avalonia integration package for
   ReactiveUI. When you install it, NuGet automatically pulls in the core
   `ReactiveUI` package as a transitive dependency. This single package gives
   you everything: `ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`,
   `ReactiveWindow<T>`, and `Interaction<TInput, TOutput>`. None of these are
   used in Iteration 0, but they must be available for Iteration 1 and beyond.

2. **`Microsoft.Extensions.DependencyInjection`** — Microsoft's lightweight DI
   container. In later iterations, you will register services like
   `IDialogService`, `ISettingsService`, and `IWorkspaceService` here and inject
   them into ViewModel constructors. In this iteration we just set up the empty
   container.

**Why not use Splat (ReactiveUI's built-in service locator)?**
ReactiveUI ships with its own service locator called Splat. When you call
`.UseReactiveUI()`, ReactiveUI registers its internal services (view locator,
logging, etc.) into Splat. That is fine — let it do that. But for *your*
application services, the project uses Microsoft's DI container because it is
more widely understood, better documented, supports constructor injection
natively, and will be familiar if you come from ASP.NET Core or other .NET
projects. The rule is simple: **never register your own services in
`Locator.Current`** — use `IServiceCollection` / `IServiceProvider` exclusively.

**Why this step is first:** Every subsequent iteration assumes these packages
are available. Installing them now (with no code changes) means you can verify
the project still builds before touching any source files.

### To do that, follow these steps

1. Open a terminal at the **solution root** (the directory containing
   `CiderPress2.sln`).

2. Run the following two commands:

   ```
   dotnet add cp2_avalonia/cp2_avalonia.csproj package ReactiveUI.Avalonia
   dotnet add cp2_avalonia/cp2_avalonia.csproj package Microsoft.Extensions.DependencyInjection
   ```

3. Open `cp2_avalonia/cp2_avalonia.csproj` and verify the new `<PackageReference>`
   entries were added. Check that:
   - `ReactiveUI.Avalonia` resolved to a version in the **11.x** series
     (e.g., 11.4.12 or later). ReactiveUI uses its own versioning scheme — the
     "11" does not correspond to the Avalonia version number. This is normal.
   - `Microsoft.Extensions.DependencyInjection` resolved to a version compatible
     with `net8.0` (expected: 8.x or 9.x).

4. **Build checkpoint:** Run `dotnet build cp2_avalonia/cp2_avalonia.csproj` and
   confirm zero errors.

> **Do not** modify any other files in this step. The only change is the
> `.csproj` file gaining two `<PackageReference>` entries.

### Now that those are done, here's what changed

- **Modified:** `cp2_avalonia/cp2_avalonia.csproj` — two new `<PackageReference>`
  entries added.
- **New capability:** The ReactiveUI and DI namespaces are now available for
  `using` directives throughout the project. No code uses them yet.
- **Behavior:** Identical to before. The packages are referenced but not called.

---

## Step 2: Wire ReactiveUI into the App Builder

### What we are going to accomplish

Avalonia applications have a **builder chain** — a fluent method sequence in
`Program.cs` that configures the application before it starts. The chain currently
looks like:

```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace();
```

Each `.UseSomething()` call registers a subsystem. We need to add
`.UseReactiveUI()` to tell Avalonia to initialize ReactiveUI's internal services.
This call does the following under the hood:

- Registers ReactiveUI's `Splat`-based service locator entries (its own internal
  view locator, scheduler, logging adapters, etc.)
- Sets up `RxApp.MainThreadScheduler` to use Avalonia's dispatcher, which ensures
  reactive pipelines that end on the UI thread work correctly.

**You do not need to understand these internals right now.** The important thing is:
without this call, ReactiveUI features like `ReactiveCommand` and `WhenAnyValue`
will not work correctly in later iterations.

**Why Program.cs and not App.axaml.cs?** The builder chain lives in `Program.cs`
where `BuildAvaloniaApp()` is defined. `App.axaml.cs` contains the `Application`
class with lifecycle methods (`Initialize`, `OnFrameworkInitializationCompleted`),
but the builder configuration happens before those methods are called. The two
files have different roles: `Program.cs` configures the Avalonia runtime itself;
`App.axaml.cs` configures your application within that runtime.

### To do that, follow these steps

1. Open `cp2_avalonia/Program.cs`.

2. At the top of the file, in the `using` block (after `using Avalonia;`), add:
   ```csharp
   using ReactiveUI.Avalonia;
   ```

3. In the `BuildAvaloniaApp()` method, insert `.UseReactiveUI()` between
   `.WithInterFont()` and `.LogToTrace()`. The complete method should look like:

   ```csharp
   // Avalonia configuration; don't remove; also used by visual designer.
   public static AppBuilder BuildAvaloniaApp()
       => AppBuilder.Configure<App>()
           .UsePlatformDetect()
           .WithInterFont()
           .UseReactiveUI()
           .LogToTrace();
   ```

4. **Do not** change anything else in `Program.cs`. Preserve the existing comment
   above the method, the `[STAThread]` attribute, and the `Main` method.

5. **Build checkpoint:** Run `dotnet build cp2_avalonia/cp2_avalonia.csproj` and
   confirm zero errors before proceeding.

> **What `.UseReactiveUI()` does NOT do:** It does not change how your existing
> code works. Your current `INotifyPropertyChanged` implementations, `RelayCommand`
> instances, and `DataContext = this` patterns all continue to work exactly as before.
> ReactiveUI is additive — it sits alongside the existing code until you choose to
> use it.

### Now that those are done, here's what changed

- **Modified:** `cp2_avalonia/Program.cs` — one `using` directive and one method
  call added.
- **New capability:** ReactiveUI's internal services are now initialized at startup.
  `RxApp.MainThreadScheduler` is wired to Avalonia's dispatcher. In later
  iterations, this enables `ReactiveCommand`, `WhenAnyValue`, and
  `ObservableAsPropertyHelper` to work correctly.
- **Behavior:** Identical to before. The application starts, runs, and shuts down
  the same way.

---

## Step 3: Set Up DI in App.axaml.cs

### What we are going to accomplish

We need a **Dependency Injection container** — a central registry where services
are registered at startup and then retrieved (resolved) by the code that needs
them. In this iteration we set up the container infrastructure but leave it empty.

Here is the concept in plain terms:

- A `ServiceCollection` is like a recipe book — you tell it "when someone asks for
  `IDialogService`, give them a `DialogService` instance."
- Calling `BuildServiceProvider()` turns that recipe book into a kitchen — an
  `IServiceProvider` that can actually create and hand out those services.
- We store the `IServiceProvider` in a static property (`App.Services`) so that
  any code in the application can access it.

In later iterations (starting with Iteration 3A), you will add lines like:
```csharp
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IClipboardService, ClipboardService>();
```
For now, the container is empty — it exists but has nothing registered in it.

**Why a static property?** ViewModels will receive their services through
constructor injection (the preferred pattern), but the very first ViewModel
needs to be created somewhere, and that somewhere is `App.axaml.cs`. Having
`App.Services` as a static property provides a single well-known location for
the root `IServiceProvider`. This is a common pattern in desktop applications
that don't have the built-in host infrastructure of ASP.NET Core.

**Why set it up now if it's empty?** Because it establishes the pattern early.
When Iteration 3A begins adding services, the DI infrastructure will already be
in place and tested. This reduces the number of moving parts in any single
iteration.

### To do that, follow these steps

1. Open `cp2_avalonia/App.axaml.cs`.

2. Add the following `using` directive to the existing using block:
   ```csharp
   using Microsoft.Extensions.DependencyInjection;
   ```
   Note: `using System;` is already present — do not add a duplicate.

3. Inside the `App` class, **after** the `ThemeMode` enum and icon color
   constants but **before** the `Initialize()` method, add this property:

   ```csharp
   /// <summary>
   /// Application-wide service provider. Populated during
   /// OnFrameworkInitializationCompleted.
   /// </summary>
   public static IServiceProvider Services { get; private set; } = null!;
   ```

   The `= null!` is a C# pattern that tells the compiler "I know this is null
   right now, but I guarantee it will be set before anyone reads it." This avoids
   a nullable warning while making the intent clear. It gets assigned in
   `OnFrameworkInitializationCompleted()`, which runs before any other code can
   access it.

4. Modify the `OnFrameworkInitializationCompleted()` method by inserting the
   `ServiceCollection` setup **before** the existing `if` block. The method
   should look like:

   ```csharp
   public override void OnFrameworkInitializationCompleted() {
       var services = new ServiceCollection();

       // Register services here in future iterations, e.g.:
       // services.AddSingleton<ISettingsService, SettingsService>();

       Services = services.BuildServiceProvider();

       if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
           desktop.MainWindow = new MainWindow();
       }
       base.OnFrameworkInitializationCompleted();
   }
   ```

   You are adding exactly three lines before the existing `if` block:
   - `var services = new ServiceCollection();`
   - The comment (a reminder for future iterations)
   - `Services = services.BuildServiceProvider();`

5. **Do not** modify any other methods in `App.axaml.cs`. The `ThemeMode` enum,
   `ApplyTheme()` method, icon brush logic, `GetMainWindow()` helper, and macOS
   native menu handlers (`OnNativeAboutClick`, `OnNativeSettingsClick`,
   `OnNativeQuitClick`) must all remain exactly as they are. Do not modify
   `Initialize()`.

6. **Build checkpoint:** Run `dotnet build cp2_avalonia/cp2_avalonia.csproj` and
   confirm zero errors before proceeding.

### Now that those are done, here's what changed

- **Modified:** `cp2_avalonia/App.axaml.cs` — one `using` directive, one static
  property, and three lines added to `OnFrameworkInitializationCompleted()`.
- **New capability:** An `IServiceProvider` is now available at `App.Services`.
  In later iterations, ViewModels and other startup code will use this to resolve
  registered services.
- **Behavior:** Identical to before. The container exists but is empty. The
  `MainWindow` is still created the same way, and no services are injected
  anywhere.

---

## Step 4: Extract Inner Classes to Models/

### What we are going to accomplish

Currently, four data classes are defined **inside** `MainWindow.axaml.cs` as
*inner classes* (also called *nested classes*). They are:

| Class | Purpose |
|---|---|
| `ConvItem` | Represents an import/export converter choice in the options panel |
| `CenterInfoItem` | A key/value pair displayed in the center info panel |
| `PartitionListItem` | A row in the partition layout list |
| `MetadataItem` | A row in the metadata list, with edit capability |

Being defined inside `MainWindow` means their fully-qualified type name is
`MainWindow.ConvItem`, `MainWindow.CenterInfoItem`, etc. This creates an
unnecessary coupling: any code that uses these types must reference `MainWindow`,
even if it has nothing to do with the Window itself.

**Why extract them now?** In later iterations, ViewModels will need to work with
these types. If they stay inside `MainWindow`, then every ViewModel that uses a
`ConvItem` or `MetadataItem` would need a reference to `MainWindow` — which
violates the core MVVM rule that ViewModels never know about Views. Moving them
to a `Models/` namespace makes them independent, reusable data objects.

Additionally, the MVVM architecture plan (`MVVM_Notes.md` §5) specifies a
`Models/` directory that will eventually hold these four classes plus
`FileListItem`, `ArchiveTreeItem`, and `DirectoryTreeItem` (those three move
in later iterations when their classes are refactored).

**What is the Models/ folder for?** In MVVM, "Model" refers to plain data objects
that represent application data. They are not Views (they have no UI) and not
ViewModels (they don't manage UI state or commands). They are simple classes that
hold data and can be used anywhere — in Views, ViewModels, Services, or tests.

**What about `MetadataItem`'s `INotifyPropertyChanged`?** `MetadataItem` implements
`INotifyPropertyChanged` because its `Value` property can be updated after
construction (via the `SetValue` method), and the UI needs to know when that
happens. In a future iteration, `MetadataItem` will be converted to extend
`ReactiveObject` and use `this.RaiseAndSetIfChanged(...)`. For now, we keep the
existing `INotifyPropertyChanged` implementation to avoid any behavior change.

**What about `MetadataItem`'s `IBrush` property?** `MetadataItem.TextForeground`
returns an Avalonia `IBrush` (either `Brushes.Black` or `Brushes.Gray` depending
on `CanEdit`). This is technically a View concern leaking into a Model — a later
iteration will replace it with a bool or enum and a value converter. For now, we
keep it as-is to maintain identical behavior.

### To do that, follow these steps

#### 4a. Create `cp2_avalonia/Models/ConvItem.cs`

1. Create the directory `cp2_avalonia/Models/` if it does not already exist.

2. Create a new file `cp2_avalonia/Models/ConvItem.cs` with the following content:

   ```csharp
   /*
    * Copyright 2023 faddenSoft
    * Copyright 2026 Lydian Scale Software
    *
    * Licensed under the Apache License, Version 2.0 (the "License");
    * you may not use this file except in compliance with the License.
    * You may obtain a copy of the License at
    *
    *     http://www.apache.org/licenses/LICENSE-2.0
    *
    * Unless required by applicable law or agreed to in writing, software
    * distributed under the License is distributed on an "AS IS" BASIS,
    * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    * See the License for the specific language governing permissions and
    * limitations under the License.
    */
   namespace cp2_avalonia.Models {
       /// <summary>
       /// Represents an import or export converter selection in the options panel.
       /// </summary>
       public class ConvItem {
           public string Tag { get; }
           public string Label { get; }
           public ConvItem(string tag, string label) { Tag = tag; Label = label; }
       }
   }
   ```

   This is a simple immutable data class — two read-only properties set in the
   constructor. No reactive features needed.

#### 4b. Create `cp2_avalonia/Models/CenterInfoItem.cs`

Create `cp2_avalonia/Models/CenterInfoItem.cs` with the following content:

```csharp
/*
 * Copyright 2023 faddenSoft
 * Copyright 2026 Lydian Scale Software
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
namespace cp2_avalonia.Models {
    /// <summary>
    /// Key/value pair displayed in the center info panel.
    /// </summary>
    public class CenterInfoItem {
        public string Name { get; }
        public string Value { get; }
        public CenterInfoItem(string name, string value) { Name = name; Value = value; }
    }
}
```

Same pattern as `ConvItem` — a simple immutable pair.

#### 4c. Create `cp2_avalonia/Models/PartitionListItem.cs`

Create `cp2_avalonia/Models/PartitionListItem.cs` with the following content:

```csharp
/*
 * Copyright 2023 faddenSoft
 * Copyright 2026 Lydian Scale Software
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using DiskArc;
using DiskArc.Multi;

namespace cp2_avalonia.Models {
    /// <summary>
    /// Item displayed in the partition layout list in the center info panel.
    /// </summary>
    public class PartitionListItem {
        public int Index { get; }
        public long StartBlock { get; }
        public long BlockCount { get; }
        public string PartName { get; }
        public string PartType { get; }
        public Partition PartRef { get; }
        public PartitionListItem(int index, Partition part) {
            PartRef = part;
            Index = index;
            StartBlock = part.StartOffset / Defs.BLOCK_SIZE;
            BlockCount = part.Length / Defs.BLOCK_SIZE;
            PartName = string.Empty;
            PartType = string.Empty;
        }
        public override string ToString() {
            return "[Part: start=" + StartBlock + " count=" + BlockCount + "]";
        }
    }
}
```

This class is slightly more complex — it takes a `Partition` object from the
`DiskArc` library and computes block offsets. Note the `using DiskArc;` and
`using DiskArc.Multi;` directives — these reference the existing unchanged
library. The class computes display values from the domain data at construction
time.

#### 4d. Create `cp2_avalonia/Models/MetadataItem.cs`

Create `cp2_avalonia/Models/MetadataItem.cs` with the following content:

```csharp
/*
 * Copyright 2023 faddenSoft
 * Copyright 2026 Lydian Scale Software
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System.ComponentModel;

using Avalonia.Media;

namespace cp2_avalonia.Models {
    /// <summary>
    /// Item displayed in the metadata list in the center info panel.
    /// </summary>
    // NOTE: MetadataItem retains INotifyPropertyChanged in Phase 0 to preserve
    // existing behavior. It will be converted to ReactiveObject (using
    // this.RaiseAndSetIfChanged) when the class is refactored in a later phase.
    // Do not use this class as a template for new Models/ classes.
    public class MetadataItem : INotifyPropertyChanged {
        public string Key { get; private set; }
        public string Value { get; private set; }
        public string? Description { get; private set; }
        public string? ValueSyntax { get; private set; }
        public bool CanEdit { get; private set; }
        // TODO (MVVM Phase 4/5): Replace IBrush dependency with a plain bool or
        // enum and let the View convert it via a value converter (e.g.,
        // BoolToForegroundConverter). This removes Avalonia.Media from Models/.
        public IBrush TextForeground => CanEdit ? Brushes.Black : Brushes.Gray;

        public MetadataItem(string key, string value, string description,
                string valueSyntax, bool canEdit) {
            Key = key;
            Value = value;
            Description = string.IsNullOrEmpty(description) ? null : description;
            ValueSyntax = string.IsNullOrEmpty(valueSyntax) ? null : valueSyntax;
            CanEdit = canEdit;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetValue(string value) {
            Value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}
```

`MetadataItem` is the most complex of the four. Key things to notice:

- It implements `INotifyPropertyChanged` directly (the old-school way). This will
  be replaced with `ReactiveObject` in a later iteration — the `NOTE` comment at
  the top says so explicitly.
- `SetValue()` updates the `Value` property and fires `PropertyChanged` so the UI
  updates. This is how Avalonia knows to redraw the cell when metadata is edited.
- `TextForeground` returns an Avalonia `IBrush` — this is a View concern that will
  be cleaned up later. The `TODO` comment marks it for future work.

#### 4e. Update References in Consuming Files

Now that the classes exist as standalone files in `cp2_avalonia.Models`, you need
to remove them from `MainWindow.axaml.cs` and update any qualified references.

**In `MainWindow.axaml.cs`:**

1. Add `using cp2_avalonia.Models;` to the top of the file.
2. Find and **remove** the four inner class definitions:
   - The `ConvItem` class
   - The `CenterInfoItem` class
   - The `PartitionListItem` class
   - The `MetadataItem` class (including its `INotifyPropertyChanged` implementation)

   All unqualified references to these types within `MainWindow.axaml.cs` (e.g.,
   `new ConvItem(...)`, `ObservableCollection<MetadataItem>`) will resolve
   automatically through the new `using` directive. No other changes needed in
   this file.

**In `MainController_Panels.cs`:**

1. Add `using cp2_avalonia.Models;` to the using block.
2. Replace all qualified references:
   - `new MainWindow.CenterInfoItem(` → `new CenterInfoItem(`
   - `MainWindow.PartitionListItem` → `PartitionListItem` (in parameter types
     and usages)
   - `MainWindow.MetadataItem` → `MetadataItem` (in parameter types and usages)

**Verification step — important:** Run a project-wide search (Ctrl+Shift+F or
`grep -rn`) for each of these patterns to confirm **zero** remaining qualified
references:
- `MainWindow.ConvItem`
- `MainWindow.CenterInfoItem`
- `MainWindow.PartitionListItem`
- `MainWindow.MetadataItem`

If the search unexpectedly finds matches in files other than `MainWindow.axaml.cs`
and `MainController_Panels.cs`, add `using cp2_avalonia.Models;` and remove the
`MainWindow.` qualifier in those files too.

**AXAML files:** No `.axaml` files reference these types by name — all bindings
use property names (e.g., `{Binding Name}`, `{Binding Value}`), not type names.
No AXAML changes are required.

**`MainController.cs`:** This file does not use qualified references to these
types and needs no changes. Verify with the search above.

### Now that those are done, here's what changed

- **Created:** `cp2_avalonia/Models/` directory with four new files:
  `ConvItem.cs`, `CenterInfoItem.cs`, `PartitionListItem.cs`, `MetadataItem.cs`.
- **Modified:** `MainWindow.axaml.cs` — inner class definitions removed,
  `using cp2_avalonia.Models;` added.
- **Modified:** `MainController_Panels.cs` — `MainWindow.*` qualifiers removed,
  `using cp2_avalonia.Models;` added.
- **New capability:** These four types are now in the `cp2_avalonia.Models`
  namespace and can be referenced from ViewModels without depending on
  `MainWindow`. This is a prerequisite for Iteration 1 (creating `MainViewModel`)
  and for future multi-viewer support where multiple `FileViewerViewModel`
  instances need these types independently of any single window.
- **Behavior:** Identical to before. The classes are the same code; only their
  file location and namespace changed.

---

## Step 5: Build and Validate

### What we are going to accomplish

This is the final verification step. The goal is to confirm that the application
is **functionally identical** to before Iteration 0 began. Every change in this
iteration was infrastructure — adding packages, wiring plumbing, and moving code
to new files. Nothing should look or behave differently to the user.

This verification step is important because it establishes a clean baseline. If
anything breaks in a later iteration, you can confidently rule out Iteration 0 as
the cause — as long as you verified it here.

### To do that, follow these steps

1. **Build:** Run `dotnet build cp2_avalonia/cp2_avalonia.csproj` and verify:
   - Zero errors
   - No new warnings (pre-existing warnings are fine)

2. **Launch:** Start the application and verify it displays the launch panel
   normally.

3. **Open a file:** Open a disk image or file archive and verify:
   - The archive tree populates on the left
   - The directory tree populates below it
   - The file list populates in the center
   - The info panel shows correct data

4. **Exercise the UI:** Verify all menu items, toolbar buttons, and keyboard
   shortcuts function as before. Try at least:
   - Actions menu operations (if you have a writable test file)
   - View menu toggles (options panel, column visibility)
   - Edit menu items
   - Toolbar buttons

5. **Settings persistence:** Close and reopen the application. Verify settings
   (window position, column visibility, theme) persisted correctly.

6. **Theme switch:** Open Edit → Settings, change the theme (Light ↔ Dark), and
   click OK. Verify:
   - The application repaints with the correct theme
   - Toolbar icons update to the appropriate color
   - Close and reopen to verify the theme preference persisted

**Expected result:** The application is functionally identical to before this
iteration. The only differences are under the hood:

| Change | Where |
|---|---|
| Two new NuGet packages | `cp2_avalonia.csproj` |
| `.UseReactiveUI()` in builder chain | `Program.cs` |
| `ServiceCollection`/`IServiceProvider` (empty) | `App.axaml.cs` |
| Four model classes in standalone files | `Models/` directory |

### Now that those are done, here's what changed

- **No new changes in this step** — this was a verification-only step.
- **Confirmed:** All prior changes are non-breaking and the application is
  production-ready at this commit point.

---

## What This Iteration Enables

With Iteration 0 complete, the foundation is in place for the rest of the MVVM
refactor. Here is what becomes possible in subsequent iterations:

| Future Iteration | What It Can Now Do | Because Of |
|---|---|---|
| **Iteration 1** (MainViewModel) | Create ViewModels inheriting from `ReactiveObject` | ReactiveUI package installed |
| **Iteration 1** (MainViewModel) | Reference `ConvItem`, `MetadataItem`, etc. from ViewModels without depending on `MainWindow` | Model classes extracted to `Models/` |
| **Iteration 2** (Commands) | Create `ReactiveCommand` instances with observable `CanExecute` | ReactiveUI package installed |
| **Iteration 2** (Commands) | Use `WhenAnyValue` for reactive property observation | ReactiveUI + `.UseReactiveUI()` wired |
| **Iteration 3A** (Services) | Register and resolve services via `App.Services` | DI container set up in `App.axaml.cs` |

The model class extraction is also a prerequisite for future multi-viewer support
(see `MVVM_Notes.md` §7.10): when multiple `FileViewerViewModel` instances exist
concurrently, they need to reference these model types independently of any single
window.

---

## Quick Reference: Files Modified in This Iteration

| File | Change |
|---|---|
| `cp2_avalonia/cp2_avalonia.csproj` | Added `ReactiveUI.Avalonia` and `Microsoft.Extensions.DependencyInjection` package references |
| `cp2_avalonia/Program.cs` | Added `using ReactiveUI.Avalonia;` and `.UseReactiveUI()` to builder chain |
| `cp2_avalonia/App.axaml.cs` | Added `using Microsoft.Extensions.DependencyInjection;`, `App.Services` property, and `ServiceCollection` setup |
| `cp2_avalonia/MainWindow.axaml.cs` | Removed four inner classes, added `using cp2_avalonia.Models;` |
| `cp2_avalonia/MainController_Panels.cs` | Removed `MainWindow.*` qualifiers, added `using cp2_avalonia.Models;` |
| `cp2_avalonia/Models/ConvItem.cs` | **New** — extracted from `MainWindow` |
| `cp2_avalonia/Models/CenterInfoItem.cs` | **New** — extracted from `MainWindow` |
| `cp2_avalonia/Models/PartitionListItem.cs` | **New** — extracted from `MainWindow` |
| `cp2_avalonia/Models/MetadataItem.cs` | **New** — extracted from `MainWindow` |
