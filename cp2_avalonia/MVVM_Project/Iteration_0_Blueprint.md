# Iteration 0 Blueprint: MVVM Infrastructure — ReactiveUI & DI Setup

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` to familiarize yourself
> with the MVVM refactor context, technology choices, and conventions before proceeding.

---

## Goal

Install ReactiveUI and DI infrastructure into the existing `cp2_avalonia` project **without
changing any application behavior**. After this iteration the app compiles, runs, and behaves
identically — but the ReactiveUI and DI plumbing is in place for subsequent phases.

Specifically:

1. Add `Avalonia.ReactiveUI` and `Microsoft.Extensions.DependencyInjection` NuGet packages.
2. Wire `.UseReactiveUI()` into the Avalonia app builder.
3. Set up a basic `IServiceProvider` in `App.axaml.cs` using `ServiceCollection`.
4. Extract the four inner classes from `MainWindow.axaml.cs` (`ConvItem`, `CenterInfoItem`,
   `PartitionListItem`, `MetadataItem`) into standalone files under `Models/`.

---

## Prerequisites

- You are on the `avalonia_mvvm` git branch.
- The workspace root is the CiderPress2 solution directory.
- The `cp2_avalonia/` project builds and runs successfully before starting.

---

## Step-by-Step Instructions

### Step 1: Add NuGet Packages

Add two package references to `cp2_avalonia/cp2_avalonia.csproj`:

```xml
<PackageReference Include="Avalonia.ReactiveUI" Version="11.2.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.*" />
```

Add them to the existing `<ItemGroup>` containing the other Avalonia packages.

After editing, run `dotnet restore` to verify the packages resolve.

---

### Step 2: Wire ReactiveUI into the App Builder

In `cp2_avalonia/Program.cs`, add `.UseReactiveUI()` to the builder chain:

```csharp
using Avalonia;
using Avalonia.ReactiveUI;

namespace cp2_avalonia {
    internal class Program {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .UseReactiveUI()
                .LogToTrace();

        [STAThread]
        public static void Main(string[] args) {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
    }
}
```

Changes:
- Add `using Avalonia.ReactiveUI;`
- Add `.UseReactiveUI()` after `.WithInterFont()`

---

### Step 3: Set Up DI in App.axaml.cs

Update `cp2_avalonia/App.axaml.cs` to create a `ServiceCollection` and build an
`IServiceProvider`. For now the container is empty — services will be registered in
later iterations.

```csharp
using System;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;

namespace cp2_avalonia {
    public partial class App : Application {
        /// <summary>
        /// Application-wide service provider. Populated during
        /// OnFrameworkInitializationCompleted.
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);
        }

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
    }
}
```

Changes:
- Add `using System;` and `using Microsoft.Extensions.DependencyInjection;`
- Add `public static IServiceProvider Services` property
- Create `ServiceCollection`, build provider, assign to `Services`

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
    public class MetadataItem : INotifyPropertyChanged {
        public string Key { get; private set; }
        public string Value { get; private set; }
        public string? Description { get; private set; }
        public string? ValueSyntax { get; private set; }
        public bool CanEdit { get; private set; }
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

#### 4e. Update `MainWindow.axaml.cs`

Remove the four inner class definitions from `MainWindow.axaml.cs` and add a
`using cp2_avalonia.Models;` directive at the top of the file. All references to
`ConvItem`, `CenterInfoItem`, `PartitionListItem`, and `MetadataItem` throughout
`MainWindow.axaml.cs` will resolve to the `Models` namespace types.

Also add `using cp2_avalonia.Models;` to any other files that reference these types
(check `MainController.cs`, `MainController_Panels.cs`, and dialog files).

---

### Step 5: Build and Validate

1. Run `dotnet build` — verify zero errors and no new warnings.
2. Launch the application — verify it starts normally and displays the launch panel.
3. Open a disk image or file archive — verify the full UI works as before.
4. Verify all menu items, toolbar buttons, and keyboard shortcuts function.
5. Close and reopen — verify settings persist.

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
