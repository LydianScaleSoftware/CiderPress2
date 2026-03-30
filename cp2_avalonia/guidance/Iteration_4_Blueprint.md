# Iteration 4 Blueprint: File List Panel

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Selecting a node in the archive tree or directory tree populates the file list in the center
panel. The center panel toggles between a file list `DataGrid` and an info panel (for disk
images, partition maps, etc.). The file list shows file entries in a sortable `DataGrid` with
all 13 columns matching the WPF version, with conditional column visibility based on the
archive type.

---

## Prerequisites

- Iteration 3 is complete: archive/directory trees populate when opening a file.
- Key WPF source files to read:
  - `cp2_wpf/FileListItem.cs` — data model for file list rows
  - `cp2_wpf/MainController_Panels.cs` (~1,137 lines) — panel management, file list
    population, column definitions
  - `cp2_wpf/MainWindow.xaml` lines 630-800 — file list `ListView`+`GridView` declaration
  - `cp2_wpf/MainWindow.xaml.cs` — file list event handlers (double-click, selection, etc.)

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/FileListItem.cs`

Read `cp2_wpf/FileListItem.cs` in full. Port with these changes:

1. Namespace: `cp2_avalonia`
2. Replace `using System.Windows` references with Avalonia equivalents.
3. The WPF class has a `StatusIcon` property (type `ControlTemplate?`) that indicates
   entry status: `sInvalidIcon` (dubious), `sErrorIcon` (damaged), `sCommentIcon`
   (has comment). These are loaded via `FindResource()`. In Avalonia, change
   `StatusIcon` to type `IImage?` and resolve icons with
   `Application.Current!.FindResource("icon_StatusInvalid") as DrawingImage` etc.
   (same pattern as Iteration 3 tree icons). For the initial port, return `null`
   — icons can be added when the icon resources are created.
4. The full property set (matching the WPF source exactly) is: `FileEntry` (type
   `IFileEntry` — used by `ItemComparer`, `FindItemByEntry()`, double-click handler,
   and multiple controller methods), `StatusIcon`, `FileName`, `PathName`, `Type`,
   `AuxType`, `CreateDate`, `ModDate`, `Access`, `DataLength`, `DataSize`, `DataFormat`,
   `RawDataLength`, `RsrcLength`, `RsrcSize`, `RsrcFormat`, `TotalSize`. These are
   pure data — port directly. Also port the private fields used for sorting:
   `mRawDataLen`, `mTotalSize`.
5. Port the `ItemComparer` inner class (implements `IComparer`) with its `ColumnId` enum
   (13 values: StatusIcon, FileName, PathName, Type, Auxtype, ModWhen, DataLen,
   RawDataLen, DataFormat, RsrcLen, RsrcFormat, TotalSize, Access). The constructor
   takes a `DataGridColumn` and `bool isAscending`. Each column has a secondary sort
   key (e.g., Type→AuxType, ModWhen→FileName). Port this directly — it's needed for
   custom sorting in Step 8.
6. Port **both** `FindItemByEntry()` overloads:
   - `FindItemByEntry(ObservableCollection<FileListItem>, IFileEntry)` → returns
     `FileListItem?`
   - `FindItemByEntry(ObservableCollection<FileListItem>, IFileEntry, out int index)`
     → returns `FileListItem?` and the list index (used by delete-then-reselect logic)

   Also port `SelectAndView()` and `SetSelectionFocusByEntry()`.
   `SetSelectionFocusByEntry` uses WPF `DataGrid.SelectRowByIndex()`
   extension — stub with `// TODO: Avalonia DataGrid row selection helper`.
7. The constructor has complex type/auxtype formatting logic (DOS, ProDOS, HFS, MacZip)
   and data/rsrc fork info population. Port directly.

### Step 2: Port `cp2_avalonia/MainController_Panels.cs`

Read `cp2_wpf/MainController_Panels.cs` in full. This is a partial class that extends
`MainController`. Port with these changes:

1. Create `cp2_avalonia/MainController_Panels.cs` as `partial class MainController`.

2. Port all ~20 boolean CanExecute properties. These are **computed properties** (no
   backing field) that read live UI state on each access — do not convert them to stored
   booleans updated by event handlers. Examples from the WPF source:
   ```csharp
   public bool AreFileEntriesSelected =>
       mMainWin.fileListDataGrid.SelectedIndex >= 0;
   public bool IsSingleEntrySelected =>
       mMainWin.fileListDataGrid.SelectedItems.Count == 1;
   public bool CanWrite {
       get {
           ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
           return arcTreeSel != null && !arcTreeSel.WorkTreeNode.IsReadOnly;
       }
   }
   ```
   Port these with the same computed-property pattern for Avalonia. Because Avalonia's
   `RelayCommand` does not have WPF's automatic `CommandManager.InvalidateRequerySuggested()`,
   you must call `RaiseCanExecuteChanged()` on all affected commands after any event
   that might change their result (tree selection, file list selection, open/close).

   The full list: `CurrentWorkObject`, `CanWrite`, `CanEditBlocks`, `CanEditBlocksCPM`,
   `CanEditSectors`, `HasChunks`, `AreFileEntriesSelected`, `IsSingleEntrySelected`,
   `IsANISelected`, `IsDiskImageSelected`, `IsPartitionSelected`,
   `IsDiskOrPartitionSelected`, `IsNibbleImageSelected`, `IsFileSystemSelected`,
   `IsMultiFileItemSelected`, `IsDefragmentableSelected`,
   `IsHierarchicalFileSystemSelected`, `IsSelectedDirRoot`, `IsSelectedArchiveRoot`,
   `IsClosableTreeSelected`. These drive all CanExecute checks across the app.

3. Port `ArchiveTree_SelectionChanged()` — this is the main dispatch method. It
   handles the selected work-tree node differently based on type:
   - `IFileSystem` → populates directory tree, sets boolean properties, calls
     `ConfigureCenterPanel()`, populates file list
   - `IArchive` → populates file list directly, sets boolean properties
   - `IDiskImage` / `IMultiPart` / `Partition` → shows center info panel
     (`ConfigureCenterInfo()`), sets notes/partition/metadata lists
   This method also sets `CurrentWorkObject` and calls `ClearCenterInfo()`.

4. Port `DirectoryTree_SelectionChanged()` — calls `ConfigureCenterPanel()` with
   5 flags (`isInfoOnly`, `isArchive`, `isHierarchic`, `hasRsrc`, `hasRaw`) then
   calls `RefreshDirAndFileList()`.

5. Port `PopulateFileList()` which delegates to three sub-methods based on mode:
   - `PopulateEntriesFromArchive()` — handles MacZip pairs with ADF attribute
     extraction (non-trivial logic: checks `MacZip.HasMacZipHeader()`, reads
     ADF headers, attaches attributes to entries)
   - `PopulateEntriesFromSingleDir()` — single directory listing
   - `PopulateEntriesFromFullDisk()` — recursive full-disk listing
   After population, calls `SetEntryCounts()` and restores previous selection.

6. Port `RefreshDirAndFileList()` — verifies directory tree and file list against
   current data (using `VerifyDirectoryTree` and `VerifyFileList` overloads),
   only repopulates if contents changed. Port all `VerifyFileList` overloads
   (archive mode, single-dir, full-disk recursive).

7. Port `SetEntryCounts()` / `ClearEntryCounts()` — updates status bar with
   "N files, M directories, X free" text. `SetEntryCounts` formats free space
   differently by filesystem type (sector-based for DOS/RDOS/Gutenberg, block-based
   for ProDOS/Pascal, KB for HFS/MFS/CP/M).

8. Port column width save/restore logic.

9. Replace WPF-specific types:
   - `ListView` references → use the `DataGrid` via bindings instead
   - `GridView`/`GridViewColumn` → Avalonia `DataGrid` columns
   - `Dispatcher.Invoke` → `Dispatcher.UIThread.InvokeAsync`
   - `Mouse.OverrideCursor = Cursors.Wait` → Avalonia `Cursor` (per Iteration 3)

### Step 3: Add the DataGrid and Info Panel to `MainWindow.axaml`

The center panel area must toggle between two views: a file list DataGrid and an info
panel (for disk images, partition maps, etc.). Use `IsVisible` bindings to switch.

Replace the placeholder in the center panel (column 2 of the triptych):

```xml
<Grid Grid.Column="2">
    <!-- Center panel option 1: file list DataGrid -->
    <DataGrid Name="fileListDataGrid"
              IsVisible="{Binding ShowCenterFileList}"
              ItemsSource="{Binding FileList}"
              AutoGenerateColumns="False"
              IsReadOnly="True"
              CanUserReorderColumns="False"
              CanUserSortColumns="True"
              SelectionMode="Extended"
              GridLinesVisibility="Vertical"
              DoubleTapped="FileListDataGrid_DoubleTapped"
              SelectionChanged="FileListDataGrid_SelectionChanged"
              Sorting="FileListDataGrid_Sorting">
        <DataGrid.Columns>
            <!-- Col 0: Status icon (dubious/damaged/comment) -->
            <DataGridTemplateColumn Header="?" Width="SizeToCells">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <Image Source="{Binding StatusIcon}" Width="16"/>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>
            <!-- Col 1: Filename (shown in single-dir mode) -->
            <DataGridTemplateColumn Header="Filename"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_FileName}"
                    Width="240">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding FileName}" TextTrimming="CharacterEllipsis"/>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>
            <!-- Col 2: Pathname (shown in full-list/archive mode) -->
            <DataGridTemplateColumn Header="Pathname"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_PathName}"
                    Width="300">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding PathName}" TextTrimming="CharacterEllipsis"/>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>
            <!-- Col 3-5: always visible -->
            <DataGridTextColumn Header="Type" Binding="{Binding Type}" Width="48"/>
            <DataGridTextColumn Header="Auxtype" Binding="{Binding AuxType}" Width="56"/>
            <DataGridTextColumn Header="Mod Date" Binding="{Binding ModDate}" Width="106"/>
            <!-- Col 6: always visible -->
            <DataGridTextColumn Header="Data Len" Binding="{Binding DataLength}" Width="65"/>
            <!-- Col 7: conditional (DOS 3.x raw files) -->
            <DataGridTextColumn Header="Raw Len" Binding="{Binding RawDataLength}"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_RawLen}"
                    Width="65"/>
            <!-- Col 8: conditional (archive format column) -->
            <DataGridTextColumn Header="Data Fmt" Binding="{Binding DataFormat}"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_Format}"
                    Width="54"/>
            <!-- Col 9: conditional (resource fork length) -->
            <DataGridTextColumn Header="Rsrc Len" Binding="{Binding RsrcLength}"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_RsrcLen}"
                    Width="65"/>
            <!-- Col 10: conditional (resource fork format) -->
            <DataGridTextColumn Header="Rsrc Fmt" Binding="{Binding RsrcFormat}"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_RsrcFormat}"
                    Width="54"/>
            <!-- Col 11: conditional (total size for filesystems) -->
            <DataGridTextColumn Header="Total Size" Binding="{Binding TotalSize}"
                    IsVisible="{Binding $parent[DataGrid].DataContext.ShowCol_TotalSize}"
                    Width="76"/>
            <!-- Col 12: always visible -->
            <DataGridTextColumn Header="Access" Binding="{Binding Access}" Width="50"/>
        </DataGrid.Columns>
    </DataGrid>

    <!-- Center panel option 2: info panel (stub — populated later) -->
    <ScrollViewer IsVisible="{Binding ShowCenterInfoPanel}">
        <StackPanel Margin="8">
            <TextBlock Text="{Binding CenterInfoText1}"/>
            <!-- TODO: full info panel with notes, partition list, metadata list -->
        </StackPanel>
    </ScrollViewer>
</Grid>
```

Also add a context menu on the DataGrid (matching the WPF version's 10-item context
menu). This can reference commands that are stubs for now:

```xml
<DataGrid.ContextMenu>
    <ContextMenu>
        <MenuItem Header="View Files" Command="{Binding ViewFilesCommand}"/>
        <MenuItem Header="Edit Attributes" Command="{Binding EditAttributesCommand}"/>
        <MenuItem Header="Extract Files" Command="{Binding ExtractFilesCommand}"/>
        <MenuItem Header="Export Files" Command="{Binding ExportFilesCommand}"/>
        <MenuItem Header="Copy" Command="{Binding CopyCommand}"/>
        <MenuItem Header="Delete Files" Command="{Binding DeleteFilesCommand}"/>
        <MenuItem Header="Test Files" Command="{Binding TestFilesCommand}"/>
        <Separator/>
        <MenuItem Header="Add Files" Command="{Binding AddFilesCommand}"/>
        <MenuItem Header="Import Files" Command="{Binding ImportFilesCommand}"/>
        <MenuItem Header="Create Directory" Command="{Binding CreateDirectoryCommand}"/>
    </ContextMenu>
</DataGrid.ContextMenu>
```

**Notes:**
- The WPF version uses `ListView` + `GridView`. Avalonia doesn't have `GridView` — use
  `DataGrid` from the `Avalonia.Controls.DataGrid` NuGet (already added in Iteration 0).
- All 13 columns match the WPF layout exactly. Conditional columns use `IsVisible`
  bindings. **Important:** Avalonia `DataGridColumn` is not part of the visual tree, so
  the `$parent[DataGrid].DataContext` binding shown above will silently fail (producing
  `null`). The AXAML `IsVisible` bindings on columns are **placeholders only** — they
  document intent but will not work at runtime. The actual column visibility must be
  set in code-behind. In `ConfigureCenterPanel()`, after setting the `ShowCol_*`
  properties, also set column visibility directly using a header-name lookup helper:
  ```csharp
  private void SetColumnVisible(string header, bool visible) {
      var col = fileListDataGrid.Columns.FirstOrDefault(
          c => c.Header?.ToString() == header);
      if (col != null) col.IsVisible = visible;
  }
  ```
  Call it for each conditional column:
  ```csharp
  SetColumnVisible("Filename", ShowCol_FileName);
  SetColumnVisible("Pathname", ShowCol_PathName);
  SetColumnVisible("Raw Len", ShowCol_RawLen);
  SetColumnVisible("Data Fmt", ShowCol_Format);
  SetColumnVisible("Rsrc Len", ShowCol_RsrcLen);
  SetColumnVisible("Rsrc Fmt", ShowCol_RsrcFormat);
  SetColumnVisible("Total Size", ShowCol_TotalSize);
  ```
  The two format columns have distinct headers (`"Data Fmt"` and `"Rsrc Fmt"`) so
  they can be independently controlled via `SetColumnVisible()`. The resource fork
  format visibility uses its own `ShowCol_RsrcFormat` property.
- `DoubleTapped` is Avalonia's equivalent of WPF's `MouseDoubleClick`.
- Filename and Pathname columns use `DataGridTemplateColumn` with `TextTrimming` to
  handle long paths (matching WPF's `CharacterEllipsis`).
- Drag & drop (`AllowDrop`, `Drop`, `PreviewMouseLeftButtonDown`, `PreviewMouseMove`)
  is deferred — add `<!-- TODO: drag & drop support -->` comment.
- DataGrid key override (WPF overrides Ctrl+C, Delete, Enter via `CommandBindings`)
  will need Avalonia `KeyBindings` — add as `// TODO` comment.
- The WPF DataGrid has `ContextMenuOpening="FileListContextMenu_ContextMenuOpening"`
  which refocuses the file list when a context menu opens (needed for keyboard
  navigation after right-click). This is deferred — add a
  `<!-- TODO: ContextMenuOpening refocus handler -->` comment on the ContextMenu.

### Step 4: Add FileList and Center Panel Properties

In `MainWindow.axaml.cs`, add these properties:

```csharp
// File list collection
public ObservableCollection<FileListItem> FileList { get; } = new();

// Selected item accessor (used by controller)
public FileListItem? SelectedFileListItem {
    get => fileListDataGrid.SelectedItem as FileListItem;
    set => fileListDataGrid.SelectedItem = value;
}

// Archive tree selected item (used by ~20 CanExecute properties in controller).
// Avalonia TreeView.SelectedItem returns the data item directly (not a container),
// so the `as ArchiveTreeItem` cast works correctly.
public ArchiveTreeItem? SelectedArchiveTreeItem {
    get => archiveTree.SelectedItem as ArchiveTreeItem;
}

// Center panel toggle: file list vs info panel
public bool ShowCenterFileList { get => !mShowCenterInfo; }
public bool ShowCenterInfoPanel { get => mShowCenterInfo; }
private bool mShowCenterInfo;

// Column visibility (set by ConfigureCenterPanel).
// These MUST use full property bodies with OnPropertyChanged() so that bindings
// (or code-behind visibility updates) react when ConfigureCenterPanel() changes
// them. Auto-properties without notification would leave stale column visibility
// when switching between archive and filesystem modes.
private bool mShowCol_FileName;
public bool ShowCol_FileName {
    get => mShowCol_FileName;
    set { mShowCol_FileName = value; OnPropertyChanged(); }
}
private bool mShowCol_PathName;
public bool ShowCol_PathName {
    get => mShowCol_PathName;
    set { mShowCol_PathName = value; OnPropertyChanged(); }
}
private bool mShowCol_Format;
public bool ShowCol_Format {
    get => mShowCol_Format;
    set { mShowCol_Format = value; OnPropertyChanged(); }
}
private bool mShowCol_RawLen;
public bool ShowCol_RawLen {
    get => mShowCol_RawLen;
    set { mShowCol_RawLen = value; OnPropertyChanged(); }
}
private bool mShowCol_RsrcLen;
public bool ShowCol_RsrcLen {
    get => mShowCol_RsrcLen;
    set { mShowCol_RsrcLen = value; OnPropertyChanged(); }
}
private bool mShowCol_TotalSize;
public bool ShowCol_TotalSize {
    get => mShowCol_TotalSize;
    set { mShowCol_TotalSize = value; OnPropertyChanged(); }
}

private bool mShowCol_RsrcFormat;
public bool ShowCol_RsrcFormat {
    get => mShowCol_RsrcFormat;
    set { mShowCol_RsrcFormat = value; OnPropertyChanged(); }
}

// Center info panel text (stub for now)
private string mCenterInfoText1 = string.Empty;
public string CenterInfoText1 {
    get => mCenterInfoText1;
    set { mCenterInfoText1 = value; OnPropertyChanged(); }
}
```

Also add the panel configuration method (ported from WPF):

```csharp
public void ConfigureCenterPanel(bool isInfoOnly, bool isArchive, bool isHierarchic,
        bool hasRsrc, bool hasRaw) {
    ShowSingleDirFileList = !(isArchive || (isHierarchic && !PreferSingleDirList));
    // ... (port logic from WPF MainWindow.xaml.cs ConfigureCenterPanel)
}
```

And the full-list vs single-directory toggle:

```csharp
public bool ShowSingleDirFileList {
    get => mShowSingleDirFileList;
    set {
        mShowSingleDirFileList = ShowCol_FileName = value;
        ShowCol_PathName = !value;
        // AXAML DataGridColumn.IsVisible bindings don't work (columns are not in
        // the visual tree), so also apply visibility directly in code-behind:
        SetColumnVisible("Filename", value);
        SetColumnVisible("Pathname", !value);
    }
}
private bool mShowSingleDirFileList;

// Sort reset state (set by sorting handler, read by ResetSortCommand CanExecute)
private bool mIsResetSortEnabled;
public bool IsResetSortEnabled {
    get => mIsResetSortEnabled;
    set { mIsResetSortEnabled = value; OnPropertyChanged(); }
}

private bool PreferSingleDirList {
    get => AppSettings.Global.GetBool(AppSettings.FILE_LIST_PREFER_SINGLE, true);
    set => AppSettings.Global.SetBool(AppSettings.FILE_LIST_PREFER_SINGLE, value);
}
```

Add helper methods on MainWindow (called by controller):
- `FileList_ScrollToTop()` — scrolls the DataGrid to the top
- `FileList_SetSelectionFocus()` — sets focus to the selected DataGrid row
  (the WPF version uses an `ItemContainerGenerator.StatusChanged` hack —
  stub with `// TODO` and a simpler `Focus()` call for now)

### Step 5: Implement Double-Click Navigation

Port `HandleFileListDoubleClick()` from `MainController_Panels.cs`. This is a complex
5-step dispatch method — port it faithfully:

1. If a single directory is selected and we're in a filesystem: select the entry in
   the directory tree via `DirectoryTreeItem.SelectItemByEntry()`, set
   `mSwitchFocusToFileList = true` to keep keyboard focus on the file list.
2. If a single entry is already open in the archive tree: find it via
   `ArchiveTreeItem.FindItemByEntry()` then call `SelectBestFrom()`.
3. If a single entry might be an archive/disk image: call
   `mWorkTree.TryCreateSub()` under a wait cursor. If successful, call
   `ArchiveTreeItem.ConstructTree()` + `SelectBestFrom()`.
4. If a directory in a file archive (no filesystem): do nothing.
5. Otherwise (multiple selection, or non-openable file): call `ViewFiles()`.

The event handler on MainWindow:

```csharp
private void FileListDataGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
    // Guard: only fire for data rows, not column headers or empty space.
    // Checking SelectedItem alone is insufficient — a previously-selected row
    // would still be selected when double-clicking the header area.
    if (e.Source is Visual source &&
        source.FindAncestorOfType<DataGridColumnHeader>() != null) {
        return;  // click was on a column header
    }
    if (fileListDataGrid.SelectedItem is FileListItem) {
        mMainCtrl.HandleFileListDoubleClick();
    }
}
```

Note: The WPF version calls `HandleFileListDoubleClick()` with no parameters (it reads
selection from the DataGrid directly). Match that signature.

### Step 6: Implement File List Selection Tracking

Track selection changes so `CanExecute` handlers for file operations can check if files
are selected. In WPF, `CommandManager.InvalidateRequerySuggested()` automatically
re-queries all bound commands when UI state changes. Avalonia has no equivalent —
instead, call `RefreshAllCommandStates()` (which calls `RaiseCanExecuteChanged()` on
each `RelayCommand`) from the selection-changed handler:

```csharp
// In MainWindow.axaml.cs
private void FileListDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
    mMainCtrl.RefreshAllCommandStates();
}
```

Do **not** pass the selection list to the controller — the controller's ~20 CanExecute
properties (Step 2 item 2) read selection state directly from
`mMainWin.fileListDataGrid.SelectedIndex` and `mMainWin.fileListDataGrid.SelectedItems`
on each access, matching the WPF pattern.

### Step 7: Update CanExecute Delegates

Now that files can be selected and tree selection sets ~20 boolean properties (Step 2
item 2), update the `CanExecute` delegates. The WPF CanExecute handlers gate on
combinations of these properties. Key patterns from the WPF source:

- `AreFileEntriesSelected` — `IsFileOpen && ShowCenterFileList && AreFileEntriesSelected`
  Used by: ViewFiles, Extract, Export, Copy, Test
- `CanDeleteFiles` — adds `CanWrite && IsMultiFileItemSelected`
- `CanAddFiles` — `IsFileOpen && CanWrite && IsMultiFileItemSelected && ShowCenterFileList`
- `IsSingleEntrySelected` — for EditAttributes
- `CanCreateDirectory` — `CanWrite && IsHierarchicalFileSystemSelected`
- `IsSubTreeSelected` — `IsClosableTreeSelected` (for CloseSubTree)
- `CanNavToParent` — checks `IsSelectedDirRoot` / `IsSelectedArchiveRoot`
- `CanDefragment` — `CanWrite && IsDefragmentableSelected`
- Disk utility commands: gate on `CanEditBlocks`, `CanEditSectors`, `IsDiskImageSelected`,
  `IsNibbleImageSelected`, `IsFileSystemSelected`, etc.

Note: several CanExecute checks also gate on `ShowCenterFileList` — file commands should
only be enabled when the file list (not info panel) is showing.

Call `RaiseCanExecuteChanged()` on all affected commands whenever the selection changes
or the center panel mode changes.

### Step 8: Implement Column Sorting

Avalonia's built-in DataGrid sorting uses simple property comparison, which won't
handle numeric columns (sizes displayed as formatted strings) or secondary sort keys
correctly. Port the WPF's custom sorting approach:

1. Handle the `DataGrid.Sorting` event (already wired in Step 3):

```csharp
private void FileListDataGrid_Sorting(object? sender, DataGridColumnEventArgs e) {
    var col = e.Column;
    var direction = (col.SortDirection != DataGridSortDirection.Ascending)
        ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending;
    col.SortDirection = direction;
    bool isAscending = direction == DataGridSortDirection.Ascending;

    // Use the ported ItemComparer from FileListItem
    var comparer = new FileListItem.ItemComparer(col, isAscending);

    // Re-sort: create sorted list and repopulate the ObservableCollection.
    // (Avalonia lacks WPF's ListCollectionView.CustomSort, so sort in-place.)
    var sorted = FileList.OrderBy(x => x, comparer).ToList();
    FileList.Clear();
    foreach (var item in sorted) {
        FileList.Add(item);
    }

    IsResetSortEnabled = true;
    e.Handled = true;
}
```

2. `ResetSortCommand` clears sort direction on all columns and repopulates from
   the original unsorted order (saved during `PopulateFileList`):

```csharp
ResetSortCommand = new RelayCommand(
    () => {
        foreach (var col in fileListDataGrid.Columns) {
            col.SortDirection = null;  // clear arrow indicators
        }
        // Repopulate from unsorted source order
        mMainCtrl.PopulateFileList(IFileEntry.NO_ENTRY, false);
        IsResetSortEnabled = false;
    },
    () => mMainCtrl.IsFileOpen && IsResetSortEnabled);
```

Note: this approach causes a full re-render on each sort (N+1 UI events: 1 Clear +
N Add). For archives with thousands of entries this will cause a visible flicker/delay.
**Preferred approach:** Use `DataGridCollectionView` if it supports `CustomSort` or
`SortDescriptions` with a custom `IComparer` — this sorts the view without modifying
the underlying collection and fires a single refresh event (matching WPF's
`ListCollectionView.CustomSort` behavior). If `DataGridCollectionView.CustomSort` is not
available in your Avalonia version, use the Clear/re-Add approach above as a fallback.

**Note on `PopulateFileList` call from ResetSortCommand:** The `mMainCtrl.PopulateFileList(`
`IFileEntry.NO_ENTRY, false)` call is safe here because `PopulateFileList` reads the
current mode (archive vs. filesystem) from the controller's existing state
(`CurrentWorkObject`, selected tree node). It does not require the caller to set mode
first. The `IsFileOpen` guard in `CanExecute` ensures the controller state is valid.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/FileListItem.cs` — full port with StatusIcon, all 14 properties, ItemComparer, utility methods |
| **Create** | `cp2_avalonia/MainController_Panels.cs` — ~20 CanExecute booleans, tree selection handlers, PopulateFileList (3 modes + MacZip), SetEntryCounts, HandleFileListDoubleClick |
| **Modify** | `cp2_avalonia/MainWindow.axaml` — DataGrid (13 columns + conditional visibility) + info panel stub + context menu in center panel |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` — FileList/SelectedFileListItem, center panel toggle, ShowCol_* properties, ConfigureCenterPanel, ShowSingleDirFileList, FileList helper methods, sorting handler |
| **Modify** | `cp2_avalonia/MainController.cs` — wire panel management methods |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Opening a ProDOS disk image shows files in the DataGrid with correct columns
- [ ] All 13 columns present: StatusIcon(?), Filename, Pathname, Type, Auxtype, Mod Date,
      Data Len, Raw Len, Format, Rsrc Len, Format(rsrc), Total Size, Access
- [ ] Conditional columns hidden/shown correctly: Filename vs Pathname toggles,
      Raw Len only for DOS 3.x, Format only for archives, Rsrc Len for resource-fork
      filesystems, Total Size for non-archives
- [ ] Center panel shows info panel (not file list) for disk image / partition map nodes
- [ ] Clicking a directory entry in archive tree updates the file list
- [ ] Clicking a directory in the directory tree updates the file list
- [ ] Full-list vs single-directory mode toggle works (toolbar buttons)
- [ ] Double-clicking a directory in file list navigates into it (selects in dir tree)
- [ ] Double-clicking a file archive entry opens it in the archive tree
- [ ] Column sorting works with correct secondary sort keys
- [ ] Reset Sort command clears sorting and restores original order
- [ ] Multiple file selection works (Ctrl+click, Shift+click)
- [ ] CanExecute handlers correctly enable/disable based on selection + center panel mode
- [ ] Status bar shows "N files, M directories" (and free space for filesystems)
- [ ] Context menu on file list has all 10 items
- [ ] Opening a `.shk` archive shows the correct file entries with MacZip pairs resolved
