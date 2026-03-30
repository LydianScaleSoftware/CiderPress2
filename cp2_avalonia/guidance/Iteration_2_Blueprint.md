# Iteration 2 Blueprint: Toolbar & Status Bar

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Add the icon toolbar and status bar to match the WPF layout. Port the vector icon
resource dictionaries. The toolbar buttons are wired to the same `ICommand` properties
created in Iteration 1.

---

## Prerequisites

- Iteration 1 is complete: menu bar with all commands is working.
- Read `cp2_wpf/MainWindow.xaml` lines 395-445 for the WPF toolbar layout.
- Read `cp2_wpf/Res/Icons.xaml` for all vector icon definitions.
- Read `cp2_wpf/Res/TreeViewItemStyle.xaml` for the TreeView style.

---

## Step-by-Step Instructions

### Step 1: Port `cp2_wpf/Res/Icons.xaml` → `cp2_avalonia/Res/Icons.axaml`

Read `cp2_wpf/Res/Icons.xaml` in full. This file contains:
- A `DisableFade` style that reduces opacity when a control is disabled
- 14 `ControlTemplate` entries that define 16×16 vector icons using WPF
  `DrawingBrush`/`DrawingGroup`/`GeometryDrawing` inside `Rectangle.Fill`

**All 14 icons must be ported in this iteration**, even though only 6 appear in the
toolbar. The remaining 8 are used by `ArchiveTreeItem.cs`, `FileListItem.cs`, and
`FileSelector.xaml` in later iterations and must be present as resources when those
files are ported.

**Toolbar icons (6):** `icon_F1Help`, `icon_ListView`, `icon_MeasureTree`,
`icon_StatusInformationOutlineNoColor`, `icon_ClearSort`, `icon_Upload`

**Non-toolbar icons (8):** `icon_StatusOK`, `icon_StatusInvalid`, `icon_StatusWarning`,
`icon_StatusError`, `icon_StatusNoNoColor` (used by `ArchiveTreeItem` for archive-node
overlay icons), `icon_DateTimePicker`, `icon_Comment` (used by `FileListItem` for
status/note icons), `icon_Refresh` (used by `FileSelector`)

Convert this to Avalonia AXAML using the **`DrawingImage` approach** (not the WPF
`ControlTemplate` + `Rectangle` + `DrawingBrush` pattern, which requires `TargetType`
adjustments and inner-namespace workarounds in Avalonia):

1. **Change root element namespaces:**
   ```xml
   <ResourceDictionary xmlns="https://github.com/avaloniaui"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
   ```

2. **Convert the `DisableFade` style:** WPF uses `Style.Triggers`. Since we are using
   `DrawingImage` with `<Image>` elements (not `Viewbox`), the disabled-fade style must
   target `Image` inside disabled buttons:
   ```xml
   <Style Selector="Button:disabled Image">
       <Setter Property="Opacity" Value="0.25"/>
   </Style>
   ```
   This fades the icon image when the parent button is disabled. No `Classes` attribute
   is needed on individual `Image` elements.

   **Important — style placement:** This keyless selector style must **not** go in
   `Icons.axaml`. A `ResourceDictionary` (loaded via `ResourceInclude`) stores only keyed
   resources — it cannot contain free-standing, auto-applying selector styles. Keyless
   styles only auto-apply when they live in an `Application.Styles` collection or a
   `Styles` root loaded via `StyleInclude`. Place this style in `App.axaml` inside
   `<Application.Styles>` (after the existing `<FluentTheme/>` entry). `Icons.axaml`
   should remain a pure `ResourceDictionary` of keyed `DrawingImage` resources.

3. **Convert each WPF icon to a `DrawingImage` resource.** Extract the `DrawingGroup`
   from each WPF `ControlTemplate`'s `Rectangle.Fill > DrawingBrush.Drawing` and wrap it
   directly in a `DrawingImage`:
   ```xml
   <DrawingImage x:Key="icon_F1Help">
       <DrawingImage.Drawing>
           <DrawingGroup>
               <!-- GeometryDrawing elements copied from the WPF icon -->
           </DrawingGroup>
       </DrawingImage.Drawing>
   </DrawingImage>
   ```
   - Drop the `ControlTemplate`, `Viewbox`, `Rectangle`, and `DrawingBrush` wrappers.
   - Drop inner `xmlns` re-declarations (these are WPF-specific; all namespaces must be
     on the root `<ResourceDictionary>`).
   - Drop `System:Double x:Key="cls-1"` resources from inside templates. If an icon uses
     an opacity value, inline it on the `Brush` element inside `<GeometryDrawing.Brush>`,
     **not** on `GeometryDrawing` itself (which has no `Opacity` attribute):
     ```xml
     <GeometryDrawing.Brush>
         <SolidColorBrush Color="#RRGGBB" Opacity="0.35"/>
     </GeometryDrawing.Brush>
     ```
   - **Audit each icon for WPF system-color `DynamicResource` references.** WPF icons
     may use brushes like `{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}`.
     Avalonia has no `SystemColors` — those dynamic resource keys will silently resolve to
     `null` (transparent brush), causing blank/invisible icon geometry. Replace each
     occurrence with an appropriate Avalonia theme resource key (e.g.,
     `{DynamicResource ThemeForegroundBrush}` for `ControlTextBrushKey`) or a hardcoded
     fallback color. If an icon uses only hardcoded hex colors, no changes are needed.

4. **Usage pattern:** In toolbar buttons and later iterations, reference icons as:
   ```xml
   <Image Source="{StaticResource icon_F1Help}" Width="16" Height="16"/>
   ```
   Do **not** use `<ContentControl Template="..."/>` — that is the `ControlTemplate`
   path, which we are not using.

   **Note on icon scaling:** `Image.Stretch` defaults to `Uniform` in Avalonia. If the
   icon's `DrawingGroup` geometry uses a coordinate space other than 0–16 (e.g., 0–100),
   the icon will be scaled to fit the 16×16 `Image` element. This is usually correct.
   If an icon appears at the wrong size, check the drawing's geometry bounds and adjust
   `Width`/`Height` or the `DrawingGroup.Transform` as needed.

5. **Preserve the license comment** at the top about the Visual Studio 2022 Image Library.

### Step 2: Port `cp2_wpf/Res/TreeViewItemStyle.xaml` → `cp2_avalonia/Res/TreeViewItemStyle.axaml`

Read `cp2_wpf/Res/TreeViewItemStyle.xaml` in full. This is a complex WPF `ControlTemplate`
for `TreeViewItem` that makes items stretch full width.

**Avalonia difference:** Avalonia's `TreeViewItem` already stretches horizontally by default
in most themes. You may not need this file at all with the Fluent theme.

**Recommended approach:**
1. First, skip this file and test how the TreeView looks in Iteration 3 without any
   custom style.
2. If the TreeView items don't stretch properly, then create a minimal Avalonia style:
   ```xml
   <Style Selector="TreeViewItem">
       <Setter Property="HorizontalAlignment" Value="Stretch"/>
   </Style>
   ```
3. Include this in `App.axaml` via `Application.Styles` or as a merged dictionary.

For this iteration, create the file as a stub with a named placeholder style so the
`wideTreeViewItemStyle` key exists for Iteration 3 reference:
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- The WPF TreeViewItemStyle provides full-width stretch for TreeViewItems.
         Avalonia's Fluent theme handles this by default. If TreeViewItems don't
         stretch properly in Iteration 3, add custom styles here. -->
    <Style x:Key="wideTreeViewItemStyle" Selector="TreeViewItem">
        <!-- Placeholder: key must exist so BasedOn references in MainWindow compile. -->
    </Style>
</ResourceDictionary>
```

**Note on keyed styles in Avalonia:** A `Style` with `x:Key` in a `ResourceDictionary`
can be looked up via `{StaticResource}` but will **not** auto-apply to matching controls.
To make the style actually apply, it must be added to a `Styles` collection (e.g.,
`MainWindow.Styles` or `Application.Styles`), or loaded via `<StyleInclude>`. Placing it
in `Window.Resources` via `ResourceInclude` makes it available for lookup but not for
automatic selector matching. This is fine for a stub; if real styling is needed in
Iteration 3, the style will need to be moved or duplicated into a `Styles` collection.

**Important:** This file is not auto-discovered. Add a `ResourceInclude` for it in
`MainWindow.axaml`'s resource dictionary (matching the WPF pattern where it is merged
in `MainWindow.xaml`):
```xml
<Window.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="avares://CiderPress2/Res/TreeViewItemStyle.axaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Window.Resources>
```

### Step 3: Wire Icons into `App.axaml`

The `ResourceInclude` for `Res/Icons.axaml` is already active in `App.axaml` (it was added in
Iteration 0 pointing at an empty stub). No changes to `App.axaml` are needed — the icons
will be available automatically now that the file has content.

### Step 4: Build the Toolbar in `MainWindow.axaml`

In the WPF project, the toolbar is a `ToolBarTray > ToolBar > Button` structure. Avalonia
has no built-in `ToolBar`/`ToolBarTray`. Use a styled `StackPanel`:

Insert this between the `</Menu>` and the main content area in the `DockPanel`:

```xml
<!-- Toolbar: replaces WPF ToolBarTray/ToolBar -->
<Border DockPanel.Dock="Top" BorderBrush="LightGray" BorderThickness="0,0,0,1"
        Padding="4,2">
    <StackPanel Orientation="Horizontal" Height="28">
        <!-- View mode buttons.
             No explicit IsEnabled needed — RelayCommand.CanExecute controls
             the enabled/disabled state automatically for all toolbar buttons. -->
        <Button Command="{Binding ShowFullListCommand}"
                ToolTip.Tip="Show full file list"
                BorderBrush="{Binding FullListBorderBrush}">
            <Image Source="{StaticResource icon_ListView}" Width="16" Height="16"/>
        </Button>
        <Button Command="{Binding ShowDirListCommand}"
                ToolTip.Tip="Show contents of single directory"
                BorderBrush="{Binding DirListBorderBrush}">
            <Image Source="{StaticResource icon_MeasureTree}" Width="16" Height="16"/>
        </Button>
        <Button Command="{Binding ShowInfoCommand}"
                ToolTip.Tip="Show information (toggle: Ctrl+I)"
                BorderBrush="{Binding InfoBorderBrush}">
            <Image Source="{StaticResource icon_StatusInformationOutlineNoColor}"
                   Width="16" Height="16"/>
        </Button>

        <Border Width="1" Background="LightGray" Margin="4,2"/>

        <Button Command="{Binding ResetSortCommand}"
                ToolTip.Tip="Reset the sort order in the file list">
            <Image Source="{StaticResource icon_ClearSort}" Width="16" Height="16"/>
        </Button>
        <Button Command="{Binding NavToParentCommand}"
                ToolTip.Tip="Move to parent">
            <Image Source="{StaticResource icon_Upload}" Width="16" Height="16"/>
        </Button>

        <Border Width="1" Background="LightGray" Margin="4,2"/>

        <!-- Drag & copy mode radio buttons -->
        <TextBlock Text="Drag &amp; Copy mode:" Margin="4,5,0,0"
                   ToolTip.Tip="Configure behavior when dragging and copying files in or out"/>
        <RadioButton Content="Add/Extract" GroupName="aeix" Margin="4,0,0,0"
                     IsChecked="{Binding IsChecked_AddExtract}"
                     ToolTip.Tip="When dragging or pasting files, perform add/extract operations."/>
        <RadioButton Content="Import/Export" GroupName="aeix" Margin="4,0,4,0"
                     IsChecked="{Binding IsChecked_ImportExport}"
                     ToolTip.Tip="When dragging or pasting files, perform import/export operations."/>
        <!-- Note: WPF sets BorderBrush on these RadioButtons to
             {DynamicResource {x:Static SystemColors.MenuHighlightBrushKey}}.
             Avalonia has no SystemColors — the default Fluent theme styling
             is sufficient, so this binding is intentionally dropped. -->

        <Border Width="1" Background="LightGray" Margin="4,2"/>

        <Button Command="{Binding HelpCommand}"
                ToolTip.Tip="Help - open manual in browser (F1)">
            <Image Source="{StaticResource icon_F1Help}" Width="16" Height="16"/>
        </Button>
    </StackPanel>
</Border>
```

**Notes:**
- WPF `ToolTip="..."` → Avalonia `ToolTip.Tip="..."`
- The icon reference syntax depends on how icons were ported in Step 1. The toolbar
  AXAML above uses `<Image Source="{StaticResource icon_...}" Width="16" Height="16"/>`,
  which is the `DrawingImage` approach specified in Step 1.
- **No explicit `IsEnabled` bindings** on toolbar buttons. Unlike the WPF original
  (which mixes `IsEnabled` bindings with `CanExecute` handlers), all Avalonia toolbar
  buttons rely solely on `RelayCommand.CanExecute`. When a command's `canExecute`
  delegate returns `false`, the button is automatically grayed out.
- Add binding properties for toolbar state (`FullListBorderBrush`, `DirListBorderBrush`,
  `InfoBorderBrush`, `IsChecked_AddExtract`, `IsChecked_ImportExport`). For now, use
  default values.
- Use `<Border>` instead of `<Separator>` for vertical dividers. Avalonia's `Separator`
  is only styled inside `Menu`/`ListBox`; in a `StackPanel` it renders incorrectly.

### Step 5: Build the Status Bar in `MainWindow.axaml`

The WPF project uses a `StatusBar` at the bottom of the `DockPanel`. Avalonia has no
built-in `StatusBar`. Use a `Border` + `Grid`:

Insert this after the toolbar section, before the main content:

```xml
<!-- Status bar -->
<Border DockPanel.Dock="Bottom" BorderBrush="LightGray" BorderThickness="0,1,0,0"
        Padding="4,2">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="Ready"/>
        <TextBlock Grid.Column="1" HorizontalAlignment="Center"
                   Text="{Binding CenterStatusText, FallbackValue=}"/>
        <TextBlock Grid.Column="2" Name="statusRightText"
                   Text="{Binding RightStatusText, FallbackValue=}"/>
    </Grid>
</Border>
```

Add the `CenterStatusText` and `RightStatusText` properties to `MainWindow.axaml.cs`:
```csharp
private string mCenterStatusText = string.Empty;
public string CenterStatusText {
    get => mCenterStatusText;
    set { mCenterStatusText = value; OnPropertyChanged(); }
}

private string mRightStatusText = string.Empty;
public string RightStatusText {
    get => mRightStatusText;
    set { mRightStatusText = value; OnPropertyChanged(); }
}
```

The `RightStatusText` property is a placeholder — it will be populated by `SetEntryCounts`
in Iteration 4 (showing "N files, M dirs, X free").

### Step 6: Add Toolbar Binding Properties

Add these stub properties to `MainWindow.axaml.cs` for toolbar state:

```csharp
// Drag & Copy mode
private bool mIsChecked_AddExtract = true;
public bool IsChecked_AddExtract {
    get => mIsChecked_AddExtract;
    set { mIsChecked_AddExtract = value; OnPropertyChanged(); }
}

private bool mIsChecked_ImportExport = false;
public bool IsChecked_ImportExport {
    get => mIsChecked_ImportExport;
    set { mIsChecked_ImportExport = value; OnPropertyChanged(); }
}
```

The `FullListBorderBrush`, `DirListBorderBrush`, and `InfoBorderBrush` properties
highlight the active view mode button. Add them as `IBrush` properties (not `Brush` —
Avalonia uses the interface) defaulting to `Brushes.Transparent`. The highlight color
is `Brushes.Green` (matching the WPF original):

```csharp
using Avalonia.Media;

// TODO (Iteration 15): Replace with a theme-aware accent brush for dark mode support,
// e.g. Application.Current.Resources["SystemAccentColor"] or a DynamicResource.
private static readonly IBrush ToolbarHighlightBrush = Brushes.Green;
private static readonly IBrush ToolbarNohiBrush = Brushes.Transparent;

private IBrush mFullListBorderBrush = ToolbarNohiBrush;
public IBrush FullListBorderBrush {
    get => mFullListBorderBrush;
    set { mFullListBorderBrush = value; OnPropertyChanged(); }
}
private IBrush mDirListBorderBrush = ToolbarNohiBrush;
public IBrush DirListBorderBrush {
    get => mDirListBorderBrush;
    set { mDirListBorderBrush = value; OnPropertyChanged(); }
}
private IBrush mInfoBorderBrush = ToolbarNohiBrush;
public IBrush InfoBorderBrush {
    get => mInfoBorderBrush;
    set { mInfoBorderBrush = value; OnPropertyChanged(); }
}
```

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Modify** | `cp2_avalonia/Res/Icons.axaml` (populate stub from Iteration 0) |
| **Create** | `cp2_avalonia/Res/TreeViewItemStyle.axaml` (stub or minimal) |
| **Modify** | `cp2_avalonia/MainWindow.axaml` (toolbar + status bar) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (toolbar properties, status text) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Toolbar appears below the menu bar with icon buttons
- [ ] Icons render correctly (not blank/broken)
- [ ] Toolbar buttons are connected to the correct commands (click → "Not Implemented")
- [ ] Help toolbar button opens browser
- [ ] Status bar appears at the bottom showing "Ready"
- [ ] Drag & Copy mode radio buttons are visible and toggleable
- [ ] Disabled toolbar buttons appear faded (via `DisableFade` style + `RelayCommand.CanExecute`)
- [ ] ToolTips appear on hover
- [ ] All 14 icons are defined in `Icons.axaml` (not just the 6 toolbar icons)
- [ ] Vertical dividers render correctly between toolbar button groups
