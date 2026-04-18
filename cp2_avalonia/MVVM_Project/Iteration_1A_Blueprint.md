# Iteration 1A Blueprint: Create MainViewModel & Move Properties

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §3.1, §6 Phase 1, §7.13.

---

## Goal

Create `MainViewModel` as a `ReactiveObject` and move **all bindable properties**
out of `MainWindow.axaml.cs` into it. After this iteration, `MainViewModel` exists
and owns all UI state, but `MainWindow` still sets `DataContext = this` temporarily.
The AXAML rebinding and controller wiring happen in Iteration 1B.

> **⚠️ CRITICAL CONSTRAINT:** `MainWindow` must keep `DataContext = this`
> throughout this entire iteration. Do NOT switch the DataContext to
> `MainViewModel`. Do NOT remove any properties or methods from
> `MainWindow.axaml.cs`. The ViewModel properties created here are
> **inert duplicates** — they are not exercised at runtime until Phase 1B.

---

## Prerequisites

- Iteration 0 is complete (ReactiveUI + DI packages installed, inner classes
  extracted to `Models/`, `App.Services` wired). In particular, `ConvItem`,
  `CenterInfoItem`, `PartitionListItem`, and `MetadataItem` already exist as
  standalone classes in `cp2_avalonia/Models/` — they are **not** inner classes
  of `MainWindow` anymore. The skeleton's `using cp2_avalonia.Models;` makes
  them available to the ViewModel.
- The application builds and runs correctly.

**Prerequisite check:** Before proceeding, verify that the four model classes
(`ConvItem`, `CenterInfoItem`, `PartitionListItem`, `MetadataItem`) exist in
`cp2_avalonia/Models/`. If they do not, **stop** — Iteration 0 must be completed
first. Do not extract them as part of this iteration.

**`MetadataItem` Avalonia dependency:** `MetadataItem.TextForeground` returns
`IBrush` (from `Avalonia.Media`). This is accepted for now — the `Models/`
namespace may carry Avalonia dependencies. If full model-layer unit testing
(without Avalonia runtime) becomes a goal, `TextForeground` should be moved
to a View-layer converter or value provider at that time.

---

## Step-by-Step Instructions

### Step 1: Create the ViewModels Directory

Create `cp2_avalonia/ViewModels/` if it doesn't already exist.

### Step 2: Create MainViewModel.cs

Create `cp2_avalonia/ViewModels/MainViewModel.cs` with the skeleton below.
All properties use `this.RaiseAndSetIfChanged(ref field, value)`.

```csharp
/*
 * Copyright 2026 faddenSoft
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

using AppCommon;

using Avalonia.Media;

using CommonUtil;

using DiskArc;
using DiskArc.Multi;

using FileConv;

using ReactiveUI;

using cp2_avalonia;
using cp2_avalonia.Models;

namespace cp2_avalonia.ViewModels {
    /// <summary>
    /// Primary ViewModel for the main application window.
    /// </summary>
    public class MainViewModel : ReactiveObject {
        /// <summary>
        /// Temporary reference to the controller. Removed in Phase 3B when
        /// controller logic is dissolved into the ViewModel and services.
        /// See MVVM_Notes.md §7.13.
        /// </summary>
        private MainController? mMainCtrl;

        // Constructor — initially empty; services added in later iterations.
        public MainViewModel() {
        }

        /// <summary>
        /// Sets the controller reference. Called by MainWindow after both
        /// the ViewModel and Controller are constructed. Temporary — will be
        /// removed in Phase 3B.
        /// </summary>
        public void SetController(MainController ctrl) {
            mMainCtrl = ctrl;
        }

        // --- Properties will be added in subsequent steps ---
    }
}
```

### Step 3: Move All Bindable Properties

Move every bindable property from `MainWindow.axaml.cs` to `MainViewModel`.
Each property that previously used the hand-rolled `OnPropertyChanged()` pattern
now uses `this.RaiseAndSetIfChanged(ref field, value)`.

**Do NOT remove the properties from `MainWindow.axaml.cs` yet** — that happens
in Iteration 1B when the DataContext is switched. In this iteration, you are
**creating the ViewModel properties** so the VM is ready.

Because `DataContext = this` remains in effect on `MainWindow`, the ViewModel
properties are **not exercised at runtime** in Phase 1A. Both classes will have
identically-named properties during this phase (intentional). Correctness
(default values, cross-property coupling, AppSettings persistence) will first
be visible in Phase 1B. Review all defaults carefully before proceeding.

Below is the complete list of properties to create on `MainViewModel`, grouped
by category. Use the exact property names listed — they must match the existing
AXAML `{Binding}` expressions.

#### 3a. Panel Visibility (4 properties)

```csharp
private bool mLaunchPanelVisible = true;
public bool LaunchPanelVisible {
    get => mLaunchPanelVisible;
    set => this.RaiseAndSetIfChanged(ref mLaunchPanelVisible, value);
}

private bool mMainPanelVisible = false;
public bool MainPanelVisible {
    get => mMainPanelVisible;
    set => this.RaiseAndSetIfChanged(ref mMainPanelVisible, value);
}

private bool mShowOptionsPanel = true;
public bool ShowOptionsPanel {
    get => mShowOptionsPanel;
    set {
        this.RaiseAndSetIfChanged(ref mShowOptionsPanel, value);
        ShowHideRotation = value ? 0 : 90;
    }
}

private double mShowHideRotation;
public double ShowHideRotation {
    get => mShowHideRotation;
    set => this.RaiseAndSetIfChanged(ref mShowHideRotation, value);
}
```

#### 3b. Debug Visibility (3 properties)

```csharp
private bool mShowDebugMenu;
public bool ShowDebugMenu {
    get => mShowDebugMenu;
    set => this.RaiseAndSetIfChanged(ref mShowDebugMenu, value);
}

private bool mIsDebugLogVisible;
public bool IsDebugLogVisible {
    get => mIsDebugLogVisible;
    set => this.RaiseAndSetIfChanged(ref mIsDebugLogVisible, value);
}

private bool mIsDropTargetVisible;
public bool IsDropTargetVisible {
    get => mIsDropTargetVisible;
    set => this.RaiseAndSetIfChanged(ref mIsDropTargetVisible, value);
}
```

#### 3c. Status Bar (2 properties)

```csharp
private string mCenterStatusText = string.Empty;
public string CenterStatusText {
    get => mCenterStatusText;
    set => this.RaiseAndSetIfChanged(ref mCenterStatusText, value);
}

private string mRightStatusText = string.Empty;
public string RightStatusText {
    get => mRightStatusText;
    set => this.RaiseAndSetIfChanged(ref mRightStatusText, value);
}
```

#### 3d. Version String (1 property)

This is a computed read-only property in the source (`=> GlobalAppVersion.AppVersion.ToString()`).
Keep it computed — no backing field or setter needed:

```csharp
public string ProgramVersionString => GlobalAppVersion.AppVersion.ToString();
```

#### 3e. ~~Layout~~ — Removed

`LeftPanelWidth` is a pure view concern — it reads/writes a Grid column width
directly from the control. Per Pre-Iteration-Notes §4, view-only state (window
placement, column widths) stays in `MainWindow.axaml.cs` and does not move to
the ViewModel.

#### 3f. Tree Collections (2 properties)

```csharp
public ObservableCollection<ArchiveTreeItem> ArchiveTreeRoot { get; } = new();
public ObservableCollection<DirectoryTreeItem> DirectoryTreeRoot { get; } = new();
```

All callers use `.Clear()` and `.Add()` — they never reassign the collection
reference. Init-only (no setter) prevents accidental disconnection from AXAML
bindings.

#### 3g. File List (2 properties)

```csharp
public ObservableCollection<FileListItem> FileList { get; } = new();

private FileListItem? mSelectedFileListItem;
public FileListItem? SelectedFileListItem {
    get => mSelectedFileListItem;
    set => this.RaiseAndSetIfChanged(ref mSelectedFileListItem, value);
}
```

> **Note:** `SelectedFileListItem` in the source delegates to
> `fileListDataGrid.SelectedItem` (a control accessor). The VM property is a
> placeholder in Phase 1A. In Iteration 1B, a two-way binding will connect
> the DataGrid's `SelectedItem` to this property. Until then, it is only
> accurate when set programmatically.

#### 3h. Recent Files (4 name/path properties + 2 computed show properties)

Read the actual `MainWindow.axaml.cs` to determine the exact number of recent
file slots and their property names. Create matching properties. The current
code has `RecentFileName1`, `RecentFilePath1`, `ShowRecentFile1`, etc. for up
to 2 visible slots (the menu items for slots 3-6 are populated differently).

`ShowRecentFile1` and `ShowRecentFile2` are **computed** properties in the source
(`=> !string.IsNullOrEmpty(mRecentFileName1)`). Do not create independent backing
fields for them — derive them from the name properties:

```csharp
private string mRecentFileName1 = string.Empty;
public string RecentFileName1 {
    get => mRecentFileName1;
    set {
        this.RaiseAndSetIfChanged(ref mRecentFileName1, value);
        this.RaisePropertyChanged(nameof(ShowRecentFile1));
    }
}

private string mRecentFilePath1 = string.Empty;
public string RecentFilePath1 {
    get => mRecentFilePath1;
    set => this.RaiseAndSetIfChanged(ref mRecentFilePath1, value);
}

public bool ShowRecentFile1 => !string.IsNullOrEmpty(mRecentFileName1);

// Repeat for RecentFileName2, RecentFilePath2, ShowRecentFile2
```

#### 3i. Converter Lists (4 properties)

The source uses `List<ConvItem>` with init-only semantics — callers populate
via `.Add()` and `.Sort()`, then call `OnPropertyChanged()` manually. The VM
keeps the settable pattern to allow full-list replacement in future phases,
but `InitImportExportConfig()` must be adapted: populate the backing field
`mImportConverters` directly via `.Add()`, then call
`this.RaisePropertyChanged(nameof(ImportConverters))` after population is
complete (matching the source's manual notification pattern).

```csharp
private List<ConvItem> mImportConverters = new();
public List<ConvItem> ImportConverters {
    get => mImportConverters;
    set => this.RaiseAndSetIfChanged(ref mImportConverters, value);
}

private List<ConvItem> mExportConverters = new();
public List<ConvItem> ExportConverters {
    get => mExportConverters;
    set => this.RaiseAndSetIfChanged(ref mExportConverters, value);
}

private ConvItem? mSelectedImportConverter;
public ConvItem? SelectedImportConverter {
    get => mSelectedImportConverter;
    set {
        mSelectedImportConverter = value;
        if (value != null) {
            AppSettings.Global.SetString(AppSettings.CONV_IMPORT_TAG, value.Tag);
        }
        this.RaisePropertyChanged();
    }
}

private ConvItem? mSelectedExportConverter;
public ConvItem? SelectedExportConverter {
    get => mSelectedExportConverter;
    set {
        mSelectedExportConverter = value;
        if (value != null && !IsExportBestChecked) {
            AppSettings.Global.SetString(AppSettings.CONV_EXPORT_TAG, value.Tag);
        }
        this.RaisePropertyChanged();
    }
}
```

> **Direct-field note:** `InitImportExportConfig()` and `PublishSideOptions()`
> set `mSelectedImportConverter` and `mSelectedExportConverter` **directly**
> (not via the property setters) to avoid triggering AppSettings writes during
> initialization and settings-change refresh. They then call
> `this.RaisePropertyChanged(nameof(SelectedImportConverter))` /
> `this.RaisePropertyChanged(nameof(SelectedExportConverter))` manually.
> Preserve this direct-field-assignment pattern in the VM.

#### 3j. Options Panel Toggles (20 properties)

These properties are backed by `AppSettings.Global` in the source — their getters
read from `AppSettings.Global.GetBool()` / `GetEnum()` and their setters write
to `AppSettings.Global.SetBool()` / `SetEnum()`. **Do not replace this with a
plain `RaiseAndSetIfChanged(ref field, value)` pattern** — that would silently
break settings persistence.

**Interim approach (until `ISettingsService` is introduced in Phase 3A):** Keep
the `AppSettings.Global` getters/setters verbatim from `MainWindow.axaml.cs`,
but replace `OnPropertyChanged()` with `this.RaisePropertyChanged()`.

**Notification semantics:** The source fires `OnPropertyChanged()` unconditionally
on every setter call, even when the value hasn't changed. The AppSettings-backed
properties below preserve this behavior by using `this.RaisePropertyChanged()`
(not `RaiseAndSetIfChanged`). For non-AppSettings properties that use
`RaiseAndSetIfChanged`, suppressing duplicate notifications is generally fine —
but verify against the source if a property's setter has side effects that
depend on always firing.

**Explicit property list (all 20):**

Add group (10):
- `IsChecked_AddExtract`, `IsChecked_ImportExport`, `IsChecked_AddCompress`,
  `IsChecked_AddRaw`, `IsChecked_AddRecurse`, `IsChecked_AddStripExt`,
  `IsChecked_AddStripPaths`, `IsChecked_AddPreserveADF`, `IsChecked_AddPreserveAS`,
  `IsChecked_AddPreserveNAPS`

Extract group (3):
- `IsChecked_ExtAddExportExt`, `IsChecked_ExtRaw`, `IsChecked_ExtStripPaths`

Extract preserve radio group (4):
- `IsChecked_ExtPreserveNone`, `IsChecked_ExtPreserveAS`,
  `IsChecked_ExtPreserveADF`, `IsChecked_ExtPreserveNAPS`

Other (3):
- `SelectedDDCPModeIndex`, `IsExportBestChecked`, `IsExportComboChecked`

**Cross-notification patterns to preserve:**
- `IsChecked_AddExtract` and `IsChecked_ImportExport` are mutually exclusive:
  each setter raises `this.RaisePropertyChanged()` for the other's name.
- `IsExportBestChecked` and `IsExportComboChecked` are mutually exclusive:
  same cross-notification pattern.
- `IsChecked_ExtPreserveNone/AS/ADF/NAPS` are a radio group: **each setter
  raises only for its own name** (same as the `IsChecked_AddCompress` example
  above). Cross-notification is handled by `PublishSideOptions()` when settings
  change externally. Do not add explicit cross-notification between the four
  setters.

Example for an AppSettings-backed property:

```csharp
public bool IsChecked_AddCompress {
    get => AppSettings.Global.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true);
    set {
        AppSettings.Global.SetBool(AppSettings.ADD_COMPRESS_ENABLED, value);
        this.RaisePropertyChanged();
    }
}
```

**`SelectedDDCPModeIndex`:** The source uses `GetBool`/`SetBool` (not
`SetEnum`) with `AppSettings.DDCP_ADD_EXTRACT`, and the setter includes a
change-guard so it only writes and fires when the value actually changes.
Preserve the full property with the guarded controller call:
```csharp
public int SelectedDDCPModeIndex {
    get => AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true) ? 0 : 1;
    set {
        bool isAddExtract = value == 0;
        if (isAddExtract != AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true)) {
            AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, isAddExtract);
            mMainCtrl?.ClearClipboardIfPending(); // TODO Phase 3B: IClipboardService
            this.RaisePropertyChanged();
        }
    }
}
```
**Radio-button `if (value)` guard pattern:** `IsChecked_AddExtract`,
`IsChecked_ImportExport`, `IsExportBestChecked`, and `IsExportComboChecked` all
use a structurally different guard than `SelectedDDCPModeIndex`. When Avalonia
deselects a RadioButton, it fires the setter with `value == false`. These setters
must only write to AppSettings when `value == true` — otherwise the deselect event
would overwrite the setting with the wrong value. Example:

```csharp
// Radio-button properties: only write AppSettings when value is true.
// Avalonia fires the setter with false when deselecting — ignore it.
public bool IsChecked_AddExtract {
    get => AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
    set {
        if (value) {
            AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, true);
            mMainCtrl?.ClearClipboardIfPending(); // TODO Phase 3B
        }
        this.RaisePropertyChanged();
        this.RaisePropertyChanged(nameof(IsChecked_ImportExport));
    }
}
```

Apply the same `if (value)` guard to:
- `IsChecked_ImportExport` (writes `DDCP_ADD_EXTRACT = false` when `value` is
  true; cross-notifies `IsChecked_AddExtract`; calls
  `mMainCtrl?.ClearClipboardIfPending()`)
- `IsExportBestChecked` (cross-notifies `IsExportComboChecked`; no controller call)
- `IsExportComboChecked` (cross-notifies `IsExportBestChecked`; no controller call)
- `IsChecked_ExtPreserveNone`, `IsChecked_ExtPreserveAS`,
  `IsChecked_ExtPreserveADF`, `IsChecked_ExtPreserveNAPS` (each cross-notifies
  only itself; no controller call. The `SetEnum` call is inside `if (value)`.)

#### 3k. Toolbar Brushes (3 properties + 2 static constants)

Move the static brush constants from `MainWindow.axaml.cs` alongside these
properties:

```csharp
private static readonly IBrush ToolbarHighlightBrush = Brushes.Green;
private static readonly IBrush ToolbarNohiBrush = Brushes.Transparent;

private IBrush mFullListBorderBrush = Brushes.Transparent;
public IBrush FullListBorderBrush {
    get => mFullListBorderBrush;
    set => this.RaiseAndSetIfChanged(ref mFullListBorderBrush, value);
}

// Repeat for DirListBorderBrush, InfoBorderBrush
```

#### 3l. Center Panel Display (8 properties)

`ShowCenterFileList` and `ShowCenterInfoPanel` are **computed inverses** of a
single backing field in the source (`mShowCenterInfo`). Do not create them as
independent settable properties — that would allow contradictory states (both
`true` simultaneously). Use a single backing field:

```csharp
private bool mShowCenterInfo;
public bool ShowCenterFileList => !mShowCenterInfo;
public bool ShowCenterInfoPanel => mShowCenterInfo;

/// <summary>
/// Sets the center panel mode. Guards against switching away from
/// info-only mode, updates toolbar brush highlights, and raises
/// notifications for ShowCenterFileList and ShowCenterInfoPanel.
/// </summary>
public void SetShowCenterInfo(bool showInfo) {
    if (HasInfoOnly && showInfo == false) {
        return; // Cannot switch to file list while in info-only mode.
    }
    mShowCenterInfo = showInfo;
    this.RaisePropertyChanged(nameof(ShowCenterFileList));
    this.RaisePropertyChanged(nameof(ShowCenterInfoPanel));
    if (mShowCenterInfo) {
        InfoBorderBrush = ToolbarHighlightBrush;
        FullListBorderBrush = DirListBorderBrush = ToolbarNohiBrush;
    } else if (ShowSingleDirFileList) {
        DirListBorderBrush = ToolbarHighlightBrush;
        FullListBorderBrush = InfoBorderBrush = ToolbarNohiBrush;
    } else {
        FullListBorderBrush = ToolbarHighlightBrush;
        DirListBorderBrush = InfoBorderBrush = ToolbarNohiBrush;
    }
}
```

> **Phase 2 command translation:** The source `SetShowCenterInfo(CenterPanelChange)`
> uses an enum with `Info`, `Files`, and `Toggle` values. The VM version takes a
> `bool`. When commands are migrated in Phase 2, translate as follows:
> - `ToggleInfoCommand` → `SetShowCenterInfo(!ShowCenterInfoPanel)`
> - `ShowInfoCommand` → `SetShowCenterInfo(true)`
> - `ShowFullListCommand` / `ShowDirListCommand` → `SetShowCenterInfo(false)`
>
> The `CenterPanelChange` enum remains a private
> member of `MainWindow` and does not need to move to the ViewModel.

`ShowSingleDirFileList`'s setter **cross-updates** `ShowCol_FileName` and
`ShowCol_PathName` in the source. Preserve this coupling:

```csharp
private bool mShowSingleDirFileList;
public bool ShowSingleDirFileList {
    get => mShowSingleDirFileList;
    set {
        this.RaiseAndSetIfChanged(ref mShowSingleDirFileList, value);
        ShowCol_FileName = value;
        ShowCol_PathName = !value;
    }
}
```

Also add `HasInfoOnly` and `PreferSingleDirList`, which are used by
`ConfigureCenterPanel()` (Step 4) but missing from the original list.
Both are `private` in the source but promoted to `public` on the VM:
`HasInfoOnly` must be settable by the controller in Phase 1B (since
`ConfigureCenterPanel` assigns it), and `PreferSingleDirList` is read
by `ConfigureCenterPanel` logic and may be set via options UI.

```csharp
private bool mHasInfoOnly;
public bool HasInfoOnly {
    get => mHasInfoOnly;
    set => this.RaiseAndSetIfChanged(ref mHasInfoOnly, value);
}

// PreferSingleDirList is AppSettings-backed (interim pattern, see 3j):
public bool PreferSingleDirList {
    get => AppSettings.Global.GetBool(AppSettings.FILE_LIST_PREFER_SINGLE, true);
    set {
        AppSettings.Global.SetBool(AppSettings.FILE_LIST_PREFER_SINGLE, value);
        this.RaisePropertyChanged();
    }
}
```

Remaining independent properties:

```csharp
private bool mIsFullListEnabled;
public bool IsFullListEnabled {
    get => mIsFullListEnabled;
    set => this.RaiseAndSetIfChanged(ref mIsFullListEnabled, value);
}

private bool mIsDirListEnabled;
public bool IsDirListEnabled {
    get => mIsDirListEnabled;
    set => this.RaiseAndSetIfChanged(ref mIsDirListEnabled, value);
}

private bool mIsResetSortEnabled;
public bool IsResetSortEnabled {
    get => mIsResetSortEnabled;
    set => this.RaiseAndSetIfChanged(ref mIsResetSortEnabled, value);
}
```

#### 3m. Column Visibility (6 properties)

**Important:** In the source, every `ShowCol_*` setter calls
`SetColumnVisible(headerString, value)`, which directly manipulates
`fileListDataGrid.Columns`. This is a View-side operation that **cannot exist
in the ViewModel**. The ViewModel property setters use only
`this.RaiseAndSetIfChanged()`. The View must observe these properties and call
`SetColumnVisible()` reactively (wired in Iteration 1B — e.g., via
`WhenAnyValue` subscriptions or AXAML `IsVisible` bindings on DataGrid columns).

```csharp
private bool mShowCol_FileName = true;
public bool ShowCol_FileName {
    get => mShowCol_FileName;
    set => this.RaiseAndSetIfChanged(ref mShowCol_FileName, value);
}

// Repeat for ShowCol_PathName, ShowCol_Format, ShowCol_RawLen,
// ShowCol_RsrcLen, ShowCol_TotalSize
```

#### 3n. Center Info Panel (11 properties)

```csharp
private string mCenterInfoText1 = string.Empty;
public string CenterInfoText1 {
    get => mCenterInfoText1;
    set => this.RaiseAndSetIfChanged(ref mCenterInfoText1, value);
}

private string mCenterInfoText2 = string.Empty;
public string CenterInfoText2 {
    get => mCenterInfoText2;
    set => this.RaiseAndSetIfChanged(ref mCenterInfoText2, value);
}

public ObservableCollection<CenterInfoItem> CenterInfoList { get; } = new();

private bool mShowDiskUtilityButtons;
public bool ShowDiskUtilityButtons {
    get => mShowDiskUtilityButtons;
    set => this.RaiseAndSetIfChanged(ref mShowDiskUtilityButtons, value);
}

private bool mShowPartitionLayout;
public bool ShowPartitionLayout {
    get => mShowPartitionLayout;
    set => this.RaiseAndSetIfChanged(ref mShowPartitionLayout, value);
}

public ObservableCollection<PartitionListItem> PartitionList { get; } = new();

private bool mShowNotes;
public bool ShowNotes {
    get => mShowNotes;
    set => this.RaiseAndSetIfChanged(ref mShowNotes, value);
}

// Notes.Note is in CommonUtil (using CommonUtil;)
public ObservableCollection<Notes.Note> NotesList { get; } = new();

public ObservableCollection<MetadataItem> MetadataList { get; } = new();

private bool mShowMetadata;
public bool ShowMetadata {
    get => mShowMetadata;
    set => this.RaiseAndSetIfChanged(ref mShowMetadata, value);
}

private bool mCanAddMetadataEntry;
public bool CanAddMetadataEntry {
    get => mCanAddMetadataEntry;
    set => this.RaiseAndSetIfChanged(ref mCanAddMetadataEntry, value);
}
```

All `ObservableCollection` properties above are init-only (no setter) — callers
use `.Clear()` and `.Add()`, never reassignment.

#### 3o. Tree Selection (placeholder properties for Phase 1B)

These are read-only control accessors in the source (e.g.,
`=> archiveTree?.SelectedItem as ArchiveTreeItem`). In the ViewModel they become
writable properties that the View will bind two-way in Iteration 1B.

**In Phase 1A these are placeholders** — the controller continues to read
`mMainWin.SelectedArchiveTreeItem` (the control-delegating property on
`MainWindow`), not the VM property. In Phase 1B, the controller will be
redirected to read from `mViewModel.SelectedArchiveTreeItem` per §7.13.

```csharp
private ArchiveTreeItem? mSelectedArchiveTreeItem;
public ArchiveTreeItem? SelectedArchiveTreeItem {
    get => mSelectedArchiveTreeItem;
    set => this.RaiseAndSetIfChanged(ref mSelectedArchiveTreeItem, value);
}

private DirectoryTreeItem? mSelectedDirectoryTreeItem;
public DirectoryTreeItem? SelectedDirectoryTreeItem {
    get => mSelectedDirectoryTreeItem;
    set => this.RaiseAndSetIfChanged(ref mSelectedDirectoryTreeItem, value);
}
```

### Step 4: Create Copies of Helper Methods

Create a copy of the following methods from `MainWindow.axaml.cs` on
`MainViewModel`. **Do NOT remove these methods from `MainWindow.axaml.cs` in
this iteration** — the controller still calls them via `mMainWin.XxxMethod()`.
The originals on `MainWindow` are deleted in Iteration 1B when the controller
is redirected to the ViewModel reference.

These methods manipulate only the VM-owned properties (collections, text, etc.)
and do not access Avalonia controls:

- `ClearCenterInfo()`
- `ClearTreesAndLists()`
- `InitImportExportConfig()`
- `PublishSideOptions()`
- `SetPartitionList(IMultiPart parts)`
- `SetNotesList(Notes notes)`
- `SetMetadataList(IMetadata metaObj)`
- `UpdateMetadata(string key, string value)`
- `AddMetadata(IMetadata.MetaEntry met, string value)`
- `RemoveMetadata(string key)`

**Removed from this list:**
- `PopulateRecentFilesMenu()` — this method directly manipulates `recentFilesMenu.Items`
  (a `MenuItem` control) and `mNativeRecentFilesItem.Menu` (macOS `NativeMenuItem`).
  It is entirely View-layer code and stays in `MainWindow.axaml.cs`. The VM portion
  (setting `RecentFileName1`, `RecentFilePath1`, etc.) is handled by the property
  setters in Step 3h. Defer native menu update wiring to Iteration 1B or 3B.
- `ConfigureCenterPanel()` — **not migrated in Phase 1A**. Its primary
  dependency, `SetShowCenterInfo()`, has already been added as a VM method in
  Step 3l. Defer the full migration of `ConfigureCenterPanel()` logic to
  Iteration 1B or Phase 3B, when the controller is dissolved. **Note:** The
  source calls `SetShowCenterInfo(CenterPanelChange)` (enum parameter); the
  VM version uses `SetShowCenterInfo(bool)`. When migrating, translate enum
  values to booleans per the Phase 2 command translation table in Step 3l.
- `PostNotification(string msg, bool success)` — this method directly
  manipulates Avalonia controls (`toastText.Text`, `toastBorder.Background`,
  `toastBorder.IsVisible`, `DispatcherTimer`). It is called from ~19 locations
  in `MainController.cs` via `mMainWin.PostNotification(...)`. It stays in
  `MainWindow.axaml.cs`. A VM-compatible mechanism (bindable notification
  model or `IViewActions` interface) will be designed in a later phase.
- `FileList_ScrollToTop()`, `FileList_SetSelectionFocus()`,
  `DirectoryTree_ScrollToTop()`, `ReapplyFileListSort()` — these are pure
  view-layer methods that manipulate Avalonia controls (`fileListDataGrid`,
  `DataGridColumn`, `directoryTree`). The controller calls them via
  `mMainWin.Method()`. They stay in `MainWindow.axaml.cs`. Phase 1B will
  specify how the controller continues to reach them (e.g., via an
  `IViewActions` interface or direct `MainWindow` reference).

**Important:** Read each method's body in `MainWindow.axaml.cs` before moving.
Some may reference Avalonia controls — those parts must stay in code-behind
(Iteration 1B will add interaction triggers or View-side handlers for those).
Only move logic that operates purely on properties and collections.

**Phase 1A inertness:** The VM copies of these methods are **dead code** during
Phase 1A. The controller continues to call the `MainWindow` originals via
`mMainWin.XxxMethod()`. The VM versions are not exercised until Phase 1B
redirects the controller to the ViewModel reference. This is expected.

**`InitImportExportConfig()` / `PublishSideOptions()` note:** Both methods set
the private backing fields `mSelectedImportConverter` and
`mSelectedExportConverter` **directly** (not via the property setters) to avoid
triggering AppSettings writes during initialization and settings-change refresh.
They then call `this.RaisePropertyChanged(nameof(SelectedImportConverter))` /
`this.RaisePropertyChanged(nameof(SelectedExportConverter))` manually. Preserve
this direct-field-assignment pattern in the VM. Also requires `using FileConv;`
for `ImportFoundry`/`ExportFoundry` (already in the skeleton).

### Step 5: Add CanExecute State Properties

These properties currently live on `MainController` and are used by command
`canExecute` predicates. In the controller, they are **computed** (read-only
getters that query `CurrentWorkObject`, DataGrid selection, tree selection,
etc. — no backing fields). On the ViewModel, they become plain settable
`bool` properties with `RaiseAndSetIfChanged`.

**Population mechanism (Phase 1B):** In Phase 1B, the controller will be
redirected to **assign** these VM properties (e.g.,
`mViewModel.IsFileOpen = mWorkTree != null`) rather than returning computed
results. In Phase 1A, these properties are inert placeholders with default
values (`false`) — they are not populated or exercised.

All properties below follow the same `bool` / `RaiseAndSetIfChanged` pattern.
In the controller, some are computed differently (e.g., `CanEditBlocks` calls
`CanAccessChunk()`, `IsANISelected` inspects `DataGrid.SelectedItems`), but
on the VM they are all simple stored state — the controller computes the value
and pushes it to the VM property.

Create them on `MainViewModel` now so they're ready for Iteration 2 (commands):

```csharp
private bool mIsFileOpen;
public bool IsFileOpen {
    get => mIsFileOpen;
    set => this.RaiseAndSetIfChanged(ref mIsFileOpen, value);
}

private bool mCanWrite;
public bool CanWrite {
    get => mCanWrite;
    set => this.RaiseAndSetIfChanged(ref mCanWrite, value);
}

private bool mAreFileEntriesSelected;
public bool AreFileEntriesSelected {
    get => mAreFileEntriesSelected;
    set => this.RaiseAndSetIfChanged(ref mAreFileEntriesSelected, value);
}

private bool mIsSingleEntrySelected;
public bool IsSingleEntrySelected {
    get => mIsSingleEntrySelected;
    set => this.RaiseAndSetIfChanged(ref mIsSingleEntrySelected, value);
}
```

Continue for ALL `canExecute`-relevant properties from `MainController`/
`MainController_Panels.cs`:
- `CanEditBlocks`, `CanEditSectors`, `HasChunks`
- `IsANISelected`, `IsDiskImageSelected`, `IsPartitionSelected`
- `IsDiskOrPartitionSelected`, `IsNibbleImageSelected`
- `IsFileSystemSelected`, `IsMultiFileItemSelected`
- `IsDefragmentableSelected`, `IsHierarchicalFileSystemSelected`
- `IsSelectedDirRoot`, `IsSelectedArchiveRoot`
- `IsClosableTreeSelected`

Read `MainController_Panels.cs` to get the exact names.

### Step 6: Build and Verify

1. Run `dotnet build cp2_avalonia/cp2_avalonia.csproj` — verify zero errors and
   no new warnings (particularly no CS8632 or similar null-safety warnings from
   newly added nullable properties).
2. Launch the application — verify it works unchanged (still using
   `DataContext = this` on `MainWindow`). This confirms the new code does not
   interfere, but does **not** prove the ViewModel properties are correct —
   they are not exercised at runtime in this phase.
3. Code-review checklist before declaring Phase 1A complete:
   - Every property name, type, and default value matches the source in
     `MainWindow.axaml.cs`.
   - Every AppSettings-backed property uses the interim pattern (Step 3j).
     Verify each AppSettings key constant (e.g., `AppSettings.ADD_COMPRESS_ENABLED`)
     against the source — a wrong constant compiles but silently reads/writes
     the wrong setting.
   - Every cross-property coupling is preserved (Steps 3a, 3h, 3l, 3m).
     Compare each cross-notification chain against the source's
     `OnPropertyChanged(nameof(...))` calls.
   - `ConfigureCenterPanel` / `SetShowCenterInfo` dependencies are correct.
   - Radio-button `if (value)` guards are present on all guarded
     properties (Step 3j): `IsChecked_AddExtract`, `IsChecked_ImportExport`,
     `IsExportBestChecked`, `IsExportComboChecked`, and the four
     `IsChecked_ExtPreserve*` properties.

**Expected result:** The `MainViewModel` class exists with all properties,
but no code references it yet. The application is functionally identical.
`SetController()` is **not** called during Phase 1A — it is wired in Phase 1B
when `MainWindow` constructs the ViewModel and connects it to the controller.
`MainWindow.axaml.cs` is not modified in this iteration.

---

## What This Enables

Iteration 1B will:
- Change `MainWindow.DataContext` to a `MainViewModel` instance
- Remove duplicate properties from `MainWindow.axaml.cs`
- Wire the interim controller reference (`MainViewModel` ↔ `MainController`)
- Update AXAML bindings if needed
