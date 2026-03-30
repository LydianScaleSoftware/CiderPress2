# Iteration 0 Blueprint: Project Scaffolding — Empty Window

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Create from scratch a minimal Avalonia application in `cp2_avalonia/` that compiles, runs,
and displays a blank window titled "CiderPress II" on all platforms. This iteration
establishes the project skeleton, `.sln` integration, and resource infrastructure.

---

## Prerequisites

- You are on the `avalonia` git branch.
- The workspace root is `/home/mlong/develop/CiderPress2/`.
- The `cp2_avalonia/` directory already exists (contains planning docs).

---

## Step-by-Step Instructions

### Step 1: Create `cp2_avalonia/cp2_avalonia.csproj`

Create the project file with these exact requirements:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AssemblyName>CiderPress2</AssemblyName>
    <ApplicationIcon>Res/cp2_app.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.*" />
    <PackageReference Include="Avalonia.Controls.DataGrid" Version="11.2.*" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.*" />
    <PackageReference Include="Avalonia.AvaloniaEdit" Version="11.2.*" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.2.*" Condition="'$(Configuration)' == 'Debug'" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Res/cp2_app.ico" />
    <AvaloniaResource Include="Res/RedX.png" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AppCommon\AppCommon.csproj" />
    <ProjectReference Include="..\CommonUtil\CommonUtil.csproj" />
    <ProjectReference Include="..\DiskArcTests\DiskArcTests.csproj" />
    <ProjectReference Include="..\DiskArc\DiskArc.csproj" />
    <ProjectReference Include="..\FileConvTests\FileConvTests.csproj" />
    <ProjectReference Include="..\FileConv\FileConv.csproj" />
  </ItemGroup>

</Project>
```

Key differences from `cp2_wpf.csproj`:
- `net8.0` (not `net8.0-windows`) — no `-windows` suffix
- No `<UseWPF>true</UseWPF>`
- Avalonia NuGet packages added (including `Avalonia.Fonts.Inter` — required by
  `.WithInterFont()` in `Program.cs`)
- `<Resource>` → `<AvaloniaResource>` for embedded assets
- Same `<AssemblyName>CiderPress2</AssemblyName>`
- Same 6 project references

**Note on version pins:** Use `11.2.*` to get the latest patch of the 11.2.x line. If this
causes restore issues, replace with the specific latest version (e.g., `11.2.3`). Check
nuget.org for the current latest `Avalonia` 11.2.x version.

---

### Step 2: Create `cp2_avalonia/Program.cs`

This is the Avalonia entry point (replaces WPF's auto-generated `Main`):

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
using System;

using Avalonia;

namespace cp2_avalonia {
    internal class Program {
        // Avalonia configuration; don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        // Note: If AvaloniaEdit provides a .UseAvaloniaEdit() extension method,
        // add it to the chain above (after .WithInterFont()). If it does not exist
        // in the installed version, AvaloniaEdit styles must be registered via
        // <StyleInclude> in App.axaml instead — see Step 3.

        [STAThread]
        public static void Main(string[] args) {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
    }
}
```

---

### Step 3: Create `cp2_avalonia/App.axaml`

This replaces `cp2_wpf/App.xaml`. It must include:
- Fluent theme
- Merged resource dictionary for `Res/Icons.axaml` (stub created in Step 8a)
- `GeneralMonoFont` and `ViewerMonoFont` resources (with cross-platform fallback)
- `CheckerBackground` DrawingBrush (checkerboard pattern for image preview backgrounds)
- `InverseBooleanConverter` (to be created in Step 7)

Read `cp2_wpf/App.xaml` for the exact resources to replicate. The Avalonia equivalent:

```xml
<!--
Copyright 2023 faddenSoft
Copyright 2026 Lydian Scale Software

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
-->

<Application x:Class="cp2_avalonia.App"
             xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:common="clr-namespace:cp2_avalonia.Common"
             RequestedThemeVariant="Default">

    <Application.Styles>
        <FluentTheme />
    </Application.Styles>

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- Stub created in Step 8a; actual icons ported in Iteration 2 -->
                <ResourceInclude Source="avares://CiderPress2/Res/Icons.axaml"/>
            </ResourceDictionary.MergedDictionaries>

            <FontFamily x:Key="GeneralMonoFont">Cascadia Mono, Consolas, Menlo, monospace</FontFamily>
            <FontFamily x:Key="ViewerMonoFont">Cascadia Mono, Consolas, Menlo, monospace</FontFamily>

            <common:InverseBooleanConverter x:Key="InvertBool"/>

            <!-- Checkerboard background for bitmap image display.
                 Replicates the pattern from cp2_wpf/App.xaml: 16x16 tile with #e8e8e8/#f0f0f0.
                 The geometry uses a 0-2 coordinate space. The first rectangle fills the
                 entire 2x2 region. The second uses an even-odd fill path that covers
                 the (0,0)-(1,1) and (1,1)-(2,2) sub-squares, creating the checker pattern. -->
            <DrawingBrush x:Key="CheckerBackground" TileMode="Tile"
                          DestinationRect="0,0,16,16">
                <DrawingBrush.Drawing>
                    <DrawingGroup>
                        <GeometryDrawing Geometry="M0,0 H2 V2 H0Z" Brush="#e8e8e8"/>
                        <GeometryDrawing Geometry="M0,0 H1 V1 H2 V2 H1 V1 H0Z" Brush="#f0f0f0"/>
                    </DrawingGroup>
                </DrawingBrush.Drawing>
            </DrawingBrush>
        </ResourceDictionary>
    </Application.Resources>

</Application>
```

**Important Avalonia differences from WPF's App.xaml:**
- No `StartupUri` — Avalonia uses `OnFrameworkInitializationCompleted()` in code-behind.
- `Viewport`/`ViewportUnits` → `DestinationRect` for tile brushes. The WPF geometry
  coordinates (0-2 range) are mapped into a 16×16 pixel tile via `DestinationRect`. Use
  `"0,0,16,16"` — Avalonia's `RelativeRect` type converter interprets values greater
  than 1.0 as absolute pixel coordinates automatically. Do **not** use a `px` suffix
  (that syntax is for CSS/Thickness, not `RelativeRect`). If the checker tiles appear
  wrong at runtime, verify by adding `SourceRect="0,0,2,2"` explicitly.
- `<FluentTheme />` in `Application.Styles` (not in resources).
- **AvaloniaEdit styles:** AvaloniaEdit requires explicit style registration. Add
  `.UseAvaloniaEdit()` to the `AppBuilder` chain in `Program.cs` (see Step 2). This
  ensures the `TextEditor` control introduced in Iteration 7 will render correctly.
  If `.UseAvaloniaEdit()` is not available as an extension method, add this to
  `<Application.Styles>` instead:
  `<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"/>`
  Without one of these, AvaloniaEdit controls will render unstyled (no scrollbars,
  wrong fonts) — the failure is silent and hard to diagnose.
- The global `TreeViewItem` style from WPF (`HorizontalContentAlignment`,
  `VerticalContentAlignment` setters) is deferred to Iteration 3 where the TreeView is
  actually implemented. Avalonia's Fluent theme already handles basic TreeViewItem styling.

---

### Step 4: Create `cp2_avalonia/App.axaml.cs`

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
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace cp2_avalonia {
    public partial class App : Application {
        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);ow
        }

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

---

### Step 5: Create `cp2_avalonia/MainWindow.axaml`

A minimal main window that just displays the title. This will be expanded dramatically
in Iterations 1-3.

```xml
<!--
Copyright 2023 faddenSoft
Copyright 2026 Lydian Scale Software

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
-->

<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:cp2_avalonia"
        mc:Ignorable="d" d:DesignWidth="1200" d:DesignHeight="800"
        x:Class="cp2_avalonia.MainWindow"
        Title="CiderPress II"
        Width="1200" Height="800"
        MinWidth="640" MinHeight="680"
        Icon="avares://CiderPress2/Res/cp2_app.ico">

    <!-- Launch panel: displayed when no file is open -->
    <TextBlock Text="CiderPress II"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               FontSize="36" FontWeight="Bold"/>

</Window>
```

**Notes on differences from WPF's `MainWindow.xaml`:**
- `Icon` uses `avares://` URI scheme (Avalonia's embedded resource scheme).
  The assembly name is `CiderPress2`, so: `avares://CiderPress2/Res/cp2_app.ico`.
- Window size matches WPF: `Width="1200" Height="800"`, `MinWidth="640" MinHeight="680"`.
- No commands, menu, toolbar, or panels yet — those come in Iterations 1-3.

---

### Step 6: Create `cp2_avalonia/MainWindow.axaml.cs`

Minimal code-behind:

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
using Avalonia.Controls;

namespace cp2_avalonia {
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();
        }
    }
}
```

---

### Step 7: Create `cp2_avalonia/Common/InverseBooleanConverter.cs`

This is referenced in `App.axaml`. Replicate from `cp2_wpf/WPFCommon/InverseBooleanConverter.cs`
but use Avalonia types.

Read `cp2_wpf/WPFCommon/InverseBooleanConverter.cs` first. The WPF version throws
`InvalidOperationException` if `targetType != typeof(bool)` and throws
`NotSupportedException` in `ConvertBack`. For Avalonia we relax both: the `Convert` method
gracefully returns the original value for non-bool targets (Avalonia sometimes passes
`typeof(object)` as the target type), and `ConvertBack` is implemented symmetrically so
two-way bindings work if needed. The `[ValueConversion]` attribute is WPF-specific and
omitted.

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
using System;
using System.Globalization;

using Avalonia.Data.Converters;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Converter that returns the boolean inverse of the input value.
    /// </summary>
    /// <remarks>
    /// Adapted from cp2_wpf/WPFCommon/InverseBooleanConverter.cs. Relaxed vs. the WPF
    /// original: does not throw on non-bool targets (Avalonia may pass typeof(object)),
    /// and ConvertBack is implemented symmetrically.
    /// </remarks>
    public class InverseBooleanConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter,
                CultureInfo culture) {
            if (value is bool boolVal) {
                return !boolVal;
            }
            return value!;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter,
                CultureInfo culture) {
            if (value is bool boolVal) {
                return !boolVal;
            }
            return value!;
        }
    }
}
```

---

### Step 8: Create `Res/` Directory and Populate Resources

#### Step 8a: Create stub `cp2_avalonia/Res/Icons.axaml`

This is referenced by the merged resource dictionary in `App.axaml`. For now it is an empty
resource dictionary; the actual icon definitions will be ported in Iteration 2.

```xml
<!--
Copyright 2023 faddenSoft
Copyright 2026 Lydian Scale Software

Licensed under the Apache License, Version 2.0.
See PORTING_OVERVIEW.md §11b for the replication strategy.
-->

<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Icon DrawingImage resources will be ported from cp2_wpf/Res/Icons.xaml
         in Iteration 2. -->
</ResourceDictionary>
```

#### Step 8b: Copy static binary resources

Copy these files (binary copy, not modify) from `cp2_wpf/Res/` to `cp2_avalonia/Res/`:

```bash
mkdir -p cp2_avalonia/Res
mkdir -p cp2_avalonia/Common
mkdir -p cp2_avalonia/Actions
mkdir -p cp2_avalonia/Tools
mkdir -p cp2_avalonia/LibTest
cp cp2_wpf/Res/cp2_app.ico cp2_avalonia/Res/cp2_app.ico
cp cp2_wpf/Res/RedX.png cp2_avalonia/Res/RedX.png
```

---

### Step 9: Update `CiderPress2.sln`

This is the most delicate step. `.sln` files do **not** officially support `#` comments, so
instead of commenting out lines we take a safer approach: back up the original file, then
remove the `cp2_wpf` entries and add the `cp2_avalonia` entries.

#### 9a: Create a backup of the original `.sln`

```bash
cp CiderPress2.sln CiderPress2.sln.original
```

This preserves an exact copy with the `cp2_wpf` project entry intact. To restore the WPF
build at any time, simply copy the backup back:
`cp CiderPress2.sln.original CiderPress2.sln`

#### 9b: Remove the `cp2_wpf` project entry

In `CiderPress2.sln`, find and **delete** this block (around line 27):
```
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "cp2_wpf", "cp2_wpf\cp2_wpf.csproj", "{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}"
EndProject
```

#### 9c: Remove the `cp2_wpf` build configuration lines

In the `GlobalSection(ProjectConfigurationPlatforms)` section, find and **delete** these
4 lines:
```
		{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}.Release|Any CPU.Build.0 = Release|Any CPU
```

#### 9d: Add the `cp2_avalonia` project entry

Generate a fresh GUID for the project. The `.sln` format uses **uppercase** GUIDs with
braces. On Linux, `uuidgen` outputs lowercase, so pipe through `tr`:

```bash
uuidgen | tr '[:lower:]' '[:upper:]'
```

Add this block where the `cp2_wpf` entry used to be:

```
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "cp2_avalonia", "cp2_avalonia\cp2_avalonia.csproj", "{NEW-GUID-HERE}"
EndProject
```

And add the build configuration lines in `GlobalSection(ProjectConfigurationPlatforms)`:

```
		{NEW-GUID-HERE}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{NEW-GUID-HERE}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{NEW-GUID-HERE}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{NEW-GUID-HERE}.Release|Any CPU.Build.0 = Release|Any CPU
```

Replace `{NEW-GUID-HERE}` with the same generated GUID (uppercase, with braces).

---

### Step 10: Verify Build & Run

```bash
cd /home/mlong/develop/CiderPress2

# Restore NuGet packages (first time will download Avalonia):
dotnet restore

# Build the entire solution:
dotnet build

# Run the Avalonia app:
dotnet run --project cp2_avalonia
```

**Expected result:** A window appears titled "CiderPress II" showing large centered text
"CiderPress II". The window should have the application icon and be resizable with the
specified min dimensions.

If the build fails:
- Check that `cp2_wpf` entries were fully removed from `.sln` (compare against
  `CiderPress2.sln.original` to see what was there). Make sure no stale GUID references
  remain.
- Check that NuGet packages restored successfully.
- Check for namespace or type resolution errors in the Avalonia files.

> **Note on MakeDist:** The `MakeDist/Build.cs` file has a `sWinTargets` array containing
> `"cp2_wpf"`. This needs to change to `"cp2_avalonia"` and move to `sTargets` (since the
> Avalonia app builds for all platforms). This is deferred to **Iteration 15** (Polish &
> Packaging). See PORTING_OVERVIEW.md §11a for details.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/cp2_avalonia.csproj` |
| **Create** | `cp2_avalonia/Program.cs` |
| **Create** | `cp2_avalonia/App.axaml` |
| **Create** | `cp2_avalonia/App.axaml.cs` |
| **Create** | `cp2_avalonia/MainWindow.axaml` |
| **Create** | `cp2_avalonia/MainWindow.axaml.cs` |
| **Create** | `cp2_avalonia/Common/InverseBooleanConverter.cs` |
| **Create** | `cp2_avalonia/Res/Icons.axaml` (empty stub) |
| **Create** | `cp2_avalonia/Actions/` (empty directory for later iterations) |
| **Create** | `cp2_avalonia/Tools/` (empty directory for later iterations) |
| **Create** | `cp2_avalonia/LibTest/` (empty directory for later iterations) |
| **Copy** | `cp2_avalonia/Res/cp2_app.ico` (from `cp2_wpf/Res/`) |
| **Copy** | `cp2_avalonia/Res/RedX.png` (from `cp2_wpf/Res/`) |
| **Copy** | `CiderPress2.sln` → `CiderPress2.sln.original` (backup) |
| **Modify** | `CiderPress2.sln` (remove cp2_wpf, add cp2_avalonia) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds with no errors
- [ ] `dotnet run --project cp2_avalonia` shows a window titled "CiderPress II"
- [ ] Window has the correct icon (cider press icon)
- [ ] Window is resizable with min constraints (640×680)
- [ ] `cp2_wpf` does NOT build (removed from .sln)
- [ ] `CiderPress2.sln.original` exists and contains the original `cp2_wpf` entry
- [ ] All other projects (cp2, AppCommon, etc.) still build
- [ ] No `System.Windows.*` namespaces in any `cp2_avalonia/` file
- [ ] The `cp2_wpf/` directory is completely unmodified
- [ ] Subdirectories `Actions/`, `Tools/`, `LibTest/`, `Common/`, `Res/` all exist under `cp2_avalonia/`
- [ ] `Icons.axaml` merges without error (no parse exception at startup)
- [ ] AvaloniaEdit styles are registered (verify no build warnings about missing styles)
