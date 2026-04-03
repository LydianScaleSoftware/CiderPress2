# Iteration 12 Blueprint: Library Tests & Bulk Compress

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Port the test-running infrastructure that allows the GUI to execute the DiskArc and FileConv
library test suites, displaying results with rich formatting. Also port the bulk compression
testing tool and the "Test Files" action worker.

---

## Prerequisites

- Iteration 11 is complete: sector editor working.
- Key WPF source files to read (ALL exist at these exact paths):
  - `cp2_wpf/LibTest/TestManager.xaml` (64 lines) — test runner dialog with RichTextBox
  - `cp2_wpf/LibTest/TestManager.xaml.cs` (245 lines) — BackgroundWorker, dynamic lib loading
  - `cp2_wpf/LibTest/TestRunner.cs` (279 lines) — reflection-based test discovery & execution
  - `cp2_wpf/LibTest/ProgressMessage.cs` (38 lines) — simple text+color data class
  - `cp2_wpf/LibTest/BulkCompress.xaml` (90 lines) — compression test dialog
  - `cp2_wpf/LibTest/BulkCompress.xaml.cs` (232 lines) — BackgroundWorker, file selector
  - `cp2_wpf/LibTest/BulkCompressTest.cs` (349 lines) — compression test logic
  - `cp2_wpf/Actions/TestProgress.cs` (209 lines) — `WorkProgress.IWorker` for Actions→Test Files
- MainController debug methods (search for `Debug_DiskArcLibTests`, `Debug_FileConvLibTests`,
  `Debug_BulkCompressTest`)

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/LibTest/TestManager.axaml`

Read `cp2_wpf/LibTest/TestManager.xaml` (64 lines). Port the layout.

**Window:** 1024×768, resizable (`CanResize="True"`; WPF `CanResizeWithGrip` has no direct
Avalonia equivalent — resizable windows get grip chrome by default).
MinWidth=640, MinHeight=480. Unique among project dialogs — most others are fixed-size.

**Layout** (DockPanel):
- **Top dock:** StackPanel with Run/Cancel button + "Retain output" CheckBox
- **Bottom dock:** Close button (IsCancel=True)
- **Center** (two areas):
  1. **RichTextBox** (`progressRichTextBox`) with FlowDocument — primary output area.
     WPF uses `AppendText(text, color)` extension for colored test progress.
     → Replace with AvaloniaEdit `TextEditor` (read-only, colored text via
     `DocumentColorizingTransformer` or `FancyTextHelper` pattern from Iteration 7).
     **Alternative:** A simpler approach is an `ItemsControl` bound to an
     `ObservableCollection<(string Text, Color Color)>` with a `TextBlock` item template.
     This avoids AvaloniaEdit for output-only colored text and naturally supports scrolling.
     Choose whichever approach is more consistent with Iteration 7's pattern.
  2. **ComboBox** (`outputSelectComboBox`) — populated with per-test results for
     selecting individual test output. **Avalonia note:** `ComboBox.Items.Add()` is not
     available. Bind `ItemsSource` to an `ObservableCollection<TestRunner.TestResult>`
     property (e.g. `OutputItems`). `PopulateOutputSelect()` populates that collection
     instead of calling `Items.Add()`.
  3. **TextBox** (`outputTextBox`) — plain text, scrollable, shows detailed failure
     output for the test selected in the ComboBox

This dialog has **TWO output areas**: colored progress (RichTextBox) + per-test detail
(ComboBox + TextBox). Ensure both are ported.

**String resources:** Remove the `<Window.Resources>` block containing
`<system:String x:Key="str_RunTest">` / `<system:String x:Key="str_CancelTest">` entries.
These are a WPF pattern. Replace with string constants in code-behind:
```csharp
private const string STR_RUN_TEST = "Run Test";
private const string STR_CANCEL_TEST = "Cancel";
```

### Step 2: Port `cp2_avalonia/LibTest/TestManager.axaml.cs`

Read `cp2_wpf/LibTest/TestManager.xaml.cs` (245 lines). Port the code-behind.

**Constructor:** `TestManager(string testLibName, string testIfaceName)`
- Do NOT accept or set `Owner` — pass owner via `ShowDialog(owner)` at the call site.
- Takes dynamic DLL name (e.g., `"DiskArcTests.dll"`) and interface name (e.g.,
  `"DiskArcTests.ITest"`) — NOT hardcoded
- Creates and configures `BackgroundWorker` with DoWork/ProgressChanged/RunWorkerCompleted
- **Thread-safety note (G-01):** AvaloniaEdit's `TextDocument` is not thread-safe. All
  document mutations (appending text, clearing) MUST happen on the UI thread. Since
  `BackgroundWorker.ProgressChanged` fires on the UI thread, direct document appends there
  are safe. Do NOT move document appends into `DoWork`. Add a comment to this effect.
- `Window_Closing` parameter: `CancelEventArgs` → `WindowClosingEventArgs`
  (`Avalonia.Controls`). `e.Cancel` still works.

**Key behaviors:**
- `RunButton_Click`: starts `BackgroundWorker`, passes library info to `TestRunner`.
  Button label toggling: replace `FindResource("str_RunTest")` / `FindResource("str_CancelTest")`
  with string constants (see Step 1 note).
- `ProgressChanged`: receives `ProgressMessage` objects, appends colored text.
  **AvaloniaEdit specifics:** Each append must (a) add text to `TextEditor.Document`,
  (b) record a `(startOffset, length, color)` span for the `DocumentColorizingTransformer`,
  (c) set caret offset to document length and call `BringCaretToView()` to auto-scroll.
  Replace `mDefaultColor = ((SolidColorBrush)progressRichTextBox.Foreground).Color` with
  a constant like `Colors.Black` or project-standard default text color.
  Replace `progressRichTextBox.ScrollToEnd()` → caret-based scrolling as above.
  Replace `mFlowDoc.Blocks.Clear()` in `ResetDialog()` →
  `progressTextEditor.Document.Text = string.Empty` (also clear the color span list).
- `RunWorkerCompleted`: populates output ComboBox via the `OutputItems` observable
  collection (not `Items.Add()`).
- `PopulateOutputSelect()`: populates `OutputItems` collection instead of calling
  `outputSelectComboBox.Items.Add()`.
- `OutputSelectComboBox_SelectedIndexChanged`: `SelectionChangedEventArgs` namespace is
  `Avalonia.Controls.SelectionChangedEventArgs` (not `System.Windows.Controls`).

### Step 3: Port `cp2_avalonia/LibTest/TestRunner.cs`

Read `cp2_wpf/LibTest/TestRunner.cs` (279 lines). Port with minimal changes.

**Key design:** Uses **reflection exclusively** — no direct project references needed:
- `Assembly.LoadFile(dllPath)` to load the test DLL
- `Mirror.FindImplementingTypes(assembly, interfaceName)` to find test classes
- Iterates all public static methods starting with `"Test"`, invokes via reflection
- Each test method takes an `AppHook` parameter

**Inner class:** `TestResult` — { Name (string), Success (bool), Exc (Exception?) }

**Color usage:** `System.Windows.Media.Colors` → replace with `Avalonia.Media.Colors`.
All four named colors (`Colors.Blue`, `.Green`, `.Red`, `.OrangeRed`) exist in Avalonia.

**`GetTestRoot()`** — searches upward for `TestData/` directory. Contains a dev-machine
hack that strips 4 path levels from `baseDir` (which is the *directory* of the exe, not
the exe itself). For WPF: `baseDir` = `.../cp2_wpf/bin/Debug/net6.0-windows/` → strip 4:
`net6.0-windows` → `Debug` → `bin` → `cp2_wpf` → solution root. For Avalonia:
`baseDir` = `.../cp2_avalonia/bin/Debug/net8.0/` → strip 4: `net8.0` → `Debug` → `bin` →
`cp2_avalonia` → solution root. **The count stays at 4** — both `net6.0-windows` and
`net8.0` are single directory components. Update the comment from `net6.0-windows` to
`net8.0`, but do NOT change the loop count. Alternatively, replace the fixed-count hack
with a search-upward strategy (walk parent directories looking for `TestData/`).

**`GetDLLLocation()`** — returns directory of the TestRunner assembly (where test DLLs
should also be located).

### Step 4: Port `cp2_avalonia/LibTest/ProgressMessage.cs`

Read `cp2_wpf/LibTest/ProgressMessage.cs` (38 lines). Simple data class:
- `Text` (string), `Color` (`Avalonia.Media.Color`), `HasColor` (bool)
- Uses `Color.FromArgb(0,0,0,0)` as "no color" sentinel — same API in Avalonia
- Replace `using System.Windows.Media` → `using Avalonia.Media`
- The `HasColor` check (`Color.A != 0`) is identical in Avalonia's `Color` struct

### Step 5: Port `cp2_avalonia/LibTest/BulkCompress.axaml` + `.cs`

Read `cp2_wpf/LibTest/BulkCompress.xaml` (90 lines) and `.cs` (232 lines).

**Window:** 1024×640, resizable (`CanResize="True"`; WPF `CanResizeWithGrip` → default
Avalonia resize chrome). MinWidth=200, MinHeight=200.

**Layout:**
- File selector ("Select File..." button + TextBox for path)
- 8 RadioButtons (2 columns) for compression format selection: Squeeze, NuLZW1, NuLZW2,
  Deflate, LZC/12, LZC/16, LZHUF, ZX0
- Run Test / Cancel button (dual-purpose, label changes — use string constants as in
  TestManager: `STR_RUN_TEST` / `STR_CANCEL_TEST`, not `FindResource()`)
- TextBox (logTextBox, read-only) for test output. **Avalonia note:** `TextBox` has no
  `AppendText()` method. **Preferred approach:** Bind to a `string LogText` property backed
  by a `StringBuilder`, and rebuild the string with `OnPropertyChanged` on each append.
  Alternatively, use `logTextBox.Text += msg.Text` (simpler but O(n²) for large outputs).
  For auto-scroll, set `logTextBox.CaretIndex = logTextBox.Text?.Length ?? 0` after
  appending (Avalonia `TextBox` has no `ScrollToEnd()`). For consistency with
  Iteration 5's `DebugMessageLog`, an `ObservableCollection<string>` bound to an
  `ItemsControl` is also viable.
- Bottom dock: Close button + progress message TextBlock
- **Border note:** The `<Border BorderBrush="LightGray" BorderThickness="1">` wrapping
  `logTextBox` translates directly to Avalonia — `LightGray` is a recognized named color.

**Constructor:** `BulkCompress(AppHook appHook)` — 1 param (no `owner`).
Do NOT accept or set `Owner` — pass owner via `ShowDialog(owner)` at the call site.
`Window_Closing` parameter: `CancelEventArgs` → `WindowClosingEventArgs`
(`Avalonia.Controls`).

**Key behaviors:**
- `ChooseFileButton_Click`: Uses `WinUtil.AskFileToOpen()` → replace with async
  `StorageProvider.OpenFilePickerAsync()`. The handler becomes `async void`.
  `RoutedEventArgs` namespace: `Avalonia.Interactivity.RoutedEventArgs` (not
  `System.Windows.RoutedEventArgs`). Same for all button click handlers in both dialogs.
- Same BackgroundWorker pattern as TestManager
- `Window_Closing` cancels running worker
- `BulkCompressTest.RunTest()` is the worker — reads archives, compresses/decompresses,
  verifies round-trip correctness

**Port `BulkCompressTest.cs`** (349 lines) — mostly platform-independent logic. Only
change: ensure `FileStream` usage works cross-platform (it should).

### Step 6: Port `cp2_avalonia/Actions/TestProgress.cs`

Read `cp2_wpf/Actions/TestProgress.cs` (209 lines). This is a `WorkProgress.IWorker`
implementation for the **Actions → Test Files** command (NOT related to the debug
library tests above).

**Constructor:** `TestProgress(object archiveOrFileSystem, List<IFileEntry> selected, AppHook appHook)`

**Namespace note:** The WPF version imports `cp2_wpf.WPFCommon` for `WorkProgress.IWorker`.
In the Avalonia project, use `using cp2_avalonia.Common;` — `WorkProgress` was ported
in Iteration 3 to `cp2_avalonia/Common/WorkProgress.axaml(.cs)`.

**Key behaviors:**
- `DoWork()`: iterates selected files, reads each fork via `arc.OpenPart()` /
  `fs.OpenFile()`, copies to `Stream.Null` to verify readability
- Reports progress via `ProgressUtil.HandleCallback()`
- `Failure` inner class: { Entry (IFileEntry), Part (FilePart) }
- `FailureResults` — list of failures set in `RunWorkerCompleted()`
- Handles IArchive and IFileSystem separately
- Uses `FilePart.RawData` for DOS filesystem testing

This is invoked by `MainController.TestFiles()` which uses `WorkProgress` dialog.
The Avalonia `TestFiles()` method skeleton:
```csharp
public async Task TestFiles() {
    if (!GetFileSelection(omitDir: false, omitOpenArc: false, closeOpenArc: true,
            oneMeansAll: false, out object? archiveOrFileSystem,
            out IFileEntry unusedDir, out List<IFileEntry>? selected, out int unused)) {
        return;
    }
    if (selected.Count == 0) {
        await MessageBox.Show(mMainWin, "No files selected.", "Empty",
            MBButton.OK, MBResult.OK);
        return;
    }
    TestProgress prog = new TestProgress(archiveOrFileSystem, selected, AppHook) {
        EnableMacOSZip = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true),
    };
    WorkProgress workDialog = new WorkProgress(prog, false);
    bool? result = await workDialog.ShowDialog<bool?>(mMainWin);  // MUST await
    if (result == true) {
        List<TestProgress.Failure>? results = prog.FailureResults!;
        if (results.Count != 0) {
            // Build failure text, show in ShowText dialog
        } else {
            mMainWin.PostNotification("Tests successful, no failures", true);
        }
    } else {
        mMainWin.PostNotification("Cancelled", false);
    }
}
```
**Critical:** `await workDialog.ShowDialog<bool?>(mMainWin)` must be awaited. Without
`await`, the code after ShowDialog runs immediately while the dialog is still open,
producing incomplete or stale results.

### Step 7: Wire DEBUG Menu Commands + TestFilesCommand

Three debug methods in `MainController` — port to async, pass owner via `ShowDialog`:
```csharp
public async Task Debug_DiskArcLibTests() {
    LibTest.TestManager dialog = new LibTest.TestManager("DiskArcTests.dll",
        "DiskArcTests.ITest");
    await dialog.ShowDialog(mMainWin);
}

public async Task Debug_FileConvLibTests() {
    LibTest.TestManager dialog = new LibTest.TestManager("FileConvTests.dll",
        "FileConvTests.ITest");
    await dialog.ShowDialog(mMainWin);
}

public async Task Debug_BulkCompressTest() {
    LibTest.BulkCompress dialog = new LibTest.BulkCompress(AppHook);
    await dialog.ShowDialog(mMainWin);
}
```

These only appear when the DEBUG menu is visible (`ShowDebugMenu` setting).

**Also wire `TestFilesCommand`** for Actions → Test Files (missing from the debug-only
commands above — this is a user-facing action):
```csharp
TestFilesCommand = new RelayCommand(
    async () => { try { await mMainCtrl.TestFiles(); }
                  catch (Exception ex) { Debug.WriteLine("TestFiles failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && ShowCenterFileList
          && mMainCtrl.AreFileEntriesSelected);
```
This matches the WPF `TestFilesCmd` with `CanExecute="AreFileEntriesSelected"` (which
checks `IsFileOpen`, `ShowCenterFileList`, and `AreFileEntriesSelected`).

**Test library loading:** Uses **reflection** (`Assembly.LoadFile`) — NO project references
to `DiskArcTests` or `FileConvTests` are needed. The test DLLs must be present in the
output directory (they should be if the solution builds all projects). **Important:** Verify
that `DiskArcTests.dll` and `FileConvTests.dll` are copied to `cp2_avalonia/bin/Debug/net8.0/`.
If not, add explicit `<ProjectReference>` entries or `<CopyToOutputDirectory>` items in
`cp2_avalonia.csproj` to ensure the test DLLs are present at runtime.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/LibTest/TestManager.axaml` |
| **Create** | `cp2_avalonia/LibTest/TestManager.axaml.cs` |
| **Create** | `cp2_avalonia/LibTest/TestRunner.cs` |
| **Create** | `cp2_avalonia/LibTest/ProgressMessage.cs` |
| **Create** | `cp2_avalonia/LibTest/BulkCompress.axaml` |
| **Create** | `cp2_avalonia/LibTest/BulkCompress.axaml.cs` |
| **Create** | `cp2_avalonia/LibTest/BulkCompressTest.cs` |
| **Create** | `cp2_avalonia/Actions/TestProgress.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (DEBUG test commands + TestFiles action) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire commands) |

---

## Verification Checklist

- [x] `dotnet build` succeeds
- [x] DEBUG menu → Run DiskArc Tests opens test runner with colored progress output
- [x] DEBUG menu → Run FileConv Tests opens test runner with different DLL
- [x] Tests execute in background without freezing UI
- [x] Progress shown with pass/fail coloring in the RichTextBox/AvaloniaEdit area
- [x] Per-test failure details selectable via ComboBox + displayed in TextBox
- [x] Scroll to see all results
- [x] Can close dialog during or after tests
- [x] DEBUG menu → Bulk Compress Test opens compression tool
- [x] Can select file, choose compression format, run test
- [x] Actions → Test Files tests selected files in archive/filesystem (uses TestProgress)
- [x] Test Files shows progress via WorkProgress dialog
