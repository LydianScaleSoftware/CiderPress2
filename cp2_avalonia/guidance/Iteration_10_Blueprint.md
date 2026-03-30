# Iteration 10 Blueprint: Edit Settings & Remaining Dialogs

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Port the application settings dialog and the remaining small-to-medium dialogs that haven't
been ported yet: EditConvertOpts, AddMetadata, EditMetadata, FindFile, ReplacePartition,
SaveAsDisk, and the `ConfigOptCtrl` dynamic control mapping system.

---

## Prerequisites

- Iteration 9 is complete: create disk/archive working.
- Key WPF source files to read:
  - `cp2_wpf/EditAppSettings.xaml` (118 lines)
  - `cp2_wpf/EditAppSettings.xaml.cs` — full file
  - `cp2_wpf/ConfigOptCtrl.cs` — full file (423 lines) — dynamic control mapping
  - `cp2_wpf/EditConvertOpts.xaml`
  - `cp2_wpf/EditConvertOpts.xaml.cs`
  - `cp2_wpf/AddMetadata.xaml`
  - `cp2_wpf/AddMetadata.xaml.cs`
  - `cp2_wpf/EditMetadata.xaml` + `.cs` (71 + 156 lines)
  - `cp2_wpf/FindFile.xaml`
  - `cp2_wpf/FindFile.xaml.cs`
  - `cp2_wpf/ReplacePartition.xaml`
  - `cp2_wpf/ReplacePartition.xaml.cs`
  - `cp2_wpf/SaveAsDisk.xaml`
  - `cp2_wpf/SaveAsDisk.xaml.cs`

---

## Step-by-Step Instructions

### Step 1: Verify `ConfigOptCtrl.cs` From Iteration 7

`ConfigOptCtrl.cs` was already ported in **Iteration 7, Step 4**. No re-port is needed.
`EditConvertOpts` (Step 4 below) and `EditAppSettings` use the same `ConfigOptCtrl`
classes. Verify the Iteration 7 port is working correctly before proceeding.

> **Reminder (from Iteration 7, Step 7):** All WPF programmatic bindings
> (`new Binding()`, `SetBinding()`, `FrameworkElement.VisibilityProperty`) must be
> replaced with imperative property assignment in `AssignControl()` / `HideControl()`.
> The `ItemVis` property (`Visibility` enum) must become `bool IsVisible`, and
> `IsAvailable` must return `!IsVisible` (hidden = available for assignment). `VisElem`
> type changes from `FrameworkElement` to `Control`. See Iteration 7 blueprint Step 7
> for full details.

### Step 2: Port `cp2_avalonia/EditAppSettings.axaml`

Read `cp2_wpf/EditAppSettings.xaml` (118 lines). This dialog uses a `TabControl` with a
single "General" tab (likely designed for future expansion).

**Window:** ~700×440, not resizable.

**Layout:** DockPanel with buttons (Apply/OK/Cancel) docked at bottom, `TabControl` filling
the rest.

**"General" tab** — 3-column Grid:
- **Column 0:** "Auto-Open Depth" GroupBox (3 radio buttons: Shallow/Sub-Volume/Max),
  "Enable MacZip" CheckBox
- **Column 2:** "Import/Export Conversion" GroupBox (2 config buttons), "Enable DOS text
  conversion" CheckBox
- **Column 4:** "Apple II Cassette Decoder" GroupBox (4 radio buttons: Zero-Crossing,
  PTP Sharp/Round/Shallow)
- **Bottom row (Col 0 only, not spanning):** "Enable DEBUG menu" CheckBox

**AXAML conversion notes:**
- `TabControl` → same in Avalonia
- Radio buttons in GroupBoxes → same pattern, use `GroupName` attribute
- Buttons at bottom → DockPanel or Grid row
- Apply button enabled by `IsDirty` binding

### Step 3: Port `cp2_avalonia/EditAppSettings.axaml.cs`

Read `cp2_wpf/EditAppSettings.xaml.cs` fully.

Key architecture:
- Works on a **local copy** of settings: `mSettings = new SettingsHolder(AppSettings.Global)`
- On Apply/OK: `AppSettings.Global.ReplaceSettings(mSettings)` pushes changes
- `IsDirty` tracks modifications; Apply button bound to it
- **`SettingsApplied` event** fires on Apply/OK so the main window can react (e.g.,
  show/hide debug menu)
- Radio button groups use the same enum-to-bool property pattern seen in other dialogs

Porting tasks:
1. Replace `DialogResult = true` → `Close(true)` (Avalonia)
2. **Remove `Owner = owner`** from the constructor (Avalonia 11 pattern — pass owner
   via `ShowDialog<bool?>(owner)` at the call site)
3. The config buttons for Import/Export conversion open `EditConvertOpts` dialogs (see
   Step 4) — **these must be `await`ed** (`ShowDialog<bool?>()` is async), making the
   click handlers `async void`. Wrap bodies in `try/catch`.
4. All settings logic is cross-platform — `SettingsHolder` is in `CommonUtil`
5. **Port `Window_Loaded` to `OnOpened()`:** `Loaded_General()` calls
   `SetAutoOpenDepth()` / `SetAudioAlg()`, each of which sets `IsDirty = true`. The
   WPF `Window_Loaded` resets `IsDirty = false` afterward. This reset must happen in
   `OnOpened()` (not the constructor) to fire after the controls are laid out:
   ```csharp
   protected override void OnOpened(EventArgs e) {
       base.OnOpened(e);
       Loaded_General();   // sets combos, which set IsDirty = true
       IsDirty = false;    // reset spurious dirty flag
   }
   ```

### Step 4: Port `cp2_avalonia/EditConvertOpts.axaml`

Read `cp2_wpf/EditConvertOpts.xaml`. This dialog edits converter options and
uses the `ConfigOptCtrl` system.

**Layout:**
- Row 0: Converter ComboBox
- Row 1: ScrollViewer with description TextBlock
- Row 2: Options GroupBox containing a **fixed pool of pre-created controls**:
  - `noOptions` TextBlock ("(none)")
  - 3 CheckBoxes (`checkBox1`, `checkBox2`, `checkBox3`)
  - 1 String input (StackPanel with label + TextBox)
  - 2 Radio button groups (GroupBox + 4 RadioButtons each)
  - **Match the control pool in FileViewer.axaml** — these are the same controls

- Row 3: Done + Cancel buttons

The constructor takes `(Window owner, bool isExport, SettingsHolder settings)` and
iterates `ExportFoundry` or `ImportFoundry` to build a `ConverterList` of
`ConverterListItem` objects (inner class with `Tag`, `Label`, `Description`,
`OptionDefs`). The code-behind creates `ConfigOptCtrl` map items from the named controls
and calls `ConfigureControls()` / `HideConvControls()` dynamically as the converter
ComboBox selection changes.

> **Remove `Owner = owner`** and **replace `DialogResult = true`** with `Close(true)`.
>
> **Initialization ordering:** The WPF constructor calls `converterCombo.SelectedIndex = 0`
> BEFORE `CreateControlMap()`. If Avalonia fires `SelectionChanged` synchronously at
> that point, `ConfigureControls()` runs while `mCustomCtrls` is still empty. Either
> (a) create the control map first, then set `SelectedIndex`, or (b) guard
> `ConfigureControls()` with a null/empty check on `mCustomCtrls`.

### Step 5: Port `cp2_avalonia/AddMetadata.axaml` + `.cs`

Read `cp2_wpf/AddMetadata.xaml`. Simple key-value entry dialog.

**Layout (6-row, 2-column Grid):**
- Row 0: "Key:" label + TextBox (mono font, `PropertyChanged` update)
- Row 1: Key syntax validation TextBlock with foreground color
- Row 2: Non-unique key warning (Red, visibility-controlled)
- Row 3: "Value:" label + TextBox (mono font)
- Row 4: Value syntax validation TextBlock with foreground color
- Row 5: OK (enabled by `IsValid`) + Cancel

Straightforward port — replace `Visibility` with `IsVisible` bools (e.g.,
`NonUniqueVisibility` → `bool IsNonUniqueVisible`). Both AddMetadata and
EditMetadata use `SystemColors.WindowTextBrush` and `Brushes.Red` — in Avalonia, use
`Avalonia.Media.Brushes` or dynamic resource references. Change field types from
`Brush` to `IBrush`.

> **Remove `Owner = owner`** from both AddMetadata and EditMetadata constructors.
> **Replace `DialogResult = true`** with `Close(true)` in `OkButton_Click`.
> EditMetadata's `DeleteButton_Click` also sets `DialogResult = true` — replace with
> `Close(true)`. **Port `Window_ContentRendered`** (focus + select text) to
> `OnOpened()` override — `ContentRendered` does not exist in Avalonia.
>
> `KeySyntaxText` / `ValueSyntaxText` in AddMetadata are getter-only properties set
> once in the constructor — no `OnPropertyChanged` needed. Preserve this pattern.

**`EditMetadata.axaml` + `.cs`** (71 + 156 = 227 lines) **is a separate dialog that
definitely exists and must be ported.** It differs from AddMetadata:
- Key field is **read-only** (pre-populated)
- Shows a **description** of the metadata entry
- Has a **Delete button** with `DoDelete` bool property and `CanDelete` guard
- `CanEdit` check can make the value field read-only
- Constructor takes `(Window owner, IMetadata metaObj, string key)`
- Called from `HandleMetadataDoubleClick()` in `MainController_Panels.cs`

### Step 6: Port `cp2_avalonia/FindFile.axaml` + `.cs`

Read `cp2_wpf/FindFile.xaml` (60 lines). Modal search dialog with callback event.

**Layout:**
- Row 0: "Filename:" label + TextBox (mono font)
- Row 1: "Current archive only" CheckBox
- Row 2: Three buttons — Find Previous, Find Next, Cancel

**Key behavior:** Despite using a callback event (`FindRequested`), this dialog is opened
with `ShowDialog()` in WPF (modal). The constructor takes `MainWindow` (not generic
`Window`), creating tighter coupling. The `FindRequested` event uses a custom delegate
and inner class `FindFileReq` with `FileName`, `CurrentArchiveOnly`, `Forward` fields.

Static fields `sLastSearch` and `sCurrentArchiveOnly` persist search configuration across
dialog opens within the same session.

> **Remove `Owner = owner`** from the constructor. **Port `Window_ContentRendered`**
> (focus/select) to `OnOpened()`. The constructor takes `MainWindow` (not `Window`) —
> consider changing to `Window` for consistency; the `FindRequested` event is the
> coupling mechanism, not the owner type.

The controller wires it as:
```csharp
dialog.FindRequested += DoFindFiles;  // private method in MainController
await dialog.ShowDialog<bool?>(mMainWin);  // modal — fires FindRequested events while open
```

> **Avalonia async wiring:** `FindFiles()` must be `async Task` because
> `ShowDialog<bool?>()` returns `Task<bool?>`. The `FindRequested` event fires during
> the `await` (while the modal dialog is displayed), so `DoFindFiles` runs on the UI
> thread as expected. After the dialog closes, unsubscribe: `dialog.FindRequested -= DoFindFiles;`.

### Step 7: Port `cp2_avalonia/ReplacePartition.axaml` + `.cs`

Read `cp2_wpf/ReplacePartition.xaml`. Destructive operation confirmation.

**Layout:**
- Row 0: Bold warning text about data destruction
- Row 1: Source size text
- Row 2: Destination size text
- Row 3: Size difference text (red if larger), with visibility control
- Row 4: "Copy" button (enabled by `IsValid`) + Cancel

Small dialog — straightforward port. However, the `ReplacePartition()` controller method
 has substantial orchestration: opens `OpenFileDialog` to select source file,
analyzes it with `FileAnalyzer`, validates it's a disk image, prepares `IDiskImage`,
defines an `EnableWriteFunc` delegate to close partition children, and post-dialog
re-scans the partition tree. Port `OpenFileDialog` → `StorageProvider.OpenFilePickerAsync`
(makes `ReplacePartition()` `async Task`). Convert `WinUtil.FILE_FILTER_*` strings to
`FilePickerFileType` objects.

> **Remove `Owner = owner`** from the constructor. Replace `DialogResult = true` in
> `CopyButton_Click` with `Close(true)`. Replace `SizeDiffVisibility` (`Visibility`
> enum) with `bool IsSizeDiffVisible`.
>
> **`MessageBox.Show()` in `CopyButton_Click`** (2 calls) and
> **`MainController.ReplacePartition()`** (4 calls): all must be replaced with the
> `MBButton`/`MBResult` helpers from `Common/MessageBoxEnums.cs` and the custom
> `ShowMessageBox` async helper. Since `ReplacePartition()` becomes
> `async Task` (from `OpenFilePickerAsync`), the async message dialogs can be
> `await`ed naturally.
>
> **`Mouse.OverrideCursor`** in `ReplacePartition()` post-dialog rescan: replace
> with `mMainWin.Cursor = new Cursor(StandardCursorType.Wait)` in try/finally
> with disposal:
> ```csharp
> var waitCursor = new Cursor(StandardCursorType.Wait);
> mMainWin.Cursor = waitCursor;
> try { /* rescan */ }
> finally { mMainWin.Cursor = null; waitCursor.Dispose(); }
> ```

### Step 8: Port `cp2_avalonia/SaveAsDisk.axaml` + `.cs`

Read `cp2_wpf/SaveAsDisk.xaml`. Disk image format conversion dialog.

**Layout:**
- Row 0: Source size text
- Row 1: "Select file type:" label
- Row 2: **10 RadioButtons** (same `GroupName`), each with `IsEnabled` and `IsChecked`
  bindings:
  - Simple block, ProDOS-order, DOS-order, 2IMG, ShrinkIt, DiskCopy 4.2, WOZ, MOOF,
    Nibble, Trackstar
- Row 3: Save + Cancel

**Code-behind is 644 lines** — substantially larger than the XAML suggests:
- `SetDefaultFileType()` — complex fallback logic to pick a valid default
- 10 pairs of `IsEnabled_FT_*` / `IsChecked_FT_*` properties that test format
  validity against the source chunk geometry
- `CreateImage()` — ~120 lines with `switch` over all 10 file types, creates the output
  disk image, calls `CopyDisk()`, handles NuFX `.sdk` two-phase creation
- `CopyDisk()` — `internal static` method also called by `ReplacePartition`
- Uses `CreateDiskImage.FileTypeValue` enum and `CreateDiskImage.SelectOutputFile()`
  from Iteration 9 — cross-iteration dependency. After the Iteration 9 port,
  `SelectOutputFile()` remains `internal static` but now accepts a `TopLevel` parameter
  (needed for `StorageProvider.SaveFilePickerAsync`) and returns `Task<string?>`.
  Call as: `await CreateDiskImage.SelectOutputFile(topLevel, fileType, is13Sector)`
  where `topLevel` is `this` (the dialog window) or the passed-in owner.
- On Save, `SelectOutputFile()` wraps `StorageProvider.SaveFilePickerAsync`

> **Remove `Owner = owner`** from constructor. **Replace `DialogResult = true`** with
> `Close(true)` in `OkButton_Click`. **Replace `Mouse.OverrideCursor`** with
> window-scoped cursor + disposal (same pattern as Iteration 8/9).
>
> **`MessageBox.Show()`** in `CreateImage()` (2 calls): replace with custom MessageBox
> helpers.
>
> **Async cascade from `SelectOutputFile()`:** After Iteration 9, this method is async
> (returns `Task<string?>`). `CreateImage()` must become `async Task<bool>`, which
> makes `OkButton_Click` `async void`. Wrap the entire `OkButton_Click` body in
> `try/catch` for async void exception safety.

### Step 9: Wire All Commands

Ensure all remaining menu commands are wired to their dialogs:
- Edit → App Settings → `EditAppSettings()` (in MainController)
- Actions → Replace Partition → `ReplacePartition()` (in MainController)
- Actions → Save As Disk Image → `SaveAsDiskImage()` (in MainController)
- Edit → Find Files → `FindFiles()` (in MainController)
- Metadata double-click → `HandleMetadataDoubleClick()` (in `MainController_Panels.cs`)
- Metadata add entry → `HandleMetadataAddEntry()` (in `MainController_Panels.cs`)

> **All controller methods calling `ShowDialog()` must be `async Task`** with
> `await dialog.ShowDialog<bool?>(mMainWin)`.  For `RelayCommand` bindings, use
> `async void` lambdas with `try/catch` (wrap the entire body to prevent unhandled
> exceptions from crashing the app — `async void` does not propagate to callers).
>
> **`CloseWorkFile()` is synchronous (`bool`):** As confirmed in Iteration 9, the WPF
> `CloseWorkFile()` never shows a dialog — it unconditionally flushes, disposes
> `mWorkTree`, clears the GUI, and returns `true`. The Avalonia port keeps it as a
> synchronous `bool`-returning method. All callers in this iteration (`RevertDisk()`,
> `SaveAsDiskImage()`, etc.) use `if (!CloseWorkFile()) return;` without `await`.

> **`Grid.Row="4"` in EditAppSettings.xaml:** The WPF XAML places the DEBUG checkbox
> at row 4 but only defines 4 rows (0-3). WPF/Avalonia handle overflow rows
> implicitly, but add a 5th `RowDefinition` for correctness.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| *(already created in Iter 7)* | `cp2_avalonia/Tools/ConfigOptCtrl.cs` |
| **Create** | `cp2_avalonia/EditAppSettings.axaml` |
| **Create** | `cp2_avalonia/EditAppSettings.axaml.cs` |
| **Create** | `cp2_avalonia/EditConvertOpts.axaml` |
| **Create** | `cp2_avalonia/EditConvertOpts.axaml.cs` |
| **Create** | `cp2_avalonia/AddMetadata.axaml` |
| **Create** | `cp2_avalonia/AddMetadata.axaml.cs` |
| **Create** | `cp2_avalonia/EditMetadata.axaml` |
| **Create** | `cp2_avalonia/EditMetadata.axaml.cs` |
| **Create** | `cp2_avalonia/FindFile.axaml` |
| **Create** | `cp2_avalonia/FindFile.axaml.cs` |
| **Create** | `cp2_avalonia/ReplacePartition.axaml` |
| **Create** | `cp2_avalonia/ReplacePartition.axaml.cs` |
| **Create** | `cp2_avalonia/SaveAsDisk.axaml` |
| **Create** | `cp2_avalonia/SaveAsDisk.axaml.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (wire remaining commands) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire remaining commands) |
| **Modify** | `cp2_avalonia/Tools/FileViewer.axaml.cs` (ConfigOptCtrl already integrated in Iter 7) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Edit → App Settings opens tabbed dialog
- [ ] All settings radio buttons and checkboxes work
- [ ] Apply button is disabled when no changes made
- [ ] Apply pushes changes without closing dialog
- [ ] OK pushes changes and closes
- [ ] Cancel discards all changes
- [ ] Debug menu visibility toggles when "Enable DEBUG menu" changes
- [ ] Import/export conversion config buttons open EditConvertOpts
- [ ] EditConvertOpts shows converter list, description, dynamic option controls
- [ ] Adding metadata shows key-value dialog with validation
- [ ] Find File opens as **modal** dialog; Find Next/Previous fire events while dialog stays open
- [ ] Replace Partition shows warning, validates sizes, Copy button works
- [ ] Save As Disk shows valid format options based on source image type
- [ ] Settings persist across application restarts
