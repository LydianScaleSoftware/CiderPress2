# Iteration 0 Blueprint: MVVM Infrastructure — ReactiveUI & DI Setup

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` to familiarize yourself
> with the MVVM refactor context, technology choices, and conventions before proceeding.

---

## Goal

Install ReactiveUI and DI infrastructure into the existing `cp2_avalonia` project **without
changing any application behavior**. After this iteration the app compiles, runs, and behaves
identically — but the ReactiveUI and DI plumbing is in place for subsequent phases.

Specifically:

1. Add `ReactiveUI.Avalonia` and `Microsoft.Extensions.DependencyInjection` NuGet packages.
2. Wire `.UseReactiveUI()` into the Avalonia app builder.
3. Set up a basic `IServiceProvider` in `App.axaml.cs` using `ServiceCollection`.
4. Extract the four inner classes from `MainWindow.axaml.cs` (`ConvItem`, `CenterInfoItem`,
   `PartitionListItem`, `MetadataItem`) into standalone files under `Models/`.

---

## Prerequisites

- You are on the `avalonia_mvvm` git branch.
- The workspace root is the CiderPress2 solution directory.
- The `cp2_avalonia/` project builds and runs successfully before starting.

> **Scope note:** `MVVM_Notes.md §6 Phase 0` lists six steps; this blueprint covers
> steps 1–4 only. Steps 5 (service interfaces) and 6 (static helper method
> refactoring) are deferred to `Iteration_3A_Blueprint.md` and later iterations
> respectively. Do not create any service interfaces or touch `ArchiveTreeItem`,
> `DirectoryTreeItem`, or `FileListItem` during this iteration.

---

## Step-by-Step Instructions

### Step 1: Add NuGet Packages

Run the following commands from the solution root to add both packages. NuGet
will resolve and record the latest compatible version in the project file.
The resulting `<ItemGroup>` placement is cosmetic; the build does not require
packages to be in a specific group.

```
dotnet add cp2_avalonia/cp2_avalonia.csproj package ReactiveUI.Avalonia
dotnet add cp2_avalonia/cp2_avalonia.csproj package Microsoft.Extensions.DependencyInjection
```

After running, open `cp2_avalonia/cp2_avalonia.csproj` and verify that:
- `ReactiveUI.Avalonia` resolved to a version in the **11.x** series
  (e.g., 11.4.12 or later). This is expected — the package uses its own
  versioning scheme independent of the Avalonia version number.
- `Microsoft.Extensions.DependencyInjection` resolved to a version compatible
  with `net8.0` (expected: 8.x or 9.x).

If either version looks wrong, consult the package's NuGet page or release notes
and pin to a known-good version explicitly.

---

### Step 2: Wire ReactiveUI into the App Builder

In `cp2_avalonia/Program.cs`, make two targeted additions — do **not** replace the
entire file. Preserve the existing comment on `BuildAvaloniaApp()` and all other
content.

1. Add `using ReactiveUI.Avalonia;` to the existing using block (after `using Avalonia;`).
2. Insert `.UseReactiveUI()` between `.WithInterFont()` and `.LogToTrace()` in the
   builder chain.

The resulting builder chain should look like:

```csharp
// Avalonia configuration; don't remove; also used by visual designer.
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .UseReactiveUI()
        .LogToTrace();
```

> **Note:** Calling `.UseReactiveUI()` causes ReactiveUI to register its internal
> services into `Splat.Locator`. This is expected and should not be interfered with.
> Application services continue to be registered exclusively in the
> `Microsoft.Extensions.DependencyInjection` `ServiceCollection`.
> See Pre-Iteration-Notes §2.

After this step, run `dotnet build cp2_avalonia/cp2_avalonia.csproj` and verify
zero errors before proceeding.

---

### Step 3: Set Up DI in App.axaml.cs

Update `cp2_avalonia/App.axaml.cs` to add DI infrastructure. Make **targeted
additions only** — do not replace the entire file. The existing `ThemeMode` enum,
`ApplyTheme()` method, icon brush logic, `GetMainWindow()` helper, and macOS native
menu handlers (`OnNativeAboutClick`, `OnNativeSettingsClick`, `OnNativeQuitClick`)
must all be preserved. Do not modify `Initialize()` or any other existing methods.

Changes to make:

1. Add `using Microsoft.Extensions.DependencyInjection;` to the existing using block.
   (`using System;` is already present — do not add a duplicate.)

2. Add the following property inside the `App` class, near the top (after the
   `ThemeMode` enum and icon color constants):

   ```csharp
   /// <summary>
   /// Application-wide service provider. Populated during
   /// OnFrameworkInitializationCompleted.
   /// </summary>
   public static IServiceProvider Services { get; private set; } = null!;
   ```

3. In `OnFrameworkInitializationCompleted()`, insert the `ServiceCollection` setup
   **before** the existing `desktop.MainWindow = new MainWindow();` line:

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

   The only change to this method is adding the three lines (`var services`,
   comment, `Services = ...`) before the existing `if` block. All other existing
   code in `App.axaml.cs` remains untouched.

After this step, run `dotnet build cp2_avalonia/cp2_avalonia.csproj` and verify
zero errors before proceeding.

---

### Step 4: Extract Inner Classes to Models/

Create the `cp2_avalonia/Models/` directory and extract the four inner classes from
`MainWindow.axaml.cs` into standalone files. Each class keeps the same name, properties,
and behavior — only the namespace and file location change.

#### 4a. Create `cp2_avalonia/Models/ConvItem.cs`

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

#### 4b. Create `cp2_avalonia/Models/CenterInfoItem.cs`

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

#### 4c. Create `cp2_avalonia/Models/PartitionListItem.cs`

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

#### 4d. Create `cp2_avalonia/Models/MetadataItem.cs`

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

#### 4e. Update References in Consuming Files

Remove the four inner class definitions from `MainWindow.axaml.cs` and add a
`using cp2_avalonia.Models;` directive at the top of the file. All references to
`ConvItem`, `CenterInfoItem`, `PartitionListItem`, and `MetadataItem` within
`MainWindow.axaml.cs` are unqualified and will resolve automatically via the
new `using`.

Also update `MainController_Panels.cs`, which references these types with their
qualified `MainWindow.*` prefix:

**In `MainController_Panels.cs`:**
1. Add `using cp2_avalonia.Models;` to the using block.
2. Replace `new MainWindow.CenterInfoItem(` with `new CenterInfoItem(` (line ~888
   and any other occurrences).
3. Replace `MainWindow.PartitionListItem` with `PartitionListItem` in parameter
   types (line ~1099 and all usages).
4. Replace `MainWindow.MetadataItem` with `MetadataItem` in parameter types
   (line ~1132 and all usages).

**Verification:** Run a project-wide search for `MainWindow\.ConvItem`,
`MainWindow\.CenterInfoItem`, `MainWindow\.PartitionListItem`, and
`MainWindow\.MetadataItem` to confirm zero remaining qualified references.
`MainController.cs` and dialog files have no qualified references to these types
and need no changes. If the search unexpectedly finds matches in other files,
add `using cp2_avalonia.Models;` and remove the `MainWindow.` qualifier in those
files before building.

**AXAML:** No `.axaml` files reference these types by name (all bindings are
property-name based). No AXAML changes are required.

---

### Step 5: Build and Validate

1. Run `dotnet build cp2_avalonia/cp2_avalonia.csproj` — verify zero errors and
   no new warnings.
2. Launch the application — verify it starts normally and displays the launch panel.
3. Open a disk image or file archive — verify the full UI works as before.
4. Verify all menu items, toolbar buttons, and keyboard shortcuts function.
5. Close and reopen — verify settings persist.
6. **Theme switch:** Open Edit → Settings, change the theme to Dark (or Light
   if currently Dark), and click OK. Verify the application repaints with the
   correct theme and that toolbar icons update to the appropriate color. Close
   and reopen to verify the theme preference persisted.

**Expected result:** The application is functionally identical to before this iteration.
The only differences are:
- Two new NuGet packages in the project file
- `.UseReactiveUI()` in the app builder
- A `ServiceCollection`/`IServiceProvider` in `App.axaml.cs` (unused for now)
- Four model classes moved from `MainWindow` inner classes to `Models/` namespace

---

## What This Enables

After this iteration, subsequent phases can:
- Create ViewModels inheriting from `ReactiveObject` (from ReactiveUI)
- Create `ReactiveCommand` instances (from ReactiveUI)
- Register and resolve services via `App.Services`
- Use `WhenAnyValue` for reactive property observation
- Reference `ConvItem`, `CenterInfoItem`, `PartitionListItem`, `MetadataItem` from
  ViewModels without depending on `MainWindow`

Extracting the inner classes is also a prerequisite for future multi-viewer
support (see `MVVM_Notes.md` §7.10): when multiple `FileViewerViewModel`
instances exist concurrently, they need to reference these model types
independently of any single window.
