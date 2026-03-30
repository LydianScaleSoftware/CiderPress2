# Iteration 1 Blueprint: Menu Bar & Stub Commands

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Implement the full menu bar matching the WPF layout. Every menu item is wired to a command
— either a stub that shows "Not Implemented" or a real handler (for simple items like Exit
and About). Keyboard shortcuts for the most important commands are also wired.

---

## Prerequisites

- Iteration 0 is complete: the project compiles and shows a blank window.
- Read `cp2_wpf/MainWindow.xaml` lines 1-310 to see all command definitions and menu items.
- Read `cp2_wpf/MainWindow.xaml.cs` to see the `CanExecute` and `Executed` handler patterns.

---

## Step-by-Step Instructions

### Step 1: Create `cp2_avalonia/Common/RelayCommand.cs`

Implement a simple `ICommand` that the entire project will use. Since we are not using
ReactiveUI or CommunityToolkit.Mvvm, we need our own minimal implementation:

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
using System.Windows.Input;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Simple ICommand implementation for binding commands in AXAML.
    /// </summary>
    public class RelayCommand : ICommand {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) {
            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute != null ? _ => canExecute() : null) {
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

Include the Apache 2.0 license header (see Pre-Iteration-Notes).

### Step 2: Port `cp2_avalonia/AppSettings.cs`

Read `cp2_wpf/AppSettings.cs` in full. Copy it to `cp2_avalonia/AppSettings.cs` and change
only the namespace from `cp2_wpf` to `cp2_avalonia`. This file contains only string
constants and a `SettingsHolder` instance — no WPF types. It should require no other changes.

### Step 3: Define All Commands on MainWindow

Edit `cp2_avalonia/MainWindow.axaml.cs` to add `ICommand` properties for every command
defined in the WPF project. The WPF `MainWindow.xaml` defines these `RoutedUICommand`
entries (with their keyboard shortcuts where applicable):

**File Menu:**
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `NewDiskImageCmd` | New _Disk Image... | Ctrl+N |
| `NewFileArchiveCmd` | _New File Archive... | — |
| `Open` (built-in) | _Open | Ctrl+O |
| `OpenPhysicalDriveCmd` | Open Physical Drive... | — |
| `CloseCmd` | _Close | Ctrl+W |
| `RecentFileCmd1` | (Recent File 1) | Ctrl+Shift+1 |
| `RecentFileCmd2` | (Recent File 2) | Ctrl+Shift+2 |
| `RecentFileCmd3` | (Recent File 3) | Ctrl+Shift+3 |
| `RecentFileCmd4` | (Recent File 4) | Ctrl+Shift+4 |
| `RecentFileCmd5` | (Recent File 5) | Ctrl+Shift+5 |
| `RecentFileCmd6` | (Recent File 6) | Ctrl+Shift+6 |

Define `RecentFile1Command` through `RecentFile6Command` as 6 separate `ICommand`
properties, each initialized with `() => NotImplemented("Recent File N")` as the stub
handler. These will be wired to dynamic menu items in Iteration 15.
| `ExitCmd` | E_xit | Alt+F4 |

**Edit Menu:**
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `Copy` (built-in) | _Copy | Ctrl+C |
| `Paste` (built-in) | _Paste | Ctrl+V |
| `Find` (built-in) | _Find | Ctrl+F |
| `SelectAll` (built-in) | Select _All | Ctrl+A |
| `EditAppSettingsCmd` | Settings... | — |

**Actions Menu:**
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `ViewFilesCmd` | _View Files | Enter |
| `AddFilesCmd` | _Add Files... | Ctrl+Shift+A |
| `ImportFilesCmd` | Import Files... | — |
| `ExtractFilesCmd` | _Extract Files... | Ctrl+E |
| `ExportFilesCmd` | E_xport Files... | — |
| `DeleteFilesCmd` | _Delete Files | Delete |
| `TestFilesCmd` | Test Files | — |
| `EditAttributesCmd` | Rename / Edit Attributes... | Alt+Enter |
| `CreateDirectoryCmd` | Create Directory... | Ctrl+Shift+N |
| `EditDirAttributesCmd` | Edit Directory Attributes... | — |
| `EditSectorsCmd` | Edit Sectors... | — |
| `EditBlocksCmd` | Edit Blocks... | — |
| `EditBlocksCPMCmd` | Edit Blocks (CP/M)... | — |
| `SaveAsDiskImageCmd` | Save As Disk Image... | — |
| `ReplacePartitionCmd` | Replace Partition Contents... | — |
| `ScanForBadBlocksCmd` | Scan for Bad Blocks | — |
| `ScanForSubVolCmd` | Scan for Sub-Volumes | — |
| `DefragmentCmd` | Defragment Filesystem | — |
| `CloseSubTreeCmd` | Close File Source | Ctrl+Shift+W |

**View Menu:**
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `ShowFullListCmd` | Show Full List | — |
| `ShowDirListCmd` | Show Directory List | — |
| `ShowInfoCmd` | Show Information | — |

**Navigate Menu:**
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `NavToParentDirCmd` | Go To Parent Directory | — |
| `NavToParentCmd` | Go To Parent | Alt+Up |

**Help Menu:**
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `Help` (built-in) | Help | F1 |
| `AboutCmd` | _About CiderPress II | — |

**Debug Menu** (visible only when `ShowDebugMenu` is true):
| Command Key | Menu Text | Shortcut |
|---|---|---|
| `Debug_DiskArcLibTestCmd` | DiskArc Library Tests... | Ctrl+Shift+T |
| `Debug_FileConvLibTestCmd` | FileConv Library Tests... | — |
| `Debug_BulkCompressTestCmd` | Bulk Compression Test... | — |
| `Debug_ShowSystemInfoCmd` | System Info... | — |
| `Debug_ShowDebugLogCmd` | Show Debug Log | — (checkable) |
| `Debug_ShowDropTargetCmd` | Show Drop/Paste Target | — (checkable) |
| `Debug_ConvertANICmd` | Convert ANI to GIF... | — |

**Toolbar-only commands** (no menu item — used only in the toolbar added in Iteration 2):

| Command Key | Toolbar Text | Shortcut |
|---|---|---|
| `ResetSortCmd` | Reset Sorting | — |
| `ToggleInfoCmd` | Toggle Information | Ctrl+I |

For every command above, add an `ICommand` property on `MainWindow`:

```csharp
// Example pattern for each command:
public ICommand NewDiskImageCommand { get; }
public ICommand ExitCommand { get; }
// ... etc.
```

In the constructor, initialize each command with a `RelayCommand`. For now, most commands
call `NotImplemented(...)`. The `ExitCommand` should actually work:

```csharp
public MainWindow() {
    // Initialize commands before InitializeComponent (AXAML bindings need them).
    ExitCommand = new RelayCommand(() => Close());
    AboutCommand = new RelayCommand(() => NotImplemented("About"));
    NewDiskImageCommand = new RelayCommand(() => NotImplemented("New Disk Image"));
    EditAppSettingsCommand = new RelayCommand(() => NotImplemented("Settings"));
    // ... etc. for all commands

    InitializeComponent();
    DataContext = this;  // Enable {Binding} to find ICommand properties
}
```

The window class must implement `INotifyPropertyChanged` (copy the pattern from
`cp2_wpf/MainWindow.xaml.cs` lines 48-60).

### Step 4: Implement `NotImplemented()` Helper

Add to `MainWindow.axaml.cs`:

```csharp
private async void NotImplemented(string featureName) {
    var dialog = new Window {
        Title = "Not Implemented",
        Width = 350, Height = 150,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new StackPanel {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = {
                new TextBlock {
                    Text = $"'{featureName}' has not been ported yet.",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(20)
                }
            }
        }
    };
    await dialog.ShowDialog(this);
}
```

### Step 5: Build the Menu Structure in `MainWindow.axaml`

Replace the placeholder `TextBlock` content in `MainWindow.axaml` with the full menu
structure. Use a `DockPanel` as the root layout (same as WPF):

```xml
<DockPanel>
    <Menu DockPanel.Dock="Top">
        <MenuItem Header="_File">
            <MenuItem Header="New _Disk Image..." Command="{Binding NewDiskImageCommand}"
                      HotKey="Ctrl+N"/>
            <MenuItem Header="_New File Archive..." Command="{Binding NewFileArchiveCommand}"/>
            <MenuItem Header="_Open" Command="{Binding OpenCommand}" HotKey="Ctrl+O"/>
            <MenuItem Header="Open Physical Drive..."
                      Command="{Binding OpenPhysicalDriveCommand}"/>
            <MenuItem Header="_Close" Command="{Binding CloseCommand}" HotKey="Ctrl+W"/>
            <Separator/>
            <MenuItem Name="recentFilesMenu" Header="Recent Files">
                <MenuItem Header="(none)"/>
            </MenuItem>
            <Separator/>
            <MenuItem Header="E_xit" Command="{Binding ExitCommand}"
                      InputGesture="Alt+F4"/>
        </MenuItem>

        <MenuItem Header="_Edit">
            <MenuItem Header="_Copy" Command="{Binding CopyCommand}" HotKey="Ctrl+C"/>
            <MenuItem Header="_Paste" Command="{Binding PasteCommand}" HotKey="Ctrl+V"/>
            <Separator/>
            <MenuItem Header="_Find" Command="{Binding FindCommand}" HotKey="Ctrl+F"/>
            <Separator/>
            <MenuItem Header="Select _All" Command="{Binding SelectAllCommand}"
                      HotKey="Ctrl+A"/>
            <Separator/>
            <MenuItem Header="Settings..." Command="{Binding EditAppSettingsCommand}"/>
        </MenuItem>

        <MenuItem Header="_Actions">
            <MenuItem Header="_View Files" Command="{Binding ViewFilesCommand}"
                      InputGesture="Enter"/>
            <MenuItem Header="_Add Files..." Command="{Binding AddFilesCommand}"
                      InputGesture="Ctrl+Shift+A"/>
            <MenuItem Header="Import Files..." Command="{Binding ImportFilesCommand}"/>
            <MenuItem Header="_Extract Files..." Command="{Binding ExtractFilesCommand}"
                      InputGesture="Ctrl+E"/>
            <MenuItem Header="E_xport Files..." Command="{Binding ExportFilesCommand}"/>
            <MenuItem Header="_Delete Files" Command="{Binding DeleteFilesCommand}"
                      InputGesture="Delete"/>
            <MenuItem Header="Test Files" Command="{Binding TestFilesCommand}"/>
            <MenuItem Header="Rename / Edit Attributes..."
                      Command="{Binding EditAttributesCommand}"
                      InputGesture="Alt+Enter"/>
            <MenuItem Header="Create Directory..."
                      Command="{Binding CreateDirectoryCommand}"
                      InputGesture="Ctrl+Shift+N"/>
            <Separator/>
            <MenuItem Header="Edit Directory Attributes..."
                      Command="{Binding EditDirAttributesCommand}"/>
            <Separator/>
            <MenuItem Header="Edit Sectors..." Command="{Binding EditSectorsCommand}"/>
            <MenuItem Header="Edit Blocks..." Command="{Binding EditBlocksCommand}"/>
            <MenuItem Header="Edit Blocks (CP/M)..."
                      Command="{Binding EditBlocksCPMCommand}"/>
            <MenuItem Header="Save As Disk Image..."
                      Command="{Binding SaveAsDiskImageCommand}"/>
            <MenuItem Header="Replace Partition Contents..."
                      Command="{Binding ReplacePartitionCommand}"/>
            <MenuItem Header="Scan for Bad Blocks"
                      Command="{Binding ScanForBadBlocksCommand}"/>
            <Separator/>
            <MenuItem Header="Scan for Sub-Volumes"
                      Command="{Binding ScanForSubVolCommand}"/>
            <MenuItem Header="Defragment Filesystem"
                      Command="{Binding DefragmentCommand}"/>
            <MenuItem Header="Close File Source"
                      Command="{Binding CloseSubTreeCommand}"
                      InputGesture="Ctrl+Shift+W"/>
        </MenuItem>

        <MenuItem Header="_View">
            <MenuItem Header="Show Full List"
                      Command="{Binding ShowFullListCommand}"/>
            <MenuItem Header="Show Directory List"
                      Command="{Binding ShowDirListCommand}"/>
            <MenuItem Header="Show Information"
                      Command="{Binding ShowInfoCommand}"/>
        </MenuItem>

        <MenuItem Header="_Navigate">
            <MenuItem Header="Go To Parent Directory"
                      Command="{Binding NavToParentDirCommand}"/>
            <MenuItem Header="Go To Parent"
                      Command="{Binding NavToParentCommand}"
                      InputGesture="Alt+Up"/>
        </MenuItem>

        <MenuItem Header="_Help">
            <MenuItem Header="Help" Command="{Binding HelpCommand}"
                      HotKey="F1"/>
            <MenuItem Header="_About CiderPress II"
                      Command="{Binding AboutCommand}"/>
        </MenuItem>

        <!-- Debug menu: visible only when ShowDebugMenu is true -->
        <MenuItem Header="_DEBUG"
                  IsVisible="{Binding ShowDebugMenu}">
            <MenuItem Header="DiskArc Library Tests..."
                      Command="{Binding Debug_DiskArcLibTestCommand}"
                      InputGesture="Ctrl+Shift+T"/>
            <MenuItem Header="FileConv Library Tests..."
                      Command="{Binding Debug_FileConvLibTestCommand}"/>
            <MenuItem Header="Bulk Compression Test..."
                      Command="{Binding Debug_BulkCompressTestCommand}"/>
            <Separator/>
            <MenuItem Header="System Info..."
                      Command="{Binding Debug_ShowSystemInfoCommand}"/>
            <MenuItem Header="Show Debug Log"
                      Command="{Binding Debug_ShowDebugLogCommand}"
                      IsChecked="{Binding IsDebugLogVisible}"
                      ToggleType="CheckBox"/>
            <MenuItem Header="Show Drop/Paste Target"
                      Command="{Binding Debug_ShowDropTargetCommand}"
                      IsChecked="{Binding IsDropTargetVisible}"
                      ToggleType="CheckBox"/>

**Note on `ToggleType="CheckBox"` binding:** The `IsChecked` binding above is one-way by
default. When the user clicks a checkable menu item, the visual check state toggles but
the bound property does not update unless the command handler explicitly sets it. The
command handlers (implemented in Iteration 5 for debug log and Iteration 13 for
drop target) must toggle the property and call `OnPropertyChanged()`.
            <Separator/>
            <MenuItem Header="Convert ANI to GIF..."
                      Command="{Binding Debug_ConvertANICommand}"/>
        </MenuItem>
    </Menu>

    <!-- Placeholder for toolbar (Iteration 2) and main content -->
    <TextBlock Text="CiderPress II"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               FontSize="36" FontWeight="Bold"/>
</DockPanel>
```

**Key AXAML differences from WPF:**
- `Command="{Binding XxxCommand}"` instead of `Command="{StaticResource XxxCmd}"`.
- `HotKey="Ctrl+N"` on `MenuItem` both registers the shortcut **and** displays it.
  Use `HotKey` only for items where the menu item is the primary activation point
  (e.g., File → Open, Edit → Copy).
- `InputGesture="Ctrl+E"` on `MenuItem` is **display-only** — it shows the shortcut text
  in the menu but does not register a hotkey. Use this for items whose shortcuts are
  registered via `Window.KeyBindings` (Step 6).  Do **not** use both `HotKey` and
  `InputGesture` on the same item, and do not duplicate a `HotKey` in `Window.KeyBindings`.
- `IsVisible="{Binding ShowDebugMenu}"` instead of
  `Visibility="{Binding ShowDebugMenu, Converter={StaticResource BoolToVis}}"`.
- No `CanExecute` attribute on menu items — the `RelayCommand`'s `CanExecute` delegate
  controls graying out automatically.

**Note on `HotKey="Ctrl+O"` global intercept:** When `OpenCommand` is bound via `HotKey`,
Ctrl+O is intercepted at the window level even when focus is inside a child control (e.g.,
a `TextBox` in a dialog). This is harmless while the command is a stub, but when the real
open-file logic is implemented (Iteration 3), keep in mind that Ctrl+O will not reach
text-editing controls in modal dialogs. This is acceptable because modal dialogs use
`ShowDialog`, which creates a separate input scope.

### Step 6: Add Keyboard Bindings for Non-Menu Shortcuts

Add to `MainWindow.axaml` (inside the `<Window>` element, before `<DockPanel>`):

```xml
<Window.KeyBindings>
    <KeyBinding Gesture="Enter" Command="{Binding ViewFilesCommand}"/>
    <KeyBinding Gesture="Delete" Command="{Binding DeleteFilesCommand}"/>
    <KeyBinding Gesture="Alt+Up" Command="{Binding NavToParentCommand}"/>
    <KeyBinding Gesture="Ctrl+I" Command="{Binding ToggleInfoCommand}"/>
    <KeyBinding Gesture="Ctrl+Shift+A" Command="{Binding AddFilesCommand}"/>
    <KeyBinding Gesture="Ctrl+E" Command="{Binding ExtractFilesCommand}"/>
    <KeyBinding Gesture="Alt+Enter" Command="{Binding EditAttributesCommand}"/>
    <KeyBinding Gesture="Ctrl+Shift+N" Command="{Binding CreateDirectoryCommand}"/>
    <KeyBinding Gesture="Ctrl+Shift+W" Command="{Binding CloseSubTreeCommand}"/>
    <KeyBinding Gesture="Ctrl+Shift+T" Command="{Binding Debug_DiskArcLibTestCommand}"/>
    <KeyBinding Gesture="Ctrl+Shift+1" Command="{Binding RecentFile1Command}"/>
    <KeyBinding Gesture="Ctrl+Shift+2" Command="{Binding RecentFile2Command}"/>
    <KeyBinding Gesture="Ctrl+Shift+3" Command="{Binding RecentFile3Command}"/>
    <KeyBinding Gesture="Ctrl+Shift+4" Command="{Binding RecentFile4Command}"/>
    <KeyBinding Gesture="Ctrl+Shift+5" Command="{Binding RecentFile5Command}"/>
    <KeyBinding Gesture="Ctrl+Shift+6" Command="{Binding RecentFile6Command}"/>
</Window.KeyBindings>
```

**Note:** Shortcuts like `Ctrl+O`, `Ctrl+W`, `Ctrl+N`, `Ctrl+C`, `Ctrl+V`, `Ctrl+A`,
`Ctrl+F`, `F1` are already specified via `HotKey` on their `MenuItem` entries. Duplicating
them in `KeyBindings` is not required (and may cause conflicts). Only add `KeyBinding` for
shortcuts that don't have a `HotKey` on their menu item.

### Step 7: Add `ShowDebugMenu`, `IsDebugLogVisible`, and `IsDropTargetVisible` Properties

In `MainWindow.axaml.cs`, add bindable properties that control DEBUG menu visibility and
the two checkable menu item states. Default `ShowDebugMenu` to `true` during development;
default the other two to `false` (they will be set by commands in Iteration 5 and
Iteration 13 respectively):

```csharp
private bool mShowDebugMenu = true;  // TODO: read from settings or #if DEBUG
public bool ShowDebugMenu {
    get => mShowDebugMenu;
    set { mShowDebugMenu = value; OnPropertyChanged(); }
}

private bool mIsDebugLogVisible;
public bool IsDebugLogVisible {
    get => mIsDebugLogVisible;
    set { mIsDebugLogVisible = value; OnPropertyChanged(); }
}

private bool mIsDropTargetVisible;
public bool IsDropTargetVisible {
    get => mIsDropTargetVisible;
    set { mIsDropTargetVisible = value; OnPropertyChanged(); }
}
```

### Step 8: Implement the Exit Command (Real Handler)

The Exit command should work immediately:

```csharp
ExitCommand = new RelayCommand(() => Close());
```

### Step 9: Implement the Help Command (Real Handler)

Open the manual in the user's default browser. The URL is
`https://ciderpress2.com/gui-manual/` (same as the WPF version's `HelpHelp()`):

```csharp
HelpCommand = new RelayCommand(() => {
    try {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = "https://ciderpress2.com/gui-manual/",
            UseShellExecute = true
        });
    } catch (Exception) {
        // Ignore failures (e.g., no browser available)
    }
});
```

### Step 10: Create `cp2_avalonia/AboutBox.axaml/.cs` — First Real Dialog

Now that the menu bar is wired, create the About Box so there is at least one benign menu
item (Help → About CiderPress II) that opens a real dialog rather than the "Not
Implemented" stub.

Read `cp2_wpf/AboutBox.xaml/.cs`. This is a simple dialog showing:
- Application name and version (from `GlobalAppVersion`)
- Copyright notice
- Link to the website (`https://ciderpress2.com/`)
- OS / runtime information (see notes on `IsAdministrator()` below)
- Debug-mode indicator (`IsVisible` boolean replacing WPF `Visibility` enum)
- Scrollable text area displaying the contents of `LegalStuff.txt`
- An OK button to close

Porting tasks:
1. Convert XAML → AXAML (namespace change, `Visibility` → `IsVisible`,
   `ResizeMode="NoResize"` → `CanResize="False"`).
2. **`DebugMessageVisibility` → `bool IsDebugBuild`:** The WPF property returns
   `Visibility.Visible`/`Visibility.Collapsed`. In Avalonia, replace with a `bool`
   property and bind via `IsVisible="{Binding IsDebugBuild}"`.
3. Replace `Hyperlink` in `TextBlock` — Avalonia doesn't have WPF's `Hyperlink`. Use a
   clickable `TextBlock` with `CommonUtil.ShellCommand.OpenUrl()` (already cross-platform
   and used elsewhere in the codebase):
   ```xml
   <TextBlock Text="https://ciderpress2.com/" Foreground="Blue"
              Cursor="Hand" TextDecorations="Underline"
              Tapped="WebsiteLink_Tapped"/>
   ```
   In code-behind:
   ```csharp
   private void WebsiteLink_Tapped(object? sender, TappedEventArgs e) {
       CommonUtil.ShellCommand.OpenUrl("https://ciderpress2.com/");
   }
   ```
4. **Load `LegalStuff.txt`** into a read-only `TextBox` (or Avalonia `TextBlock` inside a
   `ScrollViewer`). The WPF version uses `WinUtil.GetRuntimeDataDir()` to locate the file.
   Since `PlatformUtil.cs` doesn't arrive until Iteration 3, add a minimal inline helper:
   ```csharp
   private static string GetRuntimeDataDir() {
       string baseDir = AppContext.BaseDirectory;
       // In dev builds the base dir ends with e.g. cp2_avalonia/bin/Debug/net8.0/.
       // Walk up four levels to reach the solution root.
       string marker = Path.Combine(baseDir, "LegalStuff.txt");
       if (File.Exists(marker)) return baseDir;
       for (int i = 0; i < 4; i++) {
           baseDir = Path.GetDirectoryName(baseDir) ?? baseDir;
           marker = Path.Combine(baseDir, "LegalStuff.txt");
           if (File.Exists(marker)) return baseDir;
       }
       return AppContext.BaseDirectory; // fallback
   }
   ```
5. **`IsAdministrator()` in `RuntimeInfo`:** The WPF version appends " (Admin)" using
   `WinUtil.IsAdministrator()`, which calls Windows-only `WindowsPrincipal`. For now,
   omit the admin check (or use `Environment.IsPrivilegedProcess` on .NET 8+). A full
   cross-platform version will come with `PlatformUtil.cs` in Iteration 3.
6. Read the WPF version for the exact text content and layout.
7. Wire the About command in `MainWindow.axaml.cs` to open the real dialog (replacing
   the "Not Implemented" stub set up in Step 3):
   ```csharp
   AboutCommand = new RelayCommand(async () => {
       var dialog = new AboutBox();
       await dialog.ShowDialog(this);
   });
   ```

**Important — parameterless constructor:** The WPF `AboutBox` has a `Window owner`
parameter and sets `Owner = owner` in the constructor. Do **not** replicate this in
Avalonia. Use a parameterless constructor. In Avalonia, `Owner` is established by the
`ShowDialog(this)` call — do not add a `Window owner` parameter and do not set
`this.Owner` inside the constructor.

This gives you a concrete, testable dialog that exercises the AXAML → code-behind → modal
dialog → close lifecycle before any complex features are built.

**Deferred:** The WPF menus wire `SubmenuOpened` events on "Recent Files" and "DEBUG"
(for dynamic population). These are not needed until recent-file tracking is implemented
(Iteration 15) and debug menu checkmark state is wired (Iteration 12).

---

## WPF → Avalonia Command CanExecute Mapping

In the WPF project, `CanExecute` handlers determine when menu items are enabled. The common
patterns are:

| WPF Handler | Condition | For now |
|---|---|---|
| `IsFileOpen` | A disk/archive is open | Return `false` (nothing is open yet) |
| `AreFileEntriesSelected` | Files are selected in the list | Return `false` |
| `CanAddFiles` | File is open + can write | Return `false` |
| `CanCreateDirectory` | Filesystem selected + writable | Return `false` |
| `IsSubTreeSelected` | Sub-tree node selected | Return `false` |
| `IsChunkyDiskOrPartitionSelected` | Block-oriented disk | Return `false` |
| `IsNibbleImageSelected` | Nibble disk image | Return `false` |
| `IsFileSystemSelected` | Filesystem node selected | Return `false` |
| `IsANISelected` | ANI file selected | Return `false` |

Initialize all these commands with `canExecute: () => false` so they appear grayed out.
Only `Exit`, `About`, `Help`, `EditAppSettings`, and the `Debug_*` commands should be
enabled initially (no `canExecute` or always `true`).

**Note on `RelayCommand` signature:** This blueprint's `RelayCommand` (Step 1) includes a
primary `Action<object?>` constructor and a convenience `Action` overload, which is a
superset of the simpler `Action`/`Func<bool>` sketch in Pre-Iteration-Notes §5.4. The
dual-constructor version is intentional — the parameterless overload covers 95% of commands,
while the `object?` variant supports commands that receive a `CommandParameter` (e.g.,
recent-file index). Pre-Iteration-Notes is a design sketch; this blueprint is authoritative.

As future iterations implement features, these `canExecute` delegates will be replaced with
real logic.

**Note on Ctrl+C/V/A and DataGrid interaction (for Iteration 4):** In Avalonia, standard
text-editing controls (`TextBox`, `AvaloniaEdit`) mark their `KeyDown` events as handled,
so `HotKey`-registered Ctrl+C/V/A on the Edit menu items will **not** intercept those
keystrokes inside text fields. However, when focus is on a `DataGrid` row (not in cell-edit
mode), the window-level `HotKey` fires and routes to the menu's Copy/Paste commands — which
is the intended archive-level operation. Iteration 4's file-list `DataGrid` should **not**
add its own Ctrl+C handler; the menu command path handles archive file copying correctly.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Common/RelayCommand.cs` |
| **Create** | `cp2_avalonia/AppSettings.cs` |
| **Create** | `cp2_avalonia/AboutBox.axaml` |
| **Create** | `cp2_avalonia/AboutBox.axaml.cs` |
| **Modify** | `cp2_avalonia/MainWindow.axaml` (menu structure) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (commands, INotifyPropertyChanged) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Application shows a menu bar with File, Edit, Actions, View, Navigate, Help, DEBUG
- [ ] File → Exit closes the window
- [ ] Help → Help opens a URL in the browser (F1 also works)
- [ ] Help → About CiderPress II opens the About dialog with version, copyright, and links
- [ ] About dialog shows LegalStuff.txt content in scrollable text area
- [ ] About dialog links open in the browser when clicked (via `ShellCommand.OpenUrl`)
- [ ] About dialog shows OS/runtime info (no crash on non-Windows)
- [ ] All other menu items show "Not Implemented" dialog when clicked
- [ ] Most Actions menu items are grayed out (canExecute returns false)
- [ ] Keyboard shortcut Ctrl+N triggers "Not Implemented" for New Disk Image
- [ ] Keyboard shortcut Ctrl+W triggers "Not Implemented" for Close (or is grayed out)
- [ ] DEBUG menu is visible (ShowDebugMenu = true)
- [ ] DEBUG "Show Debug Log" and "Show Drop/Paste Target" render as checkable items
- [ ] No `System.Windows.*` namespaces in any `cp2_avalonia/` file
