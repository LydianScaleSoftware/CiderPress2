# Avalonia 12.1 port notes

Branch `avalonia-12.1-test` (cut from `cleanup-and-bugfixes` at `3ad50eb`). Nothing is committed;
all changes are in the working tree. Nothing was tested in the UI: this was done without being
able to drive the app, only build, unit tests, a throwaway headless XAML load check and a Release
publish.

## Package versions used

Restored (from `obj/project.assets.json`):

| Package | Version |
|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter (+ Skia, Native, HarfBuzz, FreeDesktop) | 12.1.6 |
| Avalonia.Controls.DataGrid | **12.1.2** (the highest 12.1.x on NuGet; there is no 12.1.6) |
| Avalonia.Headless, Avalonia.Headless.XUnit (tests) | 12.1.6 |
| xunit.v3 (tests) | 3.2.2 (the version Avalonia.Headless.XUnit 12.1.6 depends on; NuGet also has 4.0.x) |
| Avalonia.BuildServices | 11.3.2 (build-tooling package, pulled in transitively; unchanged) |

The csproj uses `12.1.*` for the Avalonia packages, so they float to the latest 12.1 patch.
`Avalonia.Diagnostics` is gone (see Changes). The non-Avalonia packages are untouched.

## Results

- `dotnet build cp2_avalonia/cp2_avalonia.csproj`: 0 errors, 0 warnings.
- Extra build with `-p:NoWarn=IL2026` (so the csproj's CS0618 suppression is lifted): 0 errors, 0 warnings,
  so there are no obsolete-member uses left in `cp2_avalonia`.
- `dotnet test cp2_avalonia.Tests`: 167 passed, 0 failed (same 167 as on 11.3 before the port).
- `dotnet build CiderPress2.sln -p:EnableWindowsTargeting=true`: 0 errors, 0 warnings.
- `dotnet publish cp2_avalonia/cp2_avalonia.csproj -c Release -r osx-x64`: succeeds and creates
  `cp2_avalonia/bin/Release/net10.0/osx-x64/CiderPress II.app` (46 MB). The publish prints two trim
  warnings (IL2104 for `Avalonia.Controls.DataGrid` and `Avalonia.DesignerSupport`); I did not compare
  against an 11.3 publish, so I don't know if they are new. No new trimmer roots were needed.
  Not installed or run.

## Changes

`cp2_avalonia/cp2_avalonia.csproj`
- Avalonia packages `11.3.*` -> `12.1.*`. Forced by the port.
- Removed the Debug-only `Avalonia.Diagnostics` reference (removed in 12; the code never calls `AttachDevTools`).
- Added `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>`, because 12
  defaults to compiled bindings and only 6 of the ~25 `.axaml` files declare `x:DataType`. Keeps 11.3 behaviour.
  Existing `x:DataType`/`x:CompileBindings` uses are unchanged.

Built-in GroupBox (requested)
- Deleted `Controls/GroupBox.cs` and the `controls:GroupBox` `ControlTheme` (and the `xmlns:controls`) in `App.axaml`.
- `MainWindow`, `EditAppSettings`, `EditConvertOpts`, `EditAttributes`, `EditSector`, `FileViewer`
  `.axaml`: `<common:GroupBox>` -> `<GroupBox>`; removed the now-unused `xmlns:common`.
- `App.axaml` keeps the old look with no new template:
  - resources: `GroupBoxPadding` = 8, `GroupBoxHeaderFontSize` = 14 (the old header used the body font size);
  - a `GroupBox` style setting `BorderBrush` (`SystemControlForegroundBaseMediumLowBrush`) and `CornerRadius` 4;
  - a style on `GroupBox /template/ ContentPresenter#PART_HeaderPresenter` setting `FontWeight` SemiBold.
  - The border brush is a Style setter, not a `GroupBoxBorderBrush` resource, because a `DynamicResource`
    can't be a resource-dictionary value; I hit that as a crash (see "How this was checked").
- `Tools/ConfigOptCtrl.cs` needed no change (no ambiguity once our type was removed); `FileViewer.axaml.cs`
  `optionsGroupBox.IsVisible` needed no change.

Focus / text box
- `Views/MainWindow.axaml.cs`: `Window_FocusChanged` takes `FocusChangedEventArgs` (was `RoutedEventArgs`).
- `Views/MainWindow.axaml`: `TextBox.Watermark` -> `PlaceholderText` (`findFileTextBox`).

`IInputRoot`
- `Views/MainWindow.axaml.cs` `DismissMenuTimer_Tick`: the `((IInputRoot)this).ShowAccessKeys = false;` line
  is commented out with a `TODO(avalonia12)`. I found no public replacement in the 12.x API docs.
  The rest of the Alt+Up handling is unchanged.

Clipboard and drag/drop (mechanical port: `IDataObject`/`DataObject`/`DataFormats` -> `IDataTransfer`/`DataTransfer`/`DataFormat`)
- `Models/ClipInfo.cs`: new `CrossInstanceDataFormat` = `DataFormat.CreateStringPlatformFormat("com.faddensoft.ciderpress2.clip")`.
  A *platform* format, not an application format, because Avalonia prefixes application format names and
  the native macOS/Linux drag code puts that exact UTI string on the pasteboard.
  `CROSS_INSTANCE_FORMAT` is kept for `MacOSDrag`/`LinuxDrag`, which are unchanged.
- `Common/FileListDrag.cs`: the three format names are now `DataFormat.CreateInProcessFormat<T>(...)` with
  the same identifier strings as before (`cp2_avalonia/FileListDrag...`); `TryGet` takes `IDataTransfer`.
  The `Current` fallback is unchanged.
- `Services/ClipboardService.cs`: `SetClipAsync` builds one `DataTransferItem` with `DataFormat.Text` (the JSON)
  and, when there are foreign paths, a platform string format `text/uri-list`, then `SetDataAsync`.
  `GetTextAsync` -> `TryGetTextAsync`. `GetDataAsync(DataFormats.Files)` -> `TryGetFilesAsync`.
  The string/byte[] lookup of an arbitrary format now reads the raw value through `TryGetDataAsync` +
  `TryGetRawAsync` and keeps the same string/byte[] handling.
- `ViewModels/MainViewModel.cs` `PasteOrDrop`: takes `IDataTransfer?`, reads `CrossInstanceDataFormat`, then `TryGetText()`.
- `Views/MainWindow.axaml.cs`:
  - `e.Data` -> `e.DataTransfer`, `GetFiles()` -> `TryGetFiles()`;
  - `OnLinuxCp2Drop` builds a `DataTransfer`;
  - the file-list drag builds one item with the entries/source/work-path in-process formats plus the cross-instance
    JSON; on Windows each extracted file is added as a separate `DataTransferItem.CreateFile` item (the `File` format
    is per item now). `AddStorageFilesToDataObject` was renamed `AddStorageFilesToDataTransfer`;
  - `DragDrop.DoDragDrop(e, ...)` -> `DragDrop.DoDragDropAsync(pressArgs, ...)`. The pointer press that arms the
    drag is now kept in a new field `mDragPressArgs` (set where `mDragStartPosn` is set) and passed to
    `StartFileListDragAsync(e, pressArgs)`, which still starts from `PointerMoved`;
  - `IsCrossInstanceCp2Drop` takes `IDataTransfer`.
- `Views/FileViewer.axaml.cs`: `FileListDrag.TryGet(e.DataTransfer, ...)`.
- `Tools/DropTarget.axaml.cs`: same API moves; the format list now prints `DataFormat.Identifier`.
- `Tools/LogViewer.axaml.cs`, `Controls/FancyTextViewer/FancyTextViewer.axaml.cs`: only added
  `using Avalonia.Input.Platform;` (`SetTextAsync` is now an extension method).
- `ClipInfo` is JSON text (`ToClipString`/`FromClipString`), so the removal of `BinaryFormatter` on Windows
  does not affect it. No other object goes on the clipboard.

Tests (`cp2_avalonia.Tests`)
- csproj: `xunit` 2.9.3 -> `xunit.v3` 3.2.2; `Avalonia.Headless(.XUnit)` -> `12.1.*`. No other test-project change was needed
  (`Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` kept).
- `FileListDragTests.cs`: `DataObject` -> `DataTransfer`/`DataTransferItem`; the format-name test compares `.Identifier`
  (same strings as before); removed the obsolete-warning pragma.
- `TreeViewHelperTests.cs`: `new Binding("IsExpanded", BindingMode.TwoWay)` no longer compiles ->
  `new ReflectionBinding("IsExpanded") { Mode = BindingMode.TwoWay }`.
- `CancellableExtractionTests.cs`: the xUnit v3 analyzer (xUnit1051) asked for
  `TestContext.Current.CancellationToken` on one `Task.Delay`.
- No tests were deleted or weakened.

Checked, no change needed
- `DispatcherTimer`s (`App.axaml.cs`, `MainWindow.axaml.cs`): all are created from UI-thread event handlers.
- No `TopLevel` casts, no `Gestures.` in XAML, no `WindowState` in a style, no `ToggleButton.Checked/Unchecked`
  (only `IsCheckedChanged`), no `SelectingItemsControl` subclasses, no `ExtendClientArea*`/`SystemDecorations`,
  no `KeyboardNavigationHandler`. `Program.cs` (`UsePlatformDetect()`, `WithInterFont()`) needed no change.

## How this was checked beyond build/tests

The existing tests never load `App.axaml` or any window. To get some runtime coverage of the XAML, I temporarily
pointed the test host at the real `App` and instantiated `EditAppSettings`, `EditAttributes`, `EditConvertOpts`,
`EditSector`, `FileViewer` and `MainWindow` headlessly (then removed that code again; it is not in the tree).
- It caught a real crash in my first GroupBox attempt (`DynamicResource` as a resource value), now fixed.
- After the fix all six windows construct and show. The built-in GroupBox resolved to: padding 8, corner radius 4,
  border brush `#66000000`, thickness 1, header presenter `PART_HeaderPresenter` at font size 14, SemiBold, with
  content at normal weight. So the style selector for the header does match.
- That run also showed a "Test Case Cleanup Failure" NullReferenceException inside `AvaloniaTestRunner` after the
  `EditSector` case; the window itself loaded. I did not investigate it (it was my throwaway harness). If it matters,
  open `EditSector` by hand first.
- This says nothing about layout, input, drag/drop or clipboard behaviour.

## Behaviour differences from `cleanup-and-bugfixes`

| Difference | Status |
|---|---|
| **GroupBox look.** The old control drew the header over a notch in the border line (the header "sat on" the border, masked by an opaque background). The built-in GroupBox is not that control; I did not retemplate. Spacing around the header is the built-in default (`GroupBoxHeaderMargin` 0,4,0,12), so the group boxes are likely a bit taller and the header position differs. The header text is SemiBold/14, border and padding match the old values (confirmed by reading values in the headless run), but the actual rendering is unseen. | needs a manual check |
| **Alt+Up in the directory tree.** The access-key underline mode reset (`ShowAccessKeys = false`) is gone, so the menu-bar underlines may stay visible after Alt+Up. | confirmed by reading code (line removed); effect needs a manual check |
| **Compiled bindings.** Turned off project-wide to match 11.3. Existing `x:DataType` uses still compile. 12 also turns data-annotation validation off by default, and the binding class split (`Binding` is now `ReflectionBinding`) is internal, not in app code. | confirmed by reading code |
| **File-list drag payload.** The entries/source/work path now travel as in-process formats. The identifiers are unchanged, but I could not confirm that Avalonia 12's native drag backends deliver an in-process format back to the drop handlers unchanged on macOS/Windows. `FileListDrag.Current` is the existing fallback and is still set for the whole drag, so internal drags should still work. | needs a manual check |
| **Cross-instance JSON drop/paste.** The JSON is now read through a *platform* string format with the old UTI as identifier, and the plain-text fallback is kept. On macOS this must match what `CP2PromiseProvider` writes; I believe it does (same string, used as is), but it was not run. | needs a manual check |
| **"Copy files for Finder / file manager" (`text/uri-list`).** Written as a platform string format named `text/uri-list` (the old code used a raw format string). Reading file lists from Finder/Explorer/Nautilus now goes through `TryGetFilesAsync` (DataFormat.File). Whether pasting our copy into Finder still works, and whether dropping from Finder still works, is unverified. | needs a manual check |
| **Windows drag-out.** Each extracted file is its own `DataTransferItem` (before, one list under `DataFormats.Files`). Windows is untested. | needs a manual check (Windows) |
| **Drag start.** `DoDragDropAsync` takes the original `PointerPressedEventArgs`; I pass the press stored when the drag was armed, rather than the `PointerMoved` args. Whether Avalonia 12 behaves well with a "stale" press event (a drag started after the pointer moved >threshold) is unverified. | needs a manual check |
| **Focus gating.** `Window_FocusChanged` signature only; same logic. | confirmed by reading code |
| **DataGrid.** Now `Avalonia.Controls.DataGrid` 12.1.2 (deprecated in 12.x, still ships). Sorting, column resize, the header-grip handling (`TryGetHeaderGrip`, `DataGridColumnHeader` visual lookups) and the compact row styles rely on its templates, which may have changed. | needs a manual check |
| **Selection handling.** 12 moves selection input into the item containers (`TreeViewItem`, `ListBoxItem`). No app code subclasses these controls, but the tree's Enter key and click/selection timing could differ. | needs a manual check |
| **Access keys.** Now triggered by the typed symbol rather than the virtual key; `AccessText.AccessKey` is a string. The app does not read it. | confirmed by reading code |

## Open issues and `TODO(avalonia12)` markers

- `TODO(avalonia12)` in `Views/MainWindow.axaml.cs` `DismissMenuTimer_Tick`: no public replacement found for
  `IInputRoot.ShowAccessKeys = false`. Worth asking the Avalonia maintainers, or finding what `AccessKeyHandler` does
  on 12 when a non-letter key goes up after Alt.
- Two IL2104 trim warnings on publish (`Avalonia.Controls.DataGrid`, `Avalonia.DesignerSupport`); not compared with 11.3.
- The csproj `NoWarn` still suppresses `CS0618`, which hides obsolete members from normal builds. An extra build without
  the suppression shows none today.
- The comments/log strings in `Platform/MacOSDrag.cs` still say "falling back to Avalonia DoDragDrop"; wording only,
  left as is. The Avalonia 12.2 rework of drag/drop/clipboard is still to come.
- macOS 15 Sequoia is Tier 2 (best effort) in Avalonia 12; Tier 1 is macOS 26.
- The DataGrid package is deprecated in 12.x; the plan to stay on it is unchanged.
- The `EditSector` headless cleanup exception described above (throwaway harness only, not understood).

## What to smoke-test first on macOS

Highest risk first:

1. **Drag out of the file list to Finder, and a drop from Finder** (the native `MacOSDrag` path is unchanged, but its
   Avalonia-facing receive side changed). Then drag between two CiderPress II windows (cross-instance JSON), and an
   internal drag onto a directory in the tree / a folder row in the file list (move).
2. **Copy / Paste / Cut between archives** in one window and across two instances; copy files and paste into Finder;
   copy files in Finder and paste into the app. Hex dump / note "Copy" (plain text), including the Log viewer and the
   FancyTextViewer copy.
3. **GroupBox appearance** in each dialog: Settings (5 boxes), Convert Options (3), Attributes (5), Edit Sector (2),
   File Viewer options, and the Import/Export options in the main window. Look at header position, spacing, border,
   dark theme.
4. **Alt+Up in the directory tree**: does the menu bar stay in access-key (underline) mode afterwards?
5. **Text-focus command gating**: Copy/Paste/Select All/Delete enabled state when a text box has focus vs. when the
   file list or tree has focus (Find box, sector editor, notes).
6. **DataGrid**: file list sorting by header click, column resize (including double-click on the divider to auto-fit and
   the name-column "user sized" logic), row selection, multi-select then drag, and the partition/metadata grids.
7. **Directory tree and archive tree**: Enter key, arrow keys, right-click selection, expand/collapse.
8. **File viewer**: open a file, switch tabs, options box visibility, conversion options.
9. Find-file box placeholder text ("Filename…") shows and goes away.
10. Launch, quit, and window-state restore; the startup fade-in (Opacity 0 -> 1 via `DispatcherTimer`).
11. Windows/Linux: not tested at all (you test on macOS only; Windows drag-out items and the Linux X11 paths need a pass on
    those machines later).
