# Iteration 1A Developer Manual: Create MainViewModel & Move Properties

> **Iteration:** 1A
> **Blueprint:** `cp2_avalonia/MVVM_Project/Iteration_1A_Blueprint.md`
> **Architecture Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §3.1, §6 Phase 1A, §7.13

---

## Overview

This iteration creates the `MainViewModel` class — the single object that will
eventually own all bindable UI state for the main application window. You will
copy every bindable property out of `MainWindow.axaml.cs` into this new
ViewModel class, and you will copy several helper methods as well. You will also
add "canExecute" state properties that commands will use in Iteration 2.

**Nothing changes at runtime.** The application continues to use
`DataContext = this` on `MainWindow`, so the ViewModel properties you create
here are never exercised. They are **inert duplicates** — prepared and waiting
for Iteration 1B, which switches the DataContext and makes them live.

---

## Key Concepts for Newcomers

Before diving in, here are the MVVM and ReactiveUI concepts you'll encounter.

### What is MVVM?

MVVM (Model-View-ViewModel) is a pattern that separates your application into
three layers:

- **Model** — the data and business logic (libraries like `DiskArc`,
  `AppCommon`, etc.)
- **View** — the UI (AXAML files and their thin code-behind)
- **ViewModel** — an intermediary that holds all the UI state (properties,
  commands) and exposes it for the View to bind to

The key benefit: ViewModels don't know about Views. They don't reference
Avalonia controls, windows, or visual elements. This makes them testable,
reusable, and decoupled from the UI framework.

### What is ReactiveUI?

ReactiveUI is an MVVM framework that provides:

- **`ReactiveObject`** — a base class that implements `INotifyPropertyChanged`
  for you. Instead of hand-rolling property-change notifications, you call
  `this.RaiseAndSetIfChanged(ref backingField, value)` in your setters.
- **`ReactiveCommand`** — a replacement for `RelayCommand` (used in Iteration 2).
- **`WhenAnyValue`** — a way to observe property changes as reactive streams
  (used in later iterations).

In this iteration, the only ReactiveUI feature you'll use is `ReactiveObject`
and its `RaiseAndSetIfChanged` / `RaisePropertyChanged` methods.

### What is `RaiseAndSetIfChanged`?

In the current code, properties look like this:

```csharp
// Old pattern (hand-rolled)
private bool mSomeFlag;
public bool SomeFlag {
    get => mSomeFlag;
    set { mSomeFlag = value; OnPropertyChanged(); }
}
```

With ReactiveUI, they become:

```csharp
// New pattern (ReactiveUI)
private bool mSomeFlag;
public bool SomeFlag {
    get => mSomeFlag;
    set => this.RaiseAndSetIfChanged(ref mSomeFlag, value);
}
```

`RaiseAndSetIfChanged` does three things:
1. Checks if the new value is different from the old value
2. Sets the backing field if it is
3. Fires `PropertyChanged` if it did

This is slightly different from the old pattern, which fired unconditionally.
For most properties, suppressing duplicate notifications is fine. For properties
with side effects or AppSettings backing, we'll use `RaisePropertyChanged()`
instead (explained when you encounter them).

### What is `DataContext`?

In Avalonia (and WPF), every UI element has a `DataContext` property. When you
write `{Binding SomeProperty}` in AXAML, the framework looks for `SomeProperty`
on whatever object is set as `DataContext`.

Today, `MainWindow` sets `DataContext = this`, meaning all bindings resolve
against the `MainWindow` class itself. In Iteration 1B, we'll change this to
`DataContext = new MainViewModel()`, and bindings will resolve against the
ViewModel instead. That's why property names on the ViewModel must exactly
match the existing ones in `MainWindow.axaml.cs`.

---

## Goal

Create `MainViewModel` as a `ReactiveObject` and move **all bindable properties**
out of `MainWindow.axaml.cs` into it. After this iteration:

- `MainViewModel` exists in `cp2_avalonia/ViewModels/` and owns all UI state
- `MainWindow` still sets `DataContext = this` (unchanged)
- The application builds and runs identically to before
- Both `MainWindow` and `MainViewModel` have identically-named properties
  (this is intentional and temporary)

---

## Prerequisites

- **Iteration 0 is complete:** ReactiveUI and DI packages are installed, inner
  classes (`ConvItem`, `CenterInfoItem`, `PartitionListItem`, `MetadataItem`)
  have been extracted to `cp2_avalonia/Models/`, and `App.Services` is wired.
- **The application builds and runs correctly** on the current branch.

---

## Step 1: Create the ViewModels Directory

### What we are going to accomplish

Every MVVM project organizes its ViewModels into a dedicated folder. This is a
convention from MVVM_Notes.md §5 (Proposed Folder Structure) that keeps the
project navigable as it grows. The `ViewModels/` folder will eventually hold
`MainViewModel`, dialog ViewModels, and child ViewModels for panels.

In this step, you simply create the directory.

### To do that, follow these steps

1. In the `cp2_avalonia/` project folder, create a new directory called
   `ViewModels/`.
   - If you're in a terminal: `mkdir cp2_avalonia/ViewModels`
   - Or right-click `cp2_avalonia/` in VS Code → New Folder → `ViewModels`

2. That's it. No build needed — it's just a directory.

### Now that those are done, here's what changed

- A new empty `cp2_avalonia/ViewModels/` directory exists.
- No files were modified.
- No behavior changed.

---

## Step 2: Create MainViewModel.cs

### What we are going to accomplish

This is the foundational step of the entire MVVM migration. You are creating
`MainViewModel` — the class that will eventually own every piece of UI state
that `MainWindow.axaml.cs` currently holds.

The class inherits from `ReactiveObject` (from ReactiveUI), which gives it
automatic `INotifyPropertyChanged` support. This replaces the hand-rolled
`OnPropertyChanged()` implementation in `MainWindow`.

The skeleton includes:

- **A temporary `mMainCtrl` field** — This is the "interim wiring" described in
  MVVM_Notes.md §7.13. During Phases 1 and 2, the ViewModel needs to call into
  `MainController` for certain operations (like clipboard management). This
  reference is set via `SetController()` and will be **removed in Phase 3B**
  when the controller is dissolved entirely.

- **An empty constructor** — Services will be injected here in later iterations.

- **A `SetController()` method** — Called by `MainWindow` after both objects are
  constructed. This establishes the temporary ViewModel → Controller coupling.

**Why the temporary coupling?** The current architecture has ~3,900 lines of
business logic in `MainController`. We can't move it all at once. The interim
`mMainCtrl` reference lets the ViewModel delegate to the controller during the
transition. It's an intentional, documented compromise.

### To do that, follow these steps

1. Open (or create) `cp2_avalonia/ViewModels/MainViewModel.cs`.

2. Enter the following skeleton code exactly as shown. Pay attention to:
   - The `using` directives — they cover all types the properties will need
   - The `namespace cp2_avalonia.ViewModels` — this is the ViewModels namespace
   - The `: ReactiveObject` base class — this is what makes MVVM work
   - The copyright header — matches the project's license format

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

3. Build to verify: `dotnet build cp2_avalonia/cp2_avalonia.csproj`
   - Expected: zero errors. The file compiles but nothing references it yet.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/MainViewModel.cs`
- The class exists but is not referenced anywhere — it's dead code for now.
- `MainWindow.axaml.cs` was **not modified**.
- The application runs identically.

---

## Step 3: Move All Bindable Properties

### What we are going to accomplish

This is the largest step of the iteration. You will create **every bindable
property** from `MainWindow.axaml.cs` as a matching property on `MainViewModel`.

**Why duplicates?** Because `DataContext = this` stays on `MainWindow` throughout
this iteration, you cannot remove the original properties — the AXAML bindings
still resolve against `MainWindow`. You are building the ViewModel's property
surface so it's complete and ready for the DataContext switch in Iteration 1B.

The properties fall into several categories, each with its own pattern. The
subsections below (3a through 3o) walk through each category.

**Critical rule:** Property names must **exactly match** the names in
`MainWindow.axaml.cs`. If the existing property is `LaunchPanelVisible`, the VM
property must also be `LaunchPanelVisible`. Typos will cause silent binding
failures in Iteration 1B.

All properties are added to `MainViewModel.cs` inside the class body, replacing
the `// --- Properties will be added in subsequent steps ---` comment.

### To do that, follow these steps

Open `cp2_avalonia/ViewModels/MainViewModel.cs` and add the properties described
in each subsection below. Cross-reference each property against the source in
`MainWindow.axaml.cs` to verify names, types, and default values.

---

### Step 3a: Panel Visibility (4 properties)

#### What we are going to accomplish

The main window has two major visual states: a "launch panel" (shown when no
file is open) and a "main panel" (shown when a file is open). There's also a
collapsible options panel on the right side, with a rotation animation on its
show/hide chevron.

These four properties control those states. Note that `ShowOptionsPanel`'s
setter has a side effect — it updates `ShowHideRotation` to rotate the chevron
icon. This is a **cross-property coupling** that must be preserved exactly.

#### To do that, follow these steps

1. Open `MainWindow.axaml.cs` and search for `LaunchPanelVisible`,
   `MainPanelVisible`, `ShowOptionsPanel`, and `ShowHideRotation`. Confirm
   they exist with matching types and defaults.

2. Add the following to `MainViewModel.cs`:

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

3. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Four new properties on `MainViewModel`.
- The cross-property coupling (ShowOptionsPanel → ShowHideRotation) is preserved.
- Default values match the source: launch panel visible, main panel hidden,
  options panel shown, rotation at 0.

---

### Step 3b: Debug Visibility (3 properties)

#### What we are going to accomplish

These three boolean properties control the visibility of debug-oriented UI
elements: the Debug menu, a debug log panel, and a drop-target overlay.

#### To do that, follow these steps

1. Confirm these properties exist in `MainWindow.axaml.cs` with matching names.

2. Add to `MainViewModel.cs`:

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

3. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Three new visibility properties on `MainViewModel`.
- All default to `false` (debug features are hidden by default).

---

### Step 3c: Status Bar (2 properties)

#### What we are going to accomplish

The status bar at the bottom of the main window shows two text strings: one in
the center and one on the right. These are simple string properties.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

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

2. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Two new string properties on `MainViewModel`, both defaulting to empty strings.

---

### Step 3d: Version String (1 property)

#### What we are going to accomplish

The application version string is displayed in the UI (e.g., in the title bar
or about area). In the source, it's a **computed read-only property** — it has
no backing field and no setter. It just returns the version from
`GlobalAppVersion.AppVersion`.

We keep it computed on the ViewModel too. There's no need for
`RaiseAndSetIfChanged` because the version never changes at runtime.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

```csharp
public string ProgramVersionString => GlobalAppVersion.AppVersion.ToString();
```

2. **Do NOT remove** the corresponding property from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- One new read-only computed property on `MainViewModel`.

---

### Step 3e: Layout — Removed (nothing to do)

#### What we are going to accomplish

The blueprint explicitly notes that `LeftPanelWidth` is a **pure view concern**
(it reads/writes a Grid column width directly from a control). Per the project's
coding conventions, view-only state stays in `MainWindow.axaml.cs` and does not
move to the ViewModel.

#### To do that, follow these steps

1. **Do nothing.** Do not create a `LeftPanelWidth` property on the ViewModel.

#### Now that those are done, here's what changed

- Nothing. This is a deliberate exclusion.

---

### Step 3f: Tree Collections (2 properties)

#### What we are going to accomplish

The archive tree (left panel, showing the hierarchy of archives and disk images)
and the directory tree (below it, showing folders within the selected item) are
each backed by an `ObservableCollection`. An `ObservableCollection` automatically
notifies the UI when items are added or removed — the View doesn't need to poll.

These collections are **init-only** (no setter). All callers use `.Clear()` and
`.Add()` — they never replace the collection instance. This is important because
AXAML binds to the collection object once; if you replaced it with a new
instance, the binding would still point to the old collection.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

```csharp
public ObservableCollection<ArchiveTreeItem> ArchiveTreeRoot { get; } = new();
public ObservableCollection<DirectoryTreeItem> DirectoryTreeRoot { get; } = new();
```

2. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Two new collection properties on `MainViewModel`.
- They are empty by default and readonly (no setter).

---

### Step 3g: File List (2 properties)

#### What we are going to accomplish

The file list (center panel DataGrid) shows the files within the currently
selected archive/directory. It's backed by an `ObservableCollection<FileListItem>`.

There's also a `SelectedFileListItem` property. In the source, this property
delegates to `fileListDataGrid.SelectedItem` (a control accessor). On the
ViewModel, it becomes a plain settable property. In Phase 1A, this property is
a **placeholder** — it won't be connected to the DataGrid until Iteration 1B
adds a two-way binding.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

```csharp
public ObservableCollection<FileListItem> FileList { get; } = new();

private FileListItem? mSelectedFileListItem;
public FileListItem? SelectedFileListItem {
    get => mSelectedFileListItem;
    set => this.RaiseAndSetIfChanged(ref mSelectedFileListItem, value);
}
```

2. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- One init-only collection and one nullable selection property on `MainViewModel`.
- `SelectedFileListItem` is a placeholder — it's not wired to the DataGrid yet.

---

### Step 3h: Recent Files (4 name/path properties + 2 computed show properties)

#### What we are going to accomplish

The File menu shows recently opened files. The current implementation uses
numbered properties (`RecentFileName1`, `RecentFilePath1`, etc.) for up to 2
visible "quick access" slots. The `ShowRecentFile1` and `ShowRecentFile2`
properties are **computed** — they derive their value from whether the
corresponding file name is non-empty.

This is a **cross-property coupling**: when you set `RecentFileName1`, the
setter must also notify the UI that `ShowRecentFile1` may have changed. This is
done by calling `this.RaisePropertyChanged(nameof(ShowRecentFile1))` inside the
`RecentFileName1` setter.

**What is `RaisePropertyChanged`?** Unlike `RaiseAndSetIfChanged` (which sets a
field and notifies), `RaisePropertyChanged` **only notifies** — it doesn't set
anything. Use it when you need to tell the UI "this other property may have a
new value too."

#### To do that, follow these steps

1. Read `MainWindow.axaml.cs` to confirm the exact names and the number of
   recent file slots (the blueprint specifies 2 visible slots).

2. Add to `MainViewModel.cs`:

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

private string mRecentFileName2 = string.Empty;
public string RecentFileName2 {
    get => mRecentFileName2;
    set {
        this.RaiseAndSetIfChanged(ref mRecentFileName2, value);
        this.RaisePropertyChanged(nameof(ShowRecentFile2));
    }
}

private string mRecentFilePath2 = string.Empty;
public string RecentFilePath2 {
    get => mRecentFilePath2;
    set => this.RaiseAndSetIfChanged(ref mRecentFilePath2, value);
}

public bool ShowRecentFile2 => !string.IsNullOrEmpty(mRecentFileName2);
```

3. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Six new properties on `MainViewModel` (4 settable + 2 computed).
- Cross-property notifications are preserved: setting a file name triggers
  notification for the corresponding show-flag.

---

### Step 3i: Converter Lists (4 properties)

#### What we are going to accomplish

The options panel lets the user pick import and export converters from dropdown
lists. These lists are populated during initialization and don't change
afterward. The properties use `List<ConvItem>` (not `ObservableCollection`)
because callers populate them via `.Add()` and `.Sort()`, then fire a manual
property-change notification.

The **selected converter** properties have important side effects: when the
user picks a converter, the selection is persisted to `AppSettings`. But during
initialization (in `InitImportExportConfig()`), the code sets the backing field
**directly** (bypassing the setter) to avoid writing to AppSettings. This is a
subtle but critical pattern — the blueprint calls it the "direct-field note."

**What does this mean in practice?** When `InitImportExportConfig()` runs, it
does `mSelectedImportConverter = someValue;` (not
`SelectedImportConverter = someValue;`) and then manually calls
`this.RaisePropertyChanged(nameof(SelectedImportConverter))`. This avoids
triggering the setter's AppSettings write.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

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

2. Note that `SelectedImportConverter` and `SelectedExportConverter` use
   `this.RaisePropertyChanged()` (no `nameof`) — this is the parameterless
   overload that notifies for the calling property. They do **not** use
   `RaiseAndSetIfChanged` because they have custom setter logic.

3. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Four new properties on `MainViewModel`.
- Selected converter setters persist to AppSettings (matching the source).
- The direct-field-assignment pattern is preserved for initialization.

---

### Step 3j: Options Panel Toggles (20 properties)

#### What we are going to accomplish

This is the most nuanced set of properties. The options panel has ~20 toggle
switches (checkboxes and radio buttons) that control how add, extract, and
import/export operations behave. These properties have two special
characteristics:

1. **AppSettings-backed:** Their getters read from `AppSettings.Global` and
   their setters write to `AppSettings.Global`. They don't have simple backing
   fields — the settings store **is** the backing store. This means you cannot
   use `RaiseAndSetIfChanged(ref field, value)` because there's no `ref field`.
   Instead, you use `this.RaisePropertyChanged()` (notify without a field set).

2. **Radio-button guards:** Some properties represent radio-button groups.
   When Avalonia deselects a radio button, it fires the setter with
   `value == false`. If you wrote `false` to AppSettings, you'd overwrite the
   correct value. So radio-button setters must check `if (value)` before writing.

3. **Cross-notifications:** Mutually exclusive pairs (e.g., `IsChecked_AddExtract`
   and `IsChecked_ImportExport`) must notify each other when one changes.

**Why not use `RaiseAndSetIfChanged` here?** Because there's no backing field to
pass by `ref`. The "backing store" is `AppSettings.Global`, which is an external
dictionary. The getter queries it; the setter writes to it; and
`RaisePropertyChanged()` tells the UI to re-read.

**Interim approach:** The blueprint says to keep `AppSettings.Global` access
verbatim from the source until `ISettingsService` is introduced in Phase 3A.
This is pragmatic — it avoids introducing a new abstraction before the
infrastructure is ready.

#### To do that, follow these steps

1. Read each property in `MainWindow.axaml.cs` carefully before creating it on
   the ViewModel. Pay attention to:
   - Which `AppSettings` key constant it uses
   - Whether it has an `if (value)` guard (radio-button pattern)
   - Whether it cross-notifies another property
   - Whether it calls `mMainCtrl.Something()`

2. Add the following to `MainViewModel.cs`. The example below shows
   representative properties from each group — add **all 20** as listed.

**Simple checkbox (no guard, no cross-notification):**

```csharp
public bool IsChecked_AddCompress {
    get => AppSettings.Global.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true);
    set {
        AppSettings.Global.SetBool(AppSettings.ADD_COMPRESS_ENABLED, value);
        this.RaisePropertyChanged();
    }
}
```

Use this pattern for: `IsChecked_AddCompress`, `IsChecked_AddRaw`,
`IsChecked_AddRecurse`, `IsChecked_AddStripExt`, `IsChecked_AddStripPaths`,
`IsChecked_AddPreserveADF`, `IsChecked_AddPreserveAS`,
`IsChecked_AddPreserveNAPS`, `IsChecked_ExtAddExportExt`,
`IsChecked_ExtRaw`, `IsChecked_ExtStripPaths`.

**Radio-button with guard and cross-notification (`IsChecked_AddExtract` /
`IsChecked_ImportExport` pair):**

```csharp
public bool IsChecked_AddExtract {
    get => AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
    set {
        if (value) {
            AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, true);
            mMainCtrl?.ClearClipboardIfPending(); // TODO Phase 3B: IClipboardService
        }
        this.RaisePropertyChanged();
        this.RaisePropertyChanged(nameof(IsChecked_ImportExport));
    }
}

public bool IsChecked_ImportExport {
    get => !AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
    set {
        if (value) {
            AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, false);
            mMainCtrl?.ClearClipboardIfPending(); // TODO Phase 3B: IClipboardService
        }
        this.RaisePropertyChanged();
        this.RaisePropertyChanged(nameof(IsChecked_AddExtract));
    }
}
```

**Radio-button with guard (`IsExportBestChecked` / `IsExportComboChecked` pair):**

```csharp
public bool IsExportBestChecked {
    // Read the source to find the correct AppSettings key and default value.
    // Pattern: if (value) write to AppSettings; always cross-notify partner.
    get => AppSettings.Global.GetBool(AppSettings.CONV_EXPORT_BEST, true);
    set {
        if (value) {
            AppSettings.Global.SetBool(AppSettings.CONV_EXPORT_BEST, true);
        }
        this.RaisePropertyChanged();
        this.RaisePropertyChanged(nameof(IsExportComboChecked));
    }
}

public bool IsExportComboChecked {
    get => !AppSettings.Global.GetBool(AppSettings.CONV_EXPORT_BEST, true);
    set {
        if (value) {
            AppSettings.Global.SetBool(AppSettings.CONV_EXPORT_BEST, false);
        }
        this.RaisePropertyChanged();
        this.RaisePropertyChanged(nameof(IsExportBestChecked));
    }
}
```

**Extract preserve radio group (4 properties):**

Each uses `GetEnum`/`SetEnum` with `ExtractPreserveMode`. Each has an
`if (value)` guard. Each cross-notifies **only itself** (not the other three) —
external settings changes are handled by `PublishSideOptions()`.

```csharp
public bool IsChecked_ExtPreserveNone {
    get => AppSettings.Global.GetEnum(AppSettings.EXT_PRESERVE_MODE,
               AppSettings.ExtPreserveMode.None) == AppSettings.ExtPreserveMode.None;
    set {
        if (value) {
            AppSettings.Global.SetEnum(AppSettings.EXT_PRESERVE_MODE,
                AppSettings.ExtPreserveMode.None);
        }
        this.RaisePropertyChanged();
    }
}
// Repeat for IsChecked_ExtPreserveAS, IsChecked_ExtPreserveADF, IsChecked_ExtPreserveNAPS
// with the corresponding ExtPreserveMode enum value.
```

**`SelectedDDCPModeIndex`:**

This is a ComboBox index property with a guarded setter and a controller call:

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

3. For each property, verify the `AppSettings` key constant against the source
   code in `MainWindow.axaml.cs`. A wrong constant compiles but silently
   reads/writes the wrong setting — this is a subtle and hard-to-debug error.

4. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Twenty new AppSettings-backed properties on `MainViewModel`.
- Radio-button guards prevent Avalonia deselect events from corrupting settings.
- Cross-notification patterns are preserved for mutually exclusive pairs.
- Controller calls (`mMainCtrl?.ClearClipboardIfPending()`) are preserved with
  null-conditional operators (safe because `mMainCtrl` isn't set in Phase 1A).

---

### Step 3k: Toolbar Brushes (3 properties + 2 static constants)

#### What we are going to accomplish

The toolbar has three buttons (Full List, Dir List, Info) that highlight with a
green border to show which view mode is active. The brush properties control
this highlighting. Two static constants define the highlight and no-highlight
colors.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

```csharp
private static readonly IBrush ToolbarHighlightBrush = Brushes.Green;
private static readonly IBrush ToolbarNohiBrush = Brushes.Transparent;

private IBrush mFullListBorderBrush = Brushes.Transparent;
public IBrush FullListBorderBrush {
    get => mFullListBorderBrush;
    set => this.RaiseAndSetIfChanged(ref mFullListBorderBrush, value);
}

private IBrush mDirListBorderBrush = Brushes.Transparent;
public IBrush DirListBorderBrush {
    get => mDirListBorderBrush;
    set => this.RaiseAndSetIfChanged(ref mDirListBorderBrush, value);
}

private IBrush mInfoBorderBrush = Brushes.Transparent;
public IBrush InfoBorderBrush {
    get => mInfoBorderBrush;
    set => this.RaiseAndSetIfChanged(ref mInfoBorderBrush, value);
}
```

2. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Two static constants and three brush properties on `MainViewModel`.
- These are used by `SetShowCenterInfo()` (added in the next subsection).

---

### Step 3l: Center Panel Display (8 properties)

#### What we are going to accomplish

The center area of the main window switches between two modes: a file list view
and an info panel view. This is controlled by a single backing field
(`mShowCenterInfo`), which drives two computed properties
(`ShowCenterFileList` and `ShowCenterInfoPanel`) that are **inverses** of each
other. They must never both be true simultaneously.

The `SetShowCenterInfo()` method encapsulates the switching logic: it updates
the backing field, raises notifications for both computed properties, and updates
the toolbar brush highlights.

Additional properties in this group:

- **`ShowSingleDirFileList`** — when set, it cross-updates column visibility
  (`ShowCol_FileName` and `ShowCol_PathName`).
- **`HasInfoOnly`** — prevents switching away from info mode when no file list
  is available.
- **`PreferSingleDirList`** — an AppSettings-backed preference.
- **`IsFullListEnabled`**, **`IsDirListEnabled`**, **`IsResetSortEnabled`** —
  enable/disable state for toolbar buttons.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

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

private bool mShowSingleDirFileList;
public bool ShowSingleDirFileList {
    get => mShowSingleDirFileList;
    set {
        this.RaiseAndSetIfChanged(ref mShowSingleDirFileList, value);
        ShowCol_FileName = value;
        ShowCol_PathName = !value;
    }
}

private bool mHasInfoOnly;
public bool HasInfoOnly {
    get => mHasInfoOnly;
    set => this.RaiseAndSetIfChanged(ref mHasInfoOnly, value);
}

public bool PreferSingleDirList {
    get => AppSettings.Global.GetBool(AppSettings.FILE_LIST_PREFER_SINGLE, true);
    set {
        AppSettings.Global.SetBool(AppSettings.FILE_LIST_PREFER_SINGLE, value);
        this.RaisePropertyChanged();
    }
}

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

2. **Important cross-property note:** `ShowSingleDirFileList`'s setter sets
   `ShowCol_FileName` and `ShowCol_PathName` (defined in Step 3m below). Make
   sure Step 3m is added before building, or you'll get compile errors.

3. **Phase 2 translation note:** The source uses a `CenterPanelChange` enum
   with `Info`, `Files`, and `Toggle` values. The VM's `SetShowCenterInfo(bool)`
   takes a simple boolean. When commands are migrated in Phase 2:
   - `ToggleInfoCommand` → `SetShowCenterInfo(!ShowCenterInfoPanel)`
   - `ShowInfoCommand` → `SetShowCenterInfo(true)`
   - `ShowFullListCommand` / `ShowDirListCommand` → `SetShowCenterInfo(false)`

4. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Eight new properties and one method on `MainViewModel`.
- Center panel display logic is fully encapsulated in the ViewModel.
- Toolbar brush highlighting is wired into `SetShowCenterInfo()`.

---

### Step 3m: Column Visibility (6 properties)

#### What we are going to accomplish

The file list DataGrid has columns that can be shown or hidden. In the source,
each `ShowCol_*` setter calls `SetColumnVisible(headerString, value)`, which
directly manipulates the `fileListDataGrid.Columns` collection — this is a
**View-side operation** that cannot exist in the ViewModel.

On the ViewModel, the setters use only `RaiseAndSetIfChanged`. The View must
observe these properties and call `SetColumnVisible()` reactively (wired in
Iteration 1B via `WhenAnyValue` subscriptions or AXAML bindings).

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

```csharp
private bool mShowCol_FileName = true;
public bool ShowCol_FileName {
    get => mShowCol_FileName;
    set => this.RaiseAndSetIfChanged(ref mShowCol_FileName, value);
}

private bool mShowCol_PathName;
public bool ShowCol_PathName {
    get => mShowCol_PathName;
    set => this.RaiseAndSetIfChanged(ref mShowCol_PathName, value);
}

private bool mShowCol_Format;
public bool ShowCol_Format {
    get => mShowCol_Format;
    set => this.RaiseAndSetIfChanged(ref mShowCol_Format, value);
}

private bool mShowCol_RawLen;
public bool ShowCol_RawLen {
    get => mShowCol_RawLen;
    set => this.RaiseAndSetIfChanged(ref mShowCol_RawLen, value);
}

private bool mShowCol_RsrcLen;
public bool ShowCol_RsrcLen {
    get => mShowCol_RsrcLen;
    set => this.RaiseAndSetIfChanged(ref mShowCol_RsrcLen, value);
}

private bool mShowCol_TotalSize;
public bool ShowCol_TotalSize {
    get => mShowCol_TotalSize;
    set => this.RaiseAndSetIfChanged(ref mShowCol_TotalSize, value);
}
```

2. **Key difference from source:** The source setters call
   `SetColumnVisible()`. The ViewModel setters do **not** — that's a View
   concern. Do not add any DataGrid manipulation here.

3. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Six new column visibility properties on `MainViewModel`.
- The View-side `SetColumnVisible()` logic stays in `MainWindow.axaml.cs`.

---

### Step 3n: Center Info Panel (11 properties)

#### What we are going to accomplish

When the center panel is in "info" mode, it shows text descriptions, lists
of metadata entries, partition layouts, and notes about the opened file.
These properties provide the data for that display.

The `ObservableCollection` properties are init-only (no setter), matching the
pattern from Step 3f. The boolean visibility properties control which sections
of the info panel are shown.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

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

2. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Eleven new properties on `MainViewModel` (5 settable booleans, 2 strings,
  4 init-only collections).
- The info panel data model is fully represented on the ViewModel.

---

### Step 3o: Tree Selection (2 placeholder properties)

#### What we are going to accomplish

In the source, `SelectedArchiveTreeItem` and `SelectedDirectoryTreeItem` are
read-only control accessors — they delegate to `archiveTree?.SelectedItem`.
On the ViewModel, they become writable properties that the View will bind
two-way in Iteration 1B.

**In Phase 1A, these are placeholders.** The controller continues to read
`mMainWin.SelectedArchiveTreeItem` (the control-delegating property on
`MainWindow`), not the VM property. In Phase 1B, the controller will be
redirected to read from `mViewModel.SelectedArchiveTreeItem`.

#### To do that, follow these steps

1. Add to `MainViewModel.cs`:

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

2. **Do NOT remove** the corresponding properties from `MainWindow.axaml.cs`.

#### Now that those are done, here's what changed

- Two new selection placeholder properties on `MainViewModel`.
- These will become live two-way bindings in Iteration 1B.

---

### Step 3 — Summary

At this point, you've added all bindable properties to `MainViewModel`. Build
the project now to catch any typos or missing `using` directives:

```
dotnet build cp2_avalonia/cp2_avalonia.csproj
```

Expected: zero errors. If you see errors, the most likely causes are:
- Missing `using` directives (the skeleton in Step 2 includes all needed ones)
- Properties referencing types from Step 3m before they're defined (reorder if
  needed)
- Typos in `AppSettings` key constants (compare against the source)

---

## Step 4: Create Copies of Helper Methods

### What we are going to accomplish

Several methods in `MainWindow.axaml.cs` operate purely on the properties and
collections you just created — they clear lists, populate converter dropdowns,
sync metadata, etc. These methods need to exist on the ViewModel so they can
operate on the ViewModel's properties when the DataContext switches.

**You are creating copies, not moving.** The originals stay in
`MainWindow.axaml.cs` because the controller still calls them via
`mMainWin.XxxMethod()`. The VM copies are **dead code** in Phase 1A. In
Iteration 1B, the controller will be redirected to call the ViewModel versions,
and the `MainWindow` originals will be deleted.

**Methods NOT included:** The blueprint explicitly excludes several methods:

- **`PopulateRecentFilesMenu()`** — directly manipulates `MenuItem` controls
  and native macOS menu items. This is pure View code.
- **`ConfigureCenterPanel()`** — depends on `SetShowCenterInfo(CenterPanelChange)`
  with an enum parameter. The VM has `SetShowCenterInfo(bool)`. Full migration
  deferred to Iteration 1B or Phase 3B.
- **`PostNotification()`** — directly manipulates toast controls and timers.
  Pure View code.
- **`FileList_ScrollToTop()`**, **`FileList_SetSelectionFocus()`**,
  **`DirectoryTree_ScrollToTop()`**, **`ReapplyFileListSort()`** — manipulate
  Avalonia controls. Pure View code.

### To do that, follow these steps

1. Open `MainWindow.axaml.cs` and locate each method listed below. Read the
   method body carefully.

2. For each method, **copy it** to `MainViewModel.cs` with these changes:
   - Replace any `OnPropertyChanged()` calls with `this.RaisePropertyChanged()`
   - Replace references to `mMainCtrl` with the ViewModel's `mMainCtrl` field
     (which may be null — use `mMainCtrl?.Method()`)
   - Ensure all referenced properties/fields exist on the ViewModel (they
     should, from Step 3)
   - If any line references an Avalonia control (e.g., `fileListDataGrid`),
     **do not copy that line** — leave a `// TODO: View-side operation` comment
     or omit it. Most of the listed methods should be control-free.

3. The methods to copy:

   - **`ClearCenterInfo()`** — clears info text, hides metadata/notes/partitions
   - **`ClearTreesAndLists()`** — clears all tree and list collections
   - **`InitImportExportConfig()`** — populates import/export converter lists.
     Uses `ImportFoundry` and `ExportFoundry` from `FileConv`. Sets backing
     fields directly (not via setters) to avoid AppSettings writes, then calls
     `this.RaisePropertyChanged(nameof(...))` manually.
   - **`PublishSideOptions()`** — refreshes all options panel properties from
     AppSettings. Sets `mSelectedImportConverter` and `mSelectedExportConverter`
     directly (same direct-field pattern as `InitImportExportConfig`).
   - **`SetPartitionList(IMultiPart parts)`** — populates `PartitionList`
     collection
   - **`SetNotesList(Notes notes)`** — populates `NotesList` collection
   - **`SetMetadataList(IMetadata metaObj)`** — populates `MetadataList`
     collection
   - **`UpdateMetadata(string key, string value)`** — updates a single metadata
     entry in `MetadataList`
   - **`AddMetadata(IMetadata.MetaEntry met, string value)`** — adds a metadata
     entry to `MetadataList`
   - **`RemoveMetadata(string key)`** — removes a metadata entry from
     `MetadataList`

4. **Do NOT remove** the original methods from `MainWindow.axaml.cs`.

5. Build again: `dotnet build cp2_avalonia/cp2_avalonia.csproj`
   - Expected: zero errors. The methods compile but are never called.

### Now that those are done, here's what changed

- **Modified file:** `cp2_avalonia/ViewModels/MainViewModel.cs` (new methods)
- Ten helper methods now exist on `MainViewModel` as dead code.
- `MainWindow.axaml.cs` was **not modified**.
- No runtime behavior changed.

---

## Step 5: Add CanExecute State Properties

### What we are going to accomplish

In the current architecture, commands check whether they should be enabled by
querying properties on `MainController` — things like `IsFileOpen`, `CanWrite`,
`AreFileEntriesSelected`, etc. These are **computed getters** on the controller:
they dynamically query the `WorkTree`, DataGrid selection state, tree selection,
etc.

On the ViewModel, they become **simple stored booleans** with
`RaiseAndSetIfChanged`. Why? Because in MVVM, the ViewModel owns state — it
doesn't reach into controllers or controls to compute it on the fly. Instead,
the controller (during the transition) or services (after Phase 3B) will
**push** updated values into these properties whenever the underlying state
changes.

This is a fundamental shift: **from pull (controller computes on demand) to push
(someone sets the VM property, and commands react automatically).**

In Iteration 2, these properties will be used with `WhenAnyValue` to create
`ReactiveCommand` instances whose `CanExecute` updates automatically when these
properties change. For example:

```csharp
// In Iteration 2 (not now):
OpenCommand = ReactiveCommand.Create(DoOpen);
DeleteCommand = ReactiveCommand.Create(DoDelete,
    this.WhenAnyValue(x => x.IsFileOpen, x => x.CanWrite, x => x.AreFileEntriesSelected,
        (open, write, selected) => open && write && selected));
```

**In Phase 1A, these are inert placeholders** — they default to `false` and
nobody sets or reads them.

### To do that, follow these steps

1. Open `MainController.cs` and `MainController_Panels.cs`. Search for
   properties used in command `canExecute` predicates. The blueprint lists
   all of them; verify against the source.

2. Add the following to `MainViewModel.cs`. All follow the same pattern:

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

3. Continue for **all** canExecute-relevant properties from the controller.
   Read `MainController.cs` and `MainController_Panels.cs` to get exact names.
   The complete list from the blueprint:

   - `IsFileOpen`
   - `CanWrite`
   - `AreFileEntriesSelected`
   - `IsSingleEntrySelected`
   - `CanEditBlocks`
   - `CanEditSectors`
   - `HasChunks`
   - `IsANISelected`
   - `IsDiskImageSelected`
   - `IsPartitionSelected`
   - `IsDiskOrPartitionSelected`
   - `IsNibbleImageSelected`
   - `IsFileSystemSelected`
   - `IsMultiFileItemSelected`
   - `IsDefragmentableSelected`
   - `IsHierarchicalFileSystemSelected`
   - `IsSelectedDirRoot`
   - `IsSelectedArchiveRoot`
   - `IsClosableTreeSelected`

   Each one follows the same `bool` + `RaiseAndSetIfChanged` pattern shown above.

4. **Do NOT remove** the corresponding properties from `MainController.cs` /
   `MainController_Panels.cs`.

5. Build again: `dotnet build cp2_avalonia/cp2_avalonia.csproj`

### Now that those are done, here's what changed

- **Modified file:** `cp2_avalonia/ViewModels/MainViewModel.cs` (new properties)
- ~19 new boolean properties on `MainViewModel`, all defaulting to `false`.
- These are inert — nobody reads or writes them during Phase 1A.
- They prepare for Iteration 2 (command migration) where `ReactiveCommand`
  will observe them via `WhenAnyValue`.

---

## Step 6: Build and Verify

### What we are going to accomplish

This is the final validation step. You'll confirm that the application builds
without errors, runs without changes, and that the new ViewModel code doesn't
interfere with existing behavior.

Since `DataContext = this` remains on `MainWindow`, the ViewModel properties are
**not exercised at runtime**. You're verifying that the new code compiles
correctly and doesn't accidentally break anything.

### To do that, follow these steps

1. **Build:**
   ```
   dotnet build cp2_avalonia/cp2_avalonia.csproj
   ```
   - Expected: zero errors.
   - Watch for warnings, particularly:
     - **CS8632** (nullable annotation in non-nullable context) — if you see
       these on newly added nullable properties, add `#nullable enable` to the
       file or ensure the project's nullable setting covers it.
     - **CS0169** (field never used) — expected for backing fields; acceptable
       in Phase 1A since the properties are dead code.

2. **Launch the application:**
   - Run it normally (e.g., `dotnet run --project cp2_avalonia/cp2_avalonia.csproj`
     or press F5 in VS Code).
   - Verify the launch panel appears.
   - Open a test file — verify file list, info panel, and options panel all
     render correctly.
   - Toggle options panel checkboxes — verify they persist (close and reopen).
   - This confirms the existing `MainWindow.axaml.cs` properties still work.

3. **Code-review checklist** (do this carefully before declaring complete):

   - [ ] Every property name, type, and default value matches the source in
         `MainWindow.axaml.cs`.
   - [ ] Every AppSettings-backed property uses the interim pattern (Step 3j) —
         getter reads from `AppSettings.Global`, setter writes to it, setter
         calls `this.RaisePropertyChanged()`.
   - [ ] Every AppSettings key constant (e.g., `AppSettings.ADD_COMPRESS_ENABLED`)
         is correct — compare against the source. Wrong constants compile but
         silently read/write the wrong setting.
   - [ ] Every cross-property coupling is preserved:
     - `ShowOptionsPanel` → `ShowHideRotation` (Step 3a)
     - `RecentFileName1` → `ShowRecentFile1` (Step 3h)
     - `RecentFileName2` → `ShowRecentFile2` (Step 3h)
     - `ShowSingleDirFileList` → `ShowCol_FileName`, `ShowCol_PathName` (Step 3l)
     - `SetShowCenterInfo()` → toolbar brushes (Step 3l)
     - `IsChecked_AddExtract` ↔ `IsChecked_ImportExport` (Step 3j)
     - `IsExportBestChecked` ↔ `IsExportComboChecked` (Step 3j)
   - [ ] Radio-button `if (value)` guards are present on all guarded properties:
     - `IsChecked_AddExtract`
     - `IsChecked_ImportExport`
     - `IsExportBestChecked`
     - `IsExportComboChecked`
     - `IsChecked_ExtPreserveNone`
     - `IsChecked_ExtPreserveAS`
     - `IsChecked_ExtPreserveADF`
     - `IsChecked_ExtPreserveNAPS`
   - [ ] `InitImportExportConfig()` and `PublishSideOptions()` use direct field
         assignment (not property setters) for selected converters.
   - [ ] `SetController()` is defined but **never called** in Phase 1A.

### Now that those are done, here's what changed

- **New file:** `cp2_avalonia/ViewModels/MainViewModel.cs` — contains all
  bindable properties, helper methods, and canExecute state properties.
- **New directory:** `cp2_avalonia/ViewModels/`
- **No existing files were modified.**
- **Application behavior is identical** — the ViewModel exists but is not
  referenced by any other code.

---

## What This Enables (Looking Ahead)

With `MainViewModel` complete and all properties in place, Iteration 1B will:

1. **Switch `DataContext`** — `MainWindow` will create a `MainViewModel` instance
   and set `DataContext = viewModel` instead of `DataContext = this`.
2. **Remove duplicate properties** from `MainWindow.axaml.cs` — the ViewModel
   versions become the live ones.
3. **Wire the controller** — `MainWindow` will call `viewModel.SetController(mMainCtrl)`
   so the ViewModel can delegate to the controller during the transition.
4. **Update AXAML bindings** if needed (most should work unchanged because
   property names match).

After Iteration 1B, the MVVM pattern is structurally in place: View binds to
ViewModel, ViewModel holds state, Controller provides business logic. Iteration
2 will then migrate all 51 commands from `RelayCommand` to `ReactiveCommand`.

---

## Quick Reference: Files Modified in This Iteration

| File | Action |
|---|---|
| `cp2_avalonia/ViewModels/` (directory) | Created |
| `cp2_avalonia/ViewModels/MainViewModel.cs` | Created |
| `MainWindow.axaml.cs` | **Not modified** |
| `MainController.cs` | **Not modified** |
| `MainController_Panels.cs` | **Not modified** |

---

## Quick Reference: Property Count Summary

| Category | Count | Pattern |
|---|---|---|
| Panel visibility | 4 | `RaiseAndSetIfChanged` |
| Debug visibility | 3 | `RaiseAndSetIfChanged` |
| Status bar | 2 | `RaiseAndSetIfChanged` |
| Version string | 1 | Computed (read-only) |
| Tree collections | 2 | Init-only `ObservableCollection` |
| File list | 2 | `ObservableCollection` + selection |
| Recent files | 6 | `RaiseAndSetIfChanged` + computed |
| Converter lists | 4 | `List<T>` + AppSettings setters |
| Options panel toggles | 20 | AppSettings-backed |
| Toolbar brushes | 3 (+2 static) | `RaiseAndSetIfChanged` |
| Center panel display | 8 (+1 method) | Mixed |
| Column visibility | 6 | `RaiseAndSetIfChanged` |
| Center info panel | 11 | Mixed |
| Tree selection | 2 | `RaiseAndSetIfChanged` |
| CanExecute state | ~19 | `RaiseAndSetIfChanged` |
| Helper methods | 10 | Copied from MainWindow |
| **Total properties** | **~93** | |
