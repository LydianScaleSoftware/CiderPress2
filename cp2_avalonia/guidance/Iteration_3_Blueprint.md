# Iteration 3 Blueprint: File Open & Archive/Directory Trees

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Open a disk image or file archive via File → Open, populate the archive tree (left panel),
and display the directory tree. This is the first iteration where the app actually does
something useful — loading a real Apple II disk image and navigating its structure.

---

## Prerequisites

- Iteration 2 is complete: menu bar, toolbar, and status bar are working.
- Key WPF source files to read before starting:
  - `cp2_wpf/MainController.cs` — Focus on the file open/close logic (search for
    `DoOpenFile`, `DoCloseFile`, `OpenWorkFile`, `CloseWorkFile`)
  - `cp2_wpf/ArchiveTreeItem.cs` — Full file
  - `cp2_wpf/DirectoryTreeItem.cs` — Full file
  - `cp2_wpf/WinUtil.cs` — Focus on `AskFileToOpen()`, `GetRuntimeDataDir()`
  - `cp2_wpf/WPFCommon/WindowPlacement.cs` — Full file for window placement logic
  - `cp2_wpf/WPFCommon/WorkProgress.xaml/.cs` — Progress dialog
  - `cp2_wpf/Actions/OpenProgress.cs` — Open-file background worker
  - `cp2_wpf/MainWindow.xaml` lines 455-630 — The archive tree panel, directory tree panel,
    and launch panel layout

---

## Step-by-Step Instructions

### Step 1: Create `cp2_avalonia/Common/PlatformUtil.cs`

This replaces `cp2_wpf/WPFCommon/WinUtil.cs`. Start with these critical functions:

1. **`AskFileToOpen(Window parent)`** — Opens a file picker using Avalonia's
   `StorageProvider` API:
   ```csharp
   public static async Task<string?> AskFileToOpen(Window parent) {
       var topLevel = TopLevel.GetTopLevel(parent);
       if (topLevel == null) return null;
       var files = await topLevel.StorageProvider.OpenFilePickerAsync(
           new FilePickerOpenOptions {
               Title = "Open File",
               AllowMultiple = false,
               FileTypeFilter = new[] {
                   new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
               }
           });
       return files.Count > 0 ? files[0].Path.LocalPath : null;
   }
   ```
   Read `WinUtil.cs` to get the **complete** set of `FILE_FILTER_*` definitions and convert
   them to `FilePickerFileType` arrays. Copy the full `FILE_FILTER_KNOWN` extension list
   verbatim — it contains ~35 extensions including `.shk`, `.sdk`, `.sea`, `.bny`, `.bqy`,
   `.bxy`, `.bse`, `.wav`, `.dsk`, `.po`, `.do`, `.d13`, `.2mg`, `.img`, `.iso`, `.hdv`,
   `.dc`, `.dc42`, `.dc6`, `.image`, `.ddd`, `.nib`, `.nb2`, `.raw`, `.app`, `.woz`, `.moof`,
   `.gz`, `.zip`, `.as`, `.bin`, `.macbin`, `.acu`. Do **not** use the abbreviated subset
   shown above in the sample code — always reference the authoritative list in `WinUtil.cs`.
   Also add an "All Files" entry.

   > **Return type change:** The WPF `AskFileToOpen()` returns `string.Empty` on cancel;
   > this Avalonia version returns `null`. All callers (including `OpenWorkFile()` in
   > `MainController.cs`) must use a `null` check instead of `string.IsNullOrEmpty()`.

2. **`GetRuntimeDataDir()`** — Locates the application data directory. The WPF version
   uses Windows-specific `\bin\Debug\` substring detection and walks up exactly 4 levels.
   This is a **deliberate cross-platform redesign**, not a direct port. Rewrite with
   platform-agnostic logic:
   ```csharp
   public static string GetRuntimeDataDir() {
       string baseDir = AppContext.BaseDirectory;
       // Walk up from bin/Debug/net8.0/ to find the project root during development.
       // In production the exe and settings are in the same directory.
       // Limit depth to avoid accidentally matching a sentinel file far up the tree
       // (e.g., a stale CiderPress2-settings.json in a parent directory).
       DirectoryInfo? dir = new DirectoryInfo(baseDir);
       int maxDepth = 6;
       while (dir != null && maxDepth-- > 0) {
           if (File.Exists(Path.Combine(dir.FullName, "CiderPress2-settings.json")) ||
               File.Exists(Path.Combine(dir.FullName, "CiderPress2.sln"))) {
               return dir.FullName;
           }
           dir = dir.Parent;
       }
       return baseDir;  // fallback: neither sentinel file found
   }
   ```
   Read `WinUtil.cs` carefully. The WPF version checks `baseDir.Contains(@"\bin\Debug\")`
   (Windows-only path separators) and walks up exactly 4 directory levels. The new version
   uses sentinel-file probing which works on all platforms and any number of path levels.

3. **`IsAdministrator()`** — Cross-platform privilege check. As decided in Iteration 1
   (About Box), use `Environment.IsPrivilegedProcess` (.NET 8+), which handles all
   platforms (Windows admin, Unix root) in a single call:
   ```csharp
   public static bool IsAdministrator() {
       return Environment.IsPrivilegedProcess;
   }
   ```
   This replaces the WPF-only `WindowsIdentity`/`WindowsPrincipal` approach.

### Step 2: Create `cp2_avalonia/Common/WorkProgress.axaml/.cs`

Port `cp2_wpf/WPFCommon/WorkProgress.xaml/.cs`. This is a modal progress dialog used for
background operations (open file, extract, add, etc.).

Read the WPF source file fully. Key elements:
- A `ProgressBar` (indeterminate or percentage-based)
- A text label showing the current operation
- A Cancel button
- Uses `BackgroundWorker` for async work
- **Important:** The code-behind also contains `MessageBoxQuery` and
  `OverwriteQuery` inner classes with thread synchronization (`Monitor.Wait`/`Pulse`)
  for cross-thread dialog prompts during background operations. These must be ported.
  `MessageBoxQuery` references WPF `MessageBoxResult`, `MessageBoxButton`, and
  `MessageBoxImage` enums — these are WPF-specific and do not exist in Avalonia.
  **Do not use** `MessageBoxManager` or any community package (see Pre-Iteration-Notes).
  Instead, define custom replacement enums in `cp2_avalonia/Common/MessageBoxEnums.cs`:
  ```csharp
  public enum MBResult { None, OK, Cancel, Yes, No }
  public enum MBButton { OK, OKCancel, YesNo, YesNoCancel }
  public enum MBIcon   { None, Info, Warning, Error, Question }
  ```
  Replace all `MessageBoxResult` → `MBResult`, `MessageBoxButton` → `MBButton`,
  `MessageBoxImage` → `MBIcon` throughout `MessageBoxQuery` and its callers. The
  `Monitor.Wait`/`Pulse` synchronization pattern itself is pure .NET and ports unchanged.
  The actual message-box **display** (currently `MessageBox.Show()`) will be replaced
  with a custom Avalonia dialog in the UI thread callback — this is wired later when the
  first action using `MessageBoxQuery` is fully functional (Iteration 5+), but the enum
  types and `MessageBoxQuery` plumbing must be correct now.

  **Critical — temporary stub for `MessageBoxQuery` display:** Until the real Avalonia
  message-box dialog is wired (Iteration 5), the UI-thread callback that calls
  `MessageBox.Show()` must be replaced with a **default-answer stub** that immediately
  returns `MBResult.OK` (or `MBResult.Yes` for `YesNo` prompts) and calls
  `Monitor.Pulse()` to unblock the background worker. If no stub is provided, any code
  path that triggers `MessageBoxQuery.AskUser()` (e.g., opening a read-only file that
  prompts for confirmation) will **permanently block** the background worker thread via
  infinite `Monitor.Wait()`. The stub ensures forward progress:
  ```csharp
  // Temporary stub until Iteration 5 wires the real dialog:
  mResult = (mButtons == MBButton.YesNo || mButtons == MBButton.YesNoCancel)
      ? MBResult.Yes : MBResult.OK;
  lock (mLockObj) { Monitor.Pulse(mLockObj); }
  ```
  `OverwriteQuery` uses `CallbackFacts` and is platform-neutral.

The AXAML conversion is straightforward:
- Change namespace to `https://github.com/avaloniaui`
- `Visibility` → `IsVisible`
- `BooleanToVisibilityConverter` → direct `IsVisible` binding
- No routed commands needed (it has a simple Cancel button)

### Step 3: Port `cp2_avalonia/Actions/OpenProgress.cs`

Read `cp2_wpf/Actions/OpenProgress.cs`. This wraps the file-open operation in a
`BackgroundWorker` with progress reporting.

Port with these changes:
- Namespace: `cp2_avalonia.Actions`
- Replace any `System.Windows.*` references with Avalonia equivalents.
  `BackgroundWorker` works fine in Avalonia (it's part of `System.ComponentModel`).
- Replace `Dispatcher.Invoke` with `Dispatcher.UIThread.InvokeAsync`

### Step 4: Port `cp2_avalonia/ArchiveTreeItem.cs`

Read `cp2_wpf/ArchiveTreeItem.cs` in full. Port with these changes:

1. Namespace: `cp2_avalonia`
2. Replace `using System.Windows; using System.Windows.Controls;` with Avalonia equivalents
3. The `StatusIcon` and `ReadOnlyIcon` properties are WPF `ControlTemplate` references.
   Since Iteration 2 ports all icons as `DrawingImage` resources, change these to `IImage?`:
   ```csharp
   public IImage? StatusIcon { get; set; }
   public IImage? ReadOnlyIcon { get; set; }
   ```
   In the constructor, replace the WPF `FindResource` + `ControlTemplate` cast with:
   ```csharp
   StatusIcon = Application.Current!.FindResource("icon_StatusWarning") as IImage;
   // etc. for icon_StatusInvalid, icon_StatusError, icon_StatusNoNoColor
   ```
4. Replace `using cp2_wpf.WPFCommon` with `using cp2_avalonia.Common`
5. The `BringItemIntoView()` method uses WPF-specific `VirtualizingStackPanel` and
   `ItemContainerGenerator`. This needs rethinking for Avalonia — Avalonia's `TreeView`
   does not expose container generators the same way. Stub it with a `// TODO` and revisit
   once the tree is functional. The `SelectBestFrom(TreeView, ...)` and
   `SelectItem(MainWindow, ...)` methods also take WPF control references — update
   parameter types to the Avalonia equivalents.
6. The rest is pure C# data model logic with `INotifyPropertyChanged` — should port cleanly.

### Step 5: Port `cp2_avalonia/DirectoryTreeItem.cs`

Read `cp2_wpf/DirectoryTreeItem.cs` in full. Similar changes to
`ArchiveTreeItem.cs`:

1. Namespace: `cp2_avalonia`
2. Replace WPF `using` statements
3. Simple `INotifyPropertyChanged` data model; minimal WPF dependencies.
4. Like `ArchiveTreeItem`, the `BringItemIntoView()` method uses WPF-specific
   `VirtualizingStackPanel` — stub with `// TODO` for now. `SelectItemByEntry()` takes
   a `MainWindow` reference — update to the Avalonia `MainWindow` type.

### Step 6: Begin Porting `MainController.cs`

This is the biggest file in the project (~2,934 lines). In this iteration, port only the
portions needed for opening and closing files:

1. Create `cp2_avalonia/MainController.cs`
2. Read `cp2_wpf/MainController.cs` and copy the class declaration, fields, and constructor.
3. Port these key methods:
   - Constructor (initializer, sets up `AppHook`, `DebugMessageLog`, settings loading)
   - `WindowLoaded()` — initialization after window is shown
   - `WindowClosing()` — cleanup on exit
   - `OpenWorkFile()` and `DoOpenWorkFile()` — open file handling.
     The WPF `OpenWorkFile()` is public; `DoOpenWorkFile(string pathName, bool asReadOnly)`
     is private. The Avalonia `OpenWorkFile()` becomes `async Task` because
     `PlatformUtil.AskFileToOpen()` is async. Show the corrected opening lines explicitly:
     ```csharp
     public async Task OpenWorkFile() {
         string? pathName = await PlatformUtil.AskFileToOpen(mMainWin);
         if (pathName == null) { return; }
         DoOpenWorkFile(pathName, false);
     }
     ```
     All other callers (recent-file buttons, drag-drop) go through `DoOpenWorkFile()`
     directly via `DropOpenWorkFile(string pathName)`, so the `null` check lives only here.
   - `CloseWorkFile()` — close file handling (returns `bool` indicating success)
   - `PopulateArchiveTree()` — called by `DoOpenWorkFile()` after successful load
   - `ArchiveTree_SelectionChanged(ArchiveTreeItem? newSel)` — called from the
     `MainWindow` selection-changed event handler to update the directory tree
     (defined in `MainController_Panels.cs` partial class in WPF)
   - `UpdateTitle()` — called by both open and close paths
   - `UpdateRecentFilesList()` / `UnpackRecentFileList()` — recent files management
   - `ClearEntryCounts()` — called by `CloseWorkFile()`
   - Settings load/save methods (`LoadAppSettings`, `SaveAppSettings`, `ApplyAppSettings`)
4. Replace WPF-specific types:
   - `Microsoft.Win32.OpenFileDialog` → `PlatformUtil.AskFileToOpen()`
   - `System.Windows.MessageBox` → Avalonia equivalent
   - `Dispatcher.Invoke` → `Dispatcher.UIThread.InvokeAsync`
   - `Mouse.OverrideCursor = Cursors.Wait` → set the window cursor:
     `mMainWin.Cursor = new Cursor(StandardCursorType.Wait)` (and reset to `null` in
     `finally`)
5. **Clipboard:** `CloseWorkFile()` calls `Clipboard.Clear()` if the process owns the
   clipboard contents. Avalonia's clipboard API is `TopLevel.Clipboard` — defer the
   clipboard-clear logic to a later iteration with a `// TODO: Avalonia clipboard` comment.
6. Stub out all other methods with `// TODO: port in Iteration N` comments.
7. The controller holds a reference to `MainWindow` (as `mMainWin`). Keep this pattern.
8. **Settings methods (`LoadAppSettings`, `SaveAppSettings`, `ApplyAppSettings`):** These
   reference many `mMainWin.xxxProperty = ...` calls for UI updates.  In Iteration 3, many
   of those properties don't exist yet.  Stub the missing property assignments with
   `// TODO: port when property is added in Iteration N` comments.

**Where to construct `MainController`:** In `MainWindow.axaml.cs`, declare
`private MainController mMainCtrl;` and instantiate it in the constructor **after**
`InitializeComponent()` (so named AXAML elements are available):
```csharp
public MainWindow() {
    // Initialize commands before InitializeComponent (AXAML bindings need them).
    // ... command initialization ...

    InitializeComponent();
    DataContext = this;

    mMainCtrl = new MainController(this);
}
```

### Step 7: Port `DebugMessageLog.cs`

Read `cp2_wpf/DebugMessageLog.cs`. This implements the `CommonUtil.IAppHook` debug logging.
It has minimal WPF dependencies but **does** contain a WPF Dispatcher pattern that must be
converted. Replace the WPF thread-check/dispatch pattern:

```csharp
// WPF (remove):
if (Thread.CurrentThread == Application.Current.Dispatcher.Thread) {
    raiseEvent(this, e);
} else {
    Application.Current.Dispatcher.Invoke(new Action(() => { raiseEvent(this, e); }));
}

// Avalonia (replace with):
if (Dispatcher.UIThread.CheckAccess()) {
    raiseEvent(this, e);
} else {
    Dispatcher.UIThread.InvokeAsync(() => raiseEvent(this, e));
}
```

Add `using Avalonia.Threading;` and remove any `System.Windows` usings. The rest of the
file (the `IAppHook` interface methods, string formatting, etc.) ports with namespace
change only.

### Step 8: Update `MainWindow.axaml` with Tree Panels

Replace the placeholder content in `MainWindow.axaml` with the triptych layout (matching
the WPF `MainWindow.xaml` lines 455-630):

```xml
<!-- Main content area: must come last in DockPanel for LastChildFill -->
<Grid Name="mainGrid">

    <!-- Launch panel: shown when no file is open -->
    <Grid Name="launchPanel" IsVisible="{Binding LaunchPanelVisible}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="4*"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="4*"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Grid.Column="1" HorizontalAlignment="Center">
            <TextBlock Text="CiderPress II" FontSize="36" FontWeight="Bold"
                       HorizontalAlignment="Center"/>
            <TextBlock Text="{Binding ProgramVersionString,
                          StringFormat=Version {0}}"
                       FontSize="24" HorizontalAlignment="Center"/>
        </StackPanel>

        <ScrollViewer Grid.Column="1" Grid.Row="1"
                      HorizontalAlignment="Center" VerticalAlignment="Center">
            <StackPanel>
                <Grid Width="300" Height="50" Margin="10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="10"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Button Grid.Column="0" Content="Create new disk image"
                            Command="{Binding NewDiskImageCommand}"/>
                    <Button Grid.Column="2" Content="Create new file archive"
                            Command="{Binding NewFileArchiveCommand}"/>
                </Grid>
                <Button Content="Open file" Width="300" Height="50" Margin="10"
                        Command="{Binding OpenCommand}"
                        HorizontalContentAlignment="Center"/>

                <!-- Recent files (visibility controlled by bindings) -->
                <Button Width="300" Height="50" Margin="10"
                        IsVisible="{Binding ShowRecentFile1}"
                        Command="{Binding RecentFile1Command}"
                        ToolTip.Tip="{Binding RecentFilePath1}">
                    <StackPanel>
                        <TextBlock HorizontalAlignment="Center">Recent file #1</TextBlock>
                        <TextBlock Text="{Binding RecentFileName1}"
                                   HorizontalAlignment="Center"/>
                    </StackPanel>
                </Button>
                <Button Width="300" Height="50" Margin="10"
                        IsVisible="{Binding ShowRecentFile2}"
                        Command="{Binding RecentFile2Command}"
                        ToolTip.Tip="{Binding RecentFilePath2}">
                    <StackPanel>
                        <TextBlock HorizontalAlignment="Center">Recent file #2</TextBlock>
                        <TextBlock Text="{Binding RecentFileName2}"
                                   HorizontalAlignment="Center"/>
                    </StackPanel>
                </Button>
            </StackPanel>
        </ScrollViewer>
    </Grid>

    <!-- Main triptych panel: shown when a file is open -->
    <Grid Name="mainTriptychPanel" IsVisible="{Binding MainPanelVisible}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" MinWidth="100"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*" MinWidth="150"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*" MinWidth="100"/>
        </Grid.ColumnDefinitions>

        <GridSplitter Grid.Column="1" Width="4"/>

        <!-- Left panel: archive tree + directory tree -->
        <Grid Grid.Column="0">
            <Grid.RowDefinitions>
                <RowDefinition Height="*" MinHeight="100"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="2*" MinHeight="100"/>
            </Grid.RowDefinitions>

            <!-- Archive tree -->
            <Grid Grid.Row="0" Margin="4,0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <TextBlock Grid.Row="0" Text="Archive Contents" Margin="0,0,0,8"
                           HorizontalAlignment="Center"/>
                <TreeView Grid.Row="1" Name="archiveTree"
                          ItemsSource="{Binding ArchiveTreeRoot}"
                          SelectionChanged="ArchiveTree_SelectionChanged">
                    <TreeView.ItemTemplate>
                        <TreeDataTemplate ItemsSource="{Binding Items}">
                            <StackPanel Orientation="Horizontal">
                                <Image Source="{Binding StatusIcon}"
                                       IsVisible="{Binding StatusIcon,
                                           Converter={x:Static ObjectConverters.IsNotNull}}"
                                       Width="16" Height="16" Margin="0,0,2,0"/>
                                <Image Source="{Binding ReadOnlyIcon}"
                                       IsVisible="{Binding ReadOnlyIcon,
                                           Converter={x:Static ObjectConverters.IsNotNull}}"
                                       Width="16" Height="16" Margin="0,0,2,0"/>
                                <TextBlock Text="{Binding TypeStr}"
                                           Foreground="Green"/>
                                <TextBlock Text="{Binding Name}" Padding="6,0,2,0"/>
                            </StackPanel>
                        </TreeDataTemplate>
                    </TreeView.ItemTemplate>
                </TreeView>
            </Grid>

            <GridSplitter Grid.Row="1" Height="4"/>

            <!-- Directory tree -->
            <Grid Grid.Row="2" Margin="4,0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <TextBlock Grid.Row="0" Text="Directory" Margin="0,0,0,8"
                           HorizontalAlignment="Center"/>
                <TreeView Grid.Row="1" Name="directoryTree"
                          ItemsSource="{Binding DirectoryTreeRoot}">
                    <TreeView.ItemTemplate>
                        <TreeDataTemplate ItemsSource="{Binding Items}">
                            <TextBlock Text="{Binding Name}"/>
                        </TreeDataTemplate>
                    </TreeView.ItemTemplate>
                </TreeView>
            </Grid>
        </Grid>

        <!-- Center panel: file list (Iteration 4) -->
        <Grid Grid.Column="2">
            <TextBlock Text="(file list - Iteration 4)"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Foreground="Gray"/>
        </Grid>

        <!-- Right splitter -->
        <GridSplitter Grid.Column="3" Width="4"/>

        <!-- Right panel: info panel (Iteration 7+) -->
        <Grid Grid.Column="4" MinWidth="100">
            <TextBlock Text="(info panel - later iteration)"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Foreground="Gray"/>
        </Grid>
    </Grid>
</Grid>
```

**Key Avalonia differences:**
- WPF `HierarchicalDataTemplate` → Avalonia `TreeDataTemplate`
- WPF `Visibility` enum → Avalonia `IsVisible` boolean
- The WPF archive tree uses `ContentControl` + `ControlTemplate` for `StatusIcon` and
  `ReadOnlyIcon`. The Avalonia version uses `<Image Source="{Binding StatusIcon}"/>` with
  `IImage?` properties and `ObjectConverters.IsNotNull` for null-hiding.
- WPF `SelectedItemChanged` event → Avalonia `SelectionChanged` event on `TreeView`
- The WPF TreeViews use `BasedOn="{StaticResource wideTreeViewItemStyle}"` on their
  `ItemContainerStyle`. This is **intentionally omitted** here — the style was stubbed
  with a named key in Iteration 2 and merged via `ResourceInclude`. Do **not** add
  `ItemContainerStyle` or `BasedOn` to these TreeViews. The wide-selection styling will
  be applied via the implicit `TreeViewItem` style from the resource dictionary, which
  targets all `TreeViewItem` instances automatically. If your Iteration 2 style uses an
  explicit `x:Key`, apply it by adding `Styles` include to the TreeView or by making
  the style keyless (selector-based) so it applies globally.

### Step 9: Add Binding Properties for Panel Visibility

In `MainWindow.axaml.cs`:
```csharp
private bool mLaunchPanelVisible = true;
public bool LaunchPanelVisible {
    get => mLaunchPanelVisible;
    set { mLaunchPanelVisible = value; OnPropertyChanged(); }
}

private bool mMainPanelVisible = false;
public bool MainPanelVisible {
    get => mMainPanelVisible;
    set { mMainPanelVisible = value; OnPropertyChanged(); }
}

public string ProgramVersionString =>
    AppCommon.GlobalAppVersion.AppVersion.ToString();

public ObservableCollection<ArchiveTreeItem> ArchiveTreeRoot { get; } = new();
public ObservableCollection<DirectoryTreeItem> DirectoryTreeRoot { get; } = new();

// Recent files (mirror WPF MainWindow.xaml.cs pattern)
public bool ShowRecentFile1 => !string.IsNullOrEmpty(mRecentFileName1);
public string RecentFileName1 { get => mRecentFileName1;
    set { mRecentFileName1 = value; OnPropertyChanged();
          OnPropertyChanged(nameof(ShowRecentFile1)); } }
public string RecentFilePath1 { get => mRecentFilePath1;
    set { mRecentFilePath1 = value; OnPropertyChanged(); } }
private string mRecentFileName1 = string.Empty;
private string mRecentFilePath1 = string.Empty;
// ... repeat for RecentFile2
```

Also expose `ClearTreesAndLists()` so the controller can call it:
```csharp
internal void ClearTreesAndLists() {
    ArchiveTreeRoot.Clear();
    DirectoryTreeRoot.Clear();
    // FileList.Clear();  // added in Iteration 4
}
```

### Step 10: Wire the Open Command

Replace the `NotImplemented("Open")` stub for `OpenCommand`:

```csharp
OpenCommand = new RelayCommand(async () => {
    await mMainCtrl.OpenWorkFile();
});
```

Wire the close command similarly:
```csharp
CloseCommand = new RelayCommand(
    () => mMainCtrl.CloseWorkFile(),
    () => mMainCtrl.IsFileOpen);
```

### Step 11: Implement Simple Window Placement

Create `cp2_avalonia/Common/WindowPlacement.cs` to replace the Win32
`Get/SetWindowPlacement` approach. The WPF version uses P/Invoke + XML serialization;
this is a complete rewrite using JSON. Note: existing WPF settings stored under
`MAIN_WINDOW_PLACEMENT` contain XML — the first run after migration will fail
deserialization and fall back to default window position (handled by the `catch` block).

```csharp
using System.Text.Json;

public static class WindowPlacement {
    public static string Save(Window window) {
        // When maximized, window.Position and Width/Height reflect the maximized
        // dimensions, not the normal (restored) bounds. We need to save the normal
        // bounds separately so that restoring to Normal state gives the right size.
        // Avalonia doesn't expose the "restore bounds" directly, so we track
        // normal-state position/size via attached properties or a static cache.
        // Simple approach: temporarily restore, read bounds, re-maximize.
        // Cleaner approach: cache normal bounds on PositionChanged/SizeChanged
        // events (only when WindowState == Normal) and save those here.
        //
        // For initial implementation, use a static dictionary keyed by window:
        //   static Dictionary<Window, (PixelPoint Pos, double W, double H)> sNormalBounds
        // Update it from window.PositionChanged and window.Resized events when
        // WindowState == Normal. Then use the cached values here when maximized.

        double w, h;
        int x, y;
        if (window.WindowState == WindowState.Maximized &&
                sNormalBounds.TryGetValue(window, out var nb)) {
            x = nb.Pos.X;
            y = nb.Pos.Y;
            w = nb.W;
            h = nb.H;
        } else {
            x = window.Position.X;
            y = window.Position.Y;
            w = window.Width;
            h = window.Height;
        }

        var data = new {
            X = x, Y = y, Width = w, Height = h,
            State = window.WindowState.ToString()
        };
        return JsonSerializer.Serialize(data);
    }

    public static void Restore(Window window, string json) {
        try {
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            window.Position = new PixelPoint(
                data.GetProperty("X").GetInt32(),
                data.GetProperty("Y").GetInt32());
            window.Width = data.GetProperty("Width").GetDouble();
            window.Height = data.GetProperty("Height").GetDouble();
            if (Enum.TryParse<WindowState>(
                    data.GetProperty("State").GetString(), out var state)) {
                window.WindowState = state;
            }
        } catch { /* ignore invalid placement data */ }
    }

    // Cache of normal-state bounds, updated from window events.
    private static readonly Dictionary<Window, (PixelPoint Pos, double W, double H)>
        sNormalBounds = new();

    /// <summary>
    /// Call from MainWindow constructor to start tracking normal bounds.
    /// </summary>
    public static void TrackNormalBounds(Window window) {
        void UpdateCache(Window w) {
            if (w.WindowState == WindowState.Normal) {
                sNormalBounds[w] = (w.Position, w.Width, w.Height);
            }
        }
        window.PositionChanged += (s, e) => UpdateCache(window);
        window.Resized += (size) => UpdateCache(window);
        window.Closed += (s, e) => sNormalBounds.Remove(window);
    }
}
```

### Step 12: Wire Archive Tree Selection

Handle archive tree node selection to update the directory tree:

```csharp
// In MainWindow.axaml.cs or controller:
private void ArchiveTree_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
    if (archiveTree.SelectedItem is ArchiveTreeItem item) {
        mMainCtrl.ArchiveTree_SelectionChanged(item);
    }
}
```

In `MainWindow.axaml`:
```xml
<TreeView ... SelectionChanged="ArchiveTree_SelectionChanged">
```

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Common/PlatformUtil.cs` |
| **Create** | `cp2_avalonia/Common/WorkProgress.axaml` |
| **Create** | `cp2_avalonia/Common/WorkProgress.axaml.cs` |
| **Create** | `cp2_avalonia/Common/WindowPlacement.cs` |
| **Create** | `cp2_avalonia/Common/MessageBoxEnums.cs` |
| **Create** | `cp2_avalonia/Actions/OpenProgress.cs` |
| **Create** | `cp2_avalonia/ArchiveTreeItem.cs` |
| **Create** | `cp2_avalonia/DirectoryTreeItem.cs` |
| **Create** | `cp2_avalonia/MainController.cs` (partial — open/close only) |
| **Create** | `cp2_avalonia/DebugMessageLog.cs` |
| **Modify** | `cp2_avalonia/MainWindow.axaml` (tree panels, launch panel, triptych layout) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (tree binding, panel visibility, controller) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Launch panel shows "CiderPress II" with version, Open/Create buttons, and Recent Files
- [ ] File → Open shows a file picker dialog
- [ ] Opening a ProDOS `.po` disk image populates the archive tree
- [ ] Archive tree shows the disk/partition/filesystem hierarchy with status/read-only icons
- [ ] Selecting a filesystem node in archive tree populates the directory tree
- [ ] File → Close returns to the launch panel
- [ ] Opening a `.shk` archive shows the archive structure
- [ ] Window position and size are saved/restored between sessions
- [ ] Progress dialog shows during file open (for large files)
- [ ] Wait cursor displays during file open operation
- [ ] No crashes when opening invalid or corrupted files (graceful error handling)
- [ ] Window title updates to show filename (and *READONLY* if applicable)
