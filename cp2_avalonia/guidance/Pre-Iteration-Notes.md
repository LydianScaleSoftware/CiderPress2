# CiderPress II — Avalonia Port: Pre-Iteration Notes

> **Read this file first** before starting any iteration blueprint. It contains the common
> context, code conventions, architecture decisions, and reference information shared by all
> iterations.

---

## 1. Project Overview

CiderPress II (by Andy McFadden / faddenSoft) is an Apple II disk image and file archive
utility. The workspace is at `/home/mlong/develop/CiderPress2/`. The solution file is
`CiderPress2.sln`. All work happens on the **`avalonia`** git branch.

We are porting the Windows-only WPF GUI (`cp2_wpf/`) to a cross-platform Avalonia UI project
(`cp2_avalonia/`) targeting Windows, Linux, and macOS. The WPF project is kept **fully
intact** — its entries are removed from the `.sln` (original saved as
`CiderPress2.sln.original`) so the Avalonia project builds instead.
Nothing in `cp2_avalonia/` should ever reference files inside `cp2_wpf/`.

The full planning document is at `cp2_avalonia/PORTING_OVERVIEW.md`. Refer to it for
detailed analysed of every source file, risk assessments, and the iteration plan.

---

## 2. Solution & Project Structure

### 2.1 Existing Projects (all `net8.0`, cross-platform, **no changes needed**)

| Project | Purpose |
|---|---|
| `AppCommon` | File add/extract/copy/delete workers, WorkTree, file identification |
| `CommonUtil` | CRC, formatters, path handling, `SettingsHolder`, streams |
| `DiskArc` | Disk image and file archive format support |
| `FileConv` | File format converters (graphics, text, etc.) |
| `DiskArcTests` | Tests for DiskArc |
| `FileConvTests` | Tests for FileConv |
| `cp2` | Cross-platform CLI tool (already works everywhere) |
| `MakeDist` | Build/packaging tool |

### 2.2 WPF Project (`cp2_wpf/`)

- Target: `net8.0-windows`, `UseWPF=true`
- **Assembly name:** `CiderPress2` (not `cp2_wpf`)
- GUID in `.sln`: `{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}`
- Size: 28 XAML files, 60 C# files, ~26,400 lines
- Architecture: code-behind + controller (not MVVM)
- Key files: `MainWindow.xaml/.cs`, `MainController.cs`, `MainController_Panels.cs`

### 2.3 New Avalonia Project (`cp2_avalonia/`)

- Target: `net8.0` (no `-windows` suffix — cross-platform)
- **Assembly name:** `CiderPress2` (must match WPF for consistency)
- NuGet packages:
  - `Avalonia` (11.2+, latest stable)
  - `Avalonia.Desktop`
  - `Avalonia.Themes.Fluent`
  - `Avalonia.Fonts.Inter`
  - `Avalonia.Controls.DataGrid`
  - `Avalonia.AvaloniaEdit`
  - `Avalonia.Diagnostics` (Debug configuration only)
- Project references: `AppCommon`, `CommonUtil`, `DiskArc`, `FileConv`, `DiskArcTests`,
  `FileConvTests`
  > **Why test projects?** The WPF project also references `DiskArcTests` and
  > `FileConvTests`. The GUI includes a built-in library test runner
  > (`LibTest/TestRunner.cs`) that discovers and runs tests from these assemblies at
  > runtime. The project references ensure the test DLLs are copied to the output
  > directory so that the test runner can locate them.
- Directory layout:
  ```
  cp2_avalonia/
    Res/            ← icons, images, AXAML resource dictionaries
    Common/         ← cross-platform replacements for WPFCommon/ helpers
    Actions/        ← background worker wrappers (ported from cp2_wpf/Actions/)
    Tools/          ← diagnostic/test windows (ported from cp2_wpf/Tools/)
    LibTest/        ← library test runner (ported from cp2_wpf/LibTest/)
  ```
  Create all subdirectories during **Iteration 0** (project scaffolding) so that later
  iterations can place files without needing to create directories on the fly.

---

## 3. Architecture Decisions (Locked In)

| Decision | Choice | Notes |
|---|---|---|
| Architecture | Code-behind + controller | Same as WPF. No MVVM refactor. |
| Rich text viewer | AvaloniaEdit (`Avalonia.AvaloniaEdit`) | MIT licensed. Replaces `RichTextBox`/`FlowDocument`. |
| Toolbar | Styled `StackPanel` with icon `Button` controls | No built-in Avalonia toolbar. |
| Target framework | `net8.0` | Matches all other projects. |
| Avalonia version | 11.2+ (latest stable) | |
| Theme | `Avalonia.Themes.Fluent` | |
| Assembly name | `CiderPress2` | Matches WPF output. |
| cp2_wpf disposition | Removed from `.sln`, project kept intact | Original `.sln` saved as `CiderPress2.sln.original`. |
| Resource strategy | Replicate everything into `cp2_avalonia/` | Self-contained; no file refs to `cp2_wpf/`. |

---

## 4. Code Conventions & Style

Follow the existing faddenSoft coding style used throughout CiderPress II:

- **License header:** Every new `.cs` file begins with the Apache 2.0 copyright header.
  Preserve the original faddenSoft copyright and add the Lydian Scale Software line:
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
  ```
  Use the same dual-copyright pattern for `.axaml` XML comment headers.

- **Namespace:** `cp2_avalonia` (mirroring `cp2_wpf`). Sub-namespaces follow directory
  names: `cp2_avalonia.Actions`, `cp2_avalonia.Tools`, `cp2_avalonia.LibTest`,
  `cp2_avalonia.Common`.

- **Naming conventions:**
  - Private fields: `mCamelCase` (e.g., `mMainWin`, `mDebugLog`)
  - Constants: `UPPER_SNAKE_CASE` (e.g., `SETTINGS_FILE_NAME`)
  - Properties: `PascalCase` (e.g., `AppHook`, `Global`)
  - Methods: `PascalCase`
  - Local variables: `camelCase`
  - Boolean properties for UI binding: `IsXxxEnabled`, `ShowXxxYyy`

- **Braces & formatting:**
  - Opening brace on same line as declaration (K&R style for methods/classes)
  - 4-space indentation
  - XML doc comments (`/// <summary>`) on public/internal members

- **`INotifyPropertyChanged` pattern:** The WPF code uses a standard pattern with
  `[CallerMemberName]`:
  ```csharp
  public event PropertyChangedEventHandler? PropertyChanged;
  private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
  ```
  Use the same pattern. Do **not** introduce `ReactiveUI` or `CommunityToolkit.Mvvm`
  source generators.

- **Copyright year:** New files use dual copyright: `Copyright 2023 faddenSoft` on the
  first line (preserving the original) and `Copyright 2026 Lydian Scale Software` on the
  second line.

---

## 5. Common Avalonia Patterns

### 5.1 AXAML Namespace Declaration

Every `.axaml` file starts with:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:cp2_avalonia"
        mc:Ignorable="d"
        ...>
```

For `UserControl`, replace `<Window` with `<UserControl`.

### 5.2 Key WPF → Avalonia Mappings

| WPF | Avalonia | Notes |
|---|---|---|
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` | Root namespace |
| `Visibility="Collapsed"` / `Visible` | `IsVisible="False"` / `True` | Boolean, not enum |
| `BooleanToVisibilityConverter` | `IsVisible` binding directly | Avalonia binds booleans to `IsVisible` |
| `RoutedUICommand` + `CommandBinding` | `ICommand` property + AXAML `Command="{Binding ...}"` | See §5.4 |
| `ToolBar` / `ToolBarTray` | Styled `StackPanel` with `Orientation="Horizontal"` | No built-in toolbar |
| `StatusBar` | `DockPanel` or `Grid` docked to bottom | No built-in status bar |
| `ListView` + `GridView` | `DataGrid` | Different column/sorting API |
| `RichTextBox` / `FlowDocument` | `AvaloniaEdit.TextEditor` (read-only) | Different text model |
| `Style.Triggers` / `DataTrigger` | Avalonia `Style` with CSS-like selectors + `Classes` | Pervasive change |
| `DependencyProperty` | `StyledProperty<T>` or `DirectProperty<T>` | Different property system |
| `Dispatcher.Invoke(...)` | `Dispatcher.UIThread.Post(...)` / `InvokeAsync(...)` | |
| `Microsoft.Win32.OpenFileDialog` | `TopLevel.StorageProvider.OpenFilePickerAsync(...)` | |
| `Microsoft.Win32.SaveFileDialog` | `TopLevel.StorageProvider.SaveFilePickerAsync(...)` | |
| `System.Windows.Media.Imaging.BitmapSource` | `Avalonia.Media.Imaging.Bitmap` | |
| `System.Windows.Media.Color` | `Avalonia.Media.Color` | Same name, different namespace |
| `System.Windows.Media.Colors` | `Avalonia.Media.Colors` | Same name, different namespace |
| `Freezable` | Not needed | Use `AvaloniaObject` if a base is required |
| File filter: `"Desc\|*.ext"` | `FilePickerFileType` object | See §5.3 |

### 5.3 File Dialog Filter Conversion

WPF uses pipe-delimited strings: `"All Files|*.*|Disk Images|*.po;*.do;*.2mg"`

Avalonia uses `FilePickerFileType` objects:
```csharp
var filter = new FilePickerFileType("Disk Images") {
    Patterns = new[] { "*.po", "*.do", "*.2mg" }
};
```

The WPF project defines ~20 `FILE_FILTER_*` constants in `WinUtil.cs`. Each must be converted
to `FilePickerFileType` arrays when porting file dialog calls.

### 5.4 Command System (Replacing RoutedUICommand)

The WPF app defines ~100 `RoutedUICommand` entries in `MainWindow.xaml` with `CommandBinding`
elements that wire `CanExecute` and `Executed` handlers.

Avalonia does not have `RoutedUICommand`. Replace with:

1. Define `ICommand` properties on `MainWindow` or `MainController`:
   ```csharp
   public ICommand OpenCommand { get; }
   public ICommand ExitCommand { get; }
   // etc.
   ```

2. Initialize in constructor using a simple `RelayCommand` implementation:
   ```csharp
   OpenCommand = new RelayCommand(OpenCmd_Executed, OpenCmd_CanExecute);
   ```

3. Bind in AXAML:
   ```xml
   <MenuItem Header="_Open" Command="{Binding OpenCommand}"
             HotKey="Ctrl+O"/>
   ```

4. For keyboard shortcuts, use `HotKey` on `MenuItem` or `KeyBinding` on the window:
   ```xml
   <Window.KeyBindings>
       <KeyBinding Gesture="Ctrl+W" Command="{Binding CloseCommand}"/>
   </Window.KeyBindings>
   ```

5. Implement a minimal `RelayCommand` class (since we're not using CommunityToolkit).
   This is the **authoritative version** — implement exactly this in
   `cp2_avalonia/Common/RelayCommand.cs` during Iteration 1 (Step 1). The primary
   constructor takes `Action<object?>` to support `CommandParameter`; the convenience
   overload takes a plain `Action` for commands that ignore the parameter:
   ```csharp
   public class RelayCommand : ICommand {
       private readonly Action<object?> _execute;
       private readonly Func<object?, bool>? _canExecute;
       public event EventHandler? CanExecuteChanged;

       public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) {
           _execute = execute;
           _canExecute = canExecute;
       }

       /// <summary>Convenience constructor for commands that ignore the parameter.</summary>
       public RelayCommand(Action execute, Func<bool>? canExecute = null)
           : this(_ => execute(), canExecute != null ? _ => canExecute() : null) {
       }

       public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
       public void Execute(object? parameter) => _execute(parameter);
       public void RaiseCanExecuteChanged() =>
           CanExecuteChanged?.Invoke(this, EventArgs.Empty);
   }
   ```

**CanExecute refresh pattern:** `RaiseCanExecuteChanged()` must be called whenever
state changes that affect whether commands are enabled. The primary trigger points are:

- **Selection changes:** When the user selects a different file or directory entry, many
  commands change their enabled state. Call `RaiseCanExecuteChanged()` on affected commands
  from the `SelectionChanged` handler in `MainController`.
- **Archive state changes:** After opening/closing a file, switching nodes in the archive
  tree, or completing an operation that modifies the archive, refresh all commands.
- **Batch refresh helper:** Since ~100 commands exist, maintain a method called
  `RefreshAllCommandStates()` **on `MainController`** that iterates a list of all
  `RelayCommand` instances and calls `RaiseCanExecuteChanged()` on each.
  `MainController` owns this because it already tracks application state and triggers
  UI updates. Call this from `SelectionChanged`, `PostNotification`, and after any
  archive-modifying operation.

### 5.5 "Not Implemented" Stub Pattern

Until a feature is ported, its command handler should show a simple dialog.
Do **not** use `MessageBoxManager` or `MsBox.Avalonia` — these are external NuGet packages
not in the project's dependency list.

```csharp
// When called from a Window subclass (e.g., MainWindow):
private async void NotImplemented(string featureName) {
    var dialog = new Window {
        Title = "Not Implemented",
        Width = 350, Height = 150,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new TextBlock {
            Text = $"'{featureName}' has not been ported yet.",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(20)
        }
    };
    await dialog.ShowDialog(this);  // 'this' must be a Window
}

// When called from a controller (not a Window), pass the owner window explicitly:
private async void NotImplemented(string featureName, Window owner) {
    var dialog = new Window {
        Title = "Not Implemented",
        Width = 350, Height = 150,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new TextBlock {
            Text = $"'{featureName}' has not been ported yet.",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(20)
        }
    };
    await dialog.ShowDialog(owner);
}
```

### 5.6 Mono Font Fallback

WPF hardcodes `Consolas`. For cross-platform use:

```xml
<FontFamily x:Key="GeneralMonoFont">Cascadia Mono, Consolas, Menlo, monospace</FontFamily>
<FontFamily x:Key="ViewerMonoFont">Cascadia Mono, Consolas, Menlo, monospace</FontFamily>
```

### 5.7 Bitmap Conversion (IBitmap → Avalonia IImage)

The WPF `WinUtil.ConvertToBitmapSource(IBitmap)` is the sole bridge from `FileConv.IBitmap`
to the display layer. In Avalonia, replace with a function that produces an
`Avalonia.Media.Imaging.WriteableBitmap`.

**`FileConv.IBitmap` interface** (defined in `FileConv/IBitmap.cs`):

| Member | Type | Description |
|---|---|---|
| `Width` | `int` | Bitmap width in pixels |
| `Height` | `int` | Bitmap height in pixels |
| `IsIndexed8` | `bool` | `true` = 8-bit indexed color; `false` = 32-bit direct color |
| `IsDoubled` | `bool` | `true` if pixel-doubled from native resolution (informational) |
| `GetPixels()` | `byte[]` | Densely-packed pixel data. For indexed: 1 byte/pixel (palette index). For direct: 4 bytes/pixel in **BGRA** byte order (B at offset 0, G at 1, R at 2, A at 3). Direct reference — do not modify. |
| `GetColors()` | `int[]?` | Palette as 32-bit ARGB values (0–255 entries). `null` for direct-color images. Returns a copy. |
| `ScaleUp(int mult)` | `IBitmap` | Returns a new upscaled bitmap (mult ≥ 1) |

```csharp
public static Avalonia.Media.Imaging.WriteableBitmap ConvertToAvaloniaBitmap(
        FileConv.IBitmap source) {
    // 1. Allocate: new WriteableBitmap(new PixelSize(source.Width, source.Height),
    //              new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul)
    //    NOTE: Source pixel data is non-premultiplied. Using Premul would cause
    //    visual darkening on semi-transparent pixels.
    // 2. Lock the framebuffer: using (var fb = bitmap.Lock()) { ... }
    // 3. If source.IsIndexed8:
    //      - Get palette via source.GetColors()
    //      - For each pixel, map palette index → BGRA and write to fb.Address
    //    Else (direct color):
    //      - source.GetPixels() is already BGRA — copy directly to fb.Address
    //      - Stride = source.Width * 4; copy row-by-row if fb.RowBytes != stride
    // 4. Return the bitmap
}
```

This is needed for all Apple II graphic display and must be implemented early (Iteration 7
at latest).

---

## 6. Version & Assembly Info

- `GlobalAppVersion.cs` in `AppCommon/` provides the single version number for the entire
  application (currently `1.2.0`). The Avalonia project uses this same version — do not
  create a separate version constant.
- The Avalonia `.csproj` should set `<AssemblyName>CiderPress2</AssemblyName>` to match
  the WPF project's output executable name.
- Application icon: `Res/cp2_app.ico` (copied from `cp2_wpf/Res/`).

---

## 7. Settings File

The application settings are persisted to `CiderPress2-settings.json` in the same directory
as the executable. The `AppSettings.cs` file defines string constants for all setting keys
(e.g., `MAIN_WINDOW_PLACEMENT`, `RECENT_FILES_LIST`). The `SettingsHolder` class in
`CommonUtil` handles serialization.

The Avalonia project should use the same settings file name and key constants, so settings
are portable between WPF and Avalonia builds.

---

## 8. Build & Run

```bash
# From the workspace root:
cd /home/mlong/develop/CiderPress2

# Build all projects:
dotnet build

# Run the Avalonia GUI:
dotnet run --project cp2_avalonia

# Run the CLI (for comparison/testing):
dotnet run --project cp2

# Run only in Release mode:
dotnet run --project cp2_avalonia -c Release
```

The `MakeDist` project must be updated to build `cp2_avalonia` instead of `cp2_wpf`.
Specifically, `MakeDist/Build.cs` has a `sWinTargets` array containing `"cp2_wpf"` — this
needs to change to `"cp2_avalonia"` and move to `sTargets` (cross-platform targets).

---

## 9. Testing Strategy

- After each iteration, the app should **build and run** without errors.
- Unfinished features show "Not Implemented" message boxes — the app should never crash
  due to an unported feature.
- The `DiskArcTests` and `FileConvTests` projects provide comprehensive unit tests for the
  shared libraries; these should continue to pass unmodified.
- Integration testing: Open a variety of disk images and archives (ProDOS `.po`, DOS 3.3
  `.do`, `.2mg`, `.shk`, `.zip`, `.sdk`) and verify the UI displays correctly.

---

## 10. Key WPF Source Files to Reference

When porting a specific feature, always read the corresponding WPF source file(s) as your
primary reference. The WPF code is the specification — the Avalonia code should match its
behavior as closely as possible.

| Feature/Area | WPF Source File(s) |
|---|---|
| Main window layout | `cp2_wpf/MainWindow.xaml` |
| Main window code-behind | `cp2_wpf/MainWindow.xaml.cs` |
| Controller (core logic) | `cp2_wpf/MainController.cs` |
| Panel management | `cp2_wpf/MainController_Panels.cs` |
| Application settings keys | `cp2_wpf/AppSettings.cs` |
| Application resources | `cp2_wpf/App.xaml` |
| Vector icons | `cp2_wpf/Res/Icons.xaml` |
| TreeView item style | `cp2_wpf/Res/TreeViewItemStyle.xaml` |
| File dialogs & utilities | `cp2_wpf/WinUtil.cs` |
| Window placement | `cp2_wpf/WPFCommon/WindowPlacement.cs` |
| Progress dialog | `cp2_wpf/WPFCommon/WorkProgress.xaml/.cs` |
| File viewer | `cp2_wpf/FileViewer.xaml/.cs` |
| Sector editor | `cp2_wpf/EditSector.xaml/.cs` |
| Test manager | `cp2_wpf/LibTest/TestManager.xaml/.cs` |
| Archive tree | `cp2_wpf/ArchiveTreeItem.cs` |
| Directory tree | `cp2_wpf/DirectoryTreeItem.cs` |
| File list | `cp2_wpf/FileListItem.cs` |
| Drag-drop (COM) | `cp2_wpf/WPFCommon/VirtualFileDataObject.cs` |
| Clipboard helpers | `cp2_wpf/WPFCommon/ClipHelper.cs`, `cp2_wpf/ClipInfo.cs` |

---

## 11. Common Pitfalls

1. **Don't use `net8.0-windows`** — the Avalonia project must target plain `net8.0` for
   cross-platform builds.
2. **Don't use `UseWPF` or `UseWindowsForms`** in the Avalonia `.csproj`.
3. **Don't reference `System.Windows.*`** namespaces — these are WPF. Use `Avalonia.*`.
4. **Don't delete `cp2_wpf/`** or its files. Ever. It's our reference and fallback.
5. **Don't add `ReactiveUI` or `CommunityToolkit.Mvvm` packages** — we use a simple
   `RelayCommand` implementation and manual `INotifyPropertyChanged`.
6. **Font names** — `Consolas` doesn't exist on Linux/macOS. Always use the fallback:
   `"Cascadia Mono, Consolas, Menlo, monospace"`.
7. **`Dispatcher.Invoke`** → `Dispatcher.UIThread.InvokeAsync` (Avalonia).
8. **WPF `Visibility` enum** → Avalonia `IsVisible` boolean.
9. **File dialog filters** — WPF pipe-delimited strings must become `FilePickerFileType`.
10. **`RoutedUICommand`** does not exist — use `ICommand` properties + `HotKey`/`KeyBinding`.
