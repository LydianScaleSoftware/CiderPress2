# CiderPress II — WPF to Avalonia Porting Plan

## 1. Project Overview

CiderPress II (by Andy McFadden / faddenSoft) is an Apple II disk image and file archive
utility. The solution contains:

- **cp2** — Cross-platform command-line tool (already works on Linux/macOS/Windows)
- **cp2_wpf** — Windows-only GUI built with WPF (`net8.0-windows`, `UseWPF=true`)
- Shared libraries: **AppCommon**, **CommonUtil**, **DiskArc**, **FileConv** (all target
  `net8.0` — already cross-platform)
- Test projects: **DiskArcTests**, **FileConvTests**

The WPF README itself notes: *"The ultimate goal is to develop a multi-platform GUI application
using some other toolkit, using this version as a prototype."*

**Goal:** Port `cp2_wpf` to an Avalonia UI project (`cp2_avalonia`) targeting Windows, Linux,
and macOS, mirroring the existing UI's functionality and appearance as closely as possible.
Once the Avalonia port reaches feature parity, `cp2_wpf` can be retired. In the meantime
the WPF project is kept **fully intact** as a fallback — its entries are removed from the
`.sln` (with the original saved as `CiderPress2.sln.original`) so it doesn't build by
default. To support an eventual clean separation, **all resources,
utility files, and shared UI helpers are replicated into the `cp2_avalonia` directory** —
nothing in `cp2_avalonia` should depend on files inside `cp2_wpf`.

**Approach:** Iterative / incremental. Start with a minimal running main window and
progressively add features, with unimplemented menu items showing a "Not Implemented"
message box until they are ported.

---

## 2. Codebase Scale (cp2_wpf)

| Category | Count | Lines (approx.) |
|---|---|---|
| XAML files | 28 | ~4,300 |
| C# code files | 60 | ~22,000 |
| **Total** | **88** | **~26,400** |

Largest files by line count:
- `MainController.cs` — 2,934 lines (core application logic / controller)
- `MainWindow.xaml.cs` — 1,975 lines (main window code-behind)
- `FileViewer.xaml.cs` — 1,374 lines (file viewing/conversion dialog)
- `CreateDiskImage.xaml.cs` — 1,156 lines
- `MainController_Panels.cs` — 1,137 lines (panel management)
- `MainWindow.xaml` — 1,083 lines (main window layout + commands)
- `VirtualFileDataObject.cs` — 1,095 lines (Windows COM drag-drop)
- `EditSector.xaml.cs` — 1,102 lines (sector editor)
- `EditAttributes.xaml.cs` — 1,027 lines

---

## 3. Architecture of cp2_wpf

The WPF app uses a **controller + code-behind** architecture (not MVVM):

- **`MainWindow.xaml` / `MainWindow.xaml.cs`** — Main window with menu bar, toolbar, split
  panels (archive tree, directory tree, file list), and info panel.  Defines ~100
  `RoutedUICommand` entries and ~61 `CommandBinding` entries.
- **`MainController.cs` / `MainController_Panels.cs`** — Core application logic, separated
  from the UI. The `MainWindow` delegates to `MainController` for most operations.
- **Dialog windows** — Each feature has its own Window + code-behind:
  - `AboutBox`, `AddMetadata`, `CreateDirectory`, `CreateDiskImage`, `CreateFileArchive`,
    `EditAppSettings`, `EditAttributes`, `EditConvertOpts`, `EditMetadata`, `EditSector`,
    `FileViewer`, `FindFile`, `ReplacePartition`, `SaveAsDisk`, `SelectPhysicalDrive`
- **`Actions/`** — Background worker wrappers (add, extract, delete, move, paste, test, etc.)
  with progress reporting via `BackgroundWorker` + `WorkProgress` dialog.
  Files: `AddProgress`, `ClipPasteProgress`, `DeleteProgress`, `EditAttributesProgress`,
  `ExtractProgress`, `MoveProgress`, `OpenProgress`, `OverwriteQueryDialog` (XAML dialog),
  `ProgressUtil`, `ScanBlocksProgress`, `TestProgress`.
- **`Tools/`** — Utility/debug tool windows:
  - `DropTarget.xaml/.cs` — Drag/paste test window. Shows raw data-object
    contents. Heavily uses `System.Windows.IDataObject`, `Clipboard`, `ClipHelper`,
    and `ClipInfo`. Will need Avalonia DnD/clipboard rewrite.
  - `LogViewer.xaml/.cs` — Debug log viewer. `ItemsControl` with
    `VirtualizingStackPanel`, auto-scroll behavior, "Save to File" using
    `Microsoft.Win32.SaveFileDialog`. Straightforward port; replace file dialog.
  - `ShowText.xaml/.cs` — Simple modeless/modal text display. Mono font.
    Very straightforward port.
- **`LibTest/`** — Built-in library test runner UI:
  - `TestManager.xaml/.cs` — Runs DiskArc/FileConv test suites via reflection.
    Uses `BackgroundWorker`, `FlowDocument`/`RichTextBox` for colored progress output.
    Port: replace `RichTextBox` with AvaloniaEdit or a styled `TextBlock`/`ItemsControl`.
  - `BulkCompress.xaml/.cs` — Compression codec testing dialog. Uses
    `BackgroundWorker`, radio buttons for compression format selection,
    `Microsoft.Win32.OpenFileDialog`. Straightforward port.
  - `BulkCompressTest.cs` — Test execution logic. References
    `System.Windows.Media.Color` only via `ProgressMessage`. Mostly portable.
  - `TestRunner.cs` — Reflection-based test discovery and execution.
    References `System.Windows.Media.Colors` for colored output. Minimal changes needed.
  - `ProgressMessage.cs` — Message data class. Uses `System.Windows.Media.Color`.
    Replace with `Avalonia.Media.Color`.
- **`WPFCommon/`** — Reusable WPF helpers and dialogs (see Section 5)
- **`Res/`** — Resources: `Icons.xaml` (vector icon definitions), `TreeViewItemStyle.xaml`,
  `cp2_app.ico`, `RedX.png`

---

## 4. Shared Libraries (Cross-Platform — No Changes Needed)

All of these target `net8.0` (not `-windows`) and have **no WPF or Windows dependencies**:

| Project | Purpose |
|---|---|
| `AppCommon` | File add/extract/copy/delete workers, WorkTree, file identification |
| `CommonUtil` | Utilities: CRC, formatters, path handling, settings, streams |
| `DiskArc` | Disk image and file archive format support |
| `FileConv` | File format converters (graphics, text, etc.) |
| `DiskArcTests` | Tests for DiskArc |
| `FileConvTests` | Tests for FileConv |

These can be referenced directly from the Avalonia project with no modifications.

---

## 5. Windows-Specific / WPF-Specific Code — Porting Challenges

### 5.1 Heavily Windows-Dependent Files (WPFCommon/)

These files rely on Win32 P/Invoke, COM interop, or deep WPF internals and will need full
rewrites or cross-platform replacements:

| File | Purpose | Porting Strategy |
|---|---|---|
| **`VirtualFileDataObject.cs`** | COM-based virtual file drag-drop (IDataObject, IStream, FILEDESCRIPTORW). ~1,095 lines of native interop. | **Full rewrite.** Avalonia has its own drag-drop API. Virtual file streaming will need platform-specific backends or a temp-file approach. |
| **`ClipHelper.cs`** | Receives clipboard file streams via COM IDataObject. | **Full rewrite.** Use Avalonia's `IClipboard` interface. Virtual file clipboard support is limited cross-platform; may need fallback to temp files. |
| **`WinMagic.cs`** | Win32 shell APIs: `SHGetKnownFolderPath` (known folders like Downloads), `SHGetFileInfo` (file icons). | **Replace.** Use `Environment.GetFolderPath()` for known folders. For file icons, use a bundled icon set or platform-specific queries. |
| **`WindowPlacement.cs`** | Saves/restores window position via Win32 `Get/SetWindowPlacement`. | **Replace.** Avalonia provides `Window.Position`, `Width`, `Height`, `WindowState`. Store and restore manually with simple serialization. |
| **`BrowseForFolder.cs`** | Win32 `SHBrowseForFolder` dialog. | **Replace.** Use Avalonia's `OpenFolderDialog` (via `StorageProvider` API). |

### 5.2 WPF-Specific Helpers That Need Avalonia Equivalents

| File | Purpose | Porting Strategy |
|---|---|---|
| **`WPFExtensions.cs`** | Visual tree helpers (`GetVisualChild<T>`), ListView extensions, `ColumnHeaderAutoLimiter`, `RichTextBoxExtensions`. | Port piece by piece. Avalonia has its own visual tree API. Some extensions may not be needed if Avalonia controls differ. |
| **`BindingProxy.cs`** | `Freezable`-based binding proxy for DataGrid column access. | **Replace.** Avalonia doesn't have `Freezable`. Use `{Binding}` with `RelativeSource` or pass DataContext through other means. |
| **`SelectTextOnFocus.cs`** | Attached behavior for TextBox auto-select on focus. | **Rewrite** as an Avalonia attached property / behavior. |
| **`AnimatedGifEncoder.cs`** | Creates animated GIFs from `BitmapFrame` objects. Uses `System.Windows.Media.Imaging`. | **Replace** WPF imaging types (`BitmapFrame`, `PngBitmapEncoder`) with cross-platform alternatives (SkiaSharp or Avalonia's bitmap APIs). |
| **`FileSelector.xaml`/`.cs`** | Custom file open/save dialog. | **Do not port.** Replace with Avalonia's `StorageProvider` API (`OpenFilePickerAsync`, `OpenFolderPickerAsync`). See Iteration 6 for details. |
| **`WorkProgress.xaml`/`.cs`** | Modal progress dialog using `BackgroundWorker`. | Port XAML to AXAML. `BackgroundWorker` works in Avalonia; or modernize to `Task`-based async. |
| **`CreateFolder.xaml`/`.cs`** | Simple folder name input dialog. | Straightforward XAML→AXAML port. |

### 5.3 WinUtil.cs — File Dialogs, Bitmap Conversion & Utility Methods

`WinUtil.cs` contains several Windows-specific concerns beyond file dialogs:

| Function | Purpose | Porting Strategy |
|---|---|---|
| `AskFileToOpen()` / file dialog wrappers | `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog` + ~20 `FILE_FILTER_*` format strings | Replace with Avalonia `StorageProvider` APIs. All filter strings must be converted from WPF `"Desc\|*.ext"` format to `FilePickerFileType` objects. |
| **`ConvertToBitmapSource(IBitmap)`** | Converts `FileConv.IBitmap` (8-bit indexed or 32-bit BGRA) to WPF `BitmapSource`. **Critical rendering bridge** for all Apple II graphic display. | **Rewrite.** Use `Avalonia.Media.Imaging.WriteableBitmap` or SkiaSharp `SKBitmap`. Must produce an `Avalonia.Media.IImage` from an `IBitmap`. |
| `GetRuntimeDataDir()` | Locates the settings/data directory using Windows-style backslash path detection (`\bin\Debug\`). Used for settings persistence *and* by `LibTest/TestRunner.cs` to find `TestData/`. | **Rewrite** with `Path.DirectorySeparatorChar`-aware logic or `AppContext.BaseDirectory`. |
| `IsAdministrator()` | Checks for Windows admin via `WindowsIdentity`/`WindowsPrincipal`. Used in title bar and physical drive privilege escalation (`runas`). | **Platform-abstract.** Use `geteuid() == 0` check on Linux/macOS; conditional compilation or runtime OS detection. |

### 5.4 ClipInfo.cs — Clipboard Metadata

Uses `System.Windows.DataFormats.GetDataFormat()` to register custom clipboard formats.
This is Windows-specific COM clipboard registration. Will need a cross-platform clipboard
strategy — possibly JSON serialization to text clipboard, or platform-specific clipboard
code behind an abstraction.

### 5.5 SelectPhysicalDrive — Windows-Only Feature

`SelectPhysicalDrive.xaml` provides direct physical disk access using Windows device paths
(`\\.\PhysicalDriveN`). This is inherently platform-specific.

**Strategy:** Keep as a Windows-only feature (conditionally compiled or runtime-detected), or
implement platform-specific backends (`/dev/sdX` on Linux, `/dev/diskN` on macOS) behind a
common interface.

---

## 6. XAML → AXAML Conversion

Avalonia uses AXAML (nearly identical to WPF XAML) but with key differences:

### 6.1 Namespace Changes

| WPF | Avalonia |
|---|---|
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` |
| `xmlns:d="http://schemas.microsoft.com/expression/blend/2008"` | Same or omit |
| `xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"` | Same or omit |

### 6.2 Control Differences

| WPF Control/Feature | Avalonia Equivalent | Notes |
|---|---|---|
| `Window` | `Window` | Similar but some properties differ |
| `Grid`, `StackPanel`, `DockPanel` | Same names | `DockPanel` requires `using Avalonia.Controls` |
| `Menu`, `MenuItem` | Same names | Minor property differences |
| `ToolBar`, `ToolBarTray` | No built-in equivalent | Use a styled `StackPanel` or third-party control |
| `ListView` + `GridView` | `DataGrid` or `ListBox` with templates | WPF `GridView` inside `ListView` has no direct equivalent; `DataGrid` is the closest match |
| `TreeView` | `TreeView` | Similar API; `HierarchicalDataTemplate` works |
| `DataGrid` | `DataGrid` | Avalonia's `DataGrid` is in a separate NuGet: `Avalonia.Controls.DataGrid` |
| `RichTextBox` / `FlowDocument` | No built-in equivalent | Use a third-party control or render formatted text with custom approach (see Section 8) |
| `StatusBar` | No built-in equivalent | Use a styled `DockPanel` or `Grid` at the bottom |
| `RoutedUICommand` / `CommandBinding` | `ICommand` properties on controller | Avalonia doesn't have WPF's routed command system. The ~100 commands are converted to `ICommand` properties on `MainController`/`MainWindow` and bound in AXAML. See §7. |
| `BooleanToVisibilityConverter` | Direct `IsVisible` binding (no converter needed) | Avalonia's `IsVisible` is already a `bool` — bind directly: `IsVisible="{Binding MyFlag}"`. No converter class is required. For negation, use `InverseBooleanConverter` or a computed property. |
| `Visibility.Collapsed` / `Visible` | `IsVisible = true/false` | Simpler model |
| `DependencyProperty` | `StyledProperty` or `DirectProperty` | Avalonia's property system is different |
| `Freezable` | No equivalent | Not needed; Avalonia uses a different resource/binding model |
| `Style.Triggers` / `DataTrigger` | Avalonia `Styles` with selectors | Avalonia uses CSS-like selectors and `Classes` instead of triggers |
| `InputGesture` / `KeyGesture` | `KeyGesture` | Similar but defined differently |
| `ContextMenu` | `ContextMenu` | Similar |
| `Expander` | `Expander` | Available |
| `TabControl` | `TabControl` | Available |
| `ComboBox`, `TextBox`, `CheckBox`, `RadioButton`, `Button` | Same names | Property names may differ slightly |

### 6.3 Resource and Styling Differences

- WPF `ResourceDictionary` → Avalonia `ResourceDictionary` (works similarly but uses
  different URI syntax for `Source`)
- WPF `Style` with `TargetType` and `Triggers` → Avalonia `Style` with selectors
  (e.g., `<Style Selector="TextBlock.selected">`)
- WPF `StaticResource` / `DynamicResource` → Same in Avalonia
- Avalonia uses `ThemeVariant` for light/dark theme support

---

## 7. Command System Overhaul

The WPF project defines ~100 `RoutedUICommand` entries in `MainWindow.xaml` with
`CommandBinding` entries that wire `Executed` and `CanExecute` handlers.

Avalonia does **not** support WPF's routed command infrastructure.

**Decision: Manual ICommand** — Define `ICommand` properties on `MainController`/`MainWindow`
and bind them in AXAML. This matches the existing code-behind + controller architecture.
No MVVM framework (ReactiveUI, CommunityToolkit.Mvvm) is needed.

Alternatives considered and rejected:
- *ReactiveUI Commands* — would require adopting MVVM, which is a refactor, not a port.
- *CommunityToolkit.Mvvm RelayCommand* — lighter weight but still adds an unnecessary dependency.

---

## 8. Rich Text / Formatted Text Viewing

The `FileViewer` uses WPF `RichTextBox` with `FlowDocument` to display formatted text (RTF
output from file converters). This is used for viewing Apple II files with syntax coloring,
hex dumps, etc. The `TestManager` in `LibTest/` also uses `RichTextBox`/`FlowDocument` for
colored test progress output.

Avalonia has **no built-in RichTextBox or FlowDocument**.

### AvaloniaEdit — License Check

AvaloniaEdit is licensed under the **MIT License**, which is fully compatible with CiderPress
II's **Apache License 2.0**. MIT-licensed code can be incorporated into Apache 2.0 projects
without conflict. The only obligation is to preserve the MIT copyright/license notice
(e.g., in a NOTICE file or third-party license section).

### Decision: Use AvaloniaEdit

AvaloniaEdit (NuGet: `Avalonia.AvaloniaEdit`) will replace `RichTextBox`/`FlowDocument`
in both `FileViewer` and `TestManager`. Key features it provides:
- Syntax highlighting (TextMate grammars available, or custom highlighting)
- Read-only mode for viewing
- Line numbers, word wrap, scrolling
- Cross-platform font support (recommends `Cascadia Code,Consolas,Menlo,Monospace`)

Conversion approach: Replace `RTFGenerator`/`FancyText` → RTF pipeline with direct
population of AvaloniaEdit's `TextDocument`, applying colored text runs via the
highlighting API or `TextMate` integration.

---

## 9. Drag-and-Drop / Clipboard

This is the **most complex porting challenge**. The WPF app implements full virtual file
drag-and-drop and clipboard operations using:

- `VirtualFileDataObject` (1,095 lines) — Full COM `IDataObject` implementation with
  `FILEDESCRIPTORW`, `CFSTR_FILECONTENTS`, `IStream` support
- `ClipHelper` — Receives dropped/pasted virtual file streams
- `ClipInfo` — Custom clipboard format registration for app-internal metadata

Avalonia's drag-drop API (`DragDrop.DoDragDrop`, `IDataObject`) is simpler and doesn't
support COM virtual file streams natively.

**Strategy:**
1. **Phase 1:** Implement basic file-path-based drag-drop (drag files from OS into app, drag
   to extract via temp files). This covers the most common use case.
2. **Phase 2:** For app-internal copy/paste, serialize metadata to JSON on the clipboard and
   use temp files for content.
3. **Phase 3 (optional):** Implement platform-specific virtual file streaming if needed for
   performance with large files.

---

## 10. Platform-Specific Concerns

### 10.1 File Dialogs
- WPF: `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog`
- Avalonia: `Window.StorageProvider.OpenFilePickerAsync()` / `SaveFilePickerAsync()`
- Filter format differs (Avalonia uses `FilePickerFileType` objects)

### 10.2 Font Handling
- WPF references `Consolas` as the mono font (Windows-specific)
- Avalonia: Use a fallback list or bundle a cross-platform mono font.
  Suggestion: `"Cascadia Mono, Consolas, Menlo, monospace"`

### 10.3 Application Icon
- WPF: `ApplicationIcon` in `.csproj` pointing to `.ico`
- Avalonia: Set `Icon` property on `Window`, supports `.ico` and `.png`

### 10.4 Physical Drive Access
- Windows: `\\.\PhysicalDriveN` with `CreateFile` API
- Linux: `/dev/sdX` or `/dev/loopN`
- macOS: `/dev/diskN`
- All require elevated permissions. Implement behind a platform abstraction.

### 10.5 Imaging
- WPF uses `System.Windows.Media.Imaging` (`BitmapSource`, `BitmapImage`,
  `WriteableBitmap`, `PngBitmapEncoder`, `BitmapFrame`)
- Avalonia has its own `Avalonia.Media.Imaging.Bitmap` class
- For advanced operations, use **SkiaSharp** (which Avalonia already depends on internally)

---

## 11. Third-Party / External Dependencies

The WPF project has **no NuGet package dependencies** — it only references the in-solution
shared projects. All third-party code is vendored directly:

| Vendored Code | File | License |
|---|---|---|
| `VirtualFileDataObject` | `WPFCommon/VirtualFileDataObject.cs` | MIT (David Anson) |
| `BrowseForFolder` | `WPFCommon/BrowseForFolder.cs` | pinvoke.net terms |
| `WindowPlacement` | `WPFCommon/WindowPlacement.cs` | MSDN blog (public) |

All of these are Windows-only and will be replaced, not ported.

For the Avalonia project, the following **new NuGet dependencies** will be needed:

| Package | Purpose |
|---|---|
| `Avalonia` | Core UI framework |
| `Avalonia.Desktop` | Desktop app host |
| `Avalonia.Themes.Fluent` | Fluent theme (closest to modern Windows look) |
| `Avalonia.Fonts.Inter` | Inter font family (used by `.WithInterFont()` in `Program.cs`) |
| `Avalonia.Controls.DataGrid` | DataGrid control (for file list) |
| `Avalonia.AvaloniaEdit` | Rich text / formatted text viewing (replaces RichTextBox) |
| `Avalonia.Diagnostics` (dev only) | DevTools for debugging UI |

---

## 11a. MakeDist & Build Scripts

The `MakeDist` project (and the `mkcp2.sh` shell script) automate building and packaging
CiderPress II for distribution. They require updates for the Avalonia port:

- **`MakeDist/Build.cs`** — Has a separate `sWinTargets` array containing `"cp2_wpf"` that
  is only built for Windows RIDs. This must be changed to `"cp2_avalonia"` and moved from
  `sWinTargets` to `sTargets` so it builds for **all** platform RIDs (win, linux, osx),
  since the Avalonia app is cross-platform.
- **`MakeDist/Clobber.cs`** — Scans for `.csproj` files to clean `bin/obj` dirs. This should
  self-resolve since it discovers project dirs dynamically, but verify it picks up
  `cp2_avalonia`.
- **`mkcp2.sh`** — Builds only the CLI (`cp2`), not the GUI. No changes needed.
- **Distribution contents** — MakeDist bundles `docs/Manual-cp2.md`, `README.md`,
  `LegalStuff.txt`, and `sample.cp2rc`. These are unchanged. If the Avalonia app is renamed
  or produces a different output assembly, the packaging logic may need adjustment.

These changes are deferred to **Iteration 15** (Polish & Packaging), since the project
structure may evolve during earlier iterations. See Iteration 15, Step 8 for details.

---

## 11b. Resource & Utility Replication Strategy

Because `cp2_wpf` is slated for deprecation, `cp2_avalonia` must be **self-contained** —
it must not reference or import files from `cp2_wpf`. All assets and helper code must be
copied or rewritten inside the `cp2_avalonia` tree:

| cp2_wpf Source | cp2_avalonia Destination | Action |
|---|---|---|
| `Res/cp2_app.ico` | `Res/cp2_app.ico` | Copy as-is (application icon) |
| `Res/RedX.png` | `Res/RedX.png` | Copy as-is (error overlay image) |
| `Res/Icons.xaml` | `Res/Icons.axaml` | Convert XAML→AXAML; rewrite `Style.Triggers` → Avalonia style selectors |
| `Res/TreeViewItemStyle.xaml` | `Res/TreeViewItemStyle.axaml` | Convert XAML→AXAML; adapt ControlTemplate syntax |
| `WPFCommon/InverseBooleanConverter.cs` | `Common/InverseBooleanConverter.cs` | Port: change `using System.Windows.Data` to `using Avalonia.Data.Converters`; the class implements `IValueConverter` in both — only the namespace changes |
| `WPFCommon/SelectTextOnFocus.cs` | `Common/SelectTextOnFocus.cs` | Port: change `UIElement` attached-event API to Avalonia equivalent |
| `WPFCommon/BindingProxy.cs` | `Common/BindingProxy.cs` | Rewrite: replace WPF `Freezable` base with Avalonia `AvaloniaObject` |
| `WPFCommon/WPFExtensions.cs` | `Common/AvaloniaExtensions.cs` | Port: replace `VisualTreeHelper` with Avalonia visual tree APIs |
| `WPFCommon/AnimatedGifEncoder.cs` | `Common/AnimatedGifEncoder.cs` | Port: replace `BitmapFrame`/`GifBitmapEncoder` with SkiaSharp or Avalonia bitmap APIs |
| `WPFCommon/WindowPlacement.cs` | `Common/WindowPlacement.cs` | Rewrite: replace Win32 `GetWindowPlacement`/`SetWindowPlacement` with Avalonia `Window.Position`/`ClientSize` + settings persistence |
| `WPFCommon/WinMagic.cs` | `Common/PlatformPaths.cs` | Rewrite: replace Win32 `SHGetKnownFolderPath`/`SHGetFileInfo` with `Environment.GetFolderPath` and platform-neutral icon lookup |
| `WPFCommon/BrowseForFolder.cs` | *(deleted)* | Replace with Avalonia `StorageProvider.OpenFolderPickerAsync` at call sites |
| `WPFCommon/ClipHelper.cs` | `Common/ClipHelper.cs` | Rewrite: replace COM `IDataObject` with Avalonia `IClipboard` API |
| `WPFCommon/VirtualFileDataObject.cs` | `Common/VirtualFileDataObject.cs` | Major rewrite: replace COM virtual file DnD with Avalonia DnD + temp-file fallback |
| `WPFCommon/CreateFolder.xaml/.cs` | `Common/CreateFolder.axaml/.cs` | Convert XAML→AXAML |
| `WPFCommon/FileSelector.xaml/.cs` | *(not ported)* | Replace with `StorageProvider` API at call sites (see Iteration 6) |
| `WPFCommon/WorkProgress.xaml/.cs` | `Common/WorkProgress.axaml/.cs` | Convert XAML→AXAML |

The `cp2_avalonia` directory will mirror this structure:
```
cp2_avalonia/
  Res/            ← icons, images, AXAML resource dictionaries
  Common/         ← cross-platform replacements for WPFCommon/ helpers
  Actions/        ← background worker wrappers (ported from cp2_wpf/Actions/)
  Tools/          ← diagnostic/test windows (ported from cp2_wpf/Tools/)
  LibTest/        ← library test runner (ported from cp2_wpf/LibTest/)
```

---

## 12. File-by-File Porting Inventory

### 12.1 Straightforward XAML→AXAML Ports (dialog windows)
These are self-contained dialog windows that should port relatively easily. Main work is
XAML syntax conversion, replacing `RoutedUICommand` with `ICommand`, and adjusting control
names.

| File | Lines | Notes |
|---|---|---|
| `AboutBox.xaml/.cs` | ~100 | Simple dialog |
| `AddMetadata.xaml/.cs` | ~150 | Simple form |
| `CreateDirectory.xaml/.cs` | ~192 | Simple input dialog |
| `CreateFileArchive.xaml/.cs` | ~144 | Selection dialog |
| `EditMetadata.xaml/.cs` | ~200 | Key-value editor |
| `FindFile.xaml/.cs` | ~200 | Search dialog |
| `ReplacePartition.xaml/.cs` | ~247 | Selection dialog |
| `Tools/LogViewer.xaml/.cs` | ~242 | Text display; replace `SaveFileDialog` |
| `Tools/ShowText.xaml/.cs` | ~109 | Text display; very simple |
| `WPFCommon/CreateFolder.xaml/.cs` | ~153 | Simple input |
| `WPFCommon/WorkProgress.xaml/.cs` | ~317 | Progress display |
| `Actions/OverwriteQueryDialog.xaml/.cs` | ~150 | Confirmation dialog |
| `LibTest/BulkCompress.xaml/.cs` | ~322 | Test dialog; replace `OpenFileDialog` |

### 12.2 Medium-Complexity Ports
These have more WPF-specific patterns or significant logic:

| File | Lines | Challenges |
|---|---|---|
| `EditAppSettings.xaml/.cs` | ~349 | Tab control with many bound settings |
| `EditAttributes.xaml/.cs` | ~1,027 | Complex form with conditional visibility |
| `EditConvertOpts.xaml/.cs` | ~200 | Dynamic option controls |
| `CreateDiskImage.xaml/.cs` | ~1,156 | Complex form + P/Invoke for drive query |
| `SaveAsDisk.xaml/.cs` | ~644 | Form with validation |
| `EditSector.xaml/.cs` | ~1,102 | Hex editor, custom rendering |
| `WPFCommon/FileSelector.xaml/.cs` | ~773 | Custom file dialog — **not ported**; replaced by `StorageProvider` API |
| `ConfigOptCtrl.cs` | ~423 | Dynamic control mapping |

### 12.3 Complex / High-Effort Ports
These are the core of the application and involve the most WPF-specific code:

| File | Lines | Challenges |
|---|---|---|
| `MainWindow.xaml/.cs` | ~3,058 | Menu, toolbar, split panels, 100 commands, drag-drop, all event handlers |
| `MainController.cs` | ~2,934 | Core logic — references `Microsoft.Win32`, `System.Windows` types scattered throughout |
| `MainController_Panels.cs` | ~1,137 | Panel management, data binding |
| `FileViewer.xaml/.cs` | ~1,374 | RichTextBox→AvaloniaEdit, image display, RTF→doc model, export |
| `LibTest/TestManager.xaml/.cs` | ~300 | RichTextBox→AvaloniaEdit for colored progress output; BackgroundWorker |
| `Tools/DropTarget.xaml/.cs` | ~340 | Drag-drop target window; heavy IDataObject/Clipboard use |
| `SelectPhysicalDrive.xaml/.cs` | ~243 | Windows-specific drive enumeration |

### 12.4 Pure Logic Files (Minimal WPF Dependencies)
These are mostly logic and data classes — should port with only `using` statement changes:

| File | Lines | Notes |
|---|---|---|
| `AppSettings.cs` | ~100 | Settings key constants — no WPF types |
| `ArchiveTreeItem.cs` | ~428 | Data model — may need `INotifyPropertyChanged` adjustments |
| `DirectoryTreeItem.cs` | ~200 | Data model |
| `FileListItem.cs` | ~429 | Data model — uses `BitmapSource` for icons |
| `ClipInfo.cs` | ~150 | Clipboard metadata — references `System.Windows.DataFormats` |
| `DebugMessageLog.cs` | ~100 | Logging utility |
| `AssemblyInfo.cs` | ~10 | Metadata |
| `Actions/*.cs` (non-XAML) | ~800 | Background workers — mostly logic, some `Dispatcher` calls |
| `LibTest/BulkCompressTest.cs` | ~349 | Test logic |
| `LibTest/TestRunner.cs` | ~200 | Test runner logic |
| `LibTest/ProgressMessage.cs` | ~50 | Message types |

---

## 13. Iterative Implementation Plan

The approach is to build the Avalonia app incrementally. Each iteration produces a runnable
application. Menu items and features that haven't been ported yet show a "Not Implemented"
message box. This keeps the app testable at every step and avoids big-bang integration.

### Iteration 0: Project Scaffolding — Empty Window
**Goal:** A blank Avalonia window that compiles, runs, and exits cleanly on all platforms.

1. Create `cp2_avalonia.csproj` targeting `net8.0` with Avalonia NuGet packages:
   - `Avalonia` (11.2+), `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
     `Avalonia.Controls.DataGrid`, `Avalonia.AvaloniaEdit`, `Avalonia.Diagnostics` (Debug)
2. Create `Program.cs` with Avalonia `AppBuilder` bootstrap.
3. Create `App.axaml` / `App.axaml.cs` — Fluent theme.
   Replicate application-level resources from `cp2_wpf/App.xaml`:
   - Merged resource dictionary for `Res/Icons.axaml` (empty stub initially)
   - `GeneralMonoFont` and `ViewerMonoFont` font resources
     (use cross-platform fallback: `"Cascadia Mono, Consolas, Menlo, monospace"`)
   - `CheckerBackground` tiled `DrawingBrush` (transparency checkerboard for images)
   - `InverseBooleanConverter` as a shared resource
   - AvaloniaEdit requires explicit style registration. The recommended approach for
     AvaloniaEdit 11.x is to add:
     ```xml
     <StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />
     ```
     to `App.axaml`'s `<Application.Styles>`. If that URI fails at runtime (the path
     varies between AvaloniaEdit versions), try
     `avares://AvaloniaEdit/AvaloniaEdit.xaml` or check the installed NuGet package's
     embedded resources with `dotnet list package` + inspecting the `.nupkg`. As a
     fallback, calling `AppBuilder.UseAvaloniaEdit()` (if the extension method exists)
     registers styles programmatically. **Verify** that the TextEditor control renders
     with correct fonts and colors — it will silently render unstyled if this step
     is wrong.
     The global `TreeViewItem` style is deferred to Iteration 3.
4. Create a minimal `MainWindow.axaml` — empty window with title "CiderPress II",
   correct size (1200×800), application icon.
5. Copy static resources into `cp2_avalonia/Res/`: `cp2_app.ico`, `RedX.png`.
6. Add project references: `AppCommon`, `CommonUtil`, `DiskArc`, `FileConv`,
   `DiskArcTests`, `FileConvTests`.
7. Update `CiderPress2.sln`:
   - **Back up** the original: `cp CiderPress2.sln CiderPress2.sln.original`
   - **Remove** the `cp2_wpf` project entry
     (`{B79430A3-B9D7-4EAB-86C5-138B0A2C387B}`) and its build configuration lines.
   - **Add** the new `cp2_avalonia` project entry with a fresh GUID.
   - This ensures `dotnet build` / `dotnet run` targets the Avalonia app, not the
     WPF app.  The `cp2_wpf` project and directory remain fully intact as a
     fallback — restoring the backup `.sln` re-enables the original WPF build.
8. Verify build + run on Linux (and Windows/macOS if available).

### Iteration 1: Menu Bar & Stub Commands
**Goal:** Full menu bar matching the WPF layout. Every menu item is wired to a stub that
shows "Not Implemented" (or is grayed out). The About Box is also created here so that
Help → About works as a smoke test for the menu and modal-dialog lifecycle.

1. Port the menu structure from `MainWindow.xaml` → `MainWindow.axaml`.
2. Create a command infrastructure: define `ICommand` properties on `MainWindow` (or a
   lightweight helper class). Each command either calls a stub or a real handler.
3. Implement a `NotImplemented(string featureName)` helper that shows a MessageBox.
4. Wire keyboard shortcuts (`KeyGesture` bindings) for the most important commands
   (including `F1` for Help).
5. Port `AppSettings.cs` (pure constants, no WPF types).
6. Port `AboutBox.axaml/.cs` and wire Help → About to the real dialog. Include
   `LegalStuff.txt` loading, OS/runtime info, and a minimal `GetRuntimeDataDir()` helper
   (full `PlatformUtil.cs` arrives in Iteration 3).

### Iteration 2: Toolbar & Status Bar
**Goal:** Icon toolbar and status bar/info panel matching the WPF layout.

1. Port **all 14 icons** from `Res/Icons.xaml` to Avalonia resource dictionary
   (vector `DrawingImage` or `PathIcon`). Six are used in the toolbar; the remaining
   eight (`StatusOK/Invalid/Warning/Error/NoNoColor`, `DateTimePicker`, `Comment`,
   `Refresh`) are used by `ArchiveTreeItem`, `FileListItem`, and `FileSelector` in
   later iterations and must be present now.
2. Build toolbar as a styled `StackPanel` with `Button` controls bound to the same
   `ICommand` properties from Iteration 1. Use `<Border>` for vertical dividers
   (Avalonia `Separator` is only styled inside `Menu`/`ListBox`). No explicit
   `IsEnabled` bindings — `RelayCommand.CanExecute` handles button state.
3. Create a bottom status bar area using a `Border` + `Grid` (Avalonia has no
   built-in `StatusBar`).
4. Port `Res/TreeViewItemStyle.xaml` as a stub — Avalonia's Fluent theme handles
   full-width stretch by default. Revisit in Iteration 3 if needed.

### Iteration 3: File Open & Archive/Directory Tree
**Goal:** Open a disk image or archive and display the archive tree (left panel).

1. Implement cross-platform file dialogs: create `PlatformUtil.cs` (replaces `WinUtil.cs`)
   using Avalonia `StorageProvider` APIs. `AskFileToOpen()` returns `null` on cancel
   (deliberate change from WPF's `string.Empty`). Copy the **complete**
   `FILE_FILTER_KNOWN` extension list (~35 entries) from `WinUtil.cs`.
   `GetRuntimeDataDir()` is a cross-platform redesign using sentinel-file probing
   (replaces Windows-specific `\bin\Debug\` substring + 4-level walk-up).
   `IsAdministrator()` uses `Environment.IsPrivilegedProcess` (.NET 8+).
2. Port `ArchiveTreeItem.cs` — `StatusIcon`/`ReadOnlyIcon` become `IImage?` properties
   (resolved via `Application.Current.FindResource()` as `DrawingImage`). The
   `BringItemIntoView()` and `SelectBestFrom()` methods use WPF-specific
   `VirtualizingStackPanel`/`ItemContainerGenerator` — replace with Avalonia's
   `TreeViewItem.BringIntoView()`. To locate the `TreeViewItem` for a data item,
   walk the `TreeView.ItemContainerGenerator` or use `TreeView.ContainerFromItem()`
   (Avalonia 11.2+: `treeView.ContainerFromItem(dataItem)` returns the
   `TreeViewItem`, then call `.BringIntoView()` on it). If the item is not yet
   realized (virtualized), expand parent nodes first.
3. Port `DirectoryTreeItem.cs` — same `BringIntoView()` approach applies.
4. Port the archive tree panel in `MainWindow.axaml` (`TreeDataTemplate`) with
   `<Image Source="{Binding StatusIcon}"/>` and `ReadOnlyIcon` (null-hidden via
   `ObjectConverters.IsNotNull`).
5. Port `MainController` open/close logic plus helper methods: `PopulateArchiveTree()`,
   `UpdateTitle()`, `UpdateRecentFilesList()`, `ClearEntryCounts()`. Replace
   `Mouse.OverrideCursor` with Avalonia `Cursor`. Defer `Clipboard.Clear()` logic.
6. Port `Actions/OpenProgress.cs` and `WPFCommon/WorkProgress.axaml` for progress dialog.
   Note: `WorkProgress.xaml.cs` contains `MessageBoxQuery` and `OverwriteQuery` inner
   classes with thread synchronization — must be ported.
7. Port the launch panel with Recent Files buttons (matching WPF's two-button layout
   with visibility bindings), `ClearTreesAndLists()` on `MainWindow`.
8. Implement simple window placement persistence (JSON serialization; replaces Win32
   P/Invoke + XML — first-run migration from WPF settings will gracefully fall back
   to defaults).

### Iteration 4: File List Panel
**Goal:** Selecting a node in the archive/directory tree populates the file list;
center panel toggles between file list DataGrid and info panel.

1. Port `FileListItem.cs` — `StatusIcon` becomes `IImage?` (was `ControlTemplate?`),
   all 14 properties ported (FileName, PathName, Type, AuxType, CreateDate, ModDate,
   Access, DataLength, DataSize, DataFormat, RawDataLength, RsrcLength, RsrcSize,
   RsrcFormat, TotalSize), port `ItemComparer` inner class (13-column `ColumnId`
   enum with secondary sort keys), port `FindItemByEntry`/`SelectAndView`/
   `SetSelectionFocusByEntry` (stub WPF `DataGrid.SelectRowByIndex` extension).
2. Port file list DataGrid (WPF `ListView`+`GridView` → Avalonia `DataGrid`) with
   all 13 columns matching WPF layout. Conditional column visibility via 6
   `ShowCol_*` boolean properties bound to `IsVisible`. Filename/Pathname use
   `DataGridTemplateColumn` with `TextTrimming`. Add 10-item context menu.
3. Port `MainController_Panels.cs` — all ~20 boolean CanExecute properties
   (`CanWrite`, `AreFileEntriesSelected`, `IsSingleEntrySelected`,
   `IsMultiFileItemSelected`, `IsHierarchicalFileSystemSelected`, etc.),
   `ArchiveTree_SelectionChanged` (dispatches by IFileSystem/IArchive/IDiskImage/
   IMultiPart/Partition), `DirectoryTree_SelectionChanged`,
   `RefreshDirAndFileList` (verify-before-repopulate pattern).
4. Port `PopulateFileList` with 3 sub-methods: `PopulateEntriesFromArchive`
   (MacZip pair/ADF attribute handling), `PopulateEntriesFromSingleDir`,
   `PopulateEntriesFromFullDisk` (recursive). Port `SetEntryCounts`/
   `ClearEntryCounts` for status bar ("N files, M dirs, X free").
5. Implement center panel toggle (`ShowCenterFileList`/`ShowCenterInfoPanel`
   with `CenterPanelChange` enum) and full-list vs single-directory mode
   (`ShowSingleDirFileList`, `PreferSingleDirList` setting, toolbar buttons).
6. Port `ConfigureCenterPanel(isInfoOnly, isArchive, isHierarchic, hasRsrc,
   hasRaw)` — sets column visibility, enables toolbar buttons, toggles
   center panel mode.
7. Port `HandleFileListDoubleClick` (5-step dispatch: directory→dir-tree,
   already-open→select, potential-archive→TryCreateSub, archive-dir→noop,
   else→ViewFiles). Custom sorting via `Sorting` event + `ItemComparer` +
   `ObservableCollection` re-sort (no WPF `ListCollectionView`).
   `ResetSortCommand` repopulates from unsorted order.
8. Wire CanExecute checks to gate on both selection properties and
   `ShowCenterFileList` (file commands disabled when info panel is showing).

### Iteration 5: Simple Dialogs (First Batch)
**Goal:** First batch of utility dialogs working (About Box was already created in
Iteration 1).

1. Port `Tools/ShowText.axaml` — simple mono-spaced text display window. Supports
   modal (`ShowDialog`) and modeless (`Show` + `ShowInTaskbar = true`) usage.
   ESC key handler via `KeyDown` event.
2. Port `Tools/LogViewer.axaml` — modeless log viewer with 3-column `DataTemplate`
   (timestamp `yyyy/MM/dd HH:mm:ss.fff`, bold priority letter, wrapping message).
   Port `LogEntry` wrapper class (converts `Priority` enum → single letter
   "V/D/I/W/E/S"). Auto-scroll with engage/disengage pattern (disengage when user
   scrolls up, re-engage at bottom). Replace `Microsoft.Win32.SaveFileDialog` with
   `StorageProvider.SaveFilePickerAsync()`. Toggle lifecycle:
   `Debug_ShowDebugLog()` stores `mDebugLogViewer` reference, `Closing` event
   nulls it out; close also called from `WindowClosing()`.
3. Port `CreateDirectory.axaml` — archive filesystem directory creation dialog with
   filesystem-aware validation: `IsValidDirNameFunc` delegate for syntax checking,
   `TryFindFileEntry` for uniqueness, colored error labels (`SyntaxRulesForeground`,
   `UniqueNameForeground` as `IBrush`). Replace `SystemColors.WindowTextBrush` with
   Avalonia theme-aware brush. `ContentRendered` → `Opened` event for focus.
   Port the `MainController.CreateDirectory()` caller. Note:
   `WPFCommon/CreateFolder` (host filesystem) deferred to Iteration 6 with
   `FileSelector`.
4. Port `DebugMessageLog.cs` — replace `Application.Current.Dispatcher.Thread` check
   with `Dispatcher.UIThread.CheckAccess()`, replace
   `Application.Current.Dispatcher.Invoke()` with `Dispatcher.UIThread.Invoke()`.
   Circular buffer logic ports directly.

### Iteration 6: Extract & Add Files
**Goal:** Core file operations: extract files from archives, add files to archives.

1. Port `Actions/ProgressUtil.cs` — shared callback handler using `BackgroundWorker`;
   replace `System.Windows.MessageBoxButton`/`MessageBoxImage` refs with Avalonia equivalents.
2. Port `Actions/ExtractProgress.cs` — implements `WorkProgress.IWorker`, runs
   `ExtractFileWorker` on worker thread. No Dispatcher calls (pure worker-thread code).
3. Port `Actions/AddProgress.cs` — implements `WorkProgress.IWorker`, runs `AddFileWorker`
   on worker thread. Handles transaction management and deferred error reporting.
4. Port `Actions/OverwriteQueryDialog.axaml` — overwrite confirmation dialog invoked by
   `WorkProgress.ProgressChanged` (not directly by progress classes). Two-section layout:
   "Copy and Replace" / "Don't Copy" with file info and "Do this for all" checkbox.
5. Port `MainController` methods: `HandleExtractExport()` (shared by Extract+Export),
   `HandleAddImport()` (shared by Add+Import), `AddPaths()`, `AddFileDrop()` (for drag-drop
   addition), `ConfigureAddOpts()`, `CheckPasteDropOkay()`, `GetImportSpec()`,
   `GetExportSpec()`. Import/Export share code paths with Add/Extract — they just pass
   a `ConvConfig.FileConvSpec`. Replace `FileSelector` dialog with Avalonia
   `StorageProvider` pickers (folder for extract, folder for add as simplification).
6. Port options panel (right side) — ~25 bound properties for add/extract/import/export
   settings: compression, strip paths, raw mode, preservation mode, import/export converters.
   Plus "Drag & Copy mode" toolbar radio buttons (`IsChecked_AddExtract`/`IsChecked_ImportExport`).
7. Implement drag-drop: launch panel drop-to-open (single file). File list drag source
   (VirtualFileDataObject replacement) is deferred to **Iteration 13** (Clipboard &
   Advanced Drag-and-Drop). External-file drop on the file list (add files from OS file
   manager) is also implemented in **Iteration 13, Step 5**. Internal drag-move between
   directories is a stretch goal in Iteration 13.

### Iteration 7: File Viewer (with AvaloniaEdit)
**Goal:** View file contents with formatted text, hex dumps, and images.

1. Port `FileViewer.axaml` — replace `RichTextBox` + `TextBox` with `AvaloniaEdit.TextEditor`
   in read-only mode. WPF source is at `cp2_wpf/FileViewer.xaml` (root, NOT Tools/).
   Avalonia places it in `Tools/` as organizational improvement. Convert WPF `DataTrigger`
   tab-bold styles to Avalonia `Styles` with selectors.
2. Implement `FancyTextHelper.cs` — new converter from `FancyText` annotations →
   AvaloniaEdit `DocumentColorizingTransformer` (replaces RTFGenerator→RichTextBox pipeline).
   Unifies find/search: single TextEditor eliminates WPF RichTextBox run-boundary limitation.
3. Port image display via `BitmapUtil.cs` — handle **both** `Bitmap8` (indexed, needs
   palette expansion to BGRA) and direct-color paths. Port `ConfigureMagnification()`
   with its two branches: HostConv (already decoded, resize only) vs IBitmap (convert +
   optional ScaleUp).
4. Port `ConfigOptCtrl.cs` — converter option control mapping with abstract
   base class and 3 subclasses. Replace WPF programmatic Binding with Avalonia equivalent.
   Port `IsSaveDefaultsEnabled` and save-defaults button logic.
5. Port export-to-file (Save dialog via `StorageProvider`) and clipboard copy. CellGrid
   output uses CSV as plain text. Port HostConv temp file lifecycle:
   `LaunchExternalViewer`, `DeleteTempFiles`, `FindStaleTempFiles`.
6. `AnimatedGifEncoder.cs` is used by `MainController.DoConvertANI()`, not FileViewer
   itself — deferred to **Iteration 15** (Polish & Packaging). Until then, if
   `DoConvertANI()` is invoked (e.g., via File Viewer Save/Export on an animated image),
   show the standard "Not Implemented" stub dialog rather than silently failing.

### Iteration 8: Delete, Move, Rename, Create Directory, Edit Attributes
**Goal:** File management operations.

1. Port `Actions/DeleteProgress.cs`, `Actions/MoveProgress.cs`.
   Note: `MoveFiles` is drag-drop only — no menu command. Drag-drop wiring in Iteration 13.
2. Wire `CreateDirectory` (already ported in Iteration 5) into move/rename context.
3. Port `EditAttributes.axaml` + `EditAttributes.axaml.cs` —
   complex form (file types, dates, access flags, comments). Rename uses this dialog.
4. Port `Actions/EditAttributesProgress.cs` — runs on GUI thread, **not** an
   IWorker. Has MacZip/AppleSingle rewriting logic.
5. Wire `DeleteFiles`, `CreateDirectory`, `EditAttributes` in `MainController.cs`.
   Note: There is no separate `EditDirAttributes` dialog — `EditAttributes.axaml` handles
   both file and directory attribute editing. The controller calls `EditAttributes` for
   directories too, passing the directory entry; the dialog adjusts its UI based on
   the entry type.

### Iteration 9: Create New Archives & Disk Images
**Goal:** Create blank disk images and file archives.

1. Port `CreateDiskImage.axaml` + `CreateDiskImage.axaml.cs` —
   complex form with 3 columns of radio buttons, cross-validation cascade.
   No P/Invoke to remove — WPF-specific deps are `SaveFileDialog`, `Mouse.OverrideCursor`,
   `WinUtil.FILE_FILTER_*` constants, WPF `Brushes`/`SystemColors`.
2. Port `CreateFileArchive.axaml` + `.axaml.cs` — simple dialog.
3. Wire `NewDiskImage()`, `NewFileArchive()` in `MainController.cs`.
   Note: `NewDiskImage` calls `CloseWorkFile()` before dialog; `NewFileArchive` calls it
   after (deliberate ordering difference).

### Iteration 10: Edit Settings & Remaining Dialogs
**Goal:** Application settings and remaining secondary dialogs.

1. Port `EditAppSettings.axaml` — single-tab settings dialog (one "General" tab, 118 + 231 lines).
2. Verify `ConfigOptCtrl.cs` (already ported in Iteration 7, Step 4; 423 lines).
3. Port `EditConvertOpts.axaml` — uses `ConfigOptCtrl`; constructor takes `(Window, bool isExport, SettingsHolder)`.
4. Port `AddMetadata.axaml` (73 + 134), `EditMetadata.axaml` (71 + 156 — exists, has Delete button and read-only key).
5. Port `FindFile.axaml` — modal dialog (despite callback event); static fields persist search state.
6. Port `ReplacePartition.axaml` — controller method orchestrates file open, analyze, and partition re-scan.
7. Port `SaveAsDisk.axaml` (82 + 644) — 10 format types; shares `FileTypeValue`/`SelectOutputFile` with Iter 9; `CopyDisk()` also used by `ReplacePartition`.

### Iteration 11: Sector Editor
**Goal:** Low-level sector/block editing.

1. Port `EditSector.axaml/.cs` (~1,102 lines) — hex editor with custom rendering.
   May need a custom Avalonia control for the hex view.

### Iteration 12: Library Tests & Bulk Compress
**Goal:** Built-in test running and compression testing tools.

1. Port `LibTest/TestManager.axaml` (64+245 lines) — replace `RichTextBox`/`FlowDocument` with
   AvaloniaEdit (read-only, colored text output). Has TWO output areas: colored progress +
   per-test detail (ComboBox + TextBox).
2. Port `LibTest/TestRunner.cs` (279 lines) — replace `System.Windows.Media.Colors` with
   `Avalonia.Media.Colors`. Uses reflection (`Assembly.LoadFile`) exclusively — no project refs.
3. Port `LibTest/ProgressMessage.cs` (38 lines) — replace `System.Windows.Media.Color`.
4. Port `LibTest/BulkCompress.axaml` (90+232 lines) — replace `OpenFileDialog` with
   `StorageProvider`. Has 8 radio buttons for compression format, BackgroundWorker.
5. Port `LibTest/BulkCompressTest.cs` (349 lines) — minimal changes.
6. Port `Actions/TestProgress.cs` (209 lines) — `WorkProgress.IWorker` for Actions→Test Files.
   Tests files in archives/filesystems by reading all forks to `Stream.Null`.

### Iteration 13: Clipboard & Advanced Drag-and-Drop
**Goal:** Copy/paste and drag-drop of archive contents.

1. Port `ClipInfo.cs` — replace Windows P/Invoke (`RegisterClipboardFormat`, `IDataObject`)
   with JSON-over-text-clipboard using Avalonia `IClipboard`.
2. Port copy command — replace `VirtualFileDataObject` (Windows COM streaming) with
   JSON text clipboard via `clipboard.SetTextAsync()`.
3. Port paste command — replace `PasteOrDrop(IDataObject?, IFileEntry)` for Avalonia;
   preserve ProcessId check, version check, export-mode rejection, same-archive block.
4. Port `Actions/ClipPasteProgress.cs` (150 lines) — `WorkProgress.IWorker` wrapping
   `ClipPasteWorker`. Constructor takes `(archiveOrFileSystem, leafNode, targetDir,
   clipInfo, streamGen, appHook)`.
5. Implement file-path-based drop on file list area (add files from OS file manager).
6. Port `Tools/DropTarget.axaml` — **modeless** debug window (no Owner,
   `ShowInTaskbar=True`, `Close()` not `DialogResult`). Omit Windows COM format dumps;
   show Avalonia `IDataObject` format names and `ClipInfo` JSON deserialization.
7. (Stretch) Internal drag-move between directories, app-internal virtual file paste.

**Note:** `WPFCommon/ClipHelper.cs` (318 lines, Windows COM `FileGroupDescriptorW`) and
`WPFCommon/VirtualFileDataObject.cs` are **not ported** — replaced by JSON-text approach.

### Iteration 14: Scan Blocks & Physical Drive Access
**Goal:** Low-level disk operations and platform-specific drive access.

1. Port `Actions/ScanBlocksProgress.cs` (146 lines) — `WorkProgress.IWorker` taking
   `(IDiskImage, AppHook)`. Handles both `HasSectors` (track/sector via `TestSector`)
   and `HasBlocks` (via `TestBlock`). Distinguishes unreadable vs unwritable.
2. Port `SelectPhysicalDrive.axaml` — 360px dialog, DataGrid with Device/Type/Size,
   `DiskItem` inner class wrapping `PhysicalDriveAccess.DiskInfo`, auto-selects first
   Removable, grays Fixed disks, persists read-only setting.
3. Implement cross-platform drive enumeration — either extend
   `CommonUtil/PhysicalDriveAccess.cs` (currently returns `null` on non-Windows) or
   create `PhysicalDriveInfo.cs` wrapper. Linux: `/sys/block/`, macOS: `diskutil`.
4. Port `OpenPhysicalDrive()` — replace Windows admin escalation (`WinUtil.IsAdministrator`,
   `"runas"` verb) with platform-appropriate permission handling.
5. Port `ScanForBadBlocks()` — results displayed via `ShowText` dialog (StringBuilder
   listing each failure with Block/T+S and writable status), zero failures → notification.

### Iteration 15: Polish & Parity
**Goal:** Feature parity with cp2_wpf, final polish.

1. Audit all `NotImplementedException` stubs — implement or document as intentional.
2. Port remaining `WPFCommon/` utilities (15 files total; several already ported or
   intentionally skipped — see blueprint for file-by-file status table).
3. Verify recent-files list (Ctrl+Shift+1..6 shortcuts).
4. Verify all keyboard shortcuts against actual WPF `<KeyGesture>` definitions
   (13 explicit bindings in MainWindow.xaml, plus standard clipboard commands).
5. Update `MakeDist/Build.cs` — change `sWinTargets` from `"cp2_wpf"` to `"cp2_avalonia"`
   (or move to `sTargets` since Avalonia is cross-platform).
6. Test on Windows, Linux, and macOS.
7. Address theming/visual discrepancies, high-DPI, dark mode.
8. Update documentation (README.md, Install.md, SourceNotes.md).
9. Mac/Linux integration (`.desktop` files, file associations, etc.).

---

## 14. Key Decisions (Resolved)

| Decision | Choice | Rationale |
|---|---|---|
| **Architecture** | Code-behind + controller (same as WPF) | Shortest path to a working port. MVVM refactor can come later. |
| **Rich text viewer** | **AvaloniaEdit** (`Avalonia.AvaloniaEdit`) | MIT licensed — compatible with Apache 2.0. Feature-rich, cross-platform, actively maintained. |
| **Toolbar** | Styled `StackPanel` with icon `Button` controls | No built-in Avalonia toolbar. Use `StackPanel` with `Orientation="Horizontal"` (fixed layout, matching WPF toolbar behavior). Do **not** use `WrapPanel` — it would cause toolbar buttons to reflow at narrow window widths, which differs from WPF. |
| **Target framework** | `net8.0` | Matches all other projects in the solution. |
| **Avalonia version** | 11.2+ (latest stable) | Current stable release line. |
| **Theme** | `Avalonia.Themes.Fluent` | Modern cross-platform appearance, closest to Windows 11 look. |
| **cp2_wpf disposition** | Remove from solution; keep project intact | `cp2_wpf` entries removed from `.sln` in Iteration 0 (original saved as `CiderPress2.sln.original`). The project and all its files are preserved unchanged as a fallback. Restoring the backup `.sln` re-enables the WPF build. |
| **Resource strategy** | Replicate everything into `cp2_avalonia/` | No cross-project file references to `cp2_wpf`. All assets, helpers, and utility code live under `cp2_avalonia/`. |
| **Assembly name** | `CiderPress2` | The WPF project produces `CiderPress2.exe` (not `cp2_wpf.exe`) via `<AssemblyName>CiderPress2</AssemblyName>`. The Avalonia project should use the same assembly name so that the executable name, settings paths, and MakeDist packaging remain consistent. |

---

## 15. Risks and Gotchas

- **Virtual file drag-drop** is deeply Windows-specific (COM IDataObject/IStream). Full
  parity on Linux/macOS may not be achievable. Plan for a temp-file fallback.
- **RichTextBox** absence means the file viewer will look different. AvaloniaEdit is capable
  but has a different editing model.
- **`ListView` + `GridView`** is heavily used for the file list. Avalonia's `DataGrid` is
  the replacement but has different column/sorting APIs.
- **Triggers** in WPF styles (DataTrigger, MultiDataTrigger) need conversion to Avalonia
  style selectors and classes — this is a pervasive change across all XAML files.
- **RoutedUICommand** removal affects almost every interaction in the main window. This is
  a large mechanical change.
- **Consolas font** doesn't exist on Linux/macOS — need font fallbacks.
- **`Dispatcher.Invoke`** works differently in Avalonia (`Dispatcher.UIThread.Post` /
  `InvokeAsync`).
- **`GetRuntimeDataDir()`** in `WinUtil.cs` uses Windows-specific backslash path detection
  to locate the settings directory and `TestData/`. This is entangled with settings
  persistence and the built-in test runner — both will silently fail on Linux/macOS if
  the path logic isn't rewritten early.
- **`ConvertToBitmapSource()`** in `WinUtil.cs` is the sole bridge from `FileConv.IBitmap`
  to the display layer. Without an Avalonia rewrite, no Apple II graphics will render.
- **MakeDist** must be updated to build `cp2_avalonia` (cross-platform) instead of
  `cp2_wpf` (Windows-only), or release distributions won't include the new GUI.
- **Assembly name** — The WPF project's `<AssemblyName>` is `CiderPress2`, not `cp2_wpf`.
  The Avalonia project should match this to keep the executable name and settings paths
  consistent.

---

## 16. License Summary

| Component | License | Compatible with Apache 2.0? |
|---|---|---|
| CiderPress II | Apache 2.0 | N/A (this project) |
| Avalonia | MIT | Yes |
| AvaloniaEdit | MIT | Yes |
| AvaloniaEdit.TextMate | MIT | Yes |
| TextMateSharp.Grammars | MIT | Yes |
| SkiaSharp (Avalonia dep) | MIT | Yes |

All new dependencies are MIT-licensed, which permits use, modification, and distribution
within Apache 2.0 projects. The only obligation is to include the MIT copyright notices
in the distribution (e.g., in a NOTICE or third-party licenses file).

---

## 17. Future Improvements and Corrections

- **FileSelector dialog (Add/Import files):** The WPF app uses a custom `FileSelector` dialog
  (`cp2_wpf/WPFCommon/FileSelector.xaml`) that supports simultaneous selection of both files
  and folders (`SelMode.FilesAndFolders`). This is a mini file-browser with directory navigation
  and multi-select. The Avalonia port currently uses `OpenFilePickerAsync` with
  `AllowMultiple = true`, which only allows selecting individual files (not folders). A full
  port of the `FileSelector` dialog is needed to restore the ability to add entire folders
  with their directory structure intact via the Add/Import menu commands. (Drag-and-drop of
  folders from the OS file manager still works via `AddFileDrop`.)

---

## 18. Reference Links

- Avalonia UI: https://avaloniaui.net/
- Avalonia docs: https://docs.avaloniaui.net/
- Avalonia GitHub: https://github.com/AvaloniaUI/Avalonia
- AvaloniaEdit: https://github.com/AvaloniaUI/AvaloniaEdit
- WPF → Avalonia migration guide: https://docs.avaloniaui.net/docs/get-started/wpf/
- CiderPress II: https://a2ciderpress.com/
