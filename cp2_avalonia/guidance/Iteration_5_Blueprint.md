# Iteration 5 Blueprint: Simple Dialogs (First Batch)

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Port the first batch of simple dialog windows: ShowText, LogViewer, CreateDirectory
(archive filesystem), and DebugMessageLog. These are small, self-contained XAML windows
that serve as good exercises for the AXAML porting pattern before tackling complex dialogs.

**Note:** The About Box was already created in Iteration 1 as a smoke test for the menu
bar. It does not need to be created here.

**Note:** `WPFCommon/CreateFolder` (host filesystem folder creation) is a dependency of
`FileSelector` — defer it to the Extract & Add iteration (Iteration 6). The dialog ported
here is `CreateDirectory` (archive filesystem directory creation), which is wired to the
menu bar's Actions → Create Directory command.

---

## Prerequisites

- Iteration 4 is complete: file list populates when navigating the tree.
- Key WPF source files to read:
  - `cp2_wpf/Tools/ShowText.xaml/.cs` — simple text display
  - `cp2_wpf/Tools/LogViewer.xaml/.cs` — 3-column log viewer
  - `cp2_wpf/CreateDirectory.xaml/.cs` — archive dir creation
  - `cp2_wpf/DebugMessageLog.cs` — thread-safe log with circular buffer

---

## Step-by-Step Instructions

### Step 0: Add `GeneralMonoFont` Resource to `App.axaml`

The WPF `App.xaml` defines `<FontFamily x:Key="GeneralMonoFont">Consolas</FontFamily>`
(line 29) and it is referenced in 14+ XAML files. No prior iteration added this resource
to the Avalonia project. Steps 1 and 3 of this iteration reference it, and every
subsequent dialog iteration will need it too.

Add to `cp2_avalonia/App.axaml`, inside `<Application.Resources>`:
```xml
<FontFamily x:Key="GeneralMonoFont">Consolas</FontFamily>
```

This must be done **before** building any AXAML that uses
`{StaticResource GeneralMonoFont}`, or the parser will throw
`ResourceNotFoundException` at load time.

### Step 1: Port `cp2_avalonia/Tools/ShowText.axaml/.cs`

Read `cp2_wpf/Tools/ShowText.xaml/.cs`. This is a simple window for displaying mono-spaced
text. It supports both modal and modeless usage.

Porting tasks:
1. Convert XAML → AXAML. The window has a single `TextBox` in a `Grid`:
   ```xml
   <!-- IsReadOnly and AcceptsReturn are intentional additions (not in WPF original)
        to prevent accidental edits and ensure proper multi-line display. -->
   <TextBox IsReadOnly="True" AcceptsReturn="True"
            FontFamily="{StaticResource GeneralMonoFont}"
            Text="{Binding DisplayText}"/>
   ```
2. Port the `DisplayText` property with `INotifyPropertyChanged` (text can be
   updated after construction).
3. **Modal vs modeless pattern:** The WPF constructor takes `Window owner` —
   when `owner == null`, `ShowInTaskbar` is set to `true` so the modeless window
   appears in the taskbar (otherwise it can get lost). In Avalonia:
   - Modal: `dialog.ShowDialog(ownerWindow)`
   - Modeless: `dialog.Show()` with `ShowInTaskbar = true`
   
   **Important:** Remove `Owner = owner` from the constructor body. In Avalonia 11,
   `Window.Owner` has no public setter — it is set automatically by the framework
   when you call `ShowDialog(owner)` or `Show(owner)`. Attempting to set it directly
   will not compile.
4. ESC key handler: `PreviewKeyDown` → Avalonia `KeyDown` event,
   `e.Key == Key.Escape` → `Close()`.

### Step 2: Port `cp2_avalonia/Tools/LogViewer.axaml/.cs`

Read `cp2_wpf/Tools/LogViewer.xaml/.cs`. This shows debug log
messages with auto-scroll. It is always **modeless** (toggle open/close).

Porting tasks:
1. Convert XAML → AXAML. The WPF layout is a `DockPanel` with:
   - Bottom: "Save to File" button
   - Fill: `ItemsControl` with a `ScrollViewer` template and `VirtualizingStackPanel`

2. **Port the `LogEntry` wrapper class** (defined at the bottom of `LogViewer.xaml.cs`).
   This wraps `DebugMessageLog.LogEntry` for XAML binding:
   ```csharp
   public class LogEntry {
       private static readonly string[] sSingleLetter = { "V", "D", "I", "W", "E", "S" };
       public int Index { get; private set; }
       public DateTime When { get; private set; }
       public string Priority { get; private set; }  // single letter
       public string Message { get; private set; }
       public LogEntry(DebugMessageLog.LogEntry entry) { ... }
   }
   ```

3. **Port the 3-column DataTemplate.** The WPF version uses a `Grid` with three
   columns inside the `DataTemplate`, not a simple `TextBlock`:
   ```xml
   <ItemsControl.ItemTemplate>
       <DataTemplate>
           <Grid ColumnDefinitions="Auto,Auto,*">
               <TextBlock Grid.Column="0"
                   Text="{Binding When, StringFormat='{}{0:yyyy/MM/dd HH:mm:ss.fff}'}"/>
               <TextBlock Grid.Column="1" Margin="8,0,0,0"
                   Text="{Binding Priority}" FontWeight="Bold"/>
               <TextBlock Grid.Column="2" Margin="8,0,0,0"
                   Text="{Binding Message}" TextWrapping="Wrap"/>
           </Grid>
       </DataTemplate>
   </ItemsControl.ItemTemplate>
   ```
   Use `ListBox` (not bare `ItemsControl`) with this template. `ListBox` provides
   built-in `ScrollViewer` and virtualization; bare `ItemsControl` does not scroll
   without a custom `ControlTemplate`. Disable selection highlighting so it behaves
   like the WPF `ItemsControl` (no selection chrome):
   ```xml
   <ListBox.Styles>
       <Style Selector="ListBoxItem">
           <Setter Property="Padding" Value="0"/>
       </Style>
       <Style Selector="ListBoxItem:selected /template/ ContentPresenter">
           <Setter Property="Background" Value="Transparent"/>
       </Style>
   </ListBox.Styles>
   ```
   Wire the `ScrollChanged` event on the `ListBox`'s internal `ScrollViewer` by
   finding it after the control is loaded:
   ```csharp
   logListBox.AddHandler(ScrollViewer.ScrollChangedEvent, ScrollViewer_ScrollChanged);
   ```
   This uses `AddHandler` on the `ListBox`; the `ScrollChangedEvent` bubbles up
   from the internal `ScrollViewer`. If scroll events are not received at runtime
   (template structure varies between themes), use this fallback instead:
   ```csharp
   logListBox.Loaded += (_, _) => {
       var sv = logListBox.FindDescendantOfType<ScrollViewer>();
       if (sv != null) sv.ScrollChanged += ScrollViewer_ScrollChanged;
   };
   ```

4. **Auto-scroll with engage/disengage.** The WPF version has sophisticated
   auto-scroll logic in `ScrollViewer_ScrollChanged`:
   - If `ExtentHeightChange == 0` → user scrolled: disengage auto-scroll if not
     at bottom, re-engage if scrolled to bottom
   - If `ExtentHeightChange != 0` → content changed: if auto-scroll is engaged,
     call `ScrollToVerticalOffset(ExtentHeight)` to scroll to bottom
   
   In Avalonia, `ScrollViewer` has `ScrollChanged` event but the property names
   differ. Port using these Avalonia equivalents:
   - `e.ExtentHeightChange` → `e.ExtentDelta.Y`  (Avalonia's `ExtentDelta` is a
     `Vector` with `.X`/`.Y` — not a `Size`, so `.Height` won't compile)
   - `sv.ScrollableHeight` → `sv.Extent.Height - sv.Viewport.Height`
   - `sv.ScrollToVerticalOffset(sv.ExtentHeight)` → `sv.ScrollToEnd()`
   
   ```csharp
   private void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e) {
       if (sender is not ScrollViewer sv) return;
       if (e.ExtentDelta.Y == 0) {
           // User scroll: engage/disengage auto-scroll based on position
           bool atBottom = sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 1;
           mAutoScroll = atBottom;
       } else {
           // Content changed: scroll to bottom if auto-scroll is engaged
           if (mAutoScroll) {
               sv.ScrollToEnd();
           }
       }
   }
   ```

5. **Constructor and lifecycle.** The `LogViewer` constructor:
   - Takes `DebugMessageLog log` (drop the `Window? owner` parameter — this dialog
     is always modeless with `ShowInTaskbar = true`, so no owner is needed; in
     Avalonia 11, `Window.Owner` has no public setter anyway)
   - Sets `ShowInTaskbar = true`
   - Subscribes to `mLog.RaiseLogEvent += HandleLogEvent`
   - Pulls all existing logs via `mLog.GetLogs()` into `LogEntries`
   - `Window_Closed` unsubscribes: `mLog.RaiseLogEvent -= HandleLogEvent`

6. **Save to File** button: Replace `Microsoft.Win32.SaveFileDialog` with Avalonia
   `StorageProvider.SaveFilePickerAsync()`:
   ```csharp
   var topLevel = TopLevel.GetTopLevel(this);
   var file = await topLevel!.StorageProvider.SaveFilePickerAsync(
       new FilePickerSaveOptions {
           Title = "Save Debug Log...",
           SuggestedFileName = "cp2-log.txt",
           FileTypeChoices = new[] {
               new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
               new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
           }
       });
   if (file != null) {
       await using var stream = await file.OpenWriteAsync();
       using var writer = new StreamWriter(stream);
       // Write each LogEntry formatted as "HH:mm:ss.fff P Message"
   }
   ```
   The WPF `SaveLog_Click` writes entries with `StringBuilder` to a `StreamWriter`.
   Port the formatting logic directly.

### Step 3: Port `cp2_avalonia/CreateDirectory.axaml/.cs`

Read `cp2_wpf/CreateDirectory.xaml/.cs`. This is the dialog for creating
a directory **inside an archive filesystem** (ProDOS, HFS, etc.), wired to the menu bar's
Actions → Create Directory command.

Porting tasks:
1. Convert XAML → AXAML. The layout has 5 rows:
   - Row 0: "Enter directory name:" label
   - Row 1: `TextBox` with mono font, bound to `NewFileName`
   - Row 2: Syntax rules text (colored: default or red on error)
   - Row 3: Unique name check text (colored)
   - Row 4: OK/Cancel buttons

2. Port properties with `INotifyPropertyChanged`:
   - `NewFileName` (string) — bound to TextBox, calls `UpdateControls()` on change
   - `IsValid` (bool) — controls OK button `IsEnabled`
   - `SyntaxRulesText` (string) — filesystem-specific rules from constructor
   - `SyntaxRulesForeground` / `UniqueNameForeground` (`IBrush`) — red on error,
     default label color otherwise. Replace WPF `SystemColors.WindowTextBrush`
     with Avalonia theme-aware brush. Use the `ThemeForegroundBrush` dynamic resource:
     ```csharp
     private static readonly IBrush sDefaultLabelBrush =
         Application.Current?.TryFindResource("ThemeForegroundBrush",
             Application.Current.ActualThemeVariant, out var brush) == true
         ? (IBrush)brush! : Brushes.Black;
     ```
     Or bind the labels to `{DynamicResource ThemeForegroundBrush}` in AXAML as the
     default and only switch to `Brushes.Red` in code for the error state.

3. Constructor takes `(Window parent, IFileSystem fs, IFileEntry containingDir,
   IsValidDirNameFunc func, string syntaxRules)`. Port the `IsValidDirNameFunc`
   delegate (it wraps `IFileSystem.IsValidFileName`).

4. `UpdateControls()` validates via the delegate and checks uniqueness via
   `mFileSystem.TryFindFileEntry(mContainingDir, mNewFileName, out _)`.

5. `ContentRendered` → Avalonia `Opened` event: select all text and focus TextBox.

6. `OkButton_Click` just sets `DialogResult = true`. In Avalonia, use
   `Close(true)` and show via `dialog.ShowDialog<bool?>(owner)`.
   
   **Important:** Remove `Owner = parent` from the constructor body. In Avalonia 11,
   `Window.Owner` has no public setter — ownership is set by `ShowDialog<T>(owner)`.
   
   **Note:** In Avalonia, `IsCancel="True"` on the Cancel button calls `Close()`
   which returns `null` (the default for `bool?`), not `false`. The calling code
   must check `result == true` (not `result != false`):
   ```csharp
   if (await dialog.ShowDialog<bool?>(owner) == true) {
       // user clicked OK
   }
   ```

7. The calling code in `MainController.CreateDirectory()` then calls
   `fs.CreateFile(targetDir, dialog.NewFileName, CreateMode.Directory)`,
   refreshes the file list, and selects the new entry. Port this caller too.

**Note:** `WPFCommon/CreateFolder` (host filesystem folder creation, used by
`FileSelector`) is deferred to the Extract & Add iteration when `FileSelector`
is ported.

### Step 4: Port `cp2_avalonia/DebugMessageLog.cs`

Read `cp2_wpf/DebugMessageLog.cs`. This implements `MessageLog` (from
`CommonUtil`) as a thread-safe circular buffer. Port with these specific changes:

1. The `OnRaiseLogEvent()` method checks if it's on the UI thread and dispatches
   if not. The WPF code uses:
   ```csharp
   // WPF — DO NOT USE
   Thread.CurrentThread == Application.Current.Dispatcher.Thread  // check
   Application.Current.Dispatcher.Invoke(...)                      // dispatch
   ```
   Replace with Avalonia equivalents. Use `Post()` (fire-and-forget), **not**
   `Invoke()` (synchronous blocking). Synchronous `Invoke()` risks deadlock: the
   `WorkProgress.MessageBoxQuery` blocks the UI thread with `Monitor.Wait()` —
   if a background thread logs a message and calls `Dispatcher.UIThread.Invoke()`
   while the UI thread is waiting, both threads block indefinitely.
   ```csharp
   // Avalonia
   if (Dispatcher.UIThread.CheckAccess()) {
       raiseEvent(this, e);
   } else {
       Dispatcher.UIThread.Post(() => raiseEvent(this, e));
   }
   ```

2. The rest of the class (circular buffer with `mTopEntry`/`mMaxLines`, `GetLogs()`
   unwinding, `LogEntry` inner class, `LogEventArgs`, `RaiseLogEvent` event) is
   pure logic — port directly with no changes.

3. Remove `using System.Windows;` — replace with `using Avalonia.Threading;`.

4. **Add `mDebugLog` field to `MainController.cs`.** The WPF source declares
   `private DebugMessageLog mDebugLog;` (line 57) and initializes it in the
   constructor: `mDebugLog = new DebugMessageLog();` (line 100), followed by
   `AppHook = new AppHook(mDebugLog);` (line 101). Add the same field declaration
   and initialization to the Avalonia `MainController` constructor. Without this,
   Step 5's `Debug_ShowDebugLog()` code that references `mDebugLog` will not compile.

### Step 5: Wire Commands and Toggle Lifecycle

Port the `Debug_ShowDebugLog()` toggle pattern from `MainController.cs`. The WPF
version manages the `LogViewer` as a singleton toggle:

```csharp
private Tools.LogViewer? mDebugLogViewer;
public bool IsDebugLogOpen => mDebugLogViewer != null;

public void Debug_ShowDebugLog() {
    if (mDebugLogViewer == null) {
        var dlg = new Tools.LogViewer(mDebugLog);
        dlg.Closing += (sender, e) => {
            mDebugLogViewer = null;
        };
        dlg.Show();
        mDebugLogViewer = dlg;
    } else {
        mDebugLogViewer.Close();
    }
}
```

Also close the log viewer in `WindowClosing()` (already has `mDebugLogViewer?.Close()`
in the WPF version).

The `Debug_ShowDebugLogCommand` in `MainWindow.axaml.cs`:
```csharp
Debug_ShowDebugLogCommand = new RelayCommand(
    () => {
        mMainCtrl.Debug_ShowDebugLog();
        IsDebugLogVisible = mMainCtrl.IsDebugLogOpen;
    });
```

This bridges the controller's `IsDebugLogOpen` to `MainWindow.IsDebugLogVisible`
(defined in Iteration 1, Step 7) so the DEBUG menu checkbox reflects the actual
toggle state. The WPF version does this in `DebugMenu_SubmenuOpened`; Avalonia's
data-binding approach requires the property to be updated immediately after the
toggle call.

**`MessageBoxQuery` stub:** Any background file-open (Iterations 3–5) that
triggers a `MessageBoxQuery.AskUser()` call will permanently hang the background
worker via `Monitor.Wait()` because no UI-side handler is wired. Define a
temporary no-op callback that returns `MBResult.OK` immediately and logs a
warning, to prevent silent hangs until the real dialog is wired in a later
iteration.

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Tools/ShowText.axaml` |
| **Create** | `cp2_avalonia/Tools/ShowText.axaml.cs` |
| **Create** | `cp2_avalonia/Tools/LogViewer.axaml` — 3-column `DataTemplate`, auto-scroll |
| **Create** | `cp2_avalonia/Tools/LogViewer.axaml.cs` — includes `LogEntry` wrapper class |
| **Create** | `cp2_avalonia/CreateDirectory.axaml` — archive filesystem dir creation |
| **Create** | `cp2_avalonia/CreateDirectory.axaml.cs` — fs-aware validation, colored labels |
| **Create** | `cp2_avalonia/DebugMessageLog.cs` — circular buffer, `Dispatcher.UIThread` |
| **Modify** | `cp2_avalonia/MainController.cs` — `Debug_ShowDebugLog()` toggle, `CreateDirectory()` caller |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` — wire LogViewer command |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Help → About still works (created in Iteration 1)
- [ ] DEBUG → Show Debug Log opens the log viewer window (modeless)
- [ ] Clicking DEBUG → Show Debug Log again closes it (toggle behavior)
- [ ] Log viewer shows 3-column entries: timestamp, priority (bold), message
- [ ] Log viewer auto-scrolls on new messages; disengages when user scrolls up
- [ ] Log viewer "Save to File" works with Avalonia file dialog
- [ ] Log viewer unsubscribes from log events on close (no leak)
- [ ] ShowText window displays mono-spaced text correctly (modal and modeless)
- [ ] ShowText ESC key closes the window
- [ ] CreateDirectory dialog shows filesystem syntax rules and name uniqueness check
- [ ] CreateDirectory OK button disabled when name is invalid or duplicate
- [ ] CreateDirectory dialog creates directory in archive filesystem on OK
