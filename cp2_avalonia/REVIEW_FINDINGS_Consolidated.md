# Consolidated Review Findings and Implementation Plan: cp2_avalonia

Compiled 2026-11-10 from the eight per-directory reviews:

| Short name | File | Reviewed at |
|------------|------|-------------|
| Actions | `REVIEW_FINDINGS_Actions.md` | `dc9481e` |
| Common | `REVIEW_FINDINGS_Common.md` | `dc9481e` |
| Models | `REVIEW_FINDINGS_Models.md` | `dc9481e` |
| Platform | `REVIEW_FINDINGS_Platform.md` | `9fe7762` (see note) |
| Services | `REVIEW_FINDINGS_Services.md` | `dc9481e` |
| Tools | `REVIEW_FINDINGS_Tools.md` | `dc9481e` |
| ViewModels | `REVIEW_FINDINGS_ViewModels.md` | `dc9481e` |
| Views | `REVIEW_FINDINGS_Views.md` | `dc9481e` |

The source documents stay the detailed reference (problem, reproduction, fix direction). This
document removes the duplicates, groups the work into streams that can be handed to one agent
each, sets the order, and gives each stream a test plan. Source references use the form
`Views#5` (numbered item 5 in the Views document) or `ViewModels: "Worker exceptions are only
logged"` (an unnumbered lower-severity or cleanup item, quoted by its title).

Notes on the sources:

- `9fe7762` → `dc9481e` changed only `Controls/FancyTextViewer/*`, so the Platform line numbers
  are still valid at `dc9481e`.
- Models, ViewModels and Views refer to the Platform review as `REVIEW_FINDINGS.md`. That file is
  now `REVIEW_FINDINGS_Platform.md`.
- All findings came from reading code only. Nothing was built or run. One item was checked while
  writing this document and found to be a probable false positive (see K2).

---

## 1. Rules for implementing agents

1. **Confirm before fixing.** Every finding is unverified. Reproduce it, or confirm it by
   reading the current code, before changing anything. If it does not hold, mark it
   `[~] Not reproduced` with a one-line reason and move on.
2. **Find code by symbol, not line number.** Line numbers in all documents refer to `dc9481e`
   and will drift as other streams land.
3. **Stay inside your stream's file list** (section 4). If a fix needs a file owned by another
   stream that is running at the same time, stop and report instead of editing it.
4. **Do not commit.** The user does all commits. Leave changes in the working tree and say
   what is ready to commit at the end.
5. **Mark progress here,** not in the source documents: change `[ ]` to `[x]` on the item in
   section 5 and add a short note (what changed, how it was tested). When a consolidated item is
   done, the source items it lists are done as well.
6. **Build after every item:** `dotnet build cp2_avalonia/cp2_avalonia.csproj`. No new warnings.
7. **WPF parity is the default, not a constraint.** `cp2_wpf` is deprecated. Fixing a defect
   that also exists in WPF is fine and need not be mirrored there. Behaviour that changed from
   WPF with no recorded reason should go back to WPF behaviour unless a decision in section 2
   says otherwise.
8. **No WPF settings compatibility.** The Avalonia app does not need to read or migrate WPF
   settings files or locations.
9. **Shared libraries are off limits without approval.** `DiskArc`, `FileConv`, `CommonUtil`
   and `AppCommon` are shared with `cp2_wpf` and the `cp2` command-line tool. No finding is
   expected to need a change there. If a fix turns out to, stop and ask the user first.
10. **Record upstream bugs.** When you confirm a bug that also exists in `cp2_wpf`, add one
    line to section 8 (item ID, WPF file and method, one-sentence description). Do not change
    `cp2_wpf` and do not contact anyone.
11. **Add the item's Auto tests** to `cp2_avalonia.Tests` with the fix, and run `dotnet test`
    before marking the item done.

---

## 2. Decisions (all settled 2026-11-10)

These affect several streams. All seven were reviewed with the user and decided; the rule
sections below the table are binding for implementing agents.

| ID | Decision | Affects | Outcome |
|----|----------|---------|---------------------|
| X1 | **Background access to the open archive.** DiskArc is not thread-safe, and concurrent *reads* can also collide (entries share the underlying stream). Several fixes want work moved off the UI thread (copy extraction) while others show the UI already touching the archive during background work (Linux internal drop, OS file activation, modeless viewers). | B0, B8, C1, G2, I8 | **DECIDED 2026-11-10: modal-only rule (option B).** See "X1 rule" below the table. |
| X2 | **One message-box implementation.** There are three: `DialogService.ShowMessageAsync` (uses `MessageBoxView`, supports buttons and icons), `PlatformUtil.ShowMessageAsync` (plain window, OK only) and a hand-built window in `EditAttributesProgress`. The source documents disagree: Common#6 deletes `PlatformUtil.ShowMessageAsync`, Tools#3 and Actions#4 call it. | A2, A3, A4, K3 | **DECIDED 2026-11-10: hybrid (option C).** See "X2 rule" below the table. |
| X3 | **File list text sort order.** The port changed WPF's culture-aware compare to case-sensitive ordinal (Models#6). | D1 | **DECIDED 2026-11-10: Finder/Explorer parity (natural, culture-aware order) for names.** See "X3 rule" below the table. |
| X4 | **Palette alpha 0.** `BitmapUtil.ExpandIndexed` turns alpha 0 into opaque; WPF and the direct-colour path pass it through (Common#7). | I5 | **DECIDED 2026-11-10: pass palette alpha through unchanged** (WPF behaviour, same as the direct-colour path). Checked: every `FileConv` palette is opaque (`ConvUtil.MakeRGB` forces `0xff`; SHR and QuickDraw II 640-mode palettes also end at `0xff`; nothing calls `Palette8.SetColor` with alpha 0). Alpha 0 occurs only for unused palette entries, which I5 now fills with opaque black. |
| X5 | **Settings location.** Already decided: per-user directory, no WPF migration (Common#1 / Services#7, and the `KNOWN_ISSUES.md` item). | H1 | **DECIDED 2026-11-10.** See "X5 rule" below the table. |
| X6 | **Upstream bugs.** EditSector discard-prompt ordering (ViewModels#2) also exists in WPF. | J2, A5, A6, D1, D5, I7, K1 | **DECIDED 2026-11-10.** Fix every such bug in `cp2_avalonia`; do not mirror into `cp2_wpf`. Record each confirmed one in section 8 ("Upstream notes") for the user to forward to Andy McFadden. Shared-library changes need the user's approval (rule 9). |
| X7 | **Automated tests.** There is no unit-test project for `cp2_avalonia`. `DiskArcTests` and `FileConvTests` are run from the in-app Test Manager, not from `dotnet test`. | all test plans | **DECIDED 2026-11-10: add `cp2_avalonia.Tests`.** Details in section 6. Phase 0 creates the project; each stream adds its **Auto** tests with its fixes. Manual steps are still required. |

### X1 rule (decided)

**Work on the open `WorkTree` off the UI thread happens only while a modal progress dialog
(`WorkProgress`, or the loading dialog for open) is showing.** No free-standing `Task.Run` over
archive objects. The modal dialog disables the main window, which already blocks every command,
tree click and list refresh. The rejected alternative was a busy gate checked by every
`CanExecute`; it would have had to block reads too, which amounts to re-implementing modality
by hand.

A modal dialog does not cover everything, so these ways in check a flag instead:

- **The flag:** `IDialogService.IsModalActive`, true while any `ShowDialogAsync` dialog is open.
  `DialogService` already tracks this (`mCurrentOwner` is non-null during a modal). Item B0.
- **OS file activation** (macOS "open document"): refuse or defer while the flag is set, and
  tell the user. Item G2.
- **Modeless viewer windows:** `ViewerService` shows them with `Show(owner)`, and `ShowDialog`
  disables only its owner, so a viewer stays usable during an Add or Delete. Viewers must not
  load or navigate while the flag is set. Item I8. (Not in any source review; found while
  deciding X1. Confirm the Avalonia behaviour first.)
- **Linux drag extraction** (`BuildDragData` in `Task.Run`) is the one allowed exception: the
  pointer button is held for the whole drag, so the user cannot reach other UI. The internal
  drop handler must cancel and await that task before `MoveFiles`. Item C1.

Consequence for the UX: a large Ctrl+C now shows a cancellable progress dialog (as Extract
does) instead of freezing with a normal cursor. Item B8.

### X2 rule (decided)

- **One view.** Every message box is `MessageBoxView` + `MessageBoxViewModel`. The window
  built in code in `PlatformUtil.ShowMessageAsync` and the one in `EditAttributesProgress` are
  deleted.
- **View models use `IDialogService` only.** `WorkProgressViewModel` takes an `IDialogService`
  in its constructor and handles `MessageBoxQuery` and `OverwriteQuery` itself (both view
  models are already registered with the service). Because `WorkProgress` is shown through
  `mDialogService.ShowDialogAsync`, the service's current owner is the progress window, so the
  prompts get the right owner with no extra code. The `WorkProgress` code-behind handlers are
  removed. `EditAttributesProgress` returns its error to `MainViewModel` (A4).
- **Code-behind that needs a specific owner window** (Log Viewer, DropTarget) uses
  `PlatformUtil.ShowMessageAsync(Window owner, string text, string caption, MBButton = OK,
  MBIcon = None)`, rewritten to show `MessageBoxView` and return `MBResult`. Its doc comment
  says view models must not call it. These boxes do not set `IsModalActive` (X1); that is fine
  because they are modal only to a tool window and do not touch the archive.
- Rejected: routing tool windows through per-window `IDialogService` instances.

### X3 rule (decided)

Goal: the file list sorts names the way Finder and Explorer do: case-insensitive, numbers in
numeric order (`FILE2` before `FILE10`), accented letters next to their base letter.

- **Filename and Pathname** use
  `CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase |
  CompareOptions.NumericOrdering)` (new in .NET 10; the project targets `net10.0`).
  Pathname is compared **one segment at a time**, splitting on the entry's filesystem
  separator, so a folder's contents stay together (`DIR`, `DIR/FILE`, then `DIR.BAK`).
  Names that compare equal (e.g. `PRODOS` and `ProDOS`) fall back to `string.CompareOrdinal`
  so the order is stable.
- **Type, Auxtype, Data Fmt, Rsrc Fmt, Access** use `StringComparison.OrdinalIgnoreCase`.
  Natural order is wrong for these: Auxtype is hex (`$2A00` would sort before `$1000` because
  the leading `2` is read as a number), and the others are short codes.
- **Final tie-break** (equal primary key): the Pathname comparison above, so it agrees with the
  primary key.
- **Verified on macOS (2026-11-10, .NET SDK 10.0.300):** with ICU, `IgnoreCase |
  NumericOrdering` gives `7, 007, A-1, A1, apple, Banana, Cafe, Café, Cafz, DIR, DIR.BAK,
  file1, FILE2, FILE10, PRODOS, ProDOS, ZEBRA`. **In invariant-globalization mode the numeric
  option is silently ignored** (`FILE10` before `FILE2`, `Café` after `Cafz`) and nothing
  throws.
- **Guard:** the comparer checks once (static) whether `Compare("2", "10", NumericOrdering)`
  is negative. If not (invariant mode, or a platform without ICU numeric support), it uses a
  small hand-written natural comparer: split into digit and non-digit runs, compare digit runs
  by value (then by length, so `7` before `007`), others with `OrdinalIgnoreCase`. Not yet
  checked on Windows or Linux; D1's test plan covers both.
- This is a deliberate change from WPF (which used culture order without numeric ordering).

### X5 rule (decided)

- **Per-user folder** from `Environment.GetFolderPath(SpecialFolder.ApplicationData)`
  (checked on macOS only: `~/Library/Application Support`; the Linux (`$XDG_CONFIG_HOME` or
  `~/.config`) and Windows (`%APPDATA%`) locations are from the documentation and have **not**
  been run):
  - Windows: `%APPDATA%\CiderPress2\`
  - macOS: `~/Library/Application Support/CiderPress2/`
  - Linux: `~/.config/ciderpress2/` (lowercase, per `~/.config` convention)
  - File name unchanged: `CiderPress2-settings.json`. Create the folder on first save.
- **Portable override:** if `CiderPress2-settings.json` exists directly in
  `AppContext.BaseDirectory` (no walking up), use that file instead. This is also how a
  developer gets per-checkout settings: copy the file into the build output folder.
- **Development builds use the per-user folder too.** The repository-root walk is removed from
  settings lookup, so the new path is exercised every day. (The existing ignored
  `CiderPress2/CiderPress2-settings.json` at the repo root is no longer read.)
- **Split `PlatformUtil.GetRuntimeDataDir`** into `GetAppResourceDir()` (keeps the walk up to
  the repository root, for read-only files shipped with the app such as `LegalStuff.txt`,
  which `MakeDist` places beside the executable; fix the six-level limit) and
  `GetUserSettingsDir()` (the per-user folder above).
- **Saving:** write to a temp file in the same folder, then rename over the old file. On
  failure, log the details and show a non-blocking toast once per session ("Settings could not
  be saved: …"). No modal dialog: saving happens on quit. The factory
  exists but is unused, and those view models are created before their windows, so it needs
  a deferred host for two debug-menu windows.

---

## 3. Duplicates and overlaps found

### Exact duplicates (one fix, mark both sources)

| Consolidated | Sources | Notes |
|--------------|---------|-------|
| A1 | Actions#1, ViewModels: "Worker exceptions are only logged" | Same root cause in `WorkProgressViewModel.RunWorkerCompleted`. |
| A2 | Common#6, Actions "Smaller observations" (`MessageBoxQuery` button/icon ignored) | Same handler in `WorkProgress.axaml.cs`. |
| C1 | Platform#1, Views#5 | Same race; Views#5 adds the `SetNativeDragPaths`-on-throw sub-issue. |
| H1 | Common#1, Services#7 | Common adds the `AboutBoxViewModel` legal-file caller. |
| K2 | Tools#1, ViewModels: "Log viewer adds to a bound collection from worker threads", Models#7 | See false-positive note in K2. |
| Z (dead code) | Common#10 `AskFileToOpen` path handling, Services "Smaller observations" `Path.LocalPath` | `TryGetLocalPath` fix in `FilePickerService`; the `AskFileToOpen` copy is deleted. |
| Z | Common "Also noticed" `[Serializable]` on `WindowPlacement`, Models cleanup `[Serializable]` on `ClipInfo` | One sweep. |

### Overlaps (different defects, same code; fix together)

| Consolidated | Sources | Why together |
|--------------|---------|--------------|
| B1 | Services#10, ViewModels cleanup "Three copies of the clip-building code" | One shared clip/drag extraction helper; makes B2, B3, B8 single-site fixes. |
| C2 | Platform#2, Platform#10, Views#6, Platform "Final pointer position ignored on release" | All decide *which window* receives a Linux drop. |
| C5 | Platform#5, Platform "Late callbacks from a previous drag", Platform "Empty file list served as success after 10 seconds", Views#5 second paragraph | All are export-drag completion and timeout handling. |
| D2 | Views#10, ViewModels#6 | Both are the in-place sort of `Items`: sort rebuild and selection loss. |
| D1 | Models#2, Models#3, Models#6, Models design "Sort column is resolved from the header's display text" | All in `FileListItem.ItemComparer`. |
| E | ViewModels#4, Models#4, Models cleanups (tree helpers, `PurgeSelections`, Avalonia controls in Models), ViewModels cleanup "Tree `SelectedItem` machinery is unused" | One redesign of tree selection and scrolling. The Models document already asks for these to be decided together. |
| G | ViewModels#3, Views#9 | Both are `OpenWorkFileAsync` being re-entered by OS file activation. |
| A3 | Common#5, Tools "Smaller observations" (event subscriptions in `DataContextChanged` never removed) | Same pattern; fix in `WorkProgress` and the three Tools windows together. |
| I5 | Common#7, Common#8, ViewModels "Preview bitmaps are never disposed" | All in the file viewer's bitmap-conversion path. |
| I1 | ViewModels#7, ViewModels "Navigation pool rebuilt on every selection change" | Both rebuild viewer history path lists. |
| B4 → B5 | Services#1, Services#2 | Ordering dependency: B5 makes B4 fire on every quit. |
| C10 | Views#8, Views cleanup "drag-format strings declared twice" | Same drag-to-viewer contract. |

### Conflicts between source documents

- **Message box helper** (Common#6 deletes it, Tools#3 / Actions#4 use it): resolved by X2.
  The helper stays for code-behind only and is rebuilt on `MessageBoxView`; `WorkProgress` no
  longer uses it.
- **Log viewer threading** (Tools#1 says `HandleLogEvent` runs on worker threads; Models#7
  says `DebugMessageLog` already posts to the UI thread): resolved in K2 in favour of Models#7.
- **Drag extraction on the UI thread.** Views#3 complains that a mistaken drag extracts on the UI
  thread on Windows and macOS, and Services#6 wants copy extraction moved off it. Moving drag
  extraction to the background is *not* requested by any finding; Linux already does it and
  that is what causes C1. Under the X1 rule, do not move Windows/macOS drag extraction off the
  UI thread; D4 removes the accidental drags instead.

---

## 4. Workstreams

Each stream is sized for one agent. "Files" is the ownership list for parallel work.
`MainViewModel.cs` and `MainWindow.axaml.cs` are shared hotspots; streams that touch them run in
Phase 2, one at a time, unless the touch is listed as a one-line call-site change.

| Stream | Theme | Primary files | Hotspot touches | Phase |
|--------|-------|---------------|-----------------|-------|
| Z | Dead code and no-behaviour cleanup | many (deletions) | both | 0 |
| A | Progress dialogs, worker errors, message box | `Actions/*`, `Common/WorkProgress.axaml.cs`, `ViewModels/WorkProgressViewModel.cs`, `Common/PlatformUtil.cs` (`ShowMessageAsync` only) | `MainViewModel`: toast text, `EditAttributes` call site, and the 8 `new WorkProgressViewModel(...)` calls (add the `mDialogService` argument) | 1 |
| H | Window placement and settings storage | `Common/WindowPlacement.cs`, `Services/SettingsService.cs`, `Services/ISettingsService.cs`, `Common/PlatformUtil.cs` (`GetRuntimeDataDir` only), `ViewModels/AboutBoxViewModel.cs`, `KNOWN_ISSUES.md` | `MainViewModel`: subscribe to the save-failure notice and show the toast (small; do last) | 1 |
| I | File viewer | `ViewModels/FileViewerViewModel.cs`, `Views/FileViewer.axaml(.cs)` (not drag code), `Tools/ConfigOptCtrl.cs`, `Views/EditConvertOpts.axaml.cs`, `ViewModels/EditConvertOptsViewModel.cs`, `Common/BitmapUtil.cs` | `MainViewModel` navigation-pool code (I1) | 1 |
| J | Dialog view models | `ReplacePartition*`, `EditSector*`, `EditAppSettingsViewModel.cs`, `CreateDiskImage*`, `SaveAsDiskViewModel.cs` | none | 1 |
| K | Debug log and tool windows | `Models/DebugMessageLog.cs`, `ViewModels/LogViewerViewModel.cs`, `Tools/LogViewer.*`, `Tools/DropTarget.*`, `Tools/ShowText.*` | none | 1 |
| C-native | Linux and macOS native drag internals | `Platform/LinuxDrag.cs`, `Platform/MacOSDrag.cs` | none | 1 |
| B | Clipboard, temp extraction, shutdown | `Services/ClipboardService.cs`, `Services/Cp2ClipUtil.cs`, `Services/WorkspaceService.cs`, `Services/IClipboardService.cs`, `Services/IDialogService.cs` and `DialogService.cs` (B0 only), `Models/ClipInfo.cs`, new `Actions/CopyProgress.cs` | `MainViewModel` copy/cut/drag/paste and `Shutdown` | 2 |
| D | File list sort, selection, grid input | `Models/FileListItem.cs`, `ViewModels/FileListViewModel.cs` | `MainWindow` grid handlers; `MainViewModel` viewer handlers | 2 |
| E | Trees | `Models/ArchiveTreeItem.cs`, `Models/DirectoryTreeItem.cs`, `ViewModels/ArchiveTreeViewModel.cs`, `ViewModels/DirectoryTreeViewModel.cs` | `MainWindow.FocusSelectedDirectoryTreeItem`; `MainViewModel.CloseSubTree` and callers | 2 |
| C-view | Drag and drop in the views | — | `MainWindow` drag/drop handlers, `Views/FileViewer.axaml.cs` drag | 2 |
| F | Keyboard routing | `Views/MainWindow.axaml` | `MainWindow` key handlers; `MainViewModel` `CanExecute` | 2 |
| G | Open and file-activation lifecycle | `App.axaml.cs` | `MainViewModel.OpenWorkFileAsync`; `MainWindow.OpenFileFromActivation` | 2 |
| L | Theming of the metadata grid | `Models/MetadataItem.cs` | `MainWindow.axaml` metadata grid | 2 |

### Order and parallelism

- **Phase 0 (one agent, serial).** Stream Z, then the test project from X7 (item T0). Deleting dead code
  first removes noise from every later diff, and Z touches many files, so nothing else may run
  alongside it.
- **Phase 1 (parallel).** A, H, I, J, K and C-native may run at the same time; their file lists
  do not overlap. A and H both edit `Common/PlatformUtil.cs` but different methods; if run
  together, H must not touch `ShowMessageAsync` and A must not touch `GetRuntimeDataDir`.
  A's two `MainViewModel` call-site edits are small; make them last and re-read the file first.
- **Phase 2 (serial on the hotspots).** Recommended order: **B** (largest `MainViewModel` change;
  B0 adds the X1 `IsModalActive` flag) → **G** (uses the flag and B5's `CloseWorkFile` change)
  → **C-view** → **D** → **E** → **F** → **L**.
  D, E and F all edit `MainWindow.axaml.cs` but in separate regions; they may run together only
  with explicit region ownership agreed beforehand.
- **Phase 3.** Full regression pass (section 6) on every platform available.

B0 (the X1 flag) touches only `Services/IDialogService.cs` and `Services/DialogService.cs`, so
it can be done in Phase 0 or 1 if I8 or G2 needs it earlier. I8 (stream I, Phase 1) depends on
B0; do B0 before I8 starts.

---

## 5. Consolidated items

Severity: **S1** data damage or loss, crash, or a broken feature; **S2** wrong result or
misleading UI; **S3** performance, latent, or cleanup. Test types: **Auto** (unit test, X7),
**Manual** (steps given), **Code** (confirm by reading; no runtime test needed).

### Stream Z: dead code and no-behaviour cleanup (Phase 0)

- [x] **Z1** Delete `Common/MessageBoxEnums.cs`; drop the `Services.` qualifiers it forced.
  (Common#9) **Code**: build succeeds.
  Deleted `Common/MessageBoxEnums.cs`; the `Services.MBResult/MBButton/MBIcon` qualifiers in
  `MainViewModel.cs` and `Common/WorkProgress.axaml.cs` are gone (added `using
  cp2_avalonia.Services;` to `WorkProgress.axaml.cs`). Only the `Services` enums remain. Checked:
  build, 0 warnings.
- [x] **Z2** Delete `PlatformUtil.AskFileToOpen`, `FILE_FILTER_ALL`, `FILE_FILTER_KNOWN`,
  `IsAdministrator`; delete `Common/InverseBooleanConverter.cs` and the `InvertBool` resource in
  `App.axaml`. (Common#10) **Code**. Do **not** delete `PlatformUtil.ShowMessageAsync`; X2
  keeps it.
  Removed `AskFileToOpen`, `FILE_FILTER_ALL`, `FILE_FILTER_KNOWN`, `IsAdministrator` (and the
  now-unused `using Avalonia.Platform.Storage`), `Common/InverseBooleanConverter.cs`, the
  `InvertBool` resource and the then-unused `xmlns:common` in `App.axaml`. `ShowMessageAsync`
  kept (X2). Grep found no references (cs, axaml). Checked: build.
- [x] **Z3** Delete `Tools/FancyTextHelper.cs` after checking `Controls/FancyTextViewer` has any
  rule worth keeping. (Tools#5) **Code**.
  Deleted `Tools/FancyTextHelper.cs` (no references). Comparison with `Controls/FancyTextViewer`:
  the viewer already maps default black foreground / white background to the viewer's theme
  colours (`PhysicalElement`), treats alpha 0 as opaque, and has the same font-family stacks
  (incl. Symbol). **One rule the viewer lacks:** the helper mapped white-on-black "inverse" text
  to the theme brushes `ViewerInverseForegroundBrush` / `ViewerInverseBackgroundBrush` (so it
  inverts correctly in Light/Dark). The viewer draws literal white-on-black in both themes:
  readable, but not theme-adapted. The brush resources are still defined in `App.axaml` and used
  by `FileViewer.axaml`, so they were left in place. Not ported: no behaviour change in Z.
  Checked: build.
- [x] **Z4** Delete `Views/FindFile.axaml(.cs)`; unused `MainWindow.axaml.cs` members
  (`NotImplemented`, `GetCrossInstanceClipText`, `SelectedFileListItem`,
  `SelectedDirectoryTreeItem`, `FileList_SetSelectionFocus`, the `if (false)` block);
  `AsciiChartWindow.GetCodeName` and the no-op `EscapeDisplayChar`. (Views cleanups)
  Deleted `Views/FindFile.axaml(.cs)` (no references; the `FindFileViewModel`/`FindFileReq`
  classes are live and stay), and in `MainWindow.axaml.cs`: `NotImplemented`,
  `GetCrossInstanceClipText`, `SelectedFileListItem`, `SelectedDirectoryTreeItem`,
  `FileList_SetSelectionFocus`, and the `if (false)` block (the live
  `openPhysicalDriveMenuItem.IsVisible = false;` is kept, with a new comment).
  `AsciiChartWindow`: removed `GetCodeName`; `EscapeDisplayChar` was a no-op (`'\\' => "\\"`), its
  two callers now use `((char)x).ToString()`. Checked: build.
- [x] **Z5** Models leftovers: `FileListItem.SelectAndView`, `DebugMessageLog.GetEntryCount`,
  `[Serializable]` on `ClipInfo` and `WindowPlacement`. (Models cleanup, Common "Also noticed")
  Leave `ArchiveTreeItem.SelectItem` for stream E.
  Removed `FileListItem.SelectAndView`, `DebugMessageLog.GetEntryCount`, `[Serializable]` on
  `ClipInfo` and `WindowPlacement.SavedPos` (both are serialised with System.Text.Json source
  generation). `ArchiveTreeItem.SelectItem` untouched. Checked: build.
- [x] **Z6** Actions leftovers: unused `mAppHook` in `ScanBlocksProgress` and `TestProgress`;
  `ProgressUtil.ShowCancelled`; fix the `OpenProgress` comment about the GUI thread.
  (Actions "Smaller observations")
  Removed the unused `mAppHook` field **and the now-unused `AppHook` constructor parameter** from
  `ScanBlocksProgress` and `TestProgress` (two call sites in `MainViewModel` updated), deleted
  `ProgressUtil.ShowCancelled`, and corrected the `OpenProgress.RunWorkerCompleted` comment
  (runs on a thread-pool thread via `WorkspaceService.OpenAsync`). Checked: build.
- [x] **Z7** Services leftovers: remove `IClipboardService.HasPendingContent` and
  `GetCachedXferPaths` only if B4 does not need `HasPendingContent` (it may; check B4 first);
  `FilePickerService` → `TryGetLocalPath()`; make `ClipInfo.CLIP_PREFIX` visible and use it in
  `Cp2ClipUtil`. (Services "Smaller observations")
  `HasPendingContent` kept (B4). Removed `GetCachedXferPaths` (no callers) from
  `IClipboardService`/`ClipboardService`. `FilePickerService` now uses `TryGetLocalPath()`; a
  non-local result is treated as "no selection" (null) and skipped in `OpenFilesAsync`.
  `ClipInfo.CLIP_PREFIX` is `internal const` and `Cp2ClipUtil` uses it in place of the two
  repeated literals. Checked: build.
- [x] **Z8** `MainViewModel` leftovers: duplicate `ConfigureCenterInfo` (keep one; the review
  says the view-model copy in `CenterInfoViewModel` is unused, so check which one the view
  binds to); `IsFileOpen` assigned twice; always-true `if (!isMacZip)`; dead commented-out
  command block. (ViewModels cleanups)
  The view binds `CenterInfo.*` properties, and `MainViewModel.ConfigureCenterInfo` is the copy
  that is called (from `OnArchiveTreeSelectionChanged` path); deleted the unused
  `CenterInfoViewModel.ConfigureCenterInfo` and its private `AddInfoItem`. Also removed the
  duplicate `IsFileOpen =` line, the always-true `if (!isMacZip)` in `EditAttributesImpl`
  (body un-indented; its locals renamed `isReadOnly2`/`accepted2` to avoid clashing with the
  MacZip branch), and the commented-out pass-through command block. Checked: build.
- [x] **Z9** `ConfigOptCtrl`: make the dictionary-update-in-`foreach` read as intentional;
  merge `DropTarget`'s two identical key handlers. (Tools "Smaller observations")
  `ConfigOptCtrl.LoadExportOptions`: comment only (logic unchanged) saying why replacing a value
  while enumerating is safe. `DropTarget`: `TextArea_PreviewKeyDown` removed; the text box's
  tunnel handler now uses `Window_KeyDown` (identical body). Checked: build.

**Test plan Z:** build; launch; smoke test open, view, extract, add, copy/paste, settings
dialog, debug menu windows. No behaviour change is expected.

- [x] **T0** Create `cp2_avalonia.Tests` (X7; details in section 6). Done when
  `dotnet build CiderPress2.sln` and `dotnet test cp2_avalonia.Tests` both succeed with one
  trivial test, and a `MakeDist` build does not include the test assembly.
  Created `cp2_avalonia.Tests/` (xUnit 2.9.3, `net10.0`, references `cp2_avalonia.csproj`; no
  `Avalonia.Headless.XUnit` yet), added `<InternalsVisibleTo Include="cp2_avalonia.Tests" />` to
  `cp2_avalonia.csproj`, and added the project to `CiderPress2.sln` (entries written by hand, Any
  CPU only, like `cp2_avalonia`; `dotnet sln add` also injects x64/x86 platforms, so avoid it).
  One test (`SmokeTests.ClipInfo_RecognizesClipPrefix`) reads the internal `ClipInfo.CLIP_PREFIX`,
  so it proves both the reference and `InternalsVisibleTo`. `dotnet test cp2_avalonia.Tests`:
  1 passed. `dotnet build CiderPress2.sln` succeeds only with `-p:EnableWindowsTargeting=true`
  on macOS/Linux, because `cp2_wpf` is Windows-only (NETSDK1100; this predates T0).
  `MakeDist/Build.cs` runs `dotnet publish` on `cp2` and `cp2_avalonia` (plus `cp2_wpf` on win-*)
  and copies each publish directory; the test project is not in `sTargets` and nothing references
  it (it references the app, not the reverse), so it is not packaged. `bin/Debug` of
  `cp2_avalonia` has `DiskArcTests.dll`/`FileConvTests.dll` only. No full distribution build run.

### Stream A: progress dialogs, worker errors, message box (Phase 1)

- [x] **A1 (S1)** Worker exceptions are reported as "Cancelled" with no error.
  (Actions#1, ViewModels "Worker exceptions are only logged")
  - In `WorkProgressViewModel.RunWorkerCompleted`, when `e.Error != null`, show the error
    (via A2's helper) before closing. In `MainViewModel`, say "Failed" rather than "Cancelled"
    when the worker was not cancelled.
  - `ExtractProgress`: pass the output folder to `ExtractFileWorker` as a base path instead of
    setting `Environment.CurrentDirectory` (process-wide side effect).
  - Follow-on cases the reviewer listed: `ClipPasteProgress` null `ClipEntries`;
    `DeleteProgress` / `MoveProgress` catch only `IOException`, so `SaveUpdates` is skipped on
    other exceptions; `TestProgress` `(IFileSystem)` cast. Handle each so partial work is still
    saved and the error is shown.
  - **Manual:** start an extract to a folder, delete or unmount the folder before the worker
    starts (or choose a folder without write permission) → an error box with the reason, then a
    "Failed" toast. Cancel an extract → "Cancelled" toast, no error box.
  - **Manual:** delete several files where one delete throws a non-IO exception (use a damaged
    test image from `TestData` if one triggers it; otherwise inject a throw in a debug build)
    → earlier deletions are saved, the error is shown.
  `WorkProgressViewModel.RunWorkerCompleted` now shows `e.Error` through `IDialogService` ("The
  operation failed: ...", error icon) before closing and keeps it in `WorkerError`; the seven
  "Cancelled" toasts in `MainViewModel` say "Failed" when `WorkerError` is set. `ExtractProgress` no
  longer sets `Environment.CurrentDirectory`: it checks the folder exists (throws a clear error
  otherwise) and passes it as `basePath` to `ExtractFileWorker` (which joins it in both places it
  builds a path). `DeleteProgress`/`MoveProgress` catch any exception from the library call, still
  call `SaveUpdates`, then rethrow so the dialog shows it. `ClipPasteProgress` null `ClipEntries` and
  the `TestProgress` cast now throw `InvalidOperationException` with a message. Checked: build; Auto:
  see A3. Manual not run.
- [x] **A2 (S2)** One message box; worker message box honours button and icon.
  (Common#6, Actions "Smaller observations"; implements X2)
  - Per the X2 rule: `WorkProgressViewModel` takes `IDialogService`; it shows
    `MessageBoxQuery` via `ShowMessageAsync(text, caption, button, icon)` and `OverwriteQuery`
    via `ShowDialogAsync(new OverwriteQueryViewModel(...))`, passing the results to
    `SetResult`. Delete the handlers in `WorkProgress.axaml.cs`. First confirm the query events
    are raised on the UI thread (they come from `BackgroundWorker` progress reporting).
  - Rewrite `PlatformUtil.ShowMessageAsync(owner, text, caption, button, icon)` on
    `MessageBoxView`, returning `MBResult`, for the code-behind callers (K3).
  - **Manual:** trigger a worker error message (A1 repro) → error icon shown, Enter and Esc
    dismiss it. **Code:** a query with `OKCancel` returns the button pressed.
  `WorkProgressViewModel(callbacks, isIndeterminate, IDialogService)` handles `MessageBoxQuery` (with
  its button and icon, via `ShowMessageAsync`) and `OverwriteQuery` (via `ShowDialogAsync`) itself;
  the events and the `WorkProgress` code-behind handlers are gone, and the 8 `MainViewModel` call
  sites pass `mDialogService`. `PlatformUtil.ShowMessageAsync(owner, text, caption, MBButton = OK,
  MBIcon = None)` now shows `MessageBoxView` and returns `MBResult`; its doc comment says view models
  must not call it. The query events arrive on the UI thread (`BackgroundWorker.ProgressChanged` is
  marshalled by the sync context installed by Avalonia). Auto: `WorkProgressViewModelTests` (button/icon
  passed through; button pressed returned). Manual not run.
- [x] **A3 (S2)** `async void` query handlers can block the worker forever; handlers are
  re-added on every `DataContextChanged`. (Common#5, Tools "Smaller observations")
  - After A2 the handlers live in `WorkProgressViewModel`. Wrap each in
    `try`/`catch`/`finally`; in `finally` call `SetResult(Cancel)` if no result was set; log
    the exception. Do A2 and A3 as one change. The re-subscription problem in `WorkProgress`
    goes away with the handlers; the same pattern in `LogViewer`, `DropTarget` and `ShowText`
    is K4.
  - **Auto (headless):** a fake `IDialogService` that throws → the query's `WaitForResult`
    returns `Cancel` instead of blocking.
  - Also: guard `VM!` uses in `WorkProgress.axaml.cs`; disable the Cancel button when closing
    via the title bar, as `CancelCommand` does. (Common "Also noticed")
  - **Manual (debug build):** temporarily throw inside the overwrite-query dialog constructor;
    run an extract that hits an existing file → the worker stops as cancelled, the window
    closes, no hang. Remove the injected throw.
  Both query handlers are `try`/`catch`/`finally` and the `finally` answers `Cancel` if no result was set
  (`HasResult` added to both query classes; `Pulse` -> `PulseAll`). `WorkProgress` unsubscribes from
  the previous view model, guards `VM`, and a title-bar close now disables Cancel the same way the
  button does (`RequestCancel` sets `IsCancelEnabled = false`). Auto: `WorkProgressViewModelTests`
  (throwing dialog service -> `Cancel`, no hang, with a 5 s guard; cancelled overwrite dialog ->
  `Cancel`). Manual (injected throw in the overwrite dialog) not run.
- [x] **A4 (S2)** `EditAttributesProgress` error dialog: duplicate of the message box and not
  awaited. (Actions#4) Make `DoUpdate` return the error (or `Task<bool>`) and show it through
  `IDialogService` in `MainViewModel`, so the list refresh happens after the dialog is closed.
  Removes the `Window` parameter. **Manual:** see A5 repro; the file list must not refresh
  until the error box is dismissed.
  `EditAttributesProgress` no longer takes a `Window` or builds its own error window; `DoUpdate` keeps its
  `bool` result and leaves the reason in `ErrorMessage`. `MainViewModel.RunEditAttributesAsync` sets
  the busy cursor, runs it, and awaits the error box (`IDialogService`, error icon) before the list
  refresh. Checked: build. Manual not run.
- [x] **A5 (S1)** A failed attribute edit leaves the entry half-modified in memory.
  (Actions#2; do after A4) Snapshot `new FileAttribs(fileEntry)` before `CopyAttrsTo`; restore
  and `SaveChanges` in its own `try` in the `catch`.
  - **Manual:** on a ProDOS image, edit a file: change its type and rename it to an existing
    name in the same directory → error shown; after dismissing, the list shows the *old* type
    and name. Close and reopen the image → still the old values.
  For a file system, `DoUpdate` snapshots `new FileAttribs(fileEntry)` first and, on failure, copies the
  originals back and calls `SaveChanges` in its own try/catch. The same defect exists in
  `cp2_wpf` (section 8). Manual (ProDOS: change type and rename to an existing name) not run.
- [x] **A6 (S2)** An aborted bad-block scan is reported as a clean disk; the progress bar never
  moves. (Actions#3) Return `null` from `DoWork` when `ScanDisk` fails and the worker was not
  cancelled; show an error for `null`. Report progress per track, or create the dialog as
  indeterminate.
  - **Manual:** scan a nibble image → progress advances. **Code** for the failure path, or force
    `ScanDisk` to return false in a debug build → error, not "no bad blocks".
  `ScanBlocksProgress.DoWork` throws if `ScanDisk` returns false and the worker was not cancelled, so
  the dialog shows an error (and a "Failed" toast) instead of reporting a clean disk. `ScanDisk` now
  reports progress per track (sector media) and per percent (block media). Manual (nibble image) not
  run.
- [x] **A7 (S3)** Add and paste `DoWork` bodies are copies. (Actions#5) Shared helper in
  `ProgressUtil` taking the two worker delegates; keep the deferred-failure comment.
  **Manual:** add files to a disk image and to a ShrinkIt archive; paste into both; cancel
  mid-way in each.
  New `ProgressUtil.RunAddOperation(...)` holds the archive/file-system bodies; `AddProgress` and
  `ClipPasteProgress.DoWork` call it with their two worker delegates. The deferred-failure comment is
  kept there. Behaviour unchanged. Manual (add to disk image and ShrinkIt; paste; cancel) not run.
- [x] **A8 (S3)** Small efficiency items: `ProgressUtil` flushes streams even when no dialog
  will be shown; `TestProgress` allocates a `PersistentChoices` per progress callback.
  (Actions "Smaller observations") **Manual:** run Test Files on a large archive; behaviour
  unchanged.
  `FlushStreams` in `HandleCallback` now runs only when the overwrite dialog will actually be shown (after
  the standing "all"/"none" choices). `TestProgress` reports progress with the new
  `ProgressUtil.ReportFileProgress` instead of allocating a `PersistentChoices` and going through
  `HandleCallback` per fork. Manual (Test Files on a large archive) not run.

### Stream H: window placement and settings storage (Phase 1)

- [x] **H1 (S1 for packaged builds)** Settings are written beside the executable and failure
  is silent. (Common#1, Services#7; implements X5)
  - Implement the X5 rule: `GetAppResourceDir()` / `GetUserSettingsDir()` split, per-OS
    folder names, portable override, no repo-root walk for settings, temp-file-and-rename
    save, once-per-session toast on failure. The toast needs a way from `SettingsService` to
    the UI (an event `MainViewModel` subscribes to, or a returned status on save); keep
    `SettingsService` free of view code.
  - Stop logging every settings read at debug level. (Services "Smaller observations")
  - Remove the matching entry from `KNOWN_ISSUES.md`.
  - **Auto:** per-OS folder choice and the portable override (pure function taking OS, base
    directory and an "exists" check); save with a failing writer leaves the old file intact.
  - **Manual (each OS):** delete the per-user folder, launch → folder and file recreated on
    first save; change an option, quit, relaunch → option kept. Put a settings file beside the
    executable → that file is used and the per-user one is untouched. Make the per-user folder
    read-only → one toast per session, app keeps working, quit is not blocked. About box
    still shows the legal text in a dev build and in a `MakeDist` package.
  Implemented X5. New `Services/SettingsPaths` (pure, tested): per-user folder (`%APPDATA%\CiderPress2`,
  `~/Library/Application Support/CiderPress2`, `~/.config/ciderpress2` from
  `SpecialFolder.ApplicationData`), portable override (a settings file in `AppContext.BaseDirectory`
  only), no repo-root walk for settings, `WriteFileAtomic` (temp file in the same folder, then
  `File.Move(..., overwrite)`; folder created on first save). `PlatformUtil.GetRuntimeDataDir` is split
  into `GetAppResourceDir()` (LegalStuff.txt beside the exe, else walk up with no depth limit to the
  solution file; used by `AboutBoxViewModel`) and `GetUserSettingsDir()`. `ISettingsService` gained
  `SaveFailed`; `SettingsService` raises it once per session and `MainViewModel.Initialize` shows a
  non-modal toast. Per-read debug logging removed. `KNOWN_ISSUES.md` entry removed. Auto:
  `SettingsPathsTests` (per-OS folder, portable override, fallback, 8-level walk, failing writer leaves
  the old file and no `.tmp`). Manual (each OS) not run; **note:** an existing repo-root settings file
  is no longer read, and the toast at quit may not be visible.
- [x] **H2 (S2)** Minimized and FullScreen state and bounds are saved. (Common#2)
  Save normal bounds for every non-`Normal` state; store `Minimized`/`FullScreen` as `Normal`;
  `Restore` accepts only `Normal` and `Maximized`.
  - **Manual:** quit while minimized (Windows taskbar / macOS Dock) → relaunch opens normal and
    on-screen. Quit in macOS full screen → relaunch windowed at the earlier size.
  `WindowPlacement.BuildSavedPos`: for any state other than Normal the cached normal bounds are saved
  (the cache is also seeded on `Opened`), and only `Normal`/`Maximized` are stored (Minimized and
  FullScreen become `Normal`). `ParseSavedState` accepts only those two on restore (older files with
  Minimized/FullScreen restore as Normal). Auto: `WindowPlacementTests`. Manual not run.
- [x] **H3 (S2)** Restore never checks that the saved position is on a screen. (Common#3)
  Require an intersection of at least ~100 px with some `Screens.All` working area; otherwise
  let the platform place the window. Clamp size to the chosen screen; mind physical pixels vs.
  DIPs (`Scaling`).
  - **Auto:** the rectangle-validation function, with screens as input.
  - **Manual:** move the window to a second monitor, quit, disconnect it (or edit the settings
    file to x = 10000), relaunch → window visible on the main screen.
  `Restore` now checks `IsPlacementOnScreen` against `Screens.All` working areas (saved size is DIPs,
  converted with each screen's `Scaling`): at least 100 px in each direction must be visible, else the
  saved position and state are ignored and the platform places the window; size is clamped to the
  chosen screen; non-positive or NaN sizes are ignored. If screens can't be enumerated the old
  behaviour is kept. Auto: `WindowPlacementTests` (second monitor gone, -32000, partly visible, size
  clamp, scaling). Manual (unplug a monitor) not run.
- [~] **H4 (S2, confirm first)** Normal-bounds cache can capture maximized bounds. (Common#4)
  First log `WindowState`/`Position`/size in both handlers while maximizing on each OS. If the
  ordering is confirmed, update the cache via `Dispatcher.UIThread.Post` and make the tracker
  an instance owned by `MainWindow`.
  - **Manual:** size the window small, maximize, quit, relaunch, un-maximize → returns to the
    small size and position.
  Not done: marked "confirm first" and needs a UI run on each OS (log `WindowState`/`Position`/size in
  the handlers while maximizing). I could not run the app. One related mitigation went in with H2: the
  normal-bounds cache is seeded on `Opened`. The `Dispatcher.Post` change and the instance-owned
  tracker are untouched.

### Stream I: file viewer (Phase 1)

- [x] **I1 (S2)** Viewer reloads on every main-window refresh; history path lists are rebuilt
  per entry and per selection change. (ViewModels#7, ViewModels "Navigation pool rebuilt on
  every selection change") Reload only when the resolved entry changed or became invalid;
  share one path list across history entries. Touches `MainViewModel` navigation-pool code
  (hotspot): keep that edit small and do it last.
  - **Manual:** open a viewer on an RTF or PDF (external viewer) → clicking other directories
    in the main tree does not relaunch the external viewer. Delete the viewed file in the main
    window → viewer reacts (closes or shows the next file, whichever the code intends).
    Multi-select 200 files and view → selection changes in the main list stay responsive.
  `ReconcileAfterRefresh` rebuilds history entries with a per-refresh cache (each path resolved once;
  entries built from one path list share the rebuilt list), and no longer reloads the displayed file
  when its entry is the same valid `IFileEntry` (`TryRefreshPoolKeepingDisplay` just refreshes the
  pool). It still reloads when the entry changed or became invalid, and when settings were applied
  (`MainViewModel.ApplySettings` sets `mForceViewerReload`; the only `MainViewModel` edits are that
  flag and the extra argument). `UpdatePool` computes `FullPathName` once and returns early when the
  pool is unchanged. Checked: build. **Manual not run** (external RTF/PDF viewer not relaunched on
  directory clicks; delete the viewed file; 200-file selection). Risk to watch: any case where an
  entry object is replaced but the old one still reports `IsValid`.
- [x] **I2 (S2)** Find Previous stays on the same match. (ViewModels#8) Start the backward
  search before the current match; remove the unreachable branch.
  - **Auto:** extract the search-position logic into a pure function and test next/previous
    with wrap-around over a string with three hits. **Manual:** three hits, Next ×2, Previous ×3
    → 2, 1, 3 (wrap).
  Confirmed by reading: the caret was left at the end of the match, so the backward search found the
  same match. Extracted `FileViewerViewModel.FindMatch(text, search, matchStart, matchEnd, forward)`;
  Previous now searches before the current match start and wraps. Auto: `FindMatchTests` (Next x3
  with wrap; from the 2nd hit, Previous gives 1st, wraps to 3rd, then 2nd). Manual not run. (The
  plan's "2, 1, 3" sequence reads as: after Next x2 you are on hit 2; Previous x3 gives 1, 3, 2.)
- [x] **I3 (S1)** `ConfigureControls` throws `KeyNotFoundException` when an option key is
  missing. (Tools#2) `TryGetValue(..., out v) ? v : optDef.DefaultVal`.
  - **Manual:** Random-Access Text converter, clear "Record length", view another file, press
    Back → no exception, default shown. Same in Settings → Configure export options.
  `ConfigureControls` uses new `GetOptionValue(convOptions, optDef)` (`TryGetValue`, else the option's
  default). The only other indexer on that dictionary is a write. Auto: `ConfigOptCtrlTests`. Manual not
  run.
- [x] **I4 (S3, confirm first)** `mIgnoreChange` guard never suppresses `TextChanged`.
  (Tools#4) Confirm Avalonia 11.3 raises `TextChanged` asynchronously (search the Avalonia API
  docs or source). Then skip `Updater` when the text equals the last value sent; remove the
  posted flags in `FileViewer.axaml.cs` and `EditConvertOpts.axaml.cs`.
  - **Manual:** in the file viewer, typing a non-digit in an integer option triggers one
    reformat (add a temporary log line to count `FormatFile` calls).
  Confirmed from the Avalonia API docs: `TextBox.TextChanged` "occurs asynchronously after text changes"
  (`TextChanging` is the synchronous one), so `mIgnoreChange` never suppressed anything. `TextBoxMapItem`
  now remembers the last value assigned or reported and ignores `TextChanged` events that don't change
  it. The two posted flags are gone: `FileViewer.ConfigureControls` and
  `EditConvertOpts.ConfigureForConverter` now hold the "configuring" flag only across the synchronous
  assignment (try/finally); check-box and radio events are synchronous. Result: typing a non-digit in
  an integer option now causes no reformat at all (the filtered text equals the last value), where
  the plan expected one. Manual not run.
- [x] **I5 (S2)** Bitmap conversion: palette index unchecked; alpha 0 forced opaque; 64 MB
  temporary per zoom step; old preview bitmaps never disposed. (Common#7, Common#8, ViewModels
  "Preview bitmaps are never disposed"; X4) Copy the palette into a 256-entry array, unused
  entries opaque black (`0xff000000`); remove the "alpha 0 means opaque" rule so palette alpha
  passes through; expand one row at a time into the framebuffer; dispose the previous
  `Bitmap` when replaced.
  - **Auto:** `ExpandIndexed` with a palette shorter than the indices used → no exception,
    out-of-range pixels opaque black; a palette entry with alpha 128 → output alpha 128.
  - **Manual:** view a hi-res and a super-hi-res image; step zoom up and down repeatedly →
    output identical to before, memory steady (watch the process in Activity Monitor / Task
    Manager).
  `BitmapUtil`: palette copied to a 256-entry array (`BuildPalette`, missing entries opaque black
  `0xff000000`), alpha passed through unchanged (X4), rows expanded one at a time into the framebuffer
  (`ExpandIndexedRow`; no second full-size array). `FileViewerViewModel.PreviewBitmap` disposes the
  bitmap it replaces after the binding has switched. Auto: `BitmapUtilTests` (short palette, alpha 128
  and 0 kept, row-by-row equals whole-array). Manual (hi-res/SHR zoom, memory) not run.
- [x] **I6 (S2)** Viewer lifecycle and export errors. (ViewModels lower items)
  - Deferred `ShowFile` runs after `Dispose` and leaks fork streams → set a disposed flag and
    check it.
  - History-restore flags stick after a failed load → clear them in `finally`.
  - CSV export is saved as text because `FormatFile` replaces `CellGrid` with `SimpleText` →
    keep the original output for export and copy.
  - Export failure is only logged → show it.
  - **Manual:** close a viewer immediately after opening a large file (no error, no leaked
    handle on Windows: the archive can be closed). View a CSV-type file (e.g. AppleWorks
    spreadsheet) → Export offers and writes `.csv`. Export to a read-only folder → error shown.
    View a file that fails to convert, then a good one, then Back → history works.
  (a) `mIsDisposed` set in `Dispose`; deferred `ShowFile` and the stale-descriptor reload return early.
  (b) `mPendingHistoryRestore`/`mIsRestoringHistory` cleared in `ShowFile`'s conversion-failure path and
  if the deferred load throws. (c) the original `CellGrid` is kept in `mCurDataGrid`; Export and Copy
  use it, so spreadsheets export as `.csv`. (d) Export and Copy failures raise `ErrorReported`; the
  viewer window shows it with `PlatformUtil.ShowMessageAsync(this, ...)` (no `IDialogService` is
  injected into the view model; see A2/X2). Checked: build. Manual not run.
- [x] **I8 (S1, confirm first)** Modeless viewers stay usable while a modal worker changes the
  archive. (New; found while deciding X1, not in any source review.) `ViewerService` shows
  viewers with `Show(owner)`; `ShowDialog` disables only its owner window, so a viewer can load
  or navigate (reading the archive on the UI thread) while a `WorkProgress` worker writes it.
  - **Confirm:** open a viewer on a disk image, start a long Add from the main window, then
    press Next/Prev in the viewer. If the viewer responds, the finding holds.
  - **Fix (needs B0):** while `IDialogService.IsModalActive` is set, the viewer refuses to
    load: disable its navigation, find and converter controls (bind to the flag), and make
    `ShowFile` return early. After the modal closes, the main window's refresh path (I1) brings
    the viewer up to date.
  - **Manual:** repeat the confirm steps → viewer controls are disabled during the Add and work
    again afterwards; the viewer shows correct data after the Add (including if the viewed file
    was replaced).
  Not done. It is marked "confirm first" and needs a UI run (open a viewer, start a long Add, press
  Next/Prev) that I cannot do here, and its fix depends on B0 (`IDialogService.IsModalActive`), which is
  stream B. Left for the user to confirm; B0 is small and can be done on request.
  Implemented without the "confirm first" step (I cannot run the UI), using B0. `FileViewerViewModel` takes
  an `IDialogService`; `ShowFileDeferred` and the queued load do nothing while `IsModalActive` and load
  once the modal closes. The viewer's content `DockPanel` is bound to `IsInteractionAllowed`, so its
  controls are disabled while a modal is open. Side effect to check: viewers are also disabled while
  any other modal (Settings, About, ...) is open. Manual (long Add with a viewer open) not run.

- [x] **I7 (S3)** `AnimatedGifEncoder` 0-delay rounds to 0; transparency flag never set.
  (Common "Also noticed") Clamp delay to at least 2 (hundredths); leave transparency until a
  caller needs it. **Manual:** Debug → animated GIF test (already on `KNOWN_ISSUES.md` "testing
  needed"), play in a browser at the expected speed.
  `AnimatedGifEncoder.ToGifDelay` clamps the delay to 2..65535 hundredths (transparency flag left alone).
  Confirmed in `cp2_wpf` too (section 8). Auto: `AnimatedGifEncoderTests`. Manual (Debug GIF test in a
  browser) not run.

### Stream J: dialog view models (Phase 1)

- [x] **J1 (S1)** Replace Partition Copy button does nothing (binds `CopyCommand`, view model
  exposes `OkCommand`). (ViewModels#1) Rename to match; add `x:DataType` to the view so this
  fails at build time; run `DoCopy` off the UI thread with `try`/`catch` and an error message.
  - **Manual:** Actions → Replace Partition Contents with a compatible image → Copy works;
    progress or busy cursor shown; copying onto a write-protected target shows an error.
  - Consider adding `x:DataType` to other views that lack it (search `Views/*.axaml`); report
    any further binding mismatches the compiler finds as new items.
  Confirmed: view bound `CopyCommand`, view model exposed `OkCommand`. Renamed to `CopyCommand` (now
  enabled by `IsValid && !IsBusy`); the copy runs in `Task.Run` with try/catch and an error box, with an
  indeterminate progress bar, Copy/Cancel disabled and window close refused while busy (modal dialog, so
  the X1 rule holds; `EnableWriteFunc` stays on the UI thread). `ReplacePartition.axaml` now has
  `x:DataType` + `x:CompileBindings="True"`, so a name mismatch fails the build. **Diagnostic:** I
  temporarily enabled compiled bindings on every other dialog view and the build found no mismatches
  (only `MainWindow`: tree item templates bind `IsExpanded` with no `DataType`). I kept it on
  `ReplacePartition` only, since DataGrid column bindings can't be checked without running the UI.
  Manual not run.
- [x] **J2 (S1)** Sector editor moves position before the "discard changes?" prompt, so Write
  can put edited data in the wrong block. (ViewModels#2; X6) Ask first in Prev, Next, Read and
  `OnSectorOrderChangedAsync`; change nothing if the user declines.
  - **Manual:** edit a byte in block 5, Next, answer No → still on block 5 with the edit; Write
    → writes block 5. Answer Yes → block 6 loaded, edit discarded. Repeat with sector order
    change in track/sector mode.
  Confirmed by reading (also in `cp2_wpf/EditSector.xaml.cs`, see section 8). Read, Prev, Next and the
  sector-order change now ask `ConfirmDiscardChangesAsync()` **before** changing position or order; on
  "No" nothing changes (sector-order combo is put back). `ReadFromDiskAsync` removed. Checked: build.
  Manual not run.
- [x] **J3 (S1)** Settings OK reverts converter option changes. (ViewModels#5) Give the
  Configure Import/Export sub-dialog the dialog's local `mSettings`.
  - **Manual:** Settings → Configure export options → change one → OK → OK → reopen → change
    kept. Same but Cancel the Settings dialog → change discarded.
  Confirmed: the sub-dialog merged into the global settings and Settings OK then replaced them with
  the stale local copy. New `Services/LocalSettingsService` (an `ISettingsService` over a
  `SettingsHolder`) wraps `EditAppSettingsViewModel.mSettings` and is passed to
  `EditConvertOptsViewModel`, so the change stays in the local copy until Apply/OK and is dropped on
  Cancel. The `SettingsApplied` call after the sub-dialog was removed (global unchanged). Auto:
  `LocalSettingsServiceTests`. Manual not run.
- [x] **J4 (S1)** Create Disk Image ignores `IsValid`. (ViewModels#10) `CanExecute` on
  `IsValid`, notified from the setter. Optionally merge the duplicate image-creation switches
  with `SaveAsDiskViewModel` (ViewModels cleanup); test both dialogs if so.
  - **Manual:** volume number `abc`, invalid volume name, invalid custom size → Create disabled.
    Valid input → Create enabled and works for each filesystem type.
  `CreateDiskImageViewModel.OkCommand` now has `CanExecute = IsValid`, notified from the `IsValid` setter;
  `ExecuteOkAsync` also returns early when invalid. Merging the duplicate image-creation switches with
  `SaveAsDiskViewModel` (optional) was **not** done. Checked: build. Manual not run.
- [x] **J5 (S3)** `EditSector.Window_KeyDown` handles Ctrl+C only; add Cmd+C on macOS. (Views
  cleanup) **Manual (macOS):** Cmd+C copies the sector text.
  `EditSector.Window_KeyDown` accepts Cmd+C on macOS (`KeyModifiers.Meta`), Ctrl+C elsewhere. Checked:
  build. Manual (macOS) not run.

### Stream K: debug log and tool windows (Phase 1)

- [x] **K1 (S3)** `DebugMessageLog.Clear()` leaves a stale ring-buffer cursor. (Models#5)
  Reset `mTopEntry` in the lock. **Auto:** log 100,001 lines, `Clear()`, log 3, `GetLogs()`
  returns those 3 in order.
  `DebugMessageLog.Clear()` now resets `mTopEntry` inside the lock (`mLastIndex` is kept so indices
  stay unique). Auto: `DebugMessageLogTests.Clear_AfterWrap_ReturnsNewEntriesInOrder` (100,001 lines,
  Clear, 3 lines -> 3 in order). `dotnet test`: pass.
- [x] **K2 (S3)** Log events: order and missed entries in the viewer. (Models#7; also resolves
  Tools#1 and ViewModels "Log viewer adds to a bound collection from worker threads")
  - **Probable false positive for Tools#1 / ViewModels:** `DebugMessageLog.OnRaiseLogEvent`
    already invokes subscribers directly only on the UI thread and posts otherwise, and
    `HandleLogEvent` is subscribed only to that event, so it always runs on the UI thread.
    Confirm by setting a breakpoint or assert (`Dispatcher.UIThread.VerifyAccess()`) in
    `HandleLogEvent` and running an open with the log window showing. If it holds, mark Tools#1
    and the ViewModels item "not reproduced" and only fix the misleading comments.
  - Real fix (Models#7): always post (so delivery order matches log order), subscribe before
    taking the `GetLogs()` snapshot and drop duplicates by `Index`; ignore events after close.
  - **Manual:** open Debug → Show Debug Log, open a large archive and run an extract → entries
    in index order, no gaps (check `Index` continuity), no exceptions.
  Tools#1 / ViewModels "adds from worker threads": **not reproduced** by reading: `HandleLogEvent` is
  subscribed only to `RaiseLogEvent`, which `DebugMessageLog` already marshalled to the UI thread. Only
  the misleading comments were wrong; fixed. Real fix (Models#7): `OnRaiseLogEvent` now always
  `Post`s (order matches log order); `LogViewerViewModel` subscribes before taking the `GetLogs()`
  snapshot, drops events with `Index <= last seen`, and ignores events after `OnWindowClosed`.
  `HandleLogEvent` is now `internal` for tests. Auto: `LogViewer_DropsDuplicatesAndKeepsOrder`,
  `LogViewer_IgnoresEventsAfterClose`. Manual (large open + extract with the log window up) not run.
- [x] **K3 (S1)** Log viewer Save/Copy errors are unhandled and end the app. (Tools#3; uses X2)
  `try`/`catch` in `DoSave`, `DoCopy` and `DropTarget.DoPasteAsync`; show the error.
  - **Manual:** Save to a read-only file → error box, app keeps running.
  `LogViewer.DoSave`/`DoCopy` and `DropTarget.DoPasteAsync` now catch exceptions, log them and show
  them with `PlatformUtil.ShowMessageAsync(owner, ...)` (the code-behind helper that A2 rewrites;
  signature stays compatible). Checked: build. Manual (save to read-only file) not run.
- [x] **K4 (S3)** Tools windows subscribe in `DataContextChanged` without unsubscribing.
  (Tools "Smaller observations"; same fix pattern as A3) **Code**.
  `LogViewer`, `DropTarget` and `ShowText` keep the hooked view model, unsubscribe the previous one on
  `DataContextChanged` and on close; lambdas replaced by named handlers. Checked: build.

### Stream C: native drag and drop (C-native in Phase 1, C-view in Phase 2)

All Linux items need testing on both a plain Xorg session with a reparenting window manager and
KDE Plasma (XWayland). macOS items need both Apple Silicon and, for C9, an Intel Mac or Rosetta.

C-native (`Platform/` only):

- [x] **C3 (S2)** `ReadAtomList` reads 8-byte format-32 atoms as 4-byte. (Platform#3) Read
  `IntPtr.Size` per item. **Manual:** drag a file from a file manager that offers many types
  (Dolphin, Nautilus) onto CP2 → accepted. Check with `xprop`/debug log that the type list is
  complete.
  Confirmed by reading: `fmt / 8` is 4 for format 32 while Xlib returns `long` (8 bytes). `ReadAtomList` now
  reads `IntPtr.Size` per item for format 32 (other formats return nothing). **Not run on Linux;
  needs the Manual check (Dolphin/Nautilus drop, many types).**
- [x] **C4 (S1)** `Dispose` closes the X display while the event thread may be in Xlib.
  (Platform#4, Platform "Unhook re-adds a handler for a destroyed window") Join the thread with
  a timeout before closing; make the selection spin-wait check `_disposed`; dispose
  `DropTarget`'s instance on `Closing` or skip restoring the handler if the window is gone.
  - **Manual:** close the main window during an export drag; open and close the DropTarget
    window 20 times → no crash.
  `Dispose` is idempotent, clears `_isExporting`, completes pending TCS objects, joins the event thread
  (2 s) before `XDestroyWindow`/`XCloseDisplay` (and leaks the connection with a log line if the thread
  does not stop); the selection spin-wait checks `_disposed`; the event loop wakes at least every
  50 ms. `TryUnhookAvaloniaDnD` only writes the original handler back if Avalonia still has the window
  registered (so the closed `DropTarget` window is not kept alive). `DropTarget` itself is unchanged.
  **Not run on Linux; needs the Manual check (close during export drag; open/close DropTarget x20).**
- [x] **C5 (S1)** Export drag never completes if the target ignores the drop; late callbacks
  complete the next drag; empty URI list served as success after 10 s. (Platform#5, Platform
  lower items "Late callbacks..." and "Empty file list...", Views#5 second paragraph)
  Validate the `XdndStatus` sender; timeout after `XdndDrop`; capture the
  `TaskCompletionSource` per drag (or a generation number); on extraction failure call
  `SetNativeDragPaths` with an empty set and report failure, not copy. (The view-side catch
  change is in C1; coordinate.)
  - **Manual:** drag from CP2 over an accepting file manager, quickly onto a terminal, release
    → drag ends within the timeout and a second drag works. Drag a file large enough that
    extraction exceeds 10 s → the target gets nothing and CP2 shows a failure, not success.
  `XdndStatus` and `XdndFinished` are ignored unless the sender is the current target or its aware
  top-level window (new `IsCurrentExportTarget`); `XdndFinished` also needs a drop in flight. After
  `XdndDrop` a 15 s timeout ends the drag with `None`. Delayed callbacks capture their own TCS /
  generation (`_exportGeneration`). An empty URI list (extraction failed, via
  `SetNativeDragPaths(empty)`, or the 10 s wait expired) is now refused instead of served, and the drag
  ends with `None`. The view-side failure report is C1. **Not run on Linux; needs the Manual check.
  Risk to watch:** if a target answers `XdndStatus` with a window other than the one we sent to, its
  status is now ignored and the drop will not be sent.
- [x] **C6 (S2)** Linux clipboard read timeout throws `TimeoutException`. (Platform#6) Catch
  it, return null; ignore a late `SelectionNotify` for an older request.
  **Manual:** make another X client own the clipboard without answering (e.g. a stopped
  `xclip -i` process via `kill -STOP`), paste in CP2 → falls back, no exception.
  `ReadClipboardTextAsync` catches `TimeoutException` (returns null, logs). Reads alternate between
  two property atoms (`CP2_CLIPBOARD_READ_0/1`), and a `SelectionNotify` for the other property is
  discarded as stale after its data is read and deleted. **Not run on Linux.**
- [x] **C7 (S1)** `ReadStringProperty` truncates at 256 KB and ignores INCR. (Platform#9)
  Loop on `bytesAfter`; on `INCR`, implement it or return null so Avalonia's clipboard is
  used; fix the format-32 byte count.
  - **Manual:** copy several hundred entries in one CP2 instance (clipboard JSON > 256 KB),
    paste in a second instance → all entries pasted.
  `ReadStringProperty` probes size/type first, reads the whole property in one request (capped at
  64 MB), returns null for `INCR` (so `GetRawClipTextAsync` falls back to Avalonia's clipboard, which
  implements INCR), and counts format-32 data as `IntPtr.Size` per item. INCR itself is not
  implemented. **Not run on Linux; needs the >256 KB two-instance paste check.**
- [x] **C8 (S1)** macOS file promise reports success when nothing was copied; stale
  `sProviderPaths`; copy on the main thread. (Platform#7, Platform lower "Stale entries...",
  "Promise copy runs on the main thread") Pass an `NSError` on failure; remove each provider
  entry when its promise is written; clean up when the drag fails to start; optionally
  implement `operationQueueForFilePromiseProvider:`.
  - **Manual:** drag a file to a full or read-only volume in Finder → Finder reports an error.
    Drag several large files → UI stays responsive (if the queue is implemented).
  `WritePromiseImpl` now passes an `NSError` (NSCocoaErrorDomain 512 with a localized description) to
  the completion block when the provider is missing or the copy/destination fails, and removes the
  provider's path entry once answered. `DragEndedImpl` no longer clears `sProviderPaths` (AppKit may
  deliver the promise after the end callback); stale entries are cleared at the start of the next drag
  and when the session fails to start. `operationQueueForFilePromiseProvider:` (background copy) is
  **not** implemented. **Not run; needs the Finder full/read-only volume check.**
- [x] **C9 (S1 on Intel)** Objective-C `BOOL` returns marshalled as 4-byte `bool`.
  (Platform#8) `[return: MarshalAs(UnmanagedType.I1)]` on `SendBool1`, `SendBool2`,
  `class_addMethod`. **Code** plus **Manual** on Intel or Rosetta: drag to Finder works.
  `[return: MarshalAs(UnmanagedType.I1)]` on `class_addMethod`, `SendBool1`, `SendBool2`. Checked: build.
  **Needs a drag-to-Finder check on an Intel Mac or Rosetta.**
- [x] **C11 (S3)** Linux idle wakeups (125/s), `EnsureClasses` retry cannot work, unused fields
  (`_exportPaths`, `_exportTempDir`, `firstIcon`), and the `tempDir` doc comment. (Platform
  lower items) **Manual:** idle CPU of CP2 on Linux drops; drag still works.
  Linux idle loop now blocks in `poll()` on the X connection (50 ms timeout; falls back to the old
  8 ms sleep if `libc`/`poll` can't be loaded); removed unused `_exportPaths`/`_exportTempDir` and
  fixed the `SetNativeDragPaths` `tempDir` doc (parameter kept, ignored). macOS: `EnsureClasses`
  registers once and remembers the result (the retry could never work), unused `firstIcon` removed.
  **Linux idle CPU not measured.**

C-view (Phase 2, `MainWindow.axaml.cs` drag region and `FileViewer.axaml.cs`):

- [x] **C1 (S1)** Linux internal drop can move files while extraction still runs.
  (Platform#1, Views#5; the X1 exception) The internal-drop handler cancels the extraction task
  and awaits it before `MoveFiles`. `BuildDragData` needs a cancellation token checked between
  entries.
  `BuildDragData` must not read view-model selection off the UI thread (capture it first). On a
  `BuildDragData` exception, call `SetNativeDragPaths` with empty data.
  - **Manual (Linux):** select many large files in a ProDOS or HFS image, drag onto a folder in
    the same window, release immediately → files moved correctly, no errors. Then run
    Actions → Test Files (or reopen the image) → no damage.
  Done. `MainViewModel.BuildDragData` is split: `PrepareDragData()` (UI thread: reads the
  selection, builds the `ClipFileSet`) and `ExtractDragData(plan, isCancelled)` (only writes
  files, polls `isCancelled` between entries via `ClipTransferFiles.Extract`, deletes the temp dir
  when cancelled or failed). `BuildDragData()` is now the two in sequence (Windows/macOS,
  unchanged behaviour). On Linux `StartFileListDragAsync` calls `PrepareDragData` on the UI thread
  and runs only `ExtractDragData` on the pool, through the new `Common/CancellableExtraction<T>`.
  `HandleLinuxInternalDrop` is now `HandleLinuxInternalDropAsync`: it picks the target window
  (before the await), calls `CancelAndWaitAsync()` (cancel, then await the extraction), then
  re-checks that the window is still visible and the same work object is open before
  `MoveFiles` / `LoadDroppedEntry`. Any exception while preparing or extracting leaves
  `xferData` null, so the existing branch calls `SetNativeDragPaths(empty)` (no 10 s wait).
  Checked: build 0 warnings; 5 `CancellableExtractionTests` (returns only after the worker has
  stopped, completed result kept, not started, worker failure not rethrown, double start);
  92 tests pass. **Not run on Linux; Manual step needed.** Residual: the exception to X1 assumes
  the button is held, but after a release over an *external* target the extraction can still be
  running for up to the 10-15 s data/finish timeouts while the UI is usable; this is not
  changed here (the reviews do not ask for it). Candidate follow-up: disable the main window
  until `extraction.Task` completes.
- [x] **C2 (S1)** Wrong drop target: refused foreign window becomes an internal move; own
  secondary windows ignored; root-child-only XdndAware search; release position ignored.
  (Platform#2, Platform#10, Views#6, Platform lower "Final pointer position ignored")
  Record whether the top-level window under the pointer at release is ours (descend the window
  tree for XdndAware; compare top-level windows, not only `_avaloniaWin`); check viewer and
  DropTarget windows before the main window; process the release position. Update the
  invariants in the `LinuxDrag.cs` header.
  - **Manual (Xorg with reparenting WM, and KDE XWayland):** drag to a file manager → works.
    Release over a terminal overlapping CP2 → nothing moves. Release over a viewer overlapping
    the main window → viewer shows the file, nothing moves. Fast flick-and-release onto a file
    manager → drop goes there.
  Done in `Platform/LinuxDrag.cs` and `MainWindow`. (1) `FindXdndAwareWindow` now walks the
  window chain under the pointer (`GetWindowChainUnderPointer`, repeated `XQueryPointer` down
  from the root, max 32 levels) and takes the outermost window with `XdndAware` (still honours
  `XdndProxy`); it returns Zero if the chain contains `_avaloniaWin` or the proxy window. On a
  reparenting WM the root's child is the frame, so the client is found further down (item 10;
  KWin's bridge window is still found first). (2) `ExportOnButtonRelease` first runs
  `ExportOnMotion` at the release position, and if the (possibly new) target has not accepted it
  pumps X events up to 300 ms (`PumpEventsUntil`) until its `XdndStatus` arrives
  (`_exportStatusSinceLastPos`); `_isExporting` is cleared only afterwards. (3) With no accepting
  target the callback now gets the window chain (`Action<int,int,nint[]>`); the view maps the
  chain to its windows by native handle (`TryGetPlatformHandle`) with the pure
  `DropWindowPicker.Pick` (innermost own window wins), so a refused or non-aware foreign window
  is not ours and the drop is ignored, a viewer or DropTarget window over the main window gets
  the drop (it is checked by stacking order, not by bounds), and a modal dialog is ignored.
  `IsScreenPointInWindow` is deleted. Header invariants 6 and a new 7 updated. Checked: build
  0 warnings, 5 `DropWindowPickerTests`. **Not run on Linux; Manual steps (Xorg with a
  reparenting WM, KDE XWayland) are needed.** Risks to watch: a drop on a window of ours whose
  native handle is not what `TryGetPlatformHandle` returns would now be ignored (logged as
  "Linux drop: pointer not over one of our windows"); the main window's handle is the one
  `LinuxDrag` is installed with, so it is safe by construction, viewers use the same API; and a
  target that never answers `XdndStatus` now costs 300 ms before the release is decided.
- [x] **C10 (S2, confirm on macOS)** macOS drag-to-viewer cannot work with the native drag;
  drag-format strings declared twice. (Views#8, Views cleanup) One shared constant set; let the
  viewer take the entry from the main window's drag state while `mIsDraggingFileList` is set.
  - **Manual (macOS):** drag an entry from the file list to an open viewer → the viewer shows
    it. Repeat on Windows and Linux → unchanged.
  Done, **unconfirmed on macOS.** New `Common/FileListDrag.cs` holds the three format
  strings (the copies in `MainWindow` and `FileViewer` are deleted; a test pins their values)
  and `FileListDrag.Current`, the in-process file-list drag state set in `StartFileListDragAsync`
  and cleared by the new `EndFileListDragState()` (also on the macOS `onDragEnd`). The viewer's
  `FileViewer_DragOver` / `FileViewer_Drop` use `FileListDrag.TryGet`, which takes the entries
  from the data object if it has them and otherwise from `Current`. Windows and Linux should
  behave as before because their data objects carry the formats (expected from the code, **not
  tested**). Checked: build 0 warnings, 6
  `FileListDragTests`. **Manual (macOS) NOT run.** Assumption to confirm: Avalonia raises
  `DragOver` / `Drop` on the viewer during the native `NSDraggingSession`; if it does not, this
  change has no effect and the viewer needs a different hook (for example hit-testing viewer
  windows in `MacOSDrag`'s drag-update callback).
  **macOS result (user, 2026-11-10): dragging a selected file to an open viewer works.**
- [x] **C12 (S2, found in the C10 macOS run)** On macOS, dragging from the file list could carry
  the previously selected row instead of the one grabbed (worst when a viewer window was the
  active window), and after a drag that ended with no target, row hover highlighting stayed
  frozen until the next click. **Root cause (confirmed on macOS by the user, and by a trace of
  presses, selections and pointer capture):** the native `NSDraggingSession` swallows the
  mouse-up, so Avalonia never saw the button go up and kept the pointer captured by the element
  the press had gone to; the next press was routed to that stale element, which re-selected the
  previous row. **Fix:** `MainWindow.ReleasePointerCapture` (`pointer.Capture(null)`) is called
  when the native session starts and from its `onDragEnd` callback. Also kept: a release clears
  `mDragStartPosn`, so a drag always needs a fresh press. Checked on macOS by the user:
  highlighting and dragging work. The trace showed the window is already active when the press
  arrives and the DataGrid selects the row on the press, so the earlier theories (window
  activation, a press arriving after the drag starts, a "missing press") were wrong, and the
  code written for them was removed again. **Windows and Linux not tested** (the capture
  release is macOS-only and does not run there).
- [~] **C13 (S3, found in the C12 macOS run)** **ACCEPTED as a known issue (user, 2026-11-10); recorded in `KNOWN_ISSUES.md`. Not fixed.** Viewer window flickers, and the main window can
  end up in front of a viewer, when the main window is activated by a click while a viewer is
  the active window. Not diagnosed; nothing in the drag code activates or focuses a viewer
  (the only `Activate()` calls are Enter/double-click open, file activation and Paste). Suspects:
  macOS ordering of the viewer as an owned (child) window of the main window
  (`MainViewModel` `window.Show(owner)`), and the viewer's `Activated` handler, which re-selects
  the viewer's current entry in the file list. **Manual (macOS):** with a viewer open, click
  (no drag) a row in the main window and note whether the flicker still happens; then drag.
  If a plain click also flickers it is window ordering, not the drag.
  **User result (macOS):** a plain click flickers too: the main window is brought to the
  front for a moment and then put back behind the viewer. So it is window ordering, not the
  drag, and not new. **TEMPORARY experiment:** with `CP2_VIEWER_UNOWNED=1` in the environment
  `MainViewModel` shows viewers with `window.Show()` (no owner, centred on screen) instead of
  `Show(owner)`. Decision for the user: keep viewers owned (always above the main window, with
  this flicker) or unowned (normal independent windows; the main window can cover a viewer).
  Remove the environment switch once decided.
  **Upstream check (2026-11-10, Avalonia sources on GitHub, `release/11.3` vs `main`):** this
  is Avalonia's own macOS window-ordering code, not CP2 code, and it is **not fixed in 12.x**.
  Avalonia does not use `addChildWindow`; `AvnWindow windowDidBecomeKey` calls
  `WindowImpl::BringToFront()` twice (once at once, once on `dispatch_async`), which does
  `orderFront` on the window and then `ZOrderChildWindows()` to `orderFront` each owned
  window. So clicking the main window raises it and then puts the viewer back on top, which is
  the visible flicker. `windowDidBecomeKey`, `BringToFront` and `ZOrderChildWindows` are
  identical in both branches (main only added weak-pointer safety). The related change
  AvaloniaUI/Avalonia#22221 (merged 2026-09-14, backported to 12.1.x) only fixes window levels
  for a Topmost owner. No open or closed issue about this flicker was found. Only the
  source was compared; 12.x was not run.
  **Decision:** keep viewers owned and accept the flicker. The temporary `CP2_VIEWER_UNOWNED`
  switch in `MainViewModel` was removed again (`window.Show(owner)` as before).

### Stream B: clipboard, temp extraction, shutdown (Phase 2, first)

- [x] **B0** Add `IDialogService.IsModalActive` (true while any `ShowDialogAsync` dialog is
  open, nested dialogs included), per the X1 rule. Derive it from the existing
  `mCurrentOwner` tracking. **Auto** (headless): open a modal, flag true; close, false;
  nested modal, still true until both close. Otherwise covered by the G2 and I8 manual tests.
  `IDialogService.IsModalActive` (+ `ModalStateChanged`) counts open `ShowDialogAsync` dialogs and the
  loading dialog, nested ones included. Auto (headless): `DialogServiceTests` (open, nested, close,
  and exactly two change events). Added `Avalonia.Headless.XUnit` 11.3.10 and `Avalonia.Headless`
  11.3.22 to the test project (11.3.10 alone failed against the resolved Avalonia 11.3.23; these are the
  versions in the local NuGet cache).
- [x] **B1 (S3, enabler)** One shared helper for clip and drag extraction: settings reads,
  base-directory lookup, `ClipFileSet` construction, writing transfer files and foreign
  entries, timestamps; returns transfer paths and top-level foreign paths. (Services#10,
  ViewModels cleanup "Three copies of the clip-building code")
  **Manual:** copy/paste between two CP2 instances, copy to file manager, drag to file manager,
  drag between instances → all unchanged.
  New `Services/ClipTransferFiles` (public): temp-dir naming, layout, `NeedsTempDir`, and `Extract`
  (transfer files + foreign entries + timestamps, optional cancel/progress), used by Copy, Cut and
  drag. `MainViewModel` copy/cut are one method (`CopySelectionToClipboardAsync`) and a shared
  `BuildClipSetup` does the settings reads, base directory and `ClipFileSet` construction for copy,
  cut and `BuildDragData`. `IClipboardService.SetClipAsync` now takes the already-written
  `ClipTransferFiles.Result`. Manual not run.
- [x] **B2 (S1)** URI list flattens directory trees. (Services#3) Top-level paths only (via B1).
  - **Manual (Linux; also Windows/macOS once B7 is done):** copy `DIR` containing `SUB/FILE1`
    and an empty `EMPTY`, paste in a file manager → `DIR/SUB/FILE1` and `DIR/EMPTY` exist.
  The file-manager URI list is built from `Result.TopLevelForeignPaths` only, so a pasted directory
  keeps its tree and empty directories. Manual (Linux) not run.
- [x] **B3 (S1)** Temp subdirectory `x` collides with an entry named `X`. (Services#4) Separate
  subdirectories for transfer files and foreign entries (via B1).
  - **Manual (macOS):** copy a DOS 3.3 file named `X` and paste in Finder/a file manager → file
    present. Copy a directory `X` containing `E0000`, paste into a second CP2 → correct data.
  - **Auto:** the helper's layout function never returns overlapping paths for names `x`, `X`,
    `f`, `F`.
  Transfer files are in `<tmp>/xfer/eNNNN`, foreign entries in `<tmp>/files/...`: separate
  subdirectories, so no entry name can collide. Auto: `ClipTransferFilesTests`. Manual not run.
- [x] **B4 (S1)** `ClearIfPendingAsync` wipes a clipboard it does not own. (Services#1) Return
  early when nothing is pending; clear only when `ClipInfo.ProcessId == Environment.ProcessId`;
  reset `mXferPaths`.
  - **Manual:** copy text in another app; in CP2 open a file, then open another (or close) →
    the other app's text can still be pasted. Copy files in CP2, close the work file → paste in
    another CP2 offers nothing.
  `ClearIfPendingAsync` returns at once if nothing is pending; otherwise it drops the cached entries,
  `mXferPaths` and the temp dir immediately, then clears the system clipboard only if it still holds
  a `ClipInfo` with this process id. Manual not run.
- [x] **B5 (S1)** Exit skips work-file close and clipboard temp cleanup. (Services#2; after
  B4) Call `CloseWorkFile()` at the start of `MainViewModel.Shutdown`; also register the clip
  temp directory with `TempDirectoryTracker`.
  - **Manual:** open an archive, Ctrl+C some files, quit → no `cp2clip_*` folder remains in the
    temp directory. With a modified (unsaved) volume, quit → changes are flushed (reopen and
    check).
  `Shutdown` now calls `CloseWorkFile()` (flushes file systems, disposes the work tree, runs the
  clipboard cleanup) and `ClearIfPendingAsync()`; the clipboard temp dir is also registered with
  `TempDirectoryTracker`. The clipboard-clearing part of the cleanup is asynchronous and may not finish
  at exit; the temp files are deleted synchronously. Manual not run.
- [x] **B6 (S1, security)** File paths from clipboard text are trusted on paste. (Services#9)
  Accept only full paths under `Path.GetTempPath()` inside a `cp2clip_` or `cp2drag_`
  directory; reject `..` segments and symlinks out of it.
  - **Auto:** `Cp2ClipUtil.ExtractFileData` path filter with valid, outside-temp, `..`, and
    look-alike-prefix paths. **Manual:** place a crafted clip string (prefix + JSON pointing at
    `~/.ssh/known_hosts`) on the clipboard with another tool, paste into a disk image → that
    entry is skipped.
  `Cp2ClipUtil.ExtractFileData` replaces any path that fails `IsAllowedTransferPath` with null (the entry
  is skipped): must be absolute, under the temp root, in a `cp2clip_`/`cp2drag_` directory, below it,
  with no `..` and no symlink below the temp root. Auto: `Cp2ClipUtilTests` (valid, outside, `..`,
  look-alike prefixes, home-directory file, symlinks, and the `ExtractFileData` filter). Manual
  (crafted clip text) not run.
- [~] **B7 (S2, confirm first)** `text/uri-list` string is not read by Explorer or Finder.
  (Services#5) Confirm on each OS. If confirmed, add `DataFormats.Files` with `IStorageItem`s
  for the top-level paths, keep `text/uri-list` for Linux.
  - **Manual:** copy files in CP2, paste in Explorer, Finder, and a Linux file manager → files
    appear in all three.
  Not done: marked "confirm first" and it needs a paste test in Explorer, Finder and a Linux file
  manager, which I cannot run. Adding `DataFormats.Files` is left until it is confirmed that the
  `text/uri-list` string is not read on Windows/macOS.
- [x] **B8 (S2)** Copy extracts every file twice on the UI thread. (Services#6; uses X1 and
  B1) Run the B1 helper as a `WorkProgress` action (new `CopyProgress` in `Actions/`, modelled
  on `ExtractProgress`), so it gets progress, cancel and A1's error reporting. On cancel or
  failure, delete the partial temp directory and leave the clipboard unchanged. Also stop
  writing each file twice if B1 makes one copy serve both uses. Longer-term lazy extraction is
  out of scope unless the user asks.
  - **Manual:** select all on a 32 MB hard-drive image, Ctrl+C → progress dialog, UI repaints;
    paste into another instance and into a file manager works. Ctrl+C then Cancel → clipboard
    still holds what it held before, no `cp2clip_*` directory left behind. Small copies (one
    file) → dialog flashes briefly or not at all (check `WorkProgress` has a show delay; if not,
    note it as a UX follow-up rather than adding one here).
  Copy and Cut run `CopyProgress` (new, `Actions/`) inside a `WorkProgress` dialog, so there is
  progress, cancel and A1's error reporting. On cancel or failure the partial temp directory is
  deleted and `SetClipAsync` is never called, so the clipboard is unchanged; the old clip's files are
  deleted only after the new copy succeeds. The cut state is also only changed after success. Not
  done: writing each file once. Transfer and foreign entries are different outputs (CP2 transfer
  format vs. extract format), so they cannot share a file. **UX follow-up:** `WorkProgress` has no show
  delay, so a one-file copy flashes the dialog. Manual not run.
- [~] **B9 (S3, confirm first)** Filesystems flushed parent-first. (Services#8) In a debug
  build, change a file inside a disk image nested in a ProDOS volume, close the work file; if
  the `ProDOS.InvalidateFileEntries` assert fires, flush children first.
  Not done: marked "confirm first" and it needs a debug-build run with a disk image nested in a ProDOS
  volume.
- [x] **B10 (S1, latent)** Cut and paste in the same directory deletes the originals when every
  overwrite is skipped. (ViewModels lower) Delete cut entries only for items actually pasted.
  `CutCommand` is currently unused; fix now so it is safe if enabled. **Code**, or **Manual**
  if cut is enabled in a debug build.
  `ClipPasteProgress.AnySkipped` (from a new `PersistentChoices.SkipCount`) tells the caller when an
  overwrite was skipped; then the cut originals are kept (with a toast). They are also kept when the
  paste went into the directory they came from. This is all-or-nothing, not per item: the library
  does not report which entries were pasted. Code only; `CutCommand` is still unused.
- [x] **B11 (S3)** `WorkspaceService.OpenAsync` returns `null!` and rethrows with `throw ex`;
  interface comment wrong. Cross-instance paste reads the clipboard twice. (Services "Smaller
  observations") **Code**; regression covered by G tests.
  `WorkspaceService.OpenAsync` rethrows with `ExceptionDispatchInfo` (keeps the stack) and throws
  `InvalidOperationException` instead of returning `null!`; the `IWorkspaceService` comment now says the
  caller closes the current file first. Paste reads the clipboard once (`GetClipWithTextAsync`).

### Stream G: open and file-activation lifecycle (Phase 2)

- [x] **G1 (S2)** Open guard is timing-based and can lose `--read-only`; `DropOpenWorkFile` and
  `OpenRecentFile` close the current file before the guard can refuse. (ViewModels#3)
  Treat a request for the file already open or loading as a no-op that keeps the stricter
  read-only flag; ignore an activation whose path matches the startup path; move the
  `CloseWorkFile()` call inside the guarded path.
  - **Manual (macOS):** `CiderPress2 -r disk.po` repeated 5 times → always read-only, opened
    once (check the debug log for a single open). Open Recent while a load is in progress →
    current file not closed.
  Done in `MainViewModel.OpenWorkFileAsync` (new `fromOs` parameter) and the new pure helper
  `ViewModels/OpenRequestPolicy.cs` (`IsSamePath`, `IsSatisfiedByOpenFile`). No startup-path
  special case; the duplicate is recognised by path instead. (1) A request for the file that is
  loading is dropped (logged); if it asks for read-only and the load is read-write, a flag makes
  the open reopen it read-only when the load finishes. (2) OS-delivered requests (command line
  and macOS activation, both now `fromOs: true` in `MainWindow.OpenFileDeferred`) are also
  dropped when that file is already open, unless they ask for read-only and it is writable (then
  it is reopened read-only). User commands (Open, Recent, drop) still reload an open file as
  before. (3) `DropOpenWorkFile` and `OpenRecentFile` no longer call `CloseWorkFile()`: the close
  is inside the guarded open. The picker now runs inside the guard too, and `File.Exists` is
  checked before the close, so a missing file (stale Recent entry) leaves the current file open.
  Checked: build 0 warnings, 8 new `OpenRequestPolicyTests` (path normalising, case rule,
  read-only/read-write combinations); 76 tests pass. **Manual steps NOT run** (macOS launch
  with `-r`, Open Recent during a load).
- [x] **G2 (S1)** macOS file activation can close the work file under a modal dialog or during
  a worker. (Views#9; uses X1 / B0) While `IsModalActive` is set, refuse the activation with a
  message (or queue it and run it once the flag clears; pick one and note which). Every modal
  case counts, not only workers: Edit Attributes, Edit Sector and Settings hold references into
  the work tree too.
  - **Manual (macOS):** start a long Add or Extract, double-click another image in Finder →
    message, current operation completes normally. With Edit Attributes open, same → message,
    dialog still works.
  Done: **refuse, not queue** (the modal count drops to zero between the dialogs of one
  command, so a queued open could fire in the middle of that command). `OpenWorkFileAsync` with
  `fromOs` checks `IDialogService.IsModalActive` and, new, `IFilePickerService.IsPickerActive`
  (native pickers are not counted as modal; Add Files holds tree references while its picker is
  up), and tells the user with a message box. **Changed after the first manual run:** the
  first version showed a toast; it is drawn on the main window, under the dialog, so the user
  saw it only after closing the dialog. Now `IViewActions.ShowNotice` (MainWindow) walks
  `OwnedWindows` to the topmost window and shows `PlatformUtil.ShowMessageAsync(top, ...)`
  there. That path bypasses `DialogService`, so it cannot corrupt `mCurrentOwner` (the reason
  a `DialogService` message box was rejected: a second modal on top of the progress dialog can
  close out of order). `OpenFileFromActivation` now raises the topmost window
  (`GetTopmostWindow`) instead of the main window: activating the main window put it above the
  open dialog, which hid the dialog until the box was dismissed (seen in the second manual
  run, macOS). One notice at a time. The box closes if its owner dialog closes (a
  progress dialog finishing). A different file arriving while a load is in flight gets the same
  notice; the duplicate of the file being loaded is
  silent. `App.axaml.cs` needed no change. Residual: an activation arriving in the instant
  between two dialogs of one command is not caught. Checked: build 0 warnings; no automated
  test of the refusal (needs a full `MainViewModel`). **Manual steps NOT run.**

### Stream D: file list sort, selection and grid input (Phase 2)

- [ ] **D1 (S2)** Comparer fixes: size columns overflow on 2 GB differences; Rsrc Len ignores
  MacZip resource lengths; text columns case-sensitive; column resolved from header text.
  (Models#3, Models#2, Models#6, Models design issue) Use `CompareTo`; keep numeric lengths in
  fields; map columns by `SortMemberPath` or `Tag`, assert on unknown. Text columns follow the
  X3 rule (natural culture-aware order for names, per-segment Pathname, case-insensitive
  ordinal for code columns, invariant-mode fallback). Put the name comparison in one static
  helper so other lists (e.g. the archive tree, if sorted later) can reuse it.
  - **Auto:** comparer on synthetic items: lengths 3,000,000,000 vs 100 and 4 GB vs 0; the
    X3 example list in the expected order; `DIR`, `DIR/FILE`, `DIR.BAK` (and `:` separator) in
    per-segment order; Auxtype `$1000`, `$2A00`, `$FF00` in hex order; the fallback comparer
    gives the same order as the ICU path for ASCII names; transitivity over a random set.
  - **Manual (each OS):** sort Filename and Pathname on an HFS volume or ZIP with mixed-case,
    numbered and accented names; compare with how Finder / Explorer / the Linux file manager
    orders the same names after extracting them. Sort every other column ascending/descending,
    including Rsrc Len on a MacZip archive with resource forks.
- [ ] **D2 (S2)** Sorting rebuilds the list one item at a time; an active sort drops the
  selection on every refresh and makes every refresh a full repopulate; `SetFileListSelection`
  is O(n²). (Views#10, ViewModels#6) One shared sort helper that replaces contents in one
  reset; sort before restoring the selection; verify the list as a set (or against an unsorted
  copy); suppress per-item `SelectionChanged` during batch selection.
  - **Manual:** full-list mode on a large HFS or ProDOS hard-drive image: click headers → no
    stall. With a sort active, edit attributes of a row → same row stays selected; delete a row
    → neighbour selected. Select all → fast.
- [ ] **D3 (S2)** Resize-grip detection looks for a `Thumb` that never exists. (Views#4)
  Detect resize by pointer X near header edges, or compare name-column widths at press and
  release; delete the `Thumb` code and `mSuppressSort` if no longer needed.
  - **Manual:** resize the Filename column, change directory → width kept. Double-click a
    column divider → the column to its left auto-sizes. Click a header → sorts; drag a divider
    → does not sort.
- [ ] **D4 (S2)** File-list double-tap and drag arming are not limited to data rows.
  (Views#2, Views#3) Require a `DataGridRow` ancestor in `FileListDataGrid_DoubleTapped`,
  `PartitionLayout_DoubleTapped` and `FileListDataGrid_PointerPressed`.
  - **Manual:** with a file selected, double-click the scrollbar arrow → no viewer. Press on the
    scrollbar track or blank space and drag → scrolls or nothing, no drag. Double-click a row →
    viewer opens; drag a row → drag starts. Same for the partition list.
- [ ] **D5 (S2)** Blank-grid drop uses the tree's directory in full-list mode. (Views#7)
  Use `GetFileListEmptySpaceTarget()`; also filter dropping a directory onto itself.
  (Coordinate with C-view: same drop handlers.)
  - **Manual:** full-list mode, double-click a subdirectory, drag a root file to blank grid
    space → stays at root (or moves to root). Click-jitter on a directory row → no "Bad Move"
    dialog.
- [ ] **D6 (S1)** Activating a viewer resets the main list selection to row 0. (ViewModels#9)
  Viewer handlers do nothing when the entry is not in the current list; review the "select row
  0" fallback and the TODO in `SetSelectionFocusByEntry`.
  - **Manual:** view a file in directory A; go to directory B, select several files; click the
    viewer → B's selection unchanged.

### Stream E: trees (Phase 2)

- [ ] **E1 (S1)** Close File Source never reselects the parent node, leaving the selection on a
  disposed node. (ViewModels#4) Call `_viewActions.SelectArchiveTreeItem(parentItem)`.
  - **Manual:** open an archive containing a sub-archive, select the sub-archive, Close File
    Source → parent selected, its contents listed, commands enabled correctly; run Extract →
    works.
- [ ] **E2 (S2, confirm first)** `BringItemIntoView` does nothing for tree items not yet
  realized. (Models#4) Defer the container walk until after layout (`Dispatcher.UIThread.Post`
  at a lower priority, or force layout of the expanded container).
  - **Manual:** open an image whose archive tree is taller than the panel and whose best node is
    below the fold → tree scrolls to it. Same for a deep directory tree.
- [ ] **E3 (S3)** Consolidate tree code: one generic tree-item base (`Parent`, `Items`,
  `IsExpanded`, `IsSelected`) and one container-walk helper in the view layer (replacing the
  copies in `ArchiveTreeItem`, `DirectoryTreeItem` and `MainWindow.FocusSelectedDirectoryTreeItem`);
  remove the unused `SelectedItem`/`CachedSelectedItem`/`ResetCache` machinery and
  `PurgeSelections`; remove `ArchiveTreeItem.SelectItem` pass-through; move `TreeView`/`DataGrid`
  parameters out of `Models`. (Models cleanups, ViewModels cleanup "Tree `SelectedItem`
  machinery is unused") Do E1 and E2 first so the refactor carries the fixes.
  - **Manual:** full tree regression: open nested archives, select/expand/collapse, Close File
    Source, Open sub-archive, keyboard navigation in both trees, directory double-click from the
    file list.

### Stream F: keyboard routing (Phase 2)

- [ ] **F1 (S1)** Window key bindings steal Delete, Ctrl+C, Ctrl+V, Ctrl+A from the find box,
  and Enter in the directory tree opens the viewer. (Views#1) Move these bindings to the
  controls they belong to, or have the commands decline when a `TextBox` has focus. Check the
  macOS native menu gestures (Cmd+C etc.) too.
  - **Manual (each OS):** Ctrl/Cmd+F, type a name that matches a file, then press Delete,
    Ctrl/Cmd+A, Ctrl/Cmd+C, Ctrl/Cmd+V → text editing only, no delete prompt, no paste into
    the archive. With focus in the file list, the same keys act on files. Enter in the directory
    tree → expands or selects, does not open the viewer.

### Stream L: theming (Phase 2)

- [ ] **L1 (S2)** Metadata text is hard-coded black, unreadable in the dark theme. (Models#1)
  Model exposes `CanEdit` only; the view chooses brushes with theme resources.
  - **Manual:** Dark theme, open a WOZ or 2IMG with metadata, show the Information panel →
    readable; switch to Light while open → still readable.

---

## 6. Test strategy

### Automated (X7, decided)

Project setup (item T0):

- `cp2_avalonia.Tests/` at the repository root, next to the other projects; xUnit; target
  `net10.0`; added to `CiderPress2.sln`.
- References `cp2_avalonia/cp2_avalonia.csproj` directly (referencing an executable project is
  supported). Add `<InternalsVisibleTo Include="cp2_avalonia.Tests" />` to `cp2_avalonia` so
  tests can reach internal types such as `FileListItem.ItemComparer`.
- Plain xUnit for pure logic. `Avalonia.Headless.XUnit` only for tests that need a UI
  dispatcher (B0's `IsModalActive`, A3's `WorkProgressViewModel` with a fake dialog service).
  It is a test-only dependency; the app gains none.
- Not packaged by `MakeDist`; not run by the in-app Test Manager (which stays for `DiskArcTests`
  and `FileConvTests`). Run with `dotnet test cp2_avalonia.Tests`. There is no CI in the
  repository; wiring one up is out of scope.
- Keep tests on behaviour; extract small pure functions where a fix needs one rather than
  testing through the UI.

Tests by area:

| Area | Items | What to test |
|------|-------|--------------|
| File list comparer | D1 | overflow, MacZip rsrc length, natural name order, per-segment paths, hex Auxtype, fallback comparer, transitivity, unknown column |
| Find | I2 | next/previous/wrap positions |
| Debug log | K1, K2 | `Clear` after wrap; ordering and `Index` continuity |
| Clip paths | B3, B6 | temp layout never collides; path filter accepts/rejects |
| Window placement | H2, H3 | state mapping; on-screen validation with synthetic screens |
| Settings path | H1 | per-OS directory choice; atomic save leaves old file on failure |
| Bitmap | I5 | short palette, alpha policy, row-by-row equals old output |
| Converter options | I3 | missing key returns default |
| Dialog service | B0 | `IsModalActive` across open, close and nested modals (headless) |
| Progress queries | A2, A3 | throwing dialog service → worker gets `Cancel`, no hang (headless) |

### Manual regression checklist (Phase 3, every platform available)

**Test status as of 2026-11-10: only macOS has been run, and only for the items the user
reported on (Phase 1 checks, and G). No Windows or Linux manual step has been run for any item.**
A `[x]` in section 5 means the code change is made and builds, and its Automated tests pass on
macOS; it does not mean any Windows or Linux Manual step passed. Treat every Manual step listed
for those platforms as outstanding, and the Windows and Linux paths (native drag, settings
location, file activation, window placement, key routing) as untested. Anything that says
"checked" or "verified" without naming a platform means macOS.

Run the per-item manual steps above, then this end-to-end pass:

1. Fresh profile (no settings file): launch, verify settings location (H1), window placement
   after minimize/maximize/full-screen quits (H2–H4).
2. Open each common format from `TestData` (DOS 3.3, ProDOS, HFS, NuFX, ZIP with `__MACOSX`,
   WOZ, 2IMG). Sort every column (D1, D2); resize columns (D3); full-list and tree modes.
3. Add, extract, delete, move, edit attributes, test files, bad-block scan on a nibble image;
   force one error in each (A1–A6).
4. Copy/paste: within an instance, between instances, to and from the OS file manager, with
   nested directories and a file named `X` (B1–B7); quit and check the temp directory (B5).
5. Drag and drop: to and from the file manager, between windows, onto viewers, internal moves,
   overlapping windows (C1–C10, D4, D5).
6. File viewer: text, graphics at several zooms, CSV export, find, history back/forward with
   converter option changes (I1–I6); viewer disabled during a long Add and correct afterwards
   (I8).
7. Dialogs: Replace Partition, Edit Sector, Settings → Configure options, Create Disk Image
   with invalid input (J1–J5).
8. Keyboard: find box editing keys, tree Enter (F1).
9. Dark theme: Information panel (L1).
10. Debug menu: log viewer save/copy, DropTarget, animated GIF (K1–K4, I7).
11. macOS only: `-r` launch, Finder activation during work (G1, G2); Intel/Rosetta drag (C9).
12. Run the in-app Test Manager (DiskArc and FileConv tests) to confirm no library regression.

Record results per platform in the item notes. Anything that fails becomes a new unchecked item
in section 5.

---

## 7. Cross-reference: source item → consolidated item

| Source | Item → consolidated |
|--------|---------------------|
| Actions | 1→A1, 2→A5, 3→A6, 4→A4, 5→A7; smaller: unused fields/`ShowCancelled`/comment→Z6, `MessageBoxQuery`→A2, `FlushStreams`/`PersistentChoices`→A8 |
| Common | 1→H1, 2→H2, 3→H3, 4→H4, 5→A3, 6→A2, 7→I5, 8→I5, 9→Z1, 10→Z2; also-noticed: command-line `--` and `FilePath` doc → not scheduled (see below), GIF→I7, six-level walk→H1, `VM!`/Cancel button→A3, `[Serializable]`→Z5 |
| Models | 1→L1, 2→D1, 3→D1, 4→E2, 5→K1, 6→D1, 7→K2; design→D1; cleanups: tree helpers/`PurgeSelections`/controls in Models→E3, dead members→Z5 (`SelectItem`→E3) |
| Platform | 1→C1, 2→C2, 3→C3, 4→C4, 5→C5, 6→C6, 7→C8, 8→C9, 9→C7, 10→C2; lower: 10-second empty list→C5, late callbacks→C5, release position→C2, idle wakeups→C11, unhook→C4, promise on main thread→C8, `EnsureClasses`→C11, stale `sProviderPaths`→C8, unused state→C11 |
| Services | 1→B4, 2→B5, 3→B2, 4→B3, 5→B7, 6→B8, 7→H1, 8→B9, 9→B6, 10→B1; smaller: `LocalPath`→Z7, settings read logging→H1, unused interface members→Z7, `OpenAsync`/double read→B11, `CLIP_PREFIX`→Z7 |
| Tools | 1→K2 (probable false positive), 2→I3, 3→K3, 4→I4, 5→Z3; smaller: duplicate key handlers→Z9, subscriptions→K4/A3, `foreach` update→Z9, all-zero integer rule→not scheduled |
| ViewModels | 1→J1, 2→J2, 3→G1, 4→E1, 5→J3, 6→D2, 7→I1, 8→I2, 9→D6, 10→J4; lower: log viewer threads→K2, worker exceptions→A1, deferred load→I6, navigation pool→I1, CSV export→I6, export failure→I6, preview bitmaps→I5, history flags→I6, cut-and-paste→B10; cleanups: `ConfigureCenterInfo`→Z8, tree `SelectedItem`→E3, clip-building copies→B1, disk-creation switches→J4, `MainViewModel` leftovers→Z8 |
| Views | 1→F1, 2→D4, 3→D4, 4→D3, 5→C1, 6→C2, 7→D5, 8→C10, 9→G2, 10→D2; cleanups: `FindFile`/unused members/`AsciiChart`→Z4, drag-format strings→C10, drop path extraction repeated→C-view (fold into C1/D5 if convenient), EditSector Cmd+C→J5 |

**Not scheduled** (noted by reviewers, no fix proposed or judged not worth it now; the user may
promote any of them):

- `CommandLineOptions`: arguments starting with `-` are always options (no `--`), and the
  `FilePath` doc comment omits physical drives. (Common "Also noticed")
- `ConfigOptCtrl`: an all-zero integer value is treated as unset for every `IntValue` option.
  (Tools "Smaller observations")
- Directory-row-onto-itself "Bad Move" dialog is WPF parity; it is folded into D5 as an
  optional improvement.

---

## 8. Upstream notes (for Andy McFadden)

Bugs fixed in `cp2_avalonia` that also exist in `cp2_wpf` (decision X6). The user decides
whether and when to forward this list; agents only add lines (rule 10). Items marked
*unconfirmed* come from the source reviews and have not yet been reproduced; change the mark
to *confirmed* when the item is fixed.

| Item | WPF location | Bug | Status |
|------|--------------|-----|--------|
| J2 | `cp2_wpf/EditSector.xaml.cs` `PrevButton_Click` (also Next, Read, sector order change) | Position moves before the "discard changes?" prompt; declining then Write puts the edited data in the new block | confirmed by reading (WPF `ReadButton_Click`, `PrevButton_Click`, `NextButton_Click` change position, then `ReadFromDisk` prompts) |
| A5 | `cp2_wpf/Actions/EditAttributesProgress.cs` `DoUpdate` | Failed filesystem attribute edit leaves the entry half-modified in memory | confirmed by reading (same code in `cp2_wpf`; no restore on failure / `ScanDisk` result ignored) |
| A6 | `cp2_wpf/Actions/ScanBlocksProgress.cs` `DoWork` / `ScanDisk` | `ScanDisk` failure ignored; aborted scan reported as no bad blocks | confirmed by reading (same code in `cp2_wpf`; no restore on failure / `ScanDisk` result ignored) |
| D1 | `cp2_wpf/FileListItem.cs` `ItemComparer.Compare` | `(int)(a - b)` on lengths overflows for differences of 2 GB or more; Rsrc Len sorts on `FileEntry.RsrcLength`, which is 0 for MacZip | unconfirmed |
| K1 | `cp2_wpf/DebugMessageLog.cs` `Clear` | `mTopEntry` not reset; `GetLogs` throws after a wrapped log is cleared (latent: no caller) | confirmed by reading and by an Avalonia unit test of the same code |
| I7 | `cp2_wpf/WPFCommon/AnimatedGifEncoder.cs` | Frame delay under 10 ms rounds to 0; viewers then use their default speed | confirmed by reading (`Math.Round(md.DelayMsec / 10.0)`) |
| D5 | `cp2_wpf/MainWindow.xaml.cs` file-list drop handling | Dropping a directory onto itself reaches `MoveFiles` and shows "Bad Move" | unconfirmed (parity; fix optional) |

