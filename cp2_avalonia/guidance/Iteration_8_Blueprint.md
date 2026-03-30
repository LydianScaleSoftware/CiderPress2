# Iteration 8 Blueprint: Delete, Move, Rename, Edit Attributes

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Implement destructive file operations: delete, move (within a filesystem), rename, and
edit attributes. These operate on selected file-list entries and use the existing
`WorkProgress` dialog for background execution.

---

## Prerequisites

- Iteration 7 is complete: file viewer working.
- Key WPF source files to read:
  - `cp2_wpf/Actions/DeleteProgress.cs`
  - `cp2_wpf/Actions/MoveProgress.cs`
  - `cp2_wpf/Actions/EditAttributesProgress.cs` — **not** an IWorker
  - `cp2_wpf/CreateDirectory.xaml` + `CreateDirectory.xaml.cs` — **already ported in Iteration 5**
  - `cp2_wpf/EditAttributes.xaml` — complex form with many conditional sections
  - `cp2_wpf/EditAttributes.xaml.cs` — extensive validation logic
  - `cp2_wpf/MainController.cs` — search for `DeleteFiles`, `MoveFiles`, `EditAttributes`,
    `EditDirAttributes`, `CreateDirectory`

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/Actions/DeleteProgress.cs`

Read `cp2_wpf/Actions/DeleteProgress.cs`. This is a `WorkProgress.IWorker` implementation
that wraps `AppCommon.DeleteFileWorker` in a background operation.

Porting tasks:
1. Namespace: `cp2_avalonia.Actions`
2. The class structure is straightforward — no UI, no Windows dependencies
3. Key flow: `DoWork()` starts a transaction on the archive (or operates on the filesystem
   directly), calls `DeleteFileWorker`, then saves via `mLeafNode.SaveUpdates(DoCompress)`
4. Error handling: catches exceptions, calls `mLeafNode.FlushStreams()`, returns false
5. No Dispatcher calls needed — this is all background-thread work

Wire the delete command in `MainWindow.axaml.cs`:
```csharp
DeleteFilesCommand = new RelayCommand(
    () => mMainCtrl.DeleteFiles(),
    () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
         mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList &&
         mMainCtrl.AreFileEntriesSelected);
```
**Note:** The WPF `CanDeleteFiles` handler at `MainWindow.xaml.cs` line 449 evaluates
all five conditions above. Using only `AreFileEntriesSelected` would enable Delete on
read-only archives.

### Step 2: Port `cp2_avalonia/Actions/MoveProgress.cs`

Read `cp2_wpf/Actions/MoveProgress.cs`. Similar structure to DeleteProgress but only
supports `IFileSystem` (not `IArchive`) and takes a destination directory.

Porting tasks:
1. Namespace: `cp2_avalonia.Actions`
2. Straightforward port — validates `mDestDir.IsDirectory` in constructor
3. Move is **disk-only** — archives don't support move

**Important:** There is no menu command for Move. `MoveFiles(List<IFileEntry> moveList,
IFileEntry targetDir)` is called exclusively from drag-drop handlers in MainWindow. Do
**not** create a `MoveFilesCommand`. The drag-drop wiring will be handled in Iteration 13.

### Step 3: Wire Delete in `MainController.cs`

Port the `DeleteFiles()` method from `cp2_wpf/MainController.cs`:

1. Get selected arc/dir via `GetSelectedArcDir()`, then get entries via
   `GetFileSelection(omitDir:false, omitOpenArc:false, closeOpenArc:true, oneMeansAll:false)`
2. Record `firstSelIndex` for post-delete selection adjustment
3. Show confirmation dialog using the project's established dialog pattern (the WPF code
   uses `MessageBox.Show()` with OK/Cancel — replace with the custom `MBButton`/`MBResult`
   pattern from `cp2_avalonia/Common/MessageBoxEnums.cs`, defined in Iteration 3, Step 2)
4. Create `DeleteProgress` with selected entries, the current `DiskArcNode`, and compression
   settings (`DoCompress`, `EnableMacOSZip`)
5. Show `WorkProgress` dialog modally — **must be `await`ed** (`WorkProgress.ShowDialog<bool?>()`
   returns `Task<bool?>` in Avalonia; without `await` the dialog opens non-modally)
6. Post-delete selection: if not (cancelled && IArchive), select item at the corrected index:
   ```csharp
   int selectIdx = Math.Max(0, Math.Min(firstSelIndex - 1, fileList.Count - 1));
   ```
   This handles the edge case where `firstSelIndex == 0` (deleting the first item)
   which would otherwise produce `-1`.
7. Refresh the directory and file lists

### Step 4: Wire Move in `MainController.cs`

Port the `MoveFiles(List<IFileEntry> moveList, IFileEntry targetDir)` method. This is
called from drag-drop handlers (not a menu command), so it receives pre-built parameters.

1. Validate: IFileSystem only, `targetDir` is not `NO_ENTRY`, `CanWrite`, not dubious/damaged,
   `IsDirectory`
2. Screen out no-op moves (entry already in target directory) and invalid directory-into-self
   moves
3. Create `MoveProgress` with remaining entries, `DiskArcNode`, destination, and `DoCompress`
   setting (no `EnableMacOSZip`)
4. Show `WorkProgress` dialog modally
5. Post-move: clear selection, regenerate `FileListItem` for each moved entry
6. Refresh the file list

Note: The actual drag-drop call site will be wired in Iteration 13. For now, port the
`MoveFiles` method so it is ready to be called.

### Step 5: Wire `CreateDirectory` Into Move/Rename Context

`CreateDirectory.axaml` was already ported in **Iteration 5, Step 3**. No re-port is
needed here. If the move/rename workflow needs to create directories, call the existing
`CreateDirectory` dialog from Iteration 5.

### Step 6: Port `cp2_avalonia/Actions/EditAttributesProgress.cs`

Read `cp2_wpf/Actions/EditAttributesProgress.cs`. **This is NOT a
`WorkProgress.IWorker`** — it runs synchronously on the GUI thread with a wait cursor.

Key details:
1. Namespace: `cp2_avalonia.Actions`
2. Constructor takes: parent (Window), archiveOrFileSystem, leafNode, fileEntry, adfEntry,
   newAttribs (`FileAttribs`), appHook
3. Properties: `DoCompress`, `EnableMacOSZip`
4. `DoUpdate(bool updateMacZip)` — main entry point, two branches:
   - **IArchive + updateMacZip:** calls private `HandleMacZip()` which rewrites AppleSingle
     ADF header, replaces data fork, renames both entries (complex ~60-line method)
   - **IArchive normal:** StartTransaction → `CopyAttrsTo` → `SaveUpdates` →
     CancelTransaction in finally
   - **IFileSystem:** `CopyAttrsTo` → `SaveChanges` → `SaveUpdates`
5. Replace `Mouse.OverrideCursor = Cursors.Wait` → Avalonia cursor with proper disposal:
   ```csharp
   var waitCursor = new Cursor(StandardCursorType.Wait);
   mParent.Cursor = waitCursor;
   try {
       // ... DoUpdate body ...
   } finally {
       mParent.Cursor = null;   // restore default
       waitCursor.Dispose();
   }
   ```
   Note: `mParent.Cursor` is window-scoped (not app-global like WPF's
   `Mouse.OverrideCursor`), so child dialogs (e.g., error dialogs) will show the
   normal cursor. This is acceptable.
6. Replace `MessageBox.Show()` with the project's established error dialog pattern

### Step 7: Port `cp2_avalonia/EditAttributes.axaml` — Complex Form

Read `cp2_wpf/EditAttributes.xaml`. This is a complex dialog with many
conditional sections. Port carefully.

**Window:** 500px wide, SizeToContent="Height", not resizable.

**Main Grid with 7 rows:**

**Row 0 — Filename:**
- TextBox bound to `FileName`, mono font, `MaxLength=1024`, `IsReadOnly` bound to
  `IsAllReadOnly`
- Syntax rules TextBlock with validation color
- Unique name warning with visibility binding
- Directory separator info text with visibility binding

**Row 1 — ProDOS Type GroupBox** (conditional visibility):
- File type ComboBox bound to `ProTypeList` with `DisplayMemberPath="Label"`
- Aux type hex TextBox (MaxLength=4) with validation color
- Description TextBlock

**Row 2 — HFS Type GroupBox** (conditional visibility):
- Dual entry for Type: 4-char string TextBox + hex value TextBox
- Dual entry for Creator: same pattern
- All have validation foreground colors

**Row 3 — Timestamp GroupBox** (conditional visibility):
- **5 sub-rows**: creation date/time, modification date/time, plus 3 info TextBlocks
  (local format hint, 24-hour format hint, valid date range)
- Each date/time row: DatePicker + time TextBox (HH:MM:SS format, regex-validated)
- **Avalonia DatePicker port:** `SelectedDate` uses `DateTimeOffset?` instead of WPF's
  `DateTime?`. The time string is handled separately by a TextBox with regex validation
  (`^(\d{1,2}):(\d\d)(?>:(\d\d))?$`).

  > **DateTimeOffset conversion required:** The `CreateDate` / `ModDate` properties
  > must be `DateTimeOffset?` (not `DateTime?`). In `DateTimeUpdated()`, extract the
  > date via `mCreateDate.Value.DateTime` (not a direct cast — `(DateTime?)offset`
  > does not compile). `TimestampStart`/`TimestampEnd` from
  > `arc.Characteristics.TimeStampStart` are `DateTime` — wrap with
  > `new DateTimeOffset(timestampStart)` for DatePicker binding.
  >
  > **MinYear / MaxYear, not DisplayDateStart / DisplayDateEnd:** Avalonia 11's
  > `DatePicker` is a calendar-wheel spinner. It uses `MinYear` / `MaxYear`
  > (`DateTimeOffset`) instead of WPF's `DisplayDateStart` / `DisplayDateEnd`. AXAML
  > must use these property names.

**Row 4 — Access Flags GroupBox** (conditional visibility):
- Two display modes switched by `IsVisible`:
  - Simple: Locked + Invisible checkboxes only
  - Full: Read, Write, Rename, Backup, Delete, Invisible checkboxes in 2×3 grid
- **Default both `IsLockedOnlyVisible` and `IsAllFlagsVisible` to `false`** in the
  property declarations. `PrepareAccess()` sets exactly one to `true`. If both default
  to `true` (matching the WPF XAML where both default `Visibility.Visible`), both
  panels flash visible before `PrepareAccess()` runs.
- Special cases: CPM disables Read/Rename/Delete/Invisible; Pascal collapses entire section

**Row 5 — Comment GroupBox** (conditional visibility):
- Multi-line TextBox, Height=120, `AcceptsReturn=True`, `TextWrapping="Wrap"`, MaxLength=65535
- Available for Zip and NuFX only (not MacZip ADF entries)

**Row 6 — OK/Cancel:**
- OK button enabled by `IsValid`

Key Avalonia conversion notes:
- `Visibility.Visible`/`Collapsed` → `IsVisible` bool properties
- `Foreground="{Binding SomeForeground}"` keep using `IBrush` properties
  (replace `System.Windows.Media.Brushes.Red` with `Avalonia.Media.Brushes.Red`)
- `DatePicker` exists in Avalonia but uses `DateTimeOffset?` instead of `DateTime?`
- `UpdateSourceTrigger=PropertyChanged` → Avalonia TextBox already updates on text change
  by default

> **`DirSepTextVisibility` initialization:** This property is set conditionally in the
> constructor: `Visible` for IArchive when `Characteristics.PathSeparatorChar != '\0'`,
> `Collapsed` otherwise (IFileSystem, or archives with no path separator). The Avalonia
> port (`bool IsDirSepTextVisible`) must initialize to `false` and set to `true` only
> in the IArchive branch.

> **Comment field init:** `PrepareComment()` sets the backing field `mCommentText`
> directly (not the property), avoiding `OnPropertyChanged` during construction. This
> is the WPF pattern — preserve it in the Avalonia port. The Avalonia binding reads
> from the property getter on first render, which returns `mCommentText`, so the
> initial value is picked up correctly.

### Step 8: Port `cp2_avalonia/EditAttributes.axaml.cs`

Read `cp2_wpf/EditAttributes.xaml.cs` fully. This is the largest file in
this iteration, with extensive validation logic across several regions.

Key regions and approximate sizes:
- **Filename**: `SyntaxRulesText`, `SyntaxRulesForeground`,
  `UniqueNameForeground`, `IsValidFileNameFunc` delegate, `CheckFileNameValidity()` with
  archive vs filesystem uniqueness checks
- **ProDOS/DOS/Pascal types**: `ProTypeListItem` class, `PrepareProTypeList()`
  with branches for DOS, Pascal, and ProDOS, `ProTypeCombo_SelectionChanged` handler
- **HFS types**: bidirectional char↔hex conversion via `SetHexFromChars()` and
  `SetCharsFromHex()`, validation foreground colors
- **Timestamps**: `DateTimeUpdated()` with regex time parsing, `PrepareTimestamps()`
  with format-specific CreateWhen enable/disable
- **Access flags**: `PrepareAccess()` with ProDOS/NuFX/CPM full flags vs
  locked-only for other formats, 7 boolean properties with bitwise access manipulation
- **Comment**: `PrepareComment()` — Zip and NuFX only

Key porting tasks:
1. Namespace: `cp2_avalonia`
2. Replace `Window` base class, `INotifyPropertyChanged` pattern (same)
3. Replace all `Visibility` properties with `bool` properties (e.g., `ProTypeVisibility` →
   `IsProTypeVisible`, `ShowLockedOnlyVisibility` → `IsLockedOnlyVisible`, etc.)
4. Replace `System.Windows.Media.Brushes` → `Avalonia.Media.Brushes`
5. Replace `SystemColors.WindowTextBrush` → appropriate Avalonia theme brush
6. Constructor takes 6 parameters: parent, archiveOrFileSystem, entry, adfEntry, attribs,
   isReadOnly
7. The `NewAttribs` (`FileAttribs`) output property is already cross-platform
8. Result pattern: OK button must call `Close(true)`; Cancel must call `Close(false)`
   (or just `Close()` which returns `null`). The caller checks
   `if (await dialog.ShowDialog<bool?>(owner) == true)` — without explicit `Close(true)`,
   `ShowDialog<bool?>` returns `null` and the caller never processes the edit result.
9. Port `Loaded_FileType()` — this is called from WPF's `Window_Loaded` to select the
   correct item in `proTypeCombo` after `PrepareProDOSType()` builds the list. It cannot
   run in the constructor because the ComboBox may not be laid out yet. In Avalonia,
   override `OnOpened()` (equivalent of WPF `Window_Loaded`):
   ```csharp
   protected override void OnOpened(EventArgs e) {
       base.OnOpened(e);
       Loaded_FileType();            // select correct ProDOS type combo item
       fileNameTextBox.SelectAll();  // from Window_ContentRendered
       fileNameTextBox.Focus();
   }
   ```

### Step 9: Wire Edit Attributes in `MainController.cs`

Port three public methods from `cp2_wpf/MainController.cs`:

**`EditAttributes()`** — called when editing a file from the file list:
1. Gets `ArchiveTreeItem` and `FileListItem` from the main window
2. If the entry is a directory, finds the matching `DirectoryTreeItem`
3. Calls the private `EditAttributes()` overload

**`EditDirAttributes()`** — called when editing a directory from the directory tree:
1. Gets `ArchiveTreeItem` and `DirectoryTreeItem`
2. Finds the corresponding `FileListItem` if in full-list mode
3. Calls the private `EditAttributes()` overload

**Private `EditAttributes(WorkTree.Node, IFileEntry, DirectoryTreeItem?, FileListItem?)`:**
1. **MacZip handling**: if MacZip enabled and entry is in a Zip archive, detect
   MacZip header via `Zip.HasMacZipHeader()`, extract AppleSingle stream, read attributes
   from AppleSingle entry
2. Determine read-only state from the archive or filesystem
3. Create `EditAttributes` dialog with 6 parameters (parent, archiveOrFileSystem, entry,
   adfArchiveEntry, curAttribs, isReadOnly)
4. Show modal; if cancelled, return

   > **`EditAttributes.ShowDialog<bool?>()` must be awaited** — same pattern as all
   > other Avalonia modal dialogs (`ShowDialog<T>()` returns `Task<T>`).

   Wire the commands in `MainWindow.axaml.cs`:
   ```csharp
   EditAttributesCommand = new RelayCommand(
       async () => { try { await mMainCtrl.EditAttributes(); } catch (Exception ex) {
           // Log and show error
       } },
       () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
            mMainCtrl.IsSingleEntrySelected);

   EditDirAttributesCommand = new RelayCommand(
       async () => { try { await mMainCtrl.EditDirAttributes(); } catch (Exception ex) {
           // Log and show error
       } },
       () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
            mMainCtrl.IsFileSystemSelected);
   ```
   The CanExecute conditions match the WPF `IsSingleEntrySelected` (line 406) and
   `IsFileSystemSelected` (line 490) handlers respectively.

   > **Stream lifetime for AppleSingle:** The dialog must be created **inside** the
   > `using` blocks for `adfStream` / `adfArchive`. The WPF code creates the dialog
   > and calls `ShowDialog()` within the `using` scope so `adfArchiveEntry` is still
   > valid. If the `using` blocks close before the dialog opens, `adfArchiveEntry` is
   > a disposed reference and reading its properties will fail. Preserve the WPF
   > nesting: extract `curAttribs` and create/show the dialog inside the `using` scope.
5. Create `EditAttributesProgress`, call `DoUpdate(isMacZip)` on GUI thread — **not** via
   `SetAttrWorker` or `WorkProgress`. **Gate all post-edit operations on `DoUpdate()`
   returning `true`** — the WPF code checks `if (prog.DoUpdate(isMacZip))` and only
   regenerates the file list on success. `DoUpdate()` shows a `MessageBox` on failure;
   replace with the custom `MBButton`/`MBResult` pattern from
   `cp2_avalonia/Common/MessageBoxEnums.cs` (defined in Iteration 3, Step 2).
   `DoUpdate()` is synchronous — it returns `bool` (not `async Task<bool>`), because
   it runs on the GUI thread under a wait cursor with no `await` inside.
6. Post-edit: regenerate `FileListItem` (MacZip entries use a different constructor with
   `adfEntry` + `NewAttribs`), update `ArchiveTreeItem.Name` and `DirectoryTreeItem.Name`

**Rename:** There is no separate rename method or command. Renaming is accomplished by editing
the filename field in the `EditAttributes` dialog.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Actions/DeleteProgress.cs` |
| **Create** | `cp2_avalonia/Actions/MoveProgress.cs` |
| **Create** | `cp2_avalonia/Actions/EditAttributesProgress.cs` |
| *(already created in Iter 5)* | `cp2_avalonia/CreateDirectory.axaml` |
| *(already created in Iter 5)* | `cp2_avalonia/CreateDirectory.axaml.cs` |
| **Create** | `cp2_avalonia/EditAttributes.axaml` |
| **Create** | `cp2_avalonia/EditAttributes.axaml.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (delete/move/edit-attrs/create-dir methods) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire commands) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Select file(s) → Edit → Delete confirms and deletes
- [ ] Delete key shortcut triggers delete
- [ ] After delete, file list refreshes correctly
- [ ] Move is filesystem-only (drag-drop invocation, no menu command)
- [ ] Move validates target directory, screens no-op and directory-into-self moves
- [ ] After move, file list reflects new file locations
- [ ] Select single file → Actions → Edit Attributes opens the dialog
- [ ] EditDirAttributes works for directory tree selection
- [ ] Filename editing with validation works
- [ ] ProDOS type/aux fields shown for appropriate filesystems
- [ ] HFS type/creator fields shown for HFS volumes
- [ ] Timestamp editing works (date picker + time entry)
- [ ] Access flags shown correctly (simplified or full depending on filesystem)
- [ ] Comment field shown for Zip and NuFX archives
- [ ] OK is disabled when validation fails (bad filename, bad hex, etc.)
- [ ] Read-only mode disables all editing fields
- [ ] Rename is done through Edit Attributes (no separate rename command)
- [ ] Create Directory dialog validates name uniqueness and syntax
- [ ] `EditAttributesProgress` runs on GUI thread with wait cursor (not WorkProgress)
