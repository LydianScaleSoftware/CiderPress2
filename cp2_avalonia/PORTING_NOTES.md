# Avalonia Port — Design Divergences from WPF

This document tracks intentional design or behavioral changes made during the
WPF-to-Avalonia port that go beyond a straight 1:1 conversion. Each entry describes
what changed, how it differs from the WPF version, and what supporting code was
modified.

---

## 1. Block Editor: Consolidated CP/M Block Ordering into the Editor Dialog

**Iteration:** 11 (Sector Editor)

### WPF Behavior

The WPF version provides three separate menu items under the Actions menu:

- **Edit Sectors...** — opens the hex editor in sector mode (track + sector addressing,
  DOS 3.3 sector order)
- **Edit Blocks...** — opens the hex editor in block mode (ProDOS block addressing)
- **Edit Blocks (CP/M)...** — opens the hex editor in block mode with CP/M skewed
  block addressing (`SectorOrder.CPM_KBlock`)

Each menu item launches the same `EditSector` dialog with a different `SectorEditMode`
enum value (`Sectors`, `Blocks`, or `CPMBlocks`). The CP/M menu item has its own
`CanExecute` guard (`CanEditBlocksCPM`) that checks `CPM.IsSizeAllowed()` on the disk's
formatted length. Once the dialog is open, there is no way to switch between ProDOS and
CP/M block ordering without closing and reopening via a different menu item.

### Avalonia Behavior

The "Edit Blocks (CP/M)..." menu item has been **removed**. The Actions menu now has
only two items:

- **Edit Sectors...**
- **Edit Blocks...**

When the editor opens in block mode, a **Block Order combo box** appears in the Advanced
configuration panel (alongside the existing Sector Skew and Sector Format controls). The
combo is always visible in block mode. When the disk supports meaningful CP/M remapping
(both `CPM.IsSizeAllowed()` and `NumSectorsPerTrack == 16`), the combo is enabled and
presents "ProDOS" and "CP/M" options. Otherwise the combo shows "ProDOS" but is
**disabled**, matching the pattern used by the Sector Skew combo. In sector mode, the
combo and its label are hidden entirely since block ordering is not relevant.

The CP/M option requires 16-sector floppy images because `ReadBlock` only applies
sector-order remapping when `NumSectorsPerTrack == 16`. Block-only images (e.g., 800K
`.po` files) ignore the `SectorOrder` parameter entirely, so offering CP/M ordering on
those images would be misleading.

Selecting a different block order in the combo immediately re-reads the current block
using the new `SectorOrder`, just as changing the existing Sector Skew combo does. If
there are unsaved modifications (`IsDirty`), the user is prompted to confirm abandoning
changes before the switch proceeds; declining reverts the combo to the previous selection.

### Rationale

Having the ordering choice inside the editor is more discoverable and convenient — users
can compare ProDOS and CP/M views of the same block without closing the dialog. It also
simplifies the menu structure by eliminating a specialized item that most users would
rarely need.

### Files Changed

| File | Change |
|---|---|
| `cp2_avalonia/EditSector.axaml` | Added Block Order combo box (label + `ComboBox`) in the Advanced `GroupBox`, with `IsVisible` bound to `IsBlockOrderVisible` and `IsEnabled` bound to `IsBlockOrderEnabled`. `ItemsSource` set in code-behind (not AXAML binding — see Note below). |
| `cp2_avalonia/EditSector.axaml.cs` | Added `BlockOrderItem` class (Label + SectorOrder + `ToString()`), `BlockOrderList` property, `IsBlockOrderVisible`/`IsBlockOrderEnabled` properties, `PrepareBlockOrder()` method, and `BlockOrderCombo_SelectionChanged` handler with dirty-change guard. Constructor's `CPMBlocks` case now maps to `SectorEditMode.Blocks` with `SectorOrder.CPM_KBlock`. CP/M guard checks both `CPM.IsSizeAllowed()` and `NumSectorsPerTrack == 16`. Label display (`SetSectorDataLabel`) checks `mSectorOrder == CPM_KBlock` (not `mEditMode`). Added `using DiskArc.FS` for `CPM.IsSizeAllowed()`. |
| `cp2_avalonia/MainWindow.axaml` | Removed the "Edit Blocks (CP/M)..." `MenuItem`. |
| `cp2_avalonia/MainWindow.axaml.cs` | Removed `EditBlocksCPMCommand` property and its `RelayCommand` initialization. |
| `cp2_avalonia/MainController_Panels.cs` | Removed the `EditBlocksCPMCommand.RaiseCanExecuteChanged()` call. The `CanEditBlocksCPM` property remains (unused, harmless). |

### Backward Compatibility

The `SectorEditMode.CPMBlocks` enum value is retained. If passed to the constructor (e.g.
from future code or tests), it is silently remapped to `SectorEditMode.Blocks` with
`mSectorOrder = SectorOrder.CPM_KBlock`, so the dialog opens in block mode with CP/M
ordering pre-selected in the combo. This ensures no call-site breakage.

### Note: Avalonia ComboBox Gotchas

Two Avalonia-specific issues were discovered during this work:

1. **`DisplayMemberBinding` does not exist on Avalonia `ComboBox`** — it is a WPF/DataGrid
   concept. Avalonia silently ignores the attribute. Items must render via `ToString()`
   override or an `ItemTemplate`.
2. **AXAML `ItemsSource` binding to a plain `List<T>`** — if items are added after
   `DataContext` is set, Avalonia doesn't see the changes because `List<T>` does not
   implement `INotifyCollectionChanged`. Fix: set `combo.ItemsSource = list` directly in
   code-behind after populating the list.
