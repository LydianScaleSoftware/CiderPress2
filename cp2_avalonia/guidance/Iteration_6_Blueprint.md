# Iteration 6 Blueprint: Extract & Add Files

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Implement the core file operations: extract files from archives/disk images, and add files
to them. This makes the application genuinely usable for its primary purpose.

---

## Prerequisites

- Iteration 5 is complete: basic dialogs working.
- Key WPF source files to read:
  - `cp2_wpf/Actions/ExtractProgress.cs` — implements `WorkProgress.IWorker`
  - `cp2_wpf/Actions/AddProgress.cs` — implements `WorkProgress.IWorker`
  - `cp2_wpf/Actions/ProgressUtil.cs` — shared callback handler
  - `cp2_wpf/Actions/OverwriteQueryDialog.xaml/.cs` (~100+100 lines) — invoked by WorkProgress
  - `cp2_wpf/MainController.cs` — search for `ExtractFiles`, `AddFiles`, `ExportFiles`,
    `ImportFiles`, `HandleExtractExport`, `HandleAddImport`, `AddPaths`, `AddFileDrop`,
    `ConfigureAddOpts`, `CheckPasteDropOkay` methods
  - `cp2_wpf/WPFCommon/FileSelector.xaml/.cs` — custom file/folder selection
    dialog with three modes (`SingleFile`, `SingleFolder`, `FilesAndFolders`). This
    replaced the Win32 `BrowseForFolder` (which is commented out in WPF code)

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/Actions/ProgressUtil.cs`

Read `cp2_wpf/Actions/ProgressUtil.cs`. This contains shared progress-reporting helpers
used by all background operations. Key features:
- Static `HandleCallback()` handles `CallbackFacts.Reasons` (QueryCancel, Progress,
  FileNameExists, PathTooLong, Failure, etc.)
- `PersistentChoices` class tracks Overwrite=All/None across a batch
- `ShowMessage()` / `ShowCancelled()` display messages via `WorkProgress.MessageBoxQuery`
- For overwrite conflicts, creates `WorkProgress.OverwriteQuery` and sends it via
  `bkWorker.ReportProgress(0, query)` — WorkProgress receives it and shows the
  `OverwriteQueryDialog`

Porting changes:
- Namespace: `cp2_avalonia.Actions`
- `BackgroundWorker` is in `System.ComponentModel` and works in Avalonia
- **Critical**: `MessageBoxQuery` constructor takes `MessageBoxButton` and `MessageBoxImage`
  (from `System.Windows`). Replace with the custom enums defined in Iteration 3:
  `MBButton` and `MBIcon` from `cp2_avalonia/Common/MessageBoxEnums.cs`. Also replace
  `MessageBoxResult` with `MBResult`. The `Monitor.Wait/Pulse` synchronization pattern
  itself is unchanged. The actual dialog display happens in `WorkProgress.ProgressChanged`,
  not here.

### Step 2: Port `cp2_avalonia/Actions/ExtractProgress.cs`

Read `cp2_wpf/Actions/ExtractProgress.cs`. This implements `WorkProgress.IWorker` and
runs `ExtractFileWorker` on the BackgroundWorker thread.

Key details from WPF source:
- Constructor takes: `archiveOrFileSystem`, `selectionDir`, `selected` (List<IFileEntry>),
  `outputDir`, `exportSpec`, `appHook`
- Properties: `Preserve`, `AddExportExt`, `EnableMacOSZip`, `StripPaths`, `RawMode`,
  `DefaultSpecs`
- `DoWork()` sets `Environment.CurrentDirectory` to output dir, creates
  `ExtractFileWorker` with a callback delegate that calls `ProgressUtil.HandleCallback`,
  then calls `ExtractFromArchive` or `ExtractFromDisk` depending on type
- `RunWorkerCompleted()` just checks `results is true`
- **No Dispatcher calls** — this class runs entirely on the worker thread

Porting tasks:
1. Namespace: `cp2_avalonia.Actions`
2. No Dispatcher changes needed — this class doesn't touch UI directly
3. The extraction calls into `AppCommon.ExtractFileWorker` which is already cross-platform
4. The folder selection dialog is NOT in this class — it's in `MainController.HandleExtractExport()`
5. Wire the extract command in MainWindow:
   ```csharp
   ExtractFilesCommand = new RelayCommand(
       () => mMainCtrl.ExtractFiles(),
       () => mMainCtrl != null && mMainCtrl.IsFileOpen && ShowCenterFileList &&
            mMainCtrl.AreFileEntriesSelected);
   ```

### Step 3: Port `cp2_avalonia/Actions/AddProgress.cs`

Read `cp2_wpf/Actions/AddProgress.cs`. This implements `WorkProgress.IWorker` and
runs `AddFileWorker` on the BackgroundWorker thread.

Key details from WPF source:
- Constructor takes: `archiveOrFileSystem`, `leafNode` (DiskArcNode), `addSet` (AddFileSet),
  `targetDir`, `appHook`
- Properties: `DoCompress`, `EnableMacOSZip`, `StripPaths`, `RawMode`
- `DoWork()` creates `AddFileWorker` with callback, then calls `AddFilesToArchive` (with
  transaction management: `StartTransaction`→add→`SaveUpdates`→`CancelTransaction` in
  finally) or `AddFilesToDisk` (with deferred error reporting after `SaveUpdates`)
- Catches `ConversionException` for import errors
- `RunWorkerCompleted()` just checks `results is true`
- **No Dispatcher calls** — runs entirely on worker thread

Porting tasks:
1. Namespace: `cp2_avalonia.Actions`
2. No Dispatcher changes needed
3. The file picker is NOT in this class — it's in `MainController.HandleAddImport()`
4. Wire the add command in MainWindow:
   ```csharp
   AddFilesCommand = new RelayCommand(
       () => mMainCtrl.AddFiles(),
       () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
            mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList);
   ```
   **Note:** `CanAddFiles` does not exist as a controller property. The WPF CanExecute
   handler at `MainWindow.xaml.cs` line 436 evaluates the full expression above.

5. Also wire import (same CanExecute as add):
   ```csharp
   ImportFilesCommand = new RelayCommand(
       () => mMainCtrl.ImportFiles(),
       () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
            mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList);
   ```
6. And export (same CanExecute as extract):
   ```csharp
   ExportFilesCommand = new RelayCommand(
       () => mMainCtrl.ExportFiles(),
       () => mMainCtrl != null && mMainCtrl.IsFileOpen && ShowCenterFileList &&
            mMainCtrl.AreFileEntriesSelected);
   ```

### Step 4: Port `cp2_avalonia/Actions/OverwriteQueryDialog.axaml/.cs`

Read `cp2_wpf/Actions/OverwriteQueryDialog.xaml/.cs`. This is a confirmation dialog shown
when extracting/adding files would overwrite existing files.

**Architectural note**: This dialog is NOT invoked directly by ExtractProgress/AddProgress.
The flow is:
1. Worker thread calls `ProgressUtil.HandleCallback()` for `FileNameExists`
2. `HandleCallback` creates a `WorkProgress.OverwriteQuery` and sends it via
   `bkWorker.ReportProgress(0, query)`
3. `WorkProgress.ProgressChanged` (on UI thread) receives it, shows
   `OverwriteQueryDialog` as a child of WorkProgress
4. The result is sent back via `Monitor.Wait/Pulse` synchronization

WPF dialog details:
- Title: "Overwrite Existing File?"
- Layout: Two sections — "Copy and Replace" button with new file info (name/dir/date)
  and "Don't Copy" button with existing file info (name/dir/date)
- "Do this for all conflicts" checkbox (`UseForAll` property)
- Cancel button
- Properties: `Result` (CallbackFacts.Results — Overwrite or Skip),
  `NewFileName`/`NewDirName`/`NewModWhen`, `ExistFileName`/`ExistDirName`/`ExistModWhen`
- Constructor takes `(Window parent, CallbackFacts facts)` and formats file info from
  `PathName.GetFileName`, `PathName.GetDirectoryName`, `TimeStamp.IsValidDate`

Porting tasks:
1. Convert XAML → AXAML (Window → `<Window>` with Avalonia namespaces)
2. Port the layout. The WPF outer Grid has **4 rows**, each containing nested content:
   - Row 0: Intro text ("This file already exists in this directory:")
   - Row 1: Nested 2-column Grid — column 0 has a `Button` with `Grid.RowSpan`
     spanning the text area, column 1 has 3 `TextBlock`s (new file name, dir, date)
     for "Copy and Replace"
   - Row 2: Same nested 2-column Grid structure for "Don't Copy" with existing file info
   - Row 3: `DockPanel` with "Do this for all conflicts" `CheckBox` + Cancel `Button`
   
   Provide the actual AXAML skeleton (or at minimum describe the nested grid + button
   row-span pattern) so the agent reproduces the layout correctly.
3. Return pattern: each button click must call `Close()` with an explicit value so
   `ShowDialog<bool?>` returns the correct result:
   - "Copy and Replace" button → set `Result` property, then `Close(true)`
   - "Don't Copy" button → set `Result` property, then `Close(false)`
   - Window dismissed via X button → returns `null` (treated as Cancel)
   
   **Do not** call bare `Close()` with no argument — that returns `null` for `bool?`,
   and `result == true` in Step 4's `ProgressChanged` will always fail, silently
   skipping every conflicting file even when the user clicked "Copy and Replace."
4. Replace `RoutedEventArgs` → Avalonia equivalent in button click handlers
5. **`WorkProgress.ProgressChanged` must be `async void`** because showing this dialog
   requires `await`. The full pattern:
   ```csharp
   private async void ProgressChanged(object? sender, ProgressChangedEventArgs e) {
       // ... handle progress bar, MessageBoxQuery, etc.
       OverwriteQuery? oq = e.UserState as OverwriteQuery;
       if (oq != null) {
           var dialog = new Actions.OverwriteQueryDialog(oq.Facts);
           bool? result = await dialog.ShowDialog<bool?>(this);
           if (result == true) {
               oq.SetResult(dialog.Result, dialog.UseForAll);
           } else {
               oq.SetResult(CallbackFacts.Results.Cancel, false);
           }
           return;
       }
       // ... similar async pattern for MessageBoxQuery
   }
   ```
   The worker thread is blocked at `Monitor.Wait()` during the `await`, which is
   correct — it waits until `SetResult()` calls `Monitor.Pulse()`.

### Step 5: Implement Extract/Export in `MainController.cs`

Port from `cp2_wpf/MainController.cs`. The WPF structure is:
- `ExtractFiles()` → calls `HandleExtractExport(null)`
- `ExportFiles()` → calls `HandleExtractExport(GetExportSpec())`

So `HandleExtractExport(ConvConfig.FileConvSpec? exportSpec)` is the shared method.
**This method must be `async Task`** because `WorkProgress.ShowDialog<T>()` in Avalonia
is async. If it’s called from a `RelayCommand` delegate (which is `Action`), the
calling lambda must be `async void`. Wrap the body in `try/catch` to prevent unhandled
exceptions from crashing the app (unhandled exceptions in `async void` propagate to
the thread pool and terminate the process):
```csharp
ExtractFilesCommand = new RelayCommand(
    async () => { try { await mMainCtrl.ExtractFiles(); } catch (Exception ex) {
        // Log and show error
    } },
    () => mMainCtrl != null && mMainCtrl.IsFileOpen && ShowCenterFileList &&
         mMainCtrl.AreFileEntriesSelected);
```

**Important:** Apply the same `async () => { try { await ... } catch { ... } }` wrapper
to **all** async command lambdas (Add, Import, Export, not just Extract). Each `async void`
delegate that throws an unhandled exception will terminate the process via the thread pool.

```csharp
// Example for AddFilesCommand — same pattern for ImportFilesCommand, ExportFilesCommand:
AddFilesCommand = new RelayCommand(
    async () => { try { await mMainCtrl.AddFiles(); } catch (Exception ex) {
        // Log and show error
    } },
    () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
         mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList);
```

WPF `HandleExtractExport` flow:
1. `GetFileSelection(omitDir:false, omitOpenArc:false, closeOpenArc:true, oneMeansAll:false,
   ...)` — get selected entries, archiveOrFileSystem, selectionDir
2. If empty selection, show message and return
3. If full-list mode (not single-dir), use volume dir as selectionDir
4. Read `LAST_EXTRACT_DIR` from settings for initial directory
5. Show `FileSelector(mMainWin, FileSelector.SelMode.SingleFolder, initialDir)` — in
   Avalonia, replace with `StorageProvider.OpenFolderPickerAsync`:
   ```csharp
   var topLevel = TopLevel.GetTopLevel(mMainWin);
   var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(
       new FolderPickerOpenOptions {
           Title = "Select destination for " +
               (exportSpec == null ? "extracted" : "exported") + " files",
           AllowMultiple = false
       });
   if (folders.Count == 0) return;
   string outputDir = folders[0].Path.LocalPath;
   ```
6. Save `LAST_EXTRACT_DIR` to settings
7. Create `ExtractProgress` with all settings (Preserve, AddExportExt, EnableMacOSZip,
   StripPaths, RawMode, DefaultSpecs)
8. **Important:** Remove `Owner = owner` from the `WorkProgress` constructor body.
   In Avalonia 11, `Window.Owner` has no public setter — ownership is set by
   `ShowDialog<T>(owner)`. Create `WorkProgress` then show it with `await`:
   ```csharp
   var workDialog = new WorkProgress(prog, false);
   bool? result = await workDialog.ShowDialog<bool?>(mMainWin);
   if (result == true) {
       mMainWin.PostNotification("Extraction successful", true);
   } else {
       mMainWin.PostNotification("Cancelled", false);
   }
   ```
   Do **not** write `workDialog.ShowDialog() == true` (the WPF synchronous pattern) —
   Avalonia’s `ShowDialog<T>()` returns `Task<T>` and must be awaited.

### Step 6: Implement Add/Import in `MainController.cs`

Port from `cp2_wpf/MainController.cs`. The WPF structure is:
- `AddFiles()` → calls `HandleAddImport(null)`
- `ImportFiles()` → calls `HandleAddImport(GetImportSpec())`

So `HandleAddImport(ConvConfig.FileConvSpec? spec)` is the shared method.

**File selection challenge**: The WPF uses `FileSelector(mMainWin,
FileSelector.SelMode.FilesAndFolders, initialDir)` — a custom 944-line dialog allowing
selection of both files AND folders. Avalonia's `OpenFilePickerAsync` only picks files.
Options:
- (a) Use `OpenFolderPickerAsync` to select a folder (then recurse per existing behavior)
- (b) Port `FileSelector` to Avalonia (substantial effort — defer if possible)
- (c) Use `OpenFilePickerAsync` for files-only, with a separate folder-pick option

Recommendation: Start with option (a) for simplicity; note as a known limitation.
When using `OpenFolderPickerAsync`, pass the folder path as:
```csharp
string[] pathNames = new[] { folders[0].Path.LocalPath };
```
and ensure `ConfigureAddOpts` sets `Recurse = true` so that `AddFileSet` processes
all files within the selected folder. Without `Recurse = true`, passing a single
folder path will cause `AddFileSet` to skip it or treat it as a file.

WPF `HandleAddImport` → `AddPaths` flow:
1. Read `LAST_ADD_DIR` from settings
2. Show FileSelector, get `BasePath` and `SelectedPaths`
3. `AddPaths(pathNames, IFileEntry.NO_ENTRY, spec)` calls:
   - `GetSelectedArcDir()` to get target archive/filesystem and DiskArcNode
   - `ConfigureAddOpts(isImport)` — reads ~8 settings (ParseADF, ParseAS, ParseNAPS,
     Recurse, StripExt, CheckFinderInfo, etc.)
   - Creates `AddFileSet(basePath, pathNames, addOpts, importSpec, AppHook)`
   - Creates `AddProgress(archiveOrFileSystem, daNode, fileSet, targetDir, AppHook)`
     with settings (DoCompress, EnableMacOSZip, StripPaths, RawMode)
   - Shows `WorkProgress(mMainWin, prog, false)`
   - Calls `PostNotification` and `RefreshDirAndFileList()`

Also port:
- `CheckPasteDropOkay()` — checks `CanWrite`, shows error message if read-only
- `ConfigureAddOpts(bool isImport)` — constructs `AddFileSet.AddOpts` from settings
- `AddFileDrop(IFileEntry dropTarget, string[] pathNames)` — separate entry point for
  drag-drop; checks `IsChecked_ImportExport` to decide import vs add mode

### Step 7: Implement Drag-Drop

The WPF has three drag-drop zones. Implement the launch panel drop first; note the
others as future scope.

#### 7a. Launch Panel Drop-to-Open (implement now)

WPF: `LaunchPanel_DragOver` checks `DataFormats.FileDrop` and allows only 1 file;
`LaunchPanel_Drop` calls `mMainCtrl.DropOpenWorkFile(files[0])`.

In `MainWindow.axaml`, on the launch panel:
```xml
<Grid Name="launchPanel" IsVisible="{Binding LaunchPanelVisible}"
      DragDrop.AllowDrop="True">
```

In `MainWindow.axaml.cs`:
```csharp
// In constructor or Loaded handler — register on launchPanel, NOT on `this`:
launchPanel.AddHandler(DragDrop.DropEvent, LaunchPanel_Drop);
launchPanel.AddHandler(DragDrop.DragOverEvent, LaunchPanel_DragOver);

private void LaunchPanel_DragOver(object? sender, DragEventArgs e) {
    if (e.Data.Contains(DataFormats.Files)) {
        var files = e.Data.GetFiles()?.ToList();
        if (files?.Count == 1) {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
    }
    e.DragEffects = DragDropEffects.None;
    e.Handled = true;
}

private void LaunchPanel_Drop(object? sender, DragEventArgs e) {
    if (e.Data.Contains(DataFormats.Files)) {
        var files = e.Data.GetFiles()?.ToList();
        if (files?.Count == 1) {
            string? path = files[0].TryGetLocalPath();
            if (path == null) {
                // Sandboxed/portal path — cannot open via local path.
                // Would need IStorageFile.OpenReadAsync() instead.
                return;
            }
            mMainCtrl.DropOpenWorkFile(path);
        }
    }
}
```

Note: WPF uses `DragDropEffects.Move` (not Copy) for the launch panel. **Use
`DragDropEffects.Copy` instead** — `Move` semantically requests that the source file
be deleted after the drop, which is incorrect for an open-file operation. On Linux,
some file managers honor `Move` literally. `Copy` correctly communicates “I’m reading
this file.”

#### 7b. File List Drop Target (note for future)

The WPF `FileListDataGrid_Drop` handles three cases:
1. Internal drag (files within archive) → `MoveFiles()` to target directory
2. External file drop from OS → `AddFileDrop(dropTarget, files)` to add files
3. CP2-to-CP2 drop → `PasteOrDrop(e.Data, dropTarget)` for clipboard paste

The file list is also a drag **source** — `StartFileListDrag()` uses
`VirtualFileDataObject` to drag files out to Explorer. This involves
`GenerateVFDO()`, `ClipFileSet`, multi-selection tracking with
`mPreSelection`, and minimum drag distance detection. This is complex and can be
deferred to a later iteration.

#### 7c. Directory Tree Drop Target (note for future)

The WPF `DirectoryTree_Drop` handles external file drops → `AddFileDrop()` and
internal moves → `MoveFiles()`. Defer with file list drag.

### Step 8: Import/Export (shared code paths — implement with Add/Extract)

Import and Export are NOT separate operations — they share the same code paths as Add
and Extract:
- `ImportFiles()` = `HandleAddImport(GetImportSpec())` — same as `AddFiles()` but with
  a conversion spec
- `ExportFiles()` = `HandleExtractExport(GetExportSpec())` — same as `ExtractFiles()` but
  with a conversion spec

If `HandleAddImport` and `HandleExtractExport` are implemented (Steps 5-6), Import and
Export are almost free. The remaining work is:
1. Port `GetImportSpec()` — reads import converter tag and settings from AppSettings
2. Port `GetExportSpec()` — reads export converter tag and settings from AppSettings
3. Port `GetDefaultExportSpecs()` — reads default export specs
4. These depend on the **options panel** import/export converter ComboBoxes and settings
   (see Step 9)

If the options panel isn't ready yet, implement Import/Export as stubs that call
`HandleAddImport(null)` / `HandleExtractExport(null)` (i.e., they behave like Add/Extract).

### Step 9: Options Panel Prerequisites

The right-side options panel controls ~25 settings used by add/extract/import/export.
This must exist for operations to work correctly. The WPF panel has two sections:

**Add/Import Options** (Grid Row 0-1):
- Recurse Into Directories (`IsChecked_AddRecurse`)
- Use Compression (`IsChecked_AddCompress`)
- Strip Paths (`IsChecked_AddStripPaths`)
- Raw (for DOS 3.x) (`IsChecked_AddRaw`)
- Preservation Handling group: AppleSingle, AppleDouble, NAPS checkboxes
- Import Configuration group: Strip Redundant Extensions, Conversion mode ComboBox,
  Conversion Settings button

**Extract/Export Options** (Grid Row 2-3):
- Strip Paths (`IsChecked_ExtStripPaths`)
- Add Filename Ext to Exports (`IsChecked_ExtAddExportExt`)
- Raw (for DOS 3.x) (`IsChecked_ExtRaw`)
- Preservation Mode group: None/AppleSingle/AppleDouble/NAPS radio buttons
- Export Configuration group: Best (automatic) radio, specific converter radio + ComboBox,
  Conversion Settings button

**Toolbar radio buttons** ("Drag & Copy mode"):
- Add/Extract vs Import/Export — `IsChecked_AddExtract` / `IsChecked_ImportExport`
  controls behavior of drag-drop and clipboard paste. These are mutually exclusive
  radio buttons bound to separate bool properties with custom set logic — port exactly:
  ```csharp
  public bool IsChecked_AddExtract {
      get => AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
      set { if (value) AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, true);
            OnPropertyChanged(); OnPropertyChanged(nameof(IsChecked_ImportExport)); }
  }
  public bool IsChecked_ImportExport {
      get => !AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
      set { if (value) AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, false);
            OnPropertyChanged(); OnPropertyChanged(nameof(IsChecked_AddExtract)); }
  }
  ```
  Both setters only write when `value == true`, preventing the `IsChecked=false`
  feedback from overwriting the setting. Both fire `OnPropertyChanged` for the other
  property to update the UI.

Port these as bound properties in MainWindow with `AppSettings.Global` backing,
following the same pattern as the WPF version.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Actions/ExtractProgress.cs` |
| **Create** | `cp2_avalonia/Actions/AddProgress.cs` |
| **Create** | `cp2_avalonia/Actions/ProgressUtil.cs` |
| **Create** | `cp2_avalonia/Actions/OverwriteQueryDialog.axaml` |
| **Create** | `cp2_avalonia/Actions/OverwriteQueryDialog.axaml.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (HandleExtractExport, HandleAddImport, AddPaths, AddFileDrop, ConfigureAddOpts, CheckPasteDropOkay, GetImportSpec, GetExportSpec) |
| **Modify** | `cp2_avalonia/MainWindow.axaml` (drag-drop, options panel) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (drag-drop handlers, command wiring, ~25 options properties, DragCopy mode radios, `PostNotification`) |

---

## Implementation Notes

### `PostNotification` Method

`PostNotification(string msg, bool success)` is called by `MainController` in 10+ places
(Steps 5–6, and many later iterations). The WPF version at `MainWindow.xaml.cs` line 1275
shows a toast-style notification with a green/red border and a 3-second fade-out animation.
In Avalonia, use `Avalonia.Animation` or a simple `DispatcherTimer` to fade out a toast
`Border` element. Add this method to `MainWindow.axaml.cs` and the corresponding toast
UI elements (`toastBorder`, `toastTextBlock`, `toastMessage`) to `MainWindow.axaml`.

### `WorkProgress` Constructor Signature

Iteration 3 ported `WorkProgress` from WPF's 3-parameter constructor
`(Window owner, IWorker callbacks, bool isIndeterminate)`. The `Owner = owner` assignment
was removed per Iteration 3's general guidance (Avalonia sets ownership via
`ShowDialog<T>(owner)`). Confirm that the Avalonia constructor is 2-parameter:
`(IWorker callbacks, bool isIndeterminate)`. Step 5 of this iteration instantiates
`new WorkProgress(prog, false)` — this will fail to compile if the `Window owner`
parameter was preserved.

### Options Panel AXAML

Step 9 lists all ~25 properties but does not provide the AXAML layout. The WPF options
panel is a 200+ line XAML section in `MainWindow.xaml`. Convert the WPF layout verbatim:
copy the relevant `<Grid>` sections from `cp2_wpf/MainWindow.xaml` (search for
`IsChecked_AddRecurse` to find the region) and convert XAML → AXAML with the standard
substitutions (`Visibility` → `IsVisible`, `IsChecked="{Binding ...}"` retained, etc.).

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Actions → Extract Files shows folder picker and extracts selected files
- [ ] Progress dialog shows during extraction with filename and percentage
- [ ] Overwrite query dialog appears when extracting to a directory with existing files
- [ ] "Do this for all conflicts" checkbox works (applies choice to remaining conflicts)
- [ ] Cancel button in progress dialog stops the operation
- [ ] Actions → Add Files shows folder picker and adds files to the archive
- [ ] After adding files, the file list and directory tree refresh to show new entries
- [ ] Actions → Import Files works (same flow as Add but with conversion)
- [ ] Actions → Export Files works (same flow as Extract but with conversion)
- [ ] Ctrl+E shortcut triggers extract; Ctrl+Shift+A triggers add
- [ ] Dragging a single file from the file manager onto the launch panel opens it
- [ ] Dragging multiple files onto the launch panel is rejected (DragEffects.None)
- [ ] Options panel checkboxes (compress, strip paths, raw, preserve mode) affect operations
- [ ] Error messages shown for read-only archive, empty selection, etc.
- [ ] `PostNotification` shows success/cancel message after operations
