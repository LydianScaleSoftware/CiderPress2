# Prompt: port cp2_avalonia to Avalonia 12.1 (exploratory)

Paste everything below the line into a new agent session started in
`/Users/mlong/develop/CiderPress2`.

---

You are porting the CiderPress II Avalonia app (`cp2_avalonia/`, tests in `cp2_avalonia.Tests/`)
from Avalonia 11.3 to Avalonia 12.1. The work happens on the branch `avalonia-12.1-test`, which
is already checked out. That branch was cut from `cleanup-and-bugfixes` at commit `3ad50eb`. This
is an exploratory port; the branch may be thrown away later.

## Goal

A **direct conversion**: the app on 12.1 behaves and looks as it does on `cleanup-and-bugfixes`.
Change only what Avalonia 12 forces you to change, plus the one requested swap (the built-in
GroupBox, below). No redesigns, no refactoring, no new features, no "while I'm here" cleanups.
If a 12.x API offers a nicer way to do something that still compiles the old way, leave the old
way.

The parity reference is `cleanup-and-bugfixes` at `3ad50eb`. Read it with
`git show cleanup-and-bugfixes:<path>` or `git diff cleanup-and-bugfixes -- <path>`; do not
check it out.

## Hard rules

- Never run `git commit`, `git push`, `git checkout`/`switch`, `git stash`, `git reset`, `git
  rebase`, or anything else that changes branches or history. Leave every change in the working
  tree; the user commits.
- Touch only `cp2_avalonia/` and `cp2_avalonia.Tests/`. Do not touch `DiskArc`, `FileConv`,
  `CommonUtil`, `AppCommon`, `cp2_wpf` or any other project. If a fix seems to need one, stop
  and ask.
- WPF (`cp2_wpf`) behaviour is the source of truth for user-visible behaviour. Never change
  behaviour away from it; never edit `cp2_wpf`.
- Do not add paid or commercial packages (for example Avalonia Plus Dev Tools). Do not switch
  controls to different ones (for example DataGrid to TreeDataGrid or TableView).
- Stop and ask the user before any change that would alter layout, keyboard behaviour,
  selection behaviour, menus or drag/drop/clipboard behaviour beyond a mechanical API rename.
- The user tests only on macOS 15 Sequoia on an Intel Mac (x86_64). You cannot drive the UI:
  never say anything was tested in the UI. Say plainly which manual checks were not run.
- Keep each file's existing conventions (BOM, line endings, brace style, `mFieldName` fields).
- Use the task-list tool to show your plan and the wrap-up tool at the end of each turn.

## Reference material

The full Avalonia 12.x documentation is in the RAG server (`mcp__rag__search_documents`):
collections **Avalonia docs**, **Avalonia API** and **Avalonia controls**. Start with:

- `docs/avalonia12-breaking-changes.md`: the full list of 11 → 12 breaks. Read it all once.
- `docs/input-interaction/drag-and-drop.md`, `docs/how-to/drag-and-drop-how-to.md`,
  `docs/services/clipboard.md`: the new `DataTransfer` API.
- `controls/layout/containers/groupbox.md` and `api/avalonia/controls/groupbox.mdx`.

Prefer these docs over memory for 12.x API names. Note that Avalonia 12 lists macOS 15
Sequoia as Tier 2 (best effort); Tier 1 is macOS 26.

## Known work (from a scan of the code; confirm each before changing it)

1. **Packages.** In `cp2_avalonia/cp2_avalonia.csproj` move `Avalonia`, `Avalonia.Desktop`,
   `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid` and `Avalonia.Fonts.Inter` from
   `11.3.*` to the 12.1 line (`12.1.*`). DataGrid is marked deprecated in the 12.x docs but
   still ships as a package. Check that a 12.1 version exists on NuGet first, for example with
   `dotnet package search Avalonia.Controls.DataGrid --exact-match`. If DataGrid has no 12.1.x
   build, use the highest 12.x build that restores and works, and record that. If it has no
   12.x build at all, stop and ask. Leave non-Avalonia packages alone unless they fail to
   restore.
2. **`Avalonia.Diagnostics` is removed in 12.** Delete its Debug-only `PackageReference`. The
   code does not call `AttachDevTools`, so nothing replaces it (no paid Dev Tools).
3. **Compiled bindings are on by default in 12.** Only 6 of the ~25 `.axaml` files declare
   `x:DataType`, so the rest would fail. To keep 11.3 behaviour, add
   `<AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>` to the
   csproj rather than adding `x:DataType` everywhere. Keep the existing `x:DataType` and
   `x:CompileBindings` uses as they are.
4. **Built-in GroupBox (requested).** Avalonia 12 has `Avalonia.Controls.GroupBox` (a
   `HeaderedContentControl`). Replace the homemade one:
   - Delete `cp2_avalonia/Controls/GroupBox.cs` and the `ControlTheme` for `controls:GroupBox`
     in `cp2_avalonia/App.axaml`.
   - In `MainWindow.axaml`, `EditAppSettings.axaml`, `EditConvertOpts.axaml`,
     `EditAttributes.axaml`, `EditSector.axaml` and `FileViewer.axaml`, change
     `<common:GroupBox …>` (or whatever the prefix is) to `<GroupBox …>`. Remove the xmlns
     prefix only if nothing else uses it.
   - `Tools/ConfigOptCtrl.cs` has both `using Avalonia.Controls;` and
     `using cp2_avalonia.Controls;`. It will compile against the built-in type once ours is
     gone, but check for other ambiguities. `Views/FileViewer.axaml.cs` uses
     `optionsGroupBox.IsVisible`, which needs no change.
   - Keep the look close to the old theme without retemplating. The old theme had padding 8,
     border `SystemControlForegroundBaseMediumLowBrush`, thickness 1, corner radius 4, and a
     SemiBold header in the normal body font size over a notch in the border. If the built-in
     defaults differ visibly (the docs list `GroupBoxHeaderFontSize` 16, `GroupBoxPadding` 4,
     `GroupBoxBorderBrush` `SystemControlForegroundBaseMediumBrush`), override the documented
     `GroupBox*` theme resources in `App.axaml`, or add a plain `Style` setting the GroupBox's
     own properties. Do not write a new `ControlTemplate`. Record any difference you could not
     match, such as the notched header, for the user to judge.
5. **`IInputRoot` is removed in 12.** `MainWindow.axaml.cs`, `DismissMenuTimer_Tick`, does
   `((IInputRoot)this).ShowAccessKeys = false;`. This clears the access-key underline mode
   that Alt+Up (navigate to parent) leaves behind. Find the 12.x way to do the same (search the
   API docs for the access-key handler or `TopLevel` members). If there is none, keep the rest
   of the Alt+Up handling, comment the line out with a `TODO(avalonia12)` explaining what is
   lost, and record it as an open issue. Do not redesign the Alt+Up handling.
6. **Focus events.** `GotFocus`/`LostFocus` now use `FocusChangedEventArgs`. `MainWindow`
   adds `Window_FocusChanged` for both, which tracks whether a TextBox has focus for the
   Copy/Paste/Select All/Delete gating. Fix the handler signature only.
7. **Clipboard and drag/drop: mechanical port only.** `IDataObject`/`DataObject`/`DataFormats`
   become `IAsyncDataTransfer`/`DataTransfer`/`DataFormat`. `DragEventArgs.Data` becomes
   `.DataTransfer`. `DoDragDrop` becomes `DoDragDropAsync`, which needs the
   `PointerPressedEventArgs`; our file-list drag starts in `PointerMoved` after a press arms it,
   so keep the press arguments. Clipboard reads become `TryGet*Async`.
   Files: `Services/ClipboardService.cs`, `Services/IClipboardService.cs`, `Models/ClipInfo.cs`,
   `Common/FileListDrag.cs`, `ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml.cs`,
   `Views/FileViewer.axaml.cs`, `Tools/DropTarget.axaml.cs`, `Tools/LogViewer.axaml.cs`,
   `Controls/FancyTextViewer/FancyTextViewer.axaml.cs`, the clipboard users in
   `ViewModels/FileViewerViewModel.cs`, `EditSectorViewModel.cs` and
   `OptionsPanelViewModel.cs`, `App.axaml.cs`, and the Avalonia-facing parts of
   `Platform/MacOSDrag.cs` and `Platform/LinuxDrag.cs`.
   - Keep the native macOS/Linux drag code and its behaviour as they are. Change only the
     Avalonia types it touches.
   - The whole area will be reworked properly when Avalonia 12.2 ships, so don't do that now.
   - If a feature cannot be kept with a mechanical port, stop and ask. This covers virtual
     files on the clipboard, internal copy/paste between archives, drag out to Finder and drop
     from Finder.
   - On Windows, `BinaryFormatter` is no longer used for clipboard objects. Check whether
     `ClipInfo` relied on implicit object serialization. If it did, serialize it explicitly
     (JSON or bytes) and record that.
8. **`DispatcherTimer` now uses the current dispatcher.** They are created in `App.axaml.cs`
   and `MainWindow.axaml.cs`; confirm each is created on the UI thread (no change expected).
9. **Renamed and obsolete members.** `TextBox.Watermark` becomes `PlaceholderText`
   (`findFileTextBox` in `MainWindow.axaml`). The csproj suppresses CS0618 (obsolete), so
   obsolete 12.x members are hidden in normal builds. Do one extra build with
   `dotnet build cp2_avalonia/cp2_avalonia.csproj -p:NoWarn=IL2026` to see them. Fix the
   Avalonia ones only if the fix is a direct rename. List the rest in the notes.
10. **Tests.** `Avalonia.Headless.XUnit` 12 targets xUnit v3. Move `cp2_avalonia.Tests` from
    `xunit` 2.9.3 to `xunit.v3`, and `Avalonia.Headless.XUnit`/`Avalonia.Headless` to the
    matching 12.1 versions. Follow xUnit's v3 migration guide, keeping test code changes
    mechanical. `TreeViewHelperTests.cs` builds a `new Binding(...)` in code: `Binding` still
    works (it is now `ReflectionBinding`), so change it only if it no longer compiles. Do not
    delete or weaken tests to make them pass; a test that fails because of a real 12.x behaviour
    change is a finding to report.
11. **Other breaks to check, probably not hit.**
    - Casting the visual root to `TopLevel` (use `TopLevel.GetTopLevel`).
    - `Gestures.` attached events in XAML.
    - `WindowState` set from a style.
    - `ToggleButton.Checked`/`Unchecked` events.
    - Any `SelectingItemsControl` subclass that handles selection at the `ItemsControl` level
      (12 moves selection handling into the containers).
    - Data validation now on by default in custom controls.
    - Text shaper setup: `Program.cs` uses `UsePlatformDetect()` and `WithInterFont()`, which
      should be fine.

## Process

1. Read the breaking-changes doc and this list. Then build once on the new packages to get the
   real error list, and make the task list from that.
2. Fix in small steps. After each step run
   `dotnet build cp2_avalonia/cp2_avalonia.csproj` and aim for **0 errors and 0 warnings**. Any
   warning you can't remove without redesign goes in the notes.
3. When the app builds, run `dotnet test cp2_avalonia.Tests`. All tests that pass on
   `cleanup-and-bugfixes` should pass here. Also build the solution with
   `dotnet build CiderPress2.sln -p:EnableWindowsTargeting=true` (it must not break the other
   projects).
4. Check that a Release publish still works:
   `dotnet publish cp2_avalonia/cp2_avalonia.csproj -c Release -r osx-x64`. It is trimmed, and
   12.x may need new trimmer roots. It should still produce
   `bin/Release/net10.0/osx-x64/CiderPress II.app`. Do not install it.
5. Keep a running log in `cp2_avalonia/TempTestingFiles/AVALONIA_12_1_PORT_NOTES.md` with these
   sections:
   - **Package versions used:** the exact versions that restored.
   - **Changes:** each change, with the file and the reason (which 12.x break forced it).
   - **Behaviour differences from `cleanup-and-bugfixes`:** known or suspected. Mark each as
     *confirmed by reading code* or *needs a manual check*.
   - **Open issues and TODO(avalonia12) markers.**
   - **What to smoke-test first on macOS:** a short list, highest risk first. Expect it to
     include GroupBox appearance in each dialog, Alt+Up in the directory tree,
     copy/paste/drag, text-focus command gating, DataGrid sorting and column resize, the
     tree's Enter key, and the file viewer.

## Final report

End with a summary of:

- Whether the app and tests build, with warning counts, and the test pass/fail counts.
- The package versions used.
- The list of behaviour differences and open issues.
- Which manual checks were not run (all of them, since you cannot drive the UI).
- That nothing was committed.
