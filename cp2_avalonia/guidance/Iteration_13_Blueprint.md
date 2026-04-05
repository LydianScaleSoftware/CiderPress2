# Iteration 13 Blueprint: Clipboard & Advanced Drag-and-Drop

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Implement clipboard copy/paste of file entries between archives, and advanced drag-and-drop
(within the app and from external file managers). This is one of the most technically
challenging iterations because the WPF implementation relies heavily on Windows-specific
clipboard formats and COM-based virtual file drag.

---

## Prerequisites

- Iteration 12 is complete.
- Key WPF source files to read:
  - `cp2_wpf/ClipInfo.cs` (156 lines) — clipboard data model with Windows P/Invoke
    (`RegisterClipboardFormat`, `IDataObject`, `System.Windows.Clipboard`)
  - `cp2_wpf/WPFCommon/ClipHelper.cs` (318 lines) — Windows COM clipboard helper:
    `FileGroupDescriptorW`, `FileContents`, STA thread access. **Deeply Windows-specific;
    must be completely replaced.**
  - `AppCommon/ClipFileEntry.cs`, `AppCommon/ClipFileSet.cs`, `AppCommon/ClipFileSource.cs` —
    cross-platform (already in shared library, no porting needed)
  - `cp2_wpf/Actions/ClipPasteProgress.cs` (150 lines) — `WorkProgress.IWorker` wrapping
    `ClipPasteWorker`
  - `cp2_wpf/Tools/DropTarget.xaml` (51 lines) + `.cs` (289 lines) — debug drop/paste test
    tool (**modeless** dialog: no Owner, `ShowInTaskbar=True`, `Close()` not `DialogResult`)
  - `cp2_wpf/MainController.cs` — `CopyToClipboard()` (uses `VirtualFileDataObject`, NOT
    simple JSON), `PasteOrDrop(IDataObject?, IFileEntry)` (checks ProcessId, version,
    export mode, uses `ClipPasteWorker.ClipStreamGenerator`)
  - `cp2_wpf/Delay/VirtualFileDataObject.cs` — Windows VFDO for streaming clipboard
    data. **Not needed for Avalonia** — the JSON-text approach replaces this.

---

## Architecture: Windows vs. Cross-Platform Clipboard

### The Problem

The WPF `ClipInfo.cs` uses:
- `[DllImport("user32.dll")] RegisterClipboardFormat` — registers custom clipboard formats
- Custom clipboard format names: `"faddenSoft:CiderPressII:md-v1"` and
  `"faddenSoft:CiderPressII:st-v1"`
- Binary serialization via `JsonSerializer` into clipboard `MemoryStream`
- `IDataObject` WPF interface for clipboard and drag-drop data

Additionally, the WPF implementation uses:
- `WPFCommon/ClipHelper.cs` (318 lines): COM-based `FileGroupDescriptorW`/`FileContents`
  access, STA thread clipboard access (`GetClipboardContentsSTA()`)
- `Delay/VirtualFileDataObject.cs`: Custom virtual file data object for streaming file
  contents through Windows clipboard (the "sending" side of copy/drag)
- `MainController.CopyToClipboard()` builds a full `VirtualFileDataObject` with descriptors
  and stream generators — NOT a simple JSON serialization
- `MainController.PasteOrDrop()` checks `ClipInfo.ProcessId` to detect same-process paste
  and blocks archive-to-same-archive paste; uses `ClipPasteWorker.ClipStreamGenerator`

These are deeply Windows-specific. The Avalonia port must design a cross-platform strategy.

### Recommended Approach

**Clipboard (Copy/Paste):**
1. Use Avalonia's `IClipboard` interface, accessed via `TopLevel.GetTopLevel(window)?.Clipboard`
   **Verified:** This API is not deprecated on Avalonia 11.2.8 (our current version).
   It is already in use in `FileViewer.axaml.cs` and `EditSector.axaml.cs` with zero warnings.
2. Store clipboard data as **JSON text** with a recognizable prefix/wrapper:
   ```csharp
   const string CLIP_PREFIX = "CiderPressII:clip:";

   // Copy:
   string json = JsonSerializer.Serialize(clipInfo);
   string clipText = CLIP_PREFIX + json;
   await clipboard.SetTextAsync(clipText);

   // Paste:
   string? text = await clipboard.GetTextAsync();
   if (text != null && text.StartsWith(CLIP_PREFIX)) {
       string json = text[CLIP_PREFIX.Length..];
       var clipInfo = JsonSerializer.Deserialize<ClipInfo>(json);
       // Process paste...
   }
   ```
3. This works cross-platform and allows same-app and cross-instance paste
4. Limitation: Only works with CiderPress2-to-CiderPress2 copy/paste. External apps won't
   understand the format. This is acceptable — the external workflow uses Extract/Add.

**Drag-and-Drop:**
1. **Drop from external file manager** (simplest, already done in Iteration 6 for open):
   - Handle `DragDrop.DropEvent` with `DataFormats.Files`
   - Get file paths and open or add them
2. **Drag from CiderPress2 to external** (complex, may be limited):
   - Virtual file drag (streaming files without extracting to disk first) is deeply Windows
     COM-dependent. **Skip this for initial port.**
   - Alternative: Extract selected files to a temp directory, then initiate drag with
     actual file paths. This works but is slower.
   - Or: Simply omit drag-to-external and rely on Extract command.
3. **Internal drag** (within the app, e.g., move files between directories):
   - Use Avalonia `DragDrop.DoDragDrop()` with a custom data format
   - Track source and destination within the app

### Platform Notes

**Implementation priority:** The safest cross-platform subset to implement first is:
1. **JSON text clipboard** (copy/paste via `SetTextAsync`/`GetTextAsync`) — uses only
   plain-text clipboard, which works identically on Linux, Windows, and macOS.
2. **External file drop** (dropping files from OS file manager into CiderPress2) — uses
   `DataFormats.Files` / `IStorageItem.TryGetLocalPath()`, which is well-supported on all
   desktop platforms.

These two paths have the broadest platform support and should be implemented and verified
before attempting internal drag-move or any platform-specific enhancements.

**macOS status:** macOS is currently **untested** — no macOS build environment or test
instance is available. The JSON text clipboard and external file drop paths are expected to
work on macOS via Avalonia's abstractions, but this is unverified. Known macOS
considerations that may surface later:
- `TryGetLocalPath()` may return `null` for sandboxed apps (App Store distribution)
- Pasteboard behavior differences (e.g., clipboard clearing on app exit)
- Drag-and-drop security prompts in recent macOS versions

These should be treated as deferred issues to address when a macOS test environment becomes
available. Do not add `#if` platform guards preemptively — keep the code uniform and fix
platform-specific issues as they are discovered.

**Linux (current dev platform):** Tested on X11/KDE. Wayland clipboard and drag-drop may
have quirks depending on compositor — see note G-02 in Step 7.

---

## Step-by-Step Instructions

### Step 1: Port `cp2_avalonia/ClipInfo.cs` — Cross-Platform Version

Rewrite `cp2_wpf/ClipInfo.cs` without Windows P/Invoke:

**Critical:** The WPF `ClipInfo` uses `[DllImport("user32.dll")] RegisterClipboardFormat`
in static field initializers. These run on class load and will throw `DllNotFoundException`
on Linux/macOS, crashing the application. The entire class must be rewritten with the
JSON-text approach below — no P/Invoke, no `System.Windows.IDataObject`, no COM.

The static method `IsDataFromCP2()` currently accepts `IDataObject?` and checks for a custom
clipboard format. Replace with a method that checks text content:
```csharp
public static bool IsClipTextFromCP2(string? clipText) {
    return clipText != null && clipText.StartsWith(CLIP_PREFIX);
}
```

```csharp
namespace cp2_avalonia;

using System.Text.Json;

/// <summary>
/// Clipboard data model for CiderPress2 file entries.
/// </summary>
internal class ClipInfo {
    private const string CLIP_PREFIX = "CiderPressII:clip:v1:";

    public List<ClipFileEntry>? ClipEntries { get; set; }
    public bool IsExport { get; set; }
    public int AppVersionMajor { get; set; }
    public int AppVersionMinor { get; set; }
    public int AppVersionPatch { get; set; }
    public int ProcessId { get; set; }

    public ClipInfo() { }

    public ClipInfo(List<ClipFileEntry> entries, Version version) {
        ClipEntries = entries;
        AppVersionMajor = version.Major;
        AppVersionMinor = version.Minor;
        AppVersionPatch = version.Build;
        ProcessId = Environment.ProcessId;
    }

    /// <summary>
    /// Serializes this ClipInfo to a clipboard-ready string.
    /// </summary>
    public string ToClipString() {
        return CLIP_PREFIX + JsonSerializer.Serialize(this);
    }

    /// <summary>
    /// Attempts to deserialize a ClipInfo from clipboard text.
    /// </summary>
    public static ClipInfo? FromClipString(string? text) {
        if (text == null || !text.StartsWith(CLIP_PREFIX))
            return null;
        try {
            return JsonSerializer.Deserialize<ClipInfo>(
                text[CLIP_PREFIX.Length..]);
        } catch {
            return null;
        }
    }
}
```

### Step 2: Implement Copy Command

The WPF method is `MainController.CopyToClipboard()`. It builds a `VirtualFileDataObject`
with file descriptors and stream generators — a complex Windows COM pattern. For Avalonia,
replace with the JSON-text approach.

**Key behaviors from the WPF implementation to preserve:**
- Uses `GetFileSelection(omitDir: false, omitOpenArc: false, closeOpenArc: true, ...)`
- Respects single-directory view mode (makes paths relative to selected directory)
- Reads settings: preserve mode, add export extension, raw mode, strip paths, MacZip
- Uses `ClipFileSet` (from `AppCommon/`) to prepare the file set
- Supports both extract and export mode (based on `IsChecked_ImportExport`)

```csharp
// Simplified for Avalonia (no VFDO, no COM):
public async Task CopyToClipboard() {
    // ... GetFileSelection, collect entries, create ClipFileSet ...
    // Cursor override: mMainWin.Cursor = new Cursor(StandardCursorType.Wait)
    // in try/finally around ClipFileSet construction (if needed).
    var clipInfo = new ClipInfo(clipSet.XferEntries, GlobalAppVersion.AppVersion);
    // When in export mode (IsChecked_ImportExport == true), set IsExport:
    if (exportSpec != null) {
        clipInfo.IsExport = true;
    }
    var clipboard = TopLevel.GetTopLevel(mMainWin)?.Clipboard;
    if (clipboard != null) {
        await clipboard.SetTextAsync(clipInfo.ToClipString());
    }
}
```

**Limitation:** The JSON-text approach only stores metadata, not file content streams.
Same-process paste can re-read from the open archive. **Cross-process paste strategy
(G-01):** For the initial Avalonia port, cross-process paste is **out of scope**. Paste
only works within the same running instance. `PasteOrDrop()` must check
`clipInfo.ProcessId == Environment.ProcessId` and reject cross-process paste with a
user-visible message (not silently fail). This is documented under Known Limitations.
Future work could extract to temp files during copy to enable cross-instance paste.

**Important:** `ClipFileEntry` and `ClipFileSet` in `AppCommon/` are cross-platform.
Read them to understand the data model.

### Step 3: Implement Paste Command

The WPF method is `MainController.PasteOrDrop(IDataObject? dropObj, IFileEntry dropTarget)`.
It handles both clipboard paste (dropObj=null) and drag-drop (dropObj=data). Port the paste
path for Avalonia.

**`IDataObject` type change:** WPF uses `System.Windows.IDataObject`. Avalonia uses
`Avalonia.Input.IDataObject` with a different API surface. Method signatures and all usages
must use the Avalonia type. `e.Data` in Avalonia `DragEventArgs` is `Avalonia.Input.IDataObject`.

**Async clipboard access:** WPF `Clipboard.GetDataObject()` is synchronous. Avalonia
clipboard access is async (`GetTextAsync()`). All clipboard-reading paths must be
`async Task` and awaited.

**`CanExecute` for Paste (T2-02):** WPF checks clipboard synchronously in `CanExecutePaste`.
Avalonia clipboard is async, so `CanExecute` cannot check clipboard contents synchronously.
**Recommended approach:** Always enable Paste when preconditions are met (writable target,
multi-file archive). Check clipboard content only *after* invocation. If no compatible
data is found, show a user message. This avoids async polling complexity.

**Key behaviors to preserve (from WPF `PasteOrDrop`):**
- `CheckPasteDropOkay()` — verifies writable + multi-file target
- Checks `ClipInfo.IsExport` — rejects export-mode copies with error message
- Checks `AppVersionMajor/Minor/Patch` — rejects cross-version paste
- Checks `ClipInfo.ProcessId == currentProcess.Id` — same-process detection
- Blocks archive-to-same-archive paste (can't read and write simultaneously)
- `Activate()` the window (important when receiving a drop from another window).
  Avalonia `Window.Activate()` exists and is cross-platform, though on Linux/Wayland it
  may be a no-op due to focus-stealing prevention. No code change needed.
- Gets target directory from selection or drop target
- Uses `ClipPasteWorker.ClipStreamGenerator` delegate for streaming file contents
- Creates `ClipPasteProgress` (WorkProgress.IWorker) with settings: DoCompress,
  EnableMacOSZip, ConvertDOSText, StripPaths, RawMode
- Shows WorkProgress dialog

```csharp
public async Task PasteOrDrop(Avalonia.Input.IDataObject? dropData, IFileEntry dropTarget) {
    // dropData is non-null for drag-drop, null for clipboard paste.
    if (!CheckPasteDropOkay()) return;

    var clipboard = TopLevel.GetTopLevel(mMainWin)?.Clipboard;
    string? text = await clipboard?.GetTextAsync();
    var clipInfo = ClipInfo.FromClipString(text);
    if (clipInfo == null) return;

    // ... version/export/same-process checks as above ...
    // ... create ClipStreamGenerator ...
    // ... create ClipPasteProgress, show WorkProgress dialog ...
}
```

### Step 4: Port Paste Worker

Port `cp2_wpf/Actions/ClipPasteProgress.cs` (150 lines) — a `WorkProgress.IWorker` wrapper
around `ClipPasteWorker` (from `AppCommon/`).

**Constructor:** `ClipPasteProgress(object archiveOrFileSystem, DiskArcNode leafNode,
IFileEntry targetDir, ClipInfo clipInfo, ClipPasteWorker.ClipStreamGenerator streamGen,
AppHook appHook)`

**Properties:** DoCompress, EnableMacOSZip, ConvertDOSText, StripPaths, RawMode

**`DoWork()`:**
- Gets `isSameProcess` from `ClipInfo.ProcessId` check
- Creates `ClipPasteWorker` with all settings
- For IArchive: StartTransaction → AddFilesToArchive → SaveUpdates
- For IFileSystem: AddFilesToDisk → SaveUpdates (always saves, even on partial failure)

The paste operation:
1. Reads file data from the source (may be same process or different)
2. Adds entries to the destination archive/filesystem
3. Shows progress during the operation

For same-process paste: The source data streams may still be open. The `ClipFileEntry`
objects may contain references to open `IArchive` or `IFileSystem` objects.

For cross-process paste: **Out of scope for initial port** (see Step 2 limitation note).
The `ClipStreamGenerator` delegate's constructor parameter is nullable — when `null`,
it means content is not available. `ClipPasteProgress.DoWork()` must handle the null case
by checking `clipInfo.ProcessId != Environment.ProcessId` and rejecting the operation
with a clear error message before attempting to read streams.

### Step 5: Implement File-Path Drop (Add Files)

Extend the drag-drop handling from Iteration 6 to support dropping files onto the
file list area (when an archive is open) to trigger "add files":

```csharp
private void FileList_DragOver(object? sender, DragEventArgs e) {
    // CanAddFiles is not a MainController property — inline the conditions from
    // WPF's CanAddFiles handler (MainWindow.xaml.cs line 436):
    bool canAdd = mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite
        && mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList;
    if (e.Data.Contains(DataFormats.Files) && canAdd) {
        e.DragEffects = DragDropEffects.Copy;
    } else {
        e.DragEffects = DragDropEffects.None;
    }
}

private void FileList_Drop(object? sender, DragEventArgs e) {
    IFileEntry dropTarget = IFileEntry.NO_ENTRY;
    // ... determine dropTarget from e hit-test if desired ...

    if (mIsDraggingFileList) {
        // Internal file drop — only if target is a directory.
        if (dropTarget != IFileEntry.NO_ENTRY && dropTarget.IsDirectory) {
            mMainCtrl.MoveFiles(mDragMoveList, dropTarget);
        }
    } else if (e.Data.Contains(DataFormats.Files)) {
        // External file drop from file manager.
        var files = e.Data.GetFiles()?.ToList();
        if (files != null && files.Count > 0) {
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => p != null)
                .ToArray();
            if (paths.Length > 0) {
                // AddFileDrop is the real WPF method name (MainController.cs line 1340).
                // Port it as async: await mMainCtrl.AddFileDrop(dropTarget, paths!);
                _ = mMainCtrl.AddFileDrop(dropTarget, paths!);
            }
        }
    } else {
        // Check for CiderPress2 clipboard data (internal paste via drag).
        // Use ClipInfo.IsClipTextFromCP2() on text data if needed.
    }
}
```

**Avalonia drag-drop API differences (from WPF):**
- `DataFormats.FileDrop` → `DataFormats.Files`
- `e.Data.GetDataPresent(format)` → `e.Data.Contains(format)`
- `e.Data.GetData(DataFormats.FileDrop)` returning `string[]` → `e.Data.GetFiles()`
  returning `IEnumerable<IStorageItem>?`. File paths via `f.TryGetLocalPath()` (may be
  null for non-local storage items — always null-check).
- `e.Effects = ...` → `e.DragEffects = ...` (property renamed in Avalonia)
- `e.OriginalSource` → `e.Source` (Avalonia `DragEventArgs` has no `OriginalSource`)
- `FindVisualParent<TreeViewItem>(e.OriginalSource as FrameworkElement)` →
  walk up via `control.Parent` or Avalonia visual tree helpers, starting from `e.Source`.
  Both `DependencyObject` and `FrameworkElement` are WPF-only; use `Visual` or
  `StyledElement` as the constraint type.

All three drop handlers (`LaunchPanel_Drop`, `DirectoryTree_Drop`, `FileListDataGrid_Drop`)
need these updates.

### Step 6: Port `cp2_avalonia/Tools/DropTarget.axaml` — Debug Tool

Read `cp2_wpf/Tools/DropTarget.xaml` (51 lines) + `.cs` (289 lines). This is a **modeless**
debug window — no `Owner`, `ShowInTaskbar=True`, parameterless constructor, closes via
`Close()` (not `DialogResult`). Port it for testing:

**Layout (from WPF):**
- 400×600, resizable (MinWidth=200, MinHeight=300)
- Grid: Read-only TextBox with `AllowDrop=True` filling the window
- Bottom: Paste button + Cancel button (Cancel calls `Close()`)
  **Layout fix (T3-04):** The WPF XAML has Paste spanning both columns while Cancel is in
  column 1, causing overlap. Fix in AXAML: use a `StackPanel Orientation="Horizontal"` or
  separate the buttons into explicit columns without `ColumnSpan` overlap.
- Wire `KeyDown` on the Window for Ctrl+V (WPF uses `PreviewKeyDown` — not available as
  an AXAML attribute in Avalonia).
- `Owner = null` in the WPF constructor is unnecessary in Avalonia (windows have no owner
  unless explicitly set via `ShowDialog`). Remove.
- Button click handlers: `RoutedEventArgs` namespace is
  `Avalonia.Interactivity.RoutedEventArgs` (not `System.Windows.RoutedEventArgs`).

**Key behaviors from code-behind (`DropTarget.xaml.cs`):**
- `DoPaste()`: Gets clipboard data object → `ShowDataObject()`
- `TextArea_Drop()`: Gets `e.Data` → `ShowDataObject()`
- `ShowDataObject()`: Dumps ALL available formats to the text box, with special handling
  for `FileGroupDescriptorW`, `FileContents`, `XFER_METADATA_NAME`, `XFER_STREAMS_NAME`
- `DumpDescriptors()`: FileGroupDescriptorW + FileContents (Windows-specific, skip or
  adapt for Avalonia's `IDataObject`)
- `DumpXferEntries()`: Deserializes `ClipInfo` JSON and displays entries
- Uses `Formatter` for hex dump of clipboard data

For Avalonia port: simplify to dump `IDataObject` format names and attempt to deserialize
`ClipInfo` from text. The COM-specific `FileGroupDescriptorW` dump can be omitted.
`IDataObject.GetFormats(false)` (WPF) → `dataObj.GetDataFormats()` (Avalonia, returns
`IEnumerable<string>`, no `autoConvert` parameter).

This is only visible from the DEBUG menu.

### Step 7: Internal Drag (Move Within Filesystem)

For moving files between directories within the same filesystem, implement internal drag:

1. On the file list, detect drag initiation via `PointerPressed` + `PointerMoved` (WPF
   uses `PreviewMouseLeftButtonDown` + `PreviewMouseMove` with `MouseButtonEventArgs` /
   `MouseEventArgs` — all WPF-only). Avalonia equivalents:
   - Left-button state: `e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed`
   - Position: `e.GetPosition(this)` (same API)
   - Drag threshold: `SystemParameters.MinimumHorizontalDragDistance` has no Avalonia
     equivalent; use a hardcoded constant (e.g. 4px)
2. Create a `DataObject` with a custom format containing the selected `IFileEntry` list
3. Call `await DragDrop.DoDragDrop(e, data, DragDropEffects.Move)`. **Note:** Avalonia
   `DoDragDrop` is async (returns `Task<DragDropEffects>`), unlike WPF which is synchronous.
   The `mIsDraggingFileList` flag and `mDragMoveList` cleanup must be in a `finally` block
   after the `await`. Initiate from `PointerMoved` with fire-and-forget:
   `_ = StartFileListDragAsync(e)` (common Avalonia practice).
   **Do not catch `COMException`** — the WPF code catches it around `VirtualFileDataObject
   .DoDragDrop()` but Avalonia's version does not throw COM exceptions. Use a general
   `Exception` catch or remove entirely.
4. On the directory tree, handle `DragDrop.DropEvent` to receive the moved files.
   **Reliability note (T3-03):** Instead of relying solely on `mIsDraggingFileList`, check
   `e.Data.Contains(customFormat)` as a more reliable test for internal drag data.
5. Call `MoveProgress` (from Iteration 8) to execute the move

**Wayland note (G-02):** Avalonia drag-drop on Linux/Wayland may have reliability issues
depending on the Avalonia version and compositor. Verify `DragDrop.DoDragDrop()` works on
the target Linux environment. If it silently fails or hangs under Wayland, gate or disable
the internal drag-move feature. File-path drops from external file managers (XDG protocols)
are more broadly supported.

This is optional for the initial port — file move via the menu command works as a fallback.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/ClipInfo.cs` |
| **Create** | `cp2_avalonia/Actions/ClipPasteProgress.cs` |
| **Create** | `cp2_avalonia/Tools/DropTarget.axaml` |
| **Create** | `cp2_avalonia/Tools/DropTarget.axaml.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (copy/paste/drag methods) |
| **Modify** | `cp2_avalonia/MainWindow.axaml` (drop handlers on file list area) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (drag-drop event handlers, Copy/Paste commands) |

**Command wiring in `MainWindow.axaml.cs`** (not shown elsewhere in this iteration):
```csharp
CopyCommand = new RelayCommand(
    async () => { try { await mMainCtrl.CopyToClipboard(); }
                  catch (Exception ex) { Debug.WriteLine("Copy failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && mMainCtrl.AreFileEntriesSelected);

PasteCommand = new RelayCommand(
    async () => { try { await mMainCtrl.PasteOrDrop(null, IFileEntry.NO_ENTRY); }
                  catch (Exception ex) { Debug.WriteLine("Paste failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && mMainCtrl.CanWrite
          && mMainCtrl.IsMultiFileItemSelected);
```
**Paste CanExecute:** Does NOT check clipboard content (unlike WPF). Avalonia clipboard is
async, so `CanExecute` cannot call `GetTextAsync()`. Instead, always enable Paste when
IsFileOpen + CanWrite + IsMultiFileItemSelected. Clipboard content is checked at runtime
inside `PasteOrDrop()` — if no compatible data is found, show a user-facing message.

---

## Known Limitations

- **Virtual file drag out** (dragging files from CiderPress2 to a file manager with
  streaming) is not feasible cross-platform. Users should use Extract instead.
- **Cross-instance paste** (copying in one CiderPress2 window and pasting in another) may
  be limited to metadata only (no actual file data transfer via text clipboard). Consider
  extracting to temp files during copy if this feature is important.
- **Drag from external file managers into CiderPress2** works well — file paths are
  universally available.
- **macOS untested.** All clipboard and drag-drop functionality is implemented using
  Avalonia's cross-platform abstractions and is expected to work, but has not been verified
  on macOS. Platform-specific issues (sandboxing, pasteboard behavior) are deferred until
  a macOS test environment is available.

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Select files → Ctrl+C copies to clipboard (stores JSON text)
- [ ] Ctrl+V pastes copied files into current archive (same instance)
- [ ] Paste rejects export-mode clipboard data with error
- [ ] Paste rejects cross-version clipboard data with error
- [ ] Same-archive paste blocked (can't read and write simultaneously)
- [ ] Paste shows progress for large operations (via WorkProgress dialog)
- [ ] Paste CanExecute is true when IsFileOpen + CanWrite + IsMultiFileItemSelected
      (does NOT check clipboard content — see Step 3 architecture decision)
- [ ] Paste with no compatible clipboard data shows a user-facing message (not silent failure)
- [ ] Dropping files from OS file manager onto file list triggers add
- [ ] Dropping files onto launch panel triggers open (from Iteration 6)
- [ ] DragOver shows correct cursor (copy vs. not-allowed)
- [ ] DEBUG → Drop/Paste Target opens as modeless window (multiple open, own taskbar entry)
- [ ] Drop/Paste Target shows clipboard format names and ClipInfo JSON on paste
- [ ] Internal drag-move between directories works (if implemented)
- [ ] Error handling for paste failures (source closed, incompatible archive)
