# Iteration 11 Blueprint: Sector Editor

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Port the hex sector/block editor dialog (`EditSector`). This is a specialized tool for
viewing and editing raw disk sectors or blocks in hexadecimal. It uses a DataGrid-like
display with 16 hex columns plus an ASCII text column.

---

## Prerequisites

- Iteration 10 is complete: settings and remaining dialogs working.
- Key WPF source files to read (read both FULLY):
  - `cp2_wpf/EditSector.xaml` — DataGrid layout with hex display
  - `cp2_wpf/EditSector.xaml.cs` (~1,102 lines) — complex editor logic

---

## Architecture Overview

The WPF sector editor uses a **DataGrid** to display 16 bytes per row in hex, with:
- A "row header" column showing the offset (e.g., `$0000`, `$0010`, etc.)
- 16 named columns (`C0` through `Cf`) — each showing one hex byte
- A "Text" column showing the ASCII interpretation
- Single-cell selection for editing individual bytes

The data model uses an inner class `SectorRow` (INotifyPropertyChanged) that wraps a
`byte[]` buffer. Each column property (`C0`–`Cf`) does a get/set on the specific byte
within the row's buffer slice.

The editor supports three modes: **Sectors** (track + sector), **Blocks** (block number),
and **CPM Blocks** (CP/M block number), with Prev/Next navigation, Read/Write buttons,
sector skew control, and text conversion options.

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/EditSector.axaml`

Read `cp2_wpf/EditSector.xaml`. Convert the layout to AXAML.

**Window:** SizeToContent "WidthAndHeight", `CanResize="False"` (WPF `ResizeMode="NoResize"`),
title set dynamically. Wire `Opened` event (not WPF `ContentRendered`) for initialization.

**Layout:** Grid with 2 columns × 4 rows.

**Column 0 — Data Display:**
- Label (`SectorDataLabel`) showing current location
- **DataGrid** (Name="sectorDataGrid"):
  - `ItemsSource={Binding SectorData}` (`List<SectorRow>` — NOT ObservableCollection;
    the WPF code raises PropertyChanged for the whole list when data changes)
  - `IsReadOnly=True` initially; set to false for write mode
  - `SelectionUnit=Cell`, `SelectionMode=Single`
  - 16 data columns named `C0` through `Cf`, each bound to the corresponding property
  - "Text" column (wider) bound to `AsText`
  - Row headers show the offset label via `RowLabel` binding
  - Mono font (`GeneralMonoFont`) at standard size
  - I/O error overlay: TextBlock with `IOErrorMsg` when visible

**Avalonia DataGrid differences:**
- `Avalonia.Controls.DataGrid` (from `Avalonia.Controls.DataGrid` NuGet — **already added
  to .csproj in Iteration 0**, no additional package reference needed)
- `AutoGenerateColumns=False` — same
- `SelectionUnit`: Use `DataGridSelectionUnit.Cell`. This IS supported in the Avalonia
  DataGrid API. See the **Cell selection fidelity** note below for testing guidance.
- For **row headers**: Avalonia DataGrid does NOT natively support row headers. Workarounds:
  - Option A: Add an extra "Offset" column (frozen, styled differently) — simplest
  - Option B: Use `LoadingRow` event to set `DataGridRow.Header`
  - Recommend **Option A** — add a `RowLabel` column as the first column, frozen
- Cell editing: The DataGrid is `IsReadOnly="True"` — hex editing is done through keyboard
  interception (`KeyDown` handler calling `PushDigit()`), not DataGrid cell editing mode.
  Confirm Avalonia DataGrid does not alter selection state on non-navigation keystrokes
  when `IsReadOnly="True"`.
- **`Visibility.Hidden` note:** The WPF code uses `Visibility.Hidden` (not `Collapsed`) for
  the DataGrid when showing I/O errors. Since the DataGrid and error TextBlock overlap in the
  same Grid cell, Avalonia `IsVisible = false` (equivalent to Collapsed) is acceptable.
- **RowLabel column styling (G-01):** The frozen RowLabel column should have a distinct
  (muted) background, be non-selectable if possible, and right-aligned — so it is visually
  distinct from the 16 editable data columns. If it is selectable, the selection handler
  must guard against `mCurCol` becoming −1 (see DisplayIndex offset in Step 3).
- **Cell selection fidelity (G-02):** The hex editor depends on accurate single-cell
  position tracking. Smoke-test Avalonia DataGrid cell selection early: verify
  `SelectedCellsChanged` fires exactly once per selection, `AddedCells` contains one cell,
  and keyboard-driven moves also fire the event. **Fallback if `SelectionUnit.Cell`
  doesn't work reliably:** Replace the DataGrid with a custom `ItemsControl` using a
  `UniformGrid` of `Border`+`TextBlock` cells with click-to-select logic. This is a
  significant rewrite but is the only alternative if cell-level selection events are
  unreliable.

```xml
<!-- Column 0: Offset (frozen) -->
<DataGridTextColumn Header="" Binding="{Binding RowLabel}"
    IsReadOnly="True" Width="60" />
<!-- Columns 1-16: Hex bytes (headers are single chars "0"-"F", NOT "00"-"0F") -->
<DataGridTextColumn Header="0" Binding="{Binding C0}" Width="28" />
<DataGridTextColumn Header="1" Binding="{Binding C1}" Width="28" />
<!-- ... through "F" bound to Cf ... -->
<!-- Column 17: ASCII text -->
<DataGridTextColumn Header="Text" Binding="{Binding AsText}"
    IsReadOnly="True" Width="*" />
```

**Column 1 — Controls:**
- **Section header backgrounds:** The "Location" and "Configuration" section labels in WPF
  use `{DynamicResource {x:Static SystemColors.GradientInactiveCaptionBrushKey}}`. Replace
  with a static color (e.g. `#E6EEF8`) or a themed `DynamicResource`, per earlier iterations.
- **Location section:** Track/Block number TextBox, Sector number TextBox (conditional
  visibility for sector mode), info labels
- **Navigation:** Prev/Next buttons
- **Operations:** Read, Write, Copy buttons. Write only enabled in edit mode.
- **Text Conversion:** 3 RadioButtons (High ASCII, Mac OS Roman, ISO Latin-1)
- **Sector Skew:** ComboBox with skew options
- **Sector Format:** Display-only text

**Event wiring:** WPF uses `PreviewKeyDown` (tunneling) on both the Window and the DataGrid.
Avalonia has no `PreviewKeyDown` XAML attribute. Wire `KeyDown` on the DataGrid for hex-editing
interception. For the Window-level Ctrl+C intercept, use `KeyDown` on the Window (or
`AddHandler(KeyDownEvent, handler, RoutingStrategies.Tunnel)` in code-behind if tunneling
is needed to pre-empt the DataGrid).

### Step 2: Port `SectorRow` Inner Class

Read the `SectorRow` class from `cp2_wpf/EditSector.xaml.cs`. This is the DataGrid row
data model.

```csharp
public class SectorRow : INotifyPropertyChanged {
    private byte[] mBuffer;
    private int mRowOffset;         // = RowIndex * NUM_COLS (16)
    private TxtConvMode mConvMode;  // NOT Func<byte, char>; uses Formatter.CharConvFunc
                                    // internally via static ModeToConverter()

    // Constructor: takes buffer, offset, row index, TxtConvMode (NOT Func<byte, char>)

    // 16 hex column properties (lowercase "x2" on get, property names C0-C9 then Ca-Cf):
    public string C0 {
        get => mBuffer[mRowOffset + 0].ToString("x2");
        set { /* ... parse & store ... */ }
    }
    // ... C1 through Cf follow same pattern ...

    // Row label — uppercase hex, 2 or 3 digits based on buffer size, NO "$" prefix:
    //   e.g., "00", "10", "1F0" (NOT "$0000" or "$0010")
    public string RowLabel { get; }

    // PushDigit / DoPushDigit — nibble-push hex editing (two keystrokes per byte):
    //   Called from PreviewKeyDown handler when user types 0-9 or A-F.
    //   First digit sets high nibble, second sets low nibble, then auto-advances.
    public void PushDigit(int col, byte digit) { /* delegates to DoPushDigit */ }

    // Refresh — re-raises PropertyChanged for all 16 column properties + AsText.
    //   Called after ReadFromDisk() to update the display.
    public void Refresh() { /* ... */ }

    // ASCII text column:
    public string AsText {
        get {
            // Build 16-char string using ModeToConverter(mConvMode) for each byte
        }
    }
}
```

Port this class to `cp2_avalonia/`. The `mConvMode` field uses `TxtConvMode` enum, mapped to
a `Formatter.CharConvFunc` delegate via the static `ModeToConverter()` method. Note: the
byte-editing path uses **PushDigit**, not DataGrid cell editing — users type hex digits
directly into the selected cell via keyboard input.

**Key enum arithmetic note:** `PushDigit()` callers use `(int)e.Key - Key.D0` and
`(int)e.Key - Key.A + 10` to compute nibble values. Avalonia's `Avalonia.Input.Key` enum
mirrors WPF's layout for `D0`–`D9` and `A`–`F`, so this arithmetic is expected to work.
Use explicit casts `(int)Key.D0` and `(int)Key.A` to avoid ambiguous operator resolution.
Verify against the Avalonia `Key` source if behavior is unexpected during testing.

### Step 3: Port `cp2_avalonia/EditSector.axaml.cs`

Read `cp2_wpf/EditSector.xaml.cs` (~1,100 lines) fully. This is a large file.

**Key structural elements:**

**Enums:**
- `SectorEditMode` { Sectors, Blocks, CPMBlocks } — determines whether UI shows
  track+sector or block number
- `TxtConvMode` { HighASCII, MOR, Latin } — text conversion modes

**Key properties:**
- `SectorData` (`List<SectorRow>`) — DataGrid source (NOT ObservableCollection)
- `SectorDataLabel` (string) — e.g., "Track 0 ($00), Sector 0 ($0)" or "Block 0 ($00)"
- `IOErrorMsg` / visibility properties — error display overlay
- Sector/block/track number TextBoxes with validation
- `IsDirty` — tracks whether buffer has unwritten modifications
- `IsWriteButtonEnabled` — combines `mIsEntryValid`, `IsDirty`, and writable state
- `SectorDataGridVisibility` / `IOErrorMsgVisibility` — Visibility enum (port to bool)

**Key inner types:**
- `SectorOrderItem` — { Label (string), Order (SectorOrder) } for the skew ComboBox
- `SectorOrderList` — `List<SectorOrderItem>` with 4 entries (DOS 3.3, ProDOS, CP/M, Physical)
- `EnableWriteFunc` — delegate type; MainController passes a func that closes child
  work objects before enabling write access

**Key methods:**

**`ReadFromDisk()`** — reads a sector/block from disk:
1. Calls `ConfirmDiscardChanges()` if buffer is dirty
2. Calls `mChunkAccess.ReadBlock()` or `mChunkAccess.ReadSector()` with `mSectorOrder`
3. On `BadBlockException`, fills buffer with 0xCC and shows I/O error overlay
4. Calls `row.Refresh()` on each SectorRow (NOT rebuilding the collection)
5. Sets `IsDirty = false`

**`WriteToDisk()`** — writes modified data back:
1. Calls `TryEnableWrites()` first (using the `EnableWriteFunc` delegate)
2. Calls `mChunkAccess.WriteBlock()` or `mChunkAccess.WriteSector()`
3. If block/sector differs from what was read, shows confirmation dialog
4. Sets `IsDirty = false`

**`CopyToClipboard()`** — copies hex dump to clipboard as text (uses `mFormatter.FormatHexDump`)

**`ConfirmDiscardChanges()`** — MessageBox asking to discard unwritten mods (OKCancel)

**`Window_Closing`** — calls `ConfirmDiscardChanges()` and cancels close if user declines

**`SectorDataGrid_PreviewKeyDown`** — keyboard hex editing:
- Arrow keys: MoveLeft/MoveRight/MoveUp/MoveDown (wrapping)
- Digit keys (0-9, A-F): Calls `SectorData[row].PushDigit(col, digit)` → two-keystroke
  nibble editing; auto-advances to next cell after second digit
- Tab: Focus to track/block number input
- Calls `SetPosition(col, row)` → `selectorDataGrid.SelectRowColAndFocus(row, col)`
  (WPF extension method → need Avalonia equivalent)

**`SectorDataGrid_SelectedCellsChanged`** — captures current row/col on cell selection

**Navigation:** Prev/Next buttons increment/decrement block or sector number and call
`ReadFromDisk()`. `SetPrevNextEnabled()` enables/disables based on current position.

**Text conversion:** Radio button changes update `mTxtConvMode`, then `UpdateTxtConv()`
sets `row.ConvMode` on each SectorRow and updates `mFormatter`.

**Sector order:** `PrepareSectorOrder()` initializes the ComboBox; `SectorOrderCombo_SelectionChanged`
changes `mSectorOrder` and calls `ReadFromDisk()`.

**Sector codec:** `PrepareSectorCodec()` sets `SectorCodecName` (display-only text, NOT
an interactive ComboBox). Shows the nibble codec name or "N/A".

**Porting changes:**
1. Namespace: `cp2_avalonia`
2. **Do NOT set `Owner = owner`** in the constructor. Remove the assignment entirely; pass
   the owner window via `ShowDialog<bool?>(owner)` at the call site in MainController.
3. `ContentRendered` event → override `OnOpened()` (or wire the `Opened` event in AXAML).
4. `Window_Closing` parameter: `CancelEventArgs` → `WindowClosingEventArgs`
   (`Avalonia.Controls`). `e.Cancel = true` still works. **However, since
   `ConfirmDiscardChanges()` becomes `async Task<bool>` (it shows an async dialog),
   `Window_Closing` must use an async-close guard pattern:**
   ```csharp
   private bool mUserConfirmedClose;

   private async void Window_Closing(object? sender, WindowClosingEventArgs e) {
       if (mUserConfirmedClose) return; // allow this re-triggered close
       if (mIsDirty) {
           e.Cancel = true; // prevent close synchronously
           bool discard = await ConfirmDiscardChanges();
           if (discard) {
               mUserConfirmedClose = true;
               Close(); // re-trigger Closing — allowed now due to guard
           }
       }
   }
   ```
   Without the guard flag, calling `Close()` after `await` re-triggers `Closing` while
   `mIsDirty` is still true, causing an infinite dialog loop.
5. Replace `Visibility` enum properties with `bool`:
   - `SectorDataGridVisibility` → `IsSectorDataGridVisible`
   - `IOErrorMsgVisibility` → `IsIOErrorMsgVisible`
   - `SectorVisibility` → `IsSectorVisible`
   - Comparisons like `IOErrorMsgVisibility == Visibility.Visible` → `IsIOErrorMsgVisible`
6. Replace WPF `Brush` / `SystemColors` → Avalonia `IBrush` (`Avalonia.Media`):
   - `private Brush mDefaultLabelColor = SystemColors.WindowTextBrush` →
     `IBrush mDefaultLabelColor = Brushes.Black` (or theme resource)
   - `private Brush mErrorLabelColor = Brushes.Red` →
     `IBrush mErrorLabelColor = Brushes.Red`
   - Property types `Brush` → `IBrush` for `TrackBlockLabelForeground`,
     `SectorLabelForeground`
7. `Mouse.OverrideCursor` → `Cursor = new Cursor(StandardCursorType.Wait)` / `null`
8. `MessageBox.Show(...)` → async Avalonia `MessageBox` helper using
   `MBButton`/`MBResult` from `Common/MessageBoxEnums.cs`. There are **6 call sites**:
   - `ConfirmDiscardChanges()` — modal OK/Cancel ("Discard unwritten modifications?")
   - `WriteToDisk()` — "Unable to write to this disk" error
   - `WriteToDisk()` — "Write failed" I/O error
   - `TryEnableWrites()` — "Failed to enable write access"
   - `ReadFromDisk()` / block-differs confirmation in `WriteToDisk()`
   - `EditBlocksSectors()` in MainController — "Disk sector format not recognized"

   All callers become `async Task`; event handlers use fire-and-forget with `try/catch`.
9. `SystemSounds.Exclamation.Play()` → remove the `Play()` call. Keep the error guard
   (`if (IsIOErrorMsgVisible) return;`).
10. `SystemColors.GradientInactiveCaptionBrushKey` (section header bg) → static color or
    themed `DynamicResource`, per earlier iterations.
11. `sectorDataGrid.SelectRowColAndFocus(row, col)` — write from scratch for Avalonia using
    `DataGrid.SelectedCells`, `DataGrid.ScrollIntoView()`, and `Focus()`.
12. `SectorDataGrid_SelectedCellsChanged` — Avalonia uses
    `DataGridSelectedCellsChangedEventArgs` (not WPF `SelectedCellsChangedEventArgs`).
    `e.AddedCells` collection type and `DataGridCellInfo` structure differ; adjust property
    access accordingly.
13. `Keyboard.Modifiers` → `e.KeyModifiers` (from event args, not static property).
    `ModifierKeys.None` → `KeyModifiers.None`; `ModifierKeys.Control` →
    `KeyModifiers.Control`.
14. `PreviewKeyDown` → `KeyDown` (or `AddHandler` with `RoutingStrategies.Tunnel` if needed).
15. `Clipboard.SetDataObject(clipObj)` →
    `await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(dumpText)`.
    `CopyToClipboard()` must become `async Task`. The `Window_KeyDown` Ctrl+C handler should
    use fire-and-forget: `_ = CopyToClipboard()`.
16. **DisplayIndex offset (RowLabel column):** The frozen RowLabel column at
    `DisplayIndex = 0` shifts all data columns by +1. In `SelectedCellsChanged`, subtract 1
    from `info.Column.DisplayIndex` to get the logical column index. In `SetPosition()` /
    `SelectRowColAndFocus()`, add 1 when addressing the DataGrid column. Guard against
    the RowLabel column being selected (`mCurCol` must never become −1).
17. `sectorOrderCombo.SelectedIndex = -1` → `sectorOrderCombo.SelectedItem = null`
    (Avalonia ComboBox does not reliably support `SelectedIndex = -1`).

### Step 4: Wire Sector Editor Commands

In `MainController.cs`, port the sector editor launcher. The WPF version has a **single**
method `EditBlocksSectors(EditSector.SectorEditMode editMode)` — NOT three separate methods.

The method:
1. Gets `IDiskImage` from `CurrentWorkObject` or via the `Partition`
2. Gets `IChunkAccess` from the disk image
3. Creates an `EnableWriteFunc` delegate that closes child work objects before enabling writes
4. Creates the dialog: `new EditSector(chunks, editMode, enableWriteFunc, mFormatter)`
   (4-parameter Avalonia constructor — the WPF version has 5 params with `mMainWin` as
   the first; remove the owner param). Do NOT pass owner to constructor;
   pass via `ShowDialog<bool?>(mMainWin)`.
5. After dialog closes: flushes the disk image and re-scans if writes were enabled
6. The WPF `Mouse.OverrideCursor = Cursors.Wait` / `null` around the re-scan becomes
   `mMainWin.Cursor = new Cursor(StandardCursorType.Wait)` / `null` in a try/finally.
7. The "Disk sector format not recognized" `MessageBox.Show()` → async `MessageBox` helper.

The method must become `async Task` because of `ShowDialog` and `MessageBox` calls.

```csharp
// Avalonia pattern — SINGLE async method, called with different editMode values:
public async Task EditBlocksSectors(EditSector.SectorEditMode editMode) {
    IDiskImage? diskImage = CurrentWorkObject as IDiskImage;
    // ... also checks for Partition ...
    IChunkAccess chunks = diskImage.ChunkAccess!;

    EditSector.EnableWriteFunc? ewFunc = null;
    if (!chunks.IsReadOnly) {
        ewFunc = delegate () {
            // Close children to enable write access
            return CloseSubTree(/* ... */);
        };
    }

    EditSector dialog = new EditSector(chunks, editMode, ewFunc, mFormatter);
    await dialog.ShowDialog<bool?>(mMainWin);

    // Post-dialog: flush disk image if writes were enabled
    if (diskImage != null && dialog.WritesEnabled) {
        try {
            mMainWin.Cursor = new Cursor(StandardCursorType.Wait);
            diskImage.Flush();
            // Re-scan if needed
        } finally {
            mMainWin.Cursor = null;
        }
    }
}
```

The menu wires three items to this single method with different mode arguments:
- Actions → Edit Blocks → `EditBlocksSectors(SectorEditMode.Blocks)`
- Actions → Edit Sectors → `EditBlocksSectors(SectorEditMode.Sectors)`
- Actions → Edit Blocks (CP/M) → `EditBlocksSectors(SectorEditMode.CPMBlocks)`

Wire with CanExecute guards (from WPF `CanEditBlocks`/`CanEditBlocksCPM`/`CanEditSectors`):
```csharp
EditBlocksCommand = new RelayCommand(
    async () => { try { await mMainCtrl.EditBlocksSectors(SectorEditMode.Blocks); }
                  catch (Exception ex) { Debug.WriteLine("EditBlocks failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && mMainCtrl.CanEditBlocks);

EditBlocksCPMCommand = new RelayCommand(
    async () => { try { await mMainCtrl.EditBlocksSectors(SectorEditMode.CPMBlocks); }
                  catch (Exception ex) { Debug.WriteLine("EditBlocksCPM failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && mMainCtrl.CanEditBlocksCPM);

EditSectorsCommand = new RelayCommand(
    async () => { try { await mMainCtrl.EditBlocksSectors(SectorEditMode.Sectors); }
                  catch (Exception ex) { Debug.WriteLine("EditSectors failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && mMainCtrl.CanEditSectors);
```

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/EditSector.axaml` |
| **Create** | `cp2_avalonia/EditSector.axaml.cs` (includes SectorRow class) |
| **Modify** | `cp2_avalonia/MainController.cs` (sector edit methods) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire sector edit commands) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Actions → Edit Blocks opens sector editor in block mode (block number input)
- [ ] Actions → Edit Sectors opens in sector mode (track + sector inputs)
- [ ] DataGrid shows 16 hex columns + offset + ASCII text
- [ ] Reading a valid block displays data correctly
- [ ] Reading an invalid block shows error overlay
- [ ] Mono font is used consistently
- [ ] Prev/Next buttons navigate between blocks/sectors
- [ ] Text conversion radio buttons update the ASCII column
- [ ] Sector skew ComboBox changes sector ordering
- [ ] Block number TextBox validates input
- [ ] Copy button puts hex dump on clipboard
- [ ] Write button is enabled only when data has been modified (if writable)
- [ ] Write saves changes back to the disk image
- [ ] Dialog is not resizable, sizes to content
