# Iteration 9 Blueprint: Create New Archives & Disk Images

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Implement dialogs for creating new (empty) file archives and disk images. These are
accessed from the File menu and produce new files on disk that can then be opened.

---

## Prerequisites

- Iteration 8 is complete: delete/move/edit attributes working.
- Key WPF source files to read:
  - `cp2_wpf/CreateDiskImage.xaml` — complex form with many radio buttons
  - `cp2_wpf/CreateDiskImage.xaml.cs` — extensive code-behind
  - `cp2_wpf/CreateFileArchive.xaml` — simple
  - `cp2_wpf/CreateFileArchive.xaml.cs` — simple
  - `cp2_wpf/MainController.cs` — search for `NewDiskImage`, `NewFileArchive`

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/CreateFileArchive.axaml` — Simple Dialog

Read `cp2_wpf/CreateFileArchive.xaml`. This is a simple dialog with three
radio buttons for archive type selection.

**Layout:** 3-row Grid (label, StackPanel of radios, buttons)
- `SizeToContent="WidthAndHeight"` (no fixed width), not resizable
- Row 0: "Select type of archive to create:" TextBlock
- Row 1: StackPanel with 3 RadioButtons — "Binary II", "ShrinkIt (NuFX)", "ZIP"
  (**`GroupName="ArchiveType"` is required** — Avalonia does NOT auto-group radios by
  container; without `GroupName`, all three act as independent toggles)
- Row 2: OK / Cancel button StackPanel

Port the XAML directly from the WPF source, converting to Avalonia conventions
(remove `ResizeMode`, use `CanResize="False"`, etc.). Do **not** fabricate new AXAML —
follow the actual WPF layout structure.

### Step 2: Port `cp2_avalonia/CreateFileArchive.axaml.cs`

Read `cp2_wpf/CreateFileArchive.xaml.cs`. This is straightforward.

Key details:
- Output property: `Kind` (type `FileKind` — a DiskArc enum)
- Three bool properties (`IsChecked_Binary2`, `IsChecked_NuFX`, `IsChecked_Zip`) that
  read/set `Kind` in their setters
- Constructor takes `(Window owner)`, restores last selection from
  `AppSettings.Global.GetEnum()`, defaults to `FileKind.NuFX` with a switch-case guard

  > **Remove `Owner = owner`** from the constructor body (Avalonia 11 has no public
  > setter on `Window.Owner`). Ownership is set at the call site via
  > `dialog.ShowDialog<bool?>(owner)`.

- WPF uses `DialogResult = true` — **replace with `Close(true)`** in `OkButton_Click`
  (returns `true` from `ShowDialog<bool?>()`). **Save settings before calling
  `Close(true)`** — once `Close` is called the window begins closing, so persist
  the selected `Kind` to `AppSettings` first, then close.

### Step 3: Port `cp2_avalonia/CreateDiskImage.axaml` — Complex Form

Read `cp2_wpf/CreateDiskImage.xaml`. This is the most complex creation dialog
with three columns of radio buttons separated by visual dividers.

**Layout:** 5-column Grid with 3 visual sections separated by Border dividers:

**Column 0 — Disk Size:**
- GroupBox "5.25\" Floppy": Radio buttons for 113.75KB, **140KB** (default), 160KB
- GroupBox "3.5\" Floppy": Radio buttons for 400KB, 800KB, 1440KB
- GroupBox "Other": Radio buttons for 32MB, Custom
  - Custom has a TextBox for entering size + validation message TextBlock

**Column 2 — Filesystem:**
- Radio buttons: DOS 3.2/3.3, ProDOS, HFS, UCSD Pascal, CP/M, None (zeroed)
- Volume Name TextBox (MaxLength=27) + validation TextBlock
- Volume Number TextBox (MaxLength=3) + validation TextBlock ("Must be 0-254")
- "Allocate boot tracks (DOS 3.x, CP/M)" CheckBox

**Column 4 — File Type:**
- Radio buttons: Simple block (.iso/.hdv), ProDOS-order (.po), DOS-order (.do/.d13),
  2IMG (.2mg), ShrinkIt (.sdk), DiskCopy 4.2 (.image), WOZ (.woz), MOOF (.moof),
  Nibble (.nib), Trackstar (.app)

All radio buttons have `IsEnabled` and `IsChecked` bindings. Validation messages use
colored foreground bindings.

Bottom row: Create (enabled by `IsValid`) + Cancel buttons.

Key AXAML conversion notes:
- Radio buttons: `GroupName` must be set for each group (DiskSize, Filesystem, FileType)
- Validation colors: Use `IBrush` properties and `Avalonia.Media.Brushes`
- Column dividers are `Rectangle` elements (not `Border`):
  `<Rectangle Grid.Column="1" Width="1" Stroke="LightGray" />`
- Section headers ("Disk Size", "Filesystem", "File Type") use StackPanels with
  `Background={DynamicResource SystemColors.GradientInactiveCaptionBrushKey}` — replace
  with `{DynamicResource SystemControlHighlightListAccentMediumBrush}` or a similar
  Fluent theme brush. `{x:Static SystemColors.*}` keys are WPF-only and will fail to
  compile in Avalonia.
- `GroupBox` has no built-in equivalent in Avalonia 11.x (it was not added until
  Avalonia 12.0-alpha via PR #19366). Replace each `GroupBox` with a
  `HeaderedContentControl` styled as a group:
  ```xml
  <HeaderedContentControl Header="5.25&quot; Floppy">
      <StackPanel> <!-- radio buttons --> </StackPanel>
  </HeaderedContentControl>
  ```
  **Note:** bare `HeaderedContentControl` with the default Fluent theme does NOT
  render the bordered box that WPF `GroupBox` provides — it only shows a header
  and content. To get the grouped visual, apply a custom `ControlTheme` that adds
  a `Border` around the content, or use a simple `Border` + `TextBlock` header
  pattern directly:
  ```xml
  <StackPanel>
      <TextBlock Text="5.25&quot; Floppy" FontWeight="Bold" />
      <Border BorderBrush="Gray" BorderThickness="1" CornerRadius="4" Padding="8">
          <StackPanel> <!-- radio buttons --> </StackPanel>
      </Border>
  </StackPanel>
  ```
  The `Border` + `TextBlock` approach is simpler and avoids needing a custom
  `ControlTheme` for `HeaderedContentControl`.

### Step 4: Port `cp2_avalonia/CreateDiskImage.axaml.cs`

Read `cp2_wpf/CreateDiskImage.xaml.cs` fully. This is by far the largest
file in this iteration.

**Key regions and approximate sizes:**
- **Constructor + CreateImage + SelectOutputFile**: Constructor takes
  `(Window owner, AppHook appHook)`, restores all settings. `CreateImage()` is ~200 lines
  with a 10-case switch for file type creation (DOSSector, ProDOSBlock, SimpleBlock, TwoIMG,
  NuFX, DiskCopy42, Woz, Moof, Nib, Trackstar). NuFX is a two-phase process: creates a
  temp block image in `MemoryStream`, formats filesystem, then wraps in NuFX archive.
  `SelectOutputFile()` builds per-type file filters and
  validates output extension.
- **UpdateControls**: Complex cross-validation cascade — size disables invalid
  filesystems, filesystem disables invalid file types, validates volume name per
  filesystem rules (ProDOS/HFS/Pascal), validates volume number (0-254). Broadcasts all
  property changes via `sPropList` array.
- **Disk Size region**: `DiskSizeValue` enum, `GetVolSize()`, `GetNumBlocks()`,
  `GetNumTracksSectors()`, `GetMediaKind()`, `IsFlop525`/`IsFlop35` helpers,
  8 `IsChecked_Flop*`/`IsChecked_Other_*` properties, `CustomSizeText`, validation brushes.
- **Filesystem region**: `FileSystemType` enum (reuses DiskArc type),
  6 `IsChecked_FS_*` + `IsEnabled_FS_*` pairs (each `IsEnabled` calls format-specific
  `IsSizeAllowed()`), `VolumeNameText`, `VolumeNumText`, validation brushes,
  `IsChecked_ReserveBoot`.
- **File Type region**: `FileTypeValue` enum, 10 `IsChecked_FT_*` +
  `IsEnabled_FT_*` pairs (each `IsEnabled` calls format-specific `CanCreate*` methods).

**Key porting tasks:**

1. Replace `Microsoft.Win32.SaveFileDialog` → Avalonia `StorageProvider.SaveFilePickerAsync`.
   The WPF code uses `WinUtil.FILE_FILTER_*` format strings ("Description|*.ext") — Avalonia
   uses `FilePickerFileType` objects with `Patterns` arrays. `SelectOutputFile()` builds
   filter+extension per file type; refactor to return Avalonia `FilePickerFileType` objects.
   **`SelectOutputFile()` is currently `internal static`; keep it static but add a
   `TopLevel` parameter** because `StorageProvider.SaveFilePickerAsync` requires a
   `TopLevel` reference. The new signature becomes
   `internal static async Task<string?> SelectOutputFile(TopLevel topLevel, FileTypeValue fileType, bool is13Sector)`.
   This cascades `async`/`await` through `CreateImage()` → `OkButton_Click` (which
   becomes `async void`). Callers in the dialog pass `this`; external callers
   (e.g., `SaveAsDisk`) pass their own `TopLevel` reference.

2. Replace `Mouse.OverrideCursor = Cursors.Wait` → Avalonia cursor with proper disposal.
   **`CreateImage()` has no window reference as-is** (it accesses `Mouse.OverrideCursor`
   which is application-global). Pass `this` (the dialog window) or make `CreateImage()`
   an instance method so it can set `this.Cursor`:
   ```csharp
   var waitCursor = new Cursor(StandardCursorType.Wait);
   this.Cursor = waitCursor;
   try { ... }
   finally { this.Cursor = null; waitCursor.Dispose(); }
   ```

3. **Error handling and cleanup in `CreateImage()`:** On failure, the method closes the
   `FileStream`, deletes the partially-created file (`File.Delete(pathName)`), and shows an
   error dialog. Preserve this cleanup path. Replace `MessageBox.Show(this, ...)`
   with `MBButton`/`MBResult` helpers from `Common/MessageBoxEnums.cs` and the
   custom `ShowMessageBox` async helper. If `CreateImage()` is now
   `async Task` (from the `SaveFilePickerAsync` cascade), the MessageBox replacement can
   be `await`ed naturally.

4. Replace `System.Windows.Media.Brushes` / `SystemColors.WindowTextBrush` →
   `Avalonia.Media.Brushes` + `{DynamicResource ThemeForegroundBrush}` for the default
   text color. Change field types from `Brush` to `IBrush` (`Avalonia.Media.IBrush`).

5. **Settings persistence:** Constructor restores all settings (disk size, filesystem,
   file type, volume name, volume number, custom size, reserve boot). **Save settings
   before calling `Close(true)`** in `OkButton_Click`.

   > **`DialogResult = true` → `Close(true)`** in `OkButton_Click`. Remove
   > `Owner = owner` from the constructor (same pattern as all previous iterations).

   > **`async void` exception handling:** `OkButton_Click` becomes `async void`
   > (due to the async cascade from `SelectOutputFile`/`CreateImage`). Wrap the entire
   > body in `try/catch` to prevent unhandled exceptions from crashing the app.

6. **Synchronous disk I/O note:** `CreateImage()` performs potentially large writes
   (up to 32 MB) synchronously. The WPF version does the same. This is preserved
   as-is with the wait cursor as user feedback; offloading to a background task is
   not required for this iteration.

### Step 5: Wire Menu Commands

In `MainController.cs`:

**`NewDiskImage()`** (the actual WPF method name — no "Do" prefix):
1. Call `CloseWorkFile()` first — must close before creating, in case user overwrites the
   current file. If close is cancelled, return.
2. Create `CreateDiskImage` dialog (pass `mMainWin` and `AppHook`)
3. Show modal — **must be `await`ed** (`dialog.ShowDialog<bool?>(mMainWin)` returns
   `Task<bool?>`) — the dialog handles save-file selection and image creation internally
4. If `dialog.PathName` is set (non-empty), open it via `DoOpenWorkFile(dialog.PathName, false)`

> **Note:** `CloseWorkFile()` before the dialog means a cancel leaves no file open.
> This is intentional WPF behavior — preserve it.

> **`CloseWorkFile()` is synchronous (`bool`):** In the WPF source,
> `CloseWorkFile()` never shows a dialog — it unconditionally flushes, disposes
> `mWorkTree`, clears the GUI, and returns `true` (the only `return false` path
> would be if an unsaved-changes prompt were added, but there is none). This means
> the Avalonia port can keep `CloseWorkFile()` as a synchronous `bool`-returning
> method with no `async` cascade. All 7+ call sites across iterations 3–15 can
> continue using `if (!CloseWorkFile()) return;` without `await`.

**`NewFileArchive()`** — note the deliberate ordering difference from `NewDiskImage`:
1. Create `CreateFileArchive` dialog, show modal FIRST — **must be `await`ed**
2. If cancelled, return
3. **Then** call `CloseWorkFile()` — comment says "less distracting to do this now rather
   than earlier"
4. Determine extension and filter from `dialog.Kind` (Binary2→".bny", NuFX→".shk",
   Zip→".zip")
5. Prompt for save location via `StorageProvider.SaveFilePickerAsync` (WPF uses
   `SaveFileDialog` + `WinUtil.FILE_FILTER_*` — convert filter strings to
   `FilePickerFileType` objects with `Patterns` arrays)
6. Create the archive: `Binary2.CreateArchive()` / `NuFX.CreateArchive()` /
   `Zip.CreateArchive()`, then `StartTransaction` + `CommitTransaction(stream)`
7. Open via `DoOpenWorkFile(pathName, false)`

> **Both methods must be `async Task`** (or `async void` if bound via `RelayCommand`).
> For `RelayCommand` with `async void` lambdas, wrap the body in `try/catch` to
> prevent unhandled exceptions (same pattern as Iteration 6 G2).

Wire commands:
```csharp
NewDiskImageCommand = new RelayCommand(async () => {
    try { await mMainCtrl.NewDiskImage(); }
    catch (Exception ex) { /* log + show error */ }
});
NewFileArchiveCommand = new RelayCommand(async () => {
    try { await mMainCtrl.NewFileArchive(); }
    catch (Exception ex) { /* log + show error */ }
});
```

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/CreateDiskImage.axaml` |
| **Create** | `cp2_avalonia/CreateDiskImage.axaml.cs` |
| **Create** | `cp2_avalonia/CreateFileArchive.axaml` |
| **Create** | `cp2_avalonia/CreateFileArchive.axaml.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (new disk/archive methods) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire commands) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] File → New Disk Image opens the creation dialog
- [ ] Disk size radio buttons enable/disable filesystem and file type options correctly
- [ ] Custom size field validates numeric input
- [ ] Volume name validates per filesystem rules (length, characters)
- [ ] Volume number validates for appropriate range
- [ ] "Create" button is disabled when form is invalid
- [ ] Create produces a valid disk image file at the selected path
- [ ] The newly created image opens automatically in the application
- [ ] Settings (last used size, filesystem, type) persist across sessions
- [ ] File → New File Archive shows simple 3-option dialog
- [ ] Creating Binary II, NuFX, and ZIP archives all work
- [ ] Last selected archive type persists across sessions
- [ ] Cancel on either dialog does nothing
