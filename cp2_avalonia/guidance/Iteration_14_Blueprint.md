# Iteration 14 Blueprint: Scan Blocks & Physical Drive Access

> **First:** Read `cp2_avalonia/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Port the block scanning progress dialog and the physical drive selection dialog. The
physical drive access is inherently platform-specific: Windows uses `\\.\PhysicalDriveN`
with WMI enumeration, Linux uses `/dev/sdX` or `/dev/disk/by-id/`, macOS uses
`/dev/diskN`. This iteration introduces the first significant platform-conditional code.

---

## Prerequisites

- Iteration 13 is complete.
- Key WPF source files to read:
  - `cp2_wpf/SelectPhysicalDrive.xaml` (99 lines) — drive selection dialog
  - `cp2_wpf/SelectPhysicalDrive.xaml.cs` (144 lines) — `DiskItem` inner class wrapping
    `PhysicalDriveAccess.DiskInfo`, auto-selects first Removable disk, `CanOpen` blocks
    Fixed media, saves `PHYS_OPEN_READ_ONLY` setting
  - `CommonUtil/PhysicalDriveAccess.cs` — already has platform check in `GetDiskList()`
    (returns `null` on non-Windows). `DiskInfo` class has: `Number`, `Name`, `MediaType`
    (enum: Unknown, Fixed, Removable, Other), `Size`. Windows `Win` inner class uses
    `CreateFile`/`DeviceIoControl` P/Invoke with `\\.\PhysicalDriveN` (tries 0-15).
  - `cp2_wpf/Actions/ScanBlocksProgress.cs` (146 lines) — `WorkProgress.IWorker`, takes
    `(IDiskImage diskImage, AppHook appHook)`, handles both sectors AND blocks
  - `cp2_wpf/MainController.cs` — `OpenPhysicalDrive()` (admin escalation via
    `WinUtil.IsAdministrator()` + `Process.Start` with `"runas"` verb),
    `DoOpenPhysicalDrive()` (blocks PhysicalDrive0, uses `SafeFileHandle`),
    `ScanForBadBlocks()` (results displayed via `ShowText` dialog, not MessageBox)

---

## Architecture: Platform-Conditional Physical Drive Access

### Windows (Existing)
`PhysicalDriveAccess` in `CommonUtil/` uses:
- WMI queries (`Win32_DiskDrive`) to enumerate physical disks
- `CreateFile` P/Invoke with `\\.\PhysicalDriveN` paths
- `DeviceIoControl` for geometry info

### Linux
- Enumerate `/dev/sd*`, `/dev/nvme*`, `/dev/mmcblk*`
- Or read `/proc/partitions` or `/sys/block/`
- Open device files directly (requires root or disk group membership)
- Use `ioctl(BLKGETSIZE64)` for size

### macOS
- Enumerate `/dev/disk*` (exclude `/dev/disk*s*` for partitions)
- Use `diskutil list -plist` for structured info
- Open device files (requires root)

### Recommended Implementation

Create a platform abstraction in `cp2_avalonia/Common/PhysicalDriveInfo.cs`:

```csharp
namespace cp2_avalonia.Common;

/// <summary>
/// Cross-platform physical drive enumeration.
/// </summary>
public static class PhysicalDriveInfo {
    public record DriveEntry(string DevicePath, string Label, string MediaType, long SizeBytes);

    public static List<DriveEntry> EnumerateDrives() {
        if (OperatingSystem.IsWindows())
            return EnumerateWindows();
        else if (OperatingSystem.IsLinux())
            return EnumerateLinux();
        else if (OperatingSystem.IsMacOS())
            return EnumerateMacOS();
        else
            return new List<DriveEntry>();
    }

    private static List<DriveEntry> EnumerateWindows() {
        // Delegate to existing PhysicalDriveAccess.GetDiskList()
        // (Already implemented in CommonUtil)
        var diskList = PhysicalDriveAccess.GetDiskList();
        if (diskList == null) return new List<DriveEntry>();
        return diskList.Select(d => new DriveEntry(
            d.Name,                        // DevicePath (device filename)
            "Physical disk #" + d.Number,  // Label
            d.MediaType.ToString(),        // MediaType string
            d.Size                         // SizeBytes
        )).ToList();
    }

    private static List<DriveEntry> EnumerateLinux() {
        var drives = new List<DriveEntry>();
        if (!Directory.Exists("/sys/block"))
            return drives;
        foreach (var dir in Directory.GetDirectories("/sys/block")) {
            string name = Path.GetFileName(dir);
            // Skip loop devices, ram disks, device-mapper (LVM), zram, and
            // software RAID arrays.
            if (name.StartsWith("loop") || name.StartsWith("ram") ||
                    name.StartsWith("dm-") || name.StartsWith("zram") ||
                    name.StartsWith("md"))
                continue;
            // Skip logical/stacked devices (non-empty slaves/ directory).
            string slavesDir = Path.Combine(dir, "slaves");
            if (Directory.Exists(slavesDir) &&
                    Directory.GetDirectories(slavesDir).Length > 0)
                continue;

            string sizePath = Path.Combine(dir, "size");
            if (File.Exists(sizePath)) {
                long sectors = long.Parse(File.ReadAllText(sizePath).Trim());
                long bytes = sectors * 512;
                string devPath = "/dev/" + name;

                // Read model name, if available.
                string model = "";
                string modelPath = Path.Combine(dir, "device/model");
                if (File.Exists(modelPath))
                    model = File.ReadAllText(modelPath).Trim();

                // Determine media type from the kernel's removable flag.
                string mediaType = "Fixed";
                string removablePath = Path.Combine(dir, "removable");
                if (File.Exists(removablePath)) {
                    string val = File.ReadAllText(removablePath).Trim();
                    if (val == "1")
                        mediaType = "Removable";
                }

                drives.Add(new DriveEntry(devPath, model, mediaType, bytes));
            }
        }
        return drives;
    }

    private static List<DriveEntry> EnumerateMacOS() {
        // Parse output of: diskutil list -plist
        // Or enumerate /dev/disk* and use diskutil info -plist diskN
        var drives = new List<DriveEntry>();
        // Implementation TBD — may use Process.Start("diskutil", ...)
        return drives;
    }
}
```

---

## Step-by-Step Instructions

### Step 1: Create `cp2_avalonia/Common/PhysicalDriveInfo.cs`

Implement the cross-platform drive enumeration abstraction as outlined above.

Key design decisions:
- Use `OperatingSystem.IsWindows()` / `IsLinux()` / `IsMacOS()` for platform detection
- On Windows, delegate to the existing `PhysicalDriveAccess.GetDiskList()` from `CommonUtil`
- On Linux, read from `/sys/block/` (no root needed for enumeration, only for access)
- On macOS, parse `diskutil` output
- Return a simple `DriveEntry` record list

### Step 2: Port `cp2_avalonia/SelectPhysicalDrive.axaml`

Read `cp2_wpf/SelectPhysicalDrive.xaml`. Port the layout.

**Window:** 360px wide, SizeToContent Height, `CanResize="False"`.

**Layout:**
- Row 0: "Select device to open:" TextBlock
- Row 1: DataGrid with columns:
  - Device (120px) — with tooltip bound to `FileName` (full device path)
  - Type (100px) — media type
  - Size (right-aligned, fill) — formatted MB/GB
  - Gray text for items where `CanOpen=False`
- Row 2: DockPanel — Open button + Cancel + "Open read-only" checkbox

**WPF attributes to remove (no Avalonia equivalents):**
- `SnapsToDevicePixels="True"` — Avalonia handles pixel snapping at the renderer level
- `VerticalGridLinesBrush="#FF7F7F7F"` — grid line appearance is themed in Avalonia;
  customize via DataGrid theme overrides only if needed
- `Background="{DynamicResource {x:Static SystemColors.WindowBrush}}"` — Avalonia has no
  `SystemColors` class; DataGrid background is theme-controlled, so omit entirely

**WPF CellStyle / ElementStyle / MultiDataTrigger → Avalonia approach:**
- Remove all `DataGridTextColumn.ElementStyle` and `CellStyle` blocks — these do not exist
  in Avalonia
- For tooltip on the Device column and right-alignment on the Size column, use
  `DataGridTemplateColumn` with a `DataTemplate` containing a `TextBlock`
- For grayed-out text when `CanOpen=False`: add a computed `IsGrayed` property to
  `DiskItem` (`bool IsGrayed => !CanOpen`). Use a DataGrid row style with an Avalonia
  class selector, or bind `Foreground` in each `DataGridTemplateColumn` cell template via
  a value converter that returns `LightGray` when `IsGrayed` is true and not selected.
  See Step 3 for `DiskItem.IsGrayed`.

For the DataGrid in Avalonia:
```xml
<DataGrid Name="diskItemDataGrid"
    ItemsSource="{Binding DiskItems}"
    AutoGenerateColumns="False"
    IsReadOnly="True"
    SelectionMode="Single">
  <DataGrid.Columns>
    <DataGridTemplateColumn Header="Device" Width="120">
      <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding Label}" ToolTip.Tip="{Binding FileName}"/>
        </DataTemplate>
      </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>
    <DataGridTextColumn Header="Type" Width="100" Binding="{Binding MediaType}" />
    <DataGridTemplateColumn Header="Size" Width="*">
      <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding SizeFormatted}" HorizontalAlignment="Right"/>
        </DataTemplate>
      </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>
  </DataGrid.Columns>
</DataGrid>
```

### Step 3: Port `cp2_avalonia/SelectPhysicalDrive.axaml.cs`

Port the code-behind from `cp2_wpf/SelectPhysicalDrive.xaml.cs` (144 lines):

**DiskItem inner class** — wraps `PhysicalDriveInfo.DriveEntry` (the cross-platform
record type from Step 1, NOT `PhysicalDriveAccess.DiskInfo` directly):
```csharp
public class DiskItem {
    public PhysicalDriveInfo.DriveEntry Info { get; private set; }
    public string Label { get; private set; }         // Info.Label
    public string FileName { get; private set; }      // Info.DevicePath (tooltip)
    public string MediaType { get; private set; }     // Info.MediaType
    public string SizeFormatted { get; private set; } // auto-scaled: GB for ≥1 GB, else MB
    public bool CanOpen { get; private set; }         // false for "Fixed" disks
    public bool IsGrayed => !CanOpen;                 // for grayed-out row styling

    // Format size with auto-scaling.
    private static string FormatSize(long bytes) {
        const long GB = 1024L * 1024 * 1024;
        const long MB = 1024L * 1024;
        if (bytes >= GB)
            return (bytes / (double)GB).ToString("N1") + " GB";
        else
            return (bytes / MB).ToString("N0") + " MB";
    }
}
```

**Constructor** (no `owner` parameter — Avalonia passes owner via `ShowDialog(owner)`):
1. Calls `PhysicalDriveInfo.EnumerateDrives()` (cross-platform; delegates to
   `PhysicalDriveAccess.GetDiskList()` on Windows)
2. Populates `ObservableCollection<DiskItem>` from result
3. Auto-selects first `Removable` disk (not index 0!)
4. Initializes `OpenReadOnly` from `AppSettings.PHYS_OPEN_READ_ONLY` (default `true`)

**Behaviors:**
- `IsValid` bound to OK button — set based on `CanOpen` of selected item
- `SelectionChanged` (using `Avalonia.Controls.SelectionChangedEventArgs`) updates
  `IsValid`; null-guard `SelectedItem` since Avalonia DataGrid can clear selection
  on keyboard/scroll
- `DoubleTapped` on DataGrid row → check `diskItemDataGrid.SelectedItem` (replaces
  WPF `GetClickRowColItem` extension method + `MouseButtonEventArgs`); if `CanOpen`,
  call `Finish()`
- `Finish()`: saves `PHYS_OPEN_READ_ONLY` setting, sets `SelectedDisk = item.Info`,
  calls `Close(true)` (Avalonia typed dialog result via `ShowDialog<bool?>()`)
- Use `OnOpened()` override (or `Opened` event) to focus DataGrid — replaces WPF
  `ContentRendered`

**Row styling for grayed-out items:**
- `DiskItem.IsGrayed` (`=> !CanOpen`) is the binding source for visual graying
- Use a value converter or bind `Foreground` in the `DataGridTemplateColumn` cell
  templates to `IsGrayed`, returning `LightGray` when true. Alternatively, use Avalonia
  `DataGrid` row `Classes` with a CSS-like selector for the grayed state.
- Tooltip on Device column shows full `FileName` (handled by `ToolTip.Tip` binding in
  the `DataGridTemplateColumn` cell template — see Step 2)

**For cross-platform:** `GetDiskList()` returns `null` on non-Windows. Either:
1. Extend `PhysicalDriveAccess` in CommonUtil with Linux/macOS implementations, or
2. Create `PhysicalDriveInfo.cs` in `cp2_avalonia/` that wraps the call and adds
   platform-specific enumeration (as described in Architecture section above)

### Step 4: Port `cp2_avalonia/Actions/ScanBlocksProgress.cs`

Port `cp2_wpf/Actions/ScanBlocksProgress.cs` (146 lines) — a `WorkProgress.IWorker`.

**Namespace:** The WPF source uses `using cp2_wpf.WPFCommon;` to access `WorkProgress.IWorker`.
The Avalonia port must reference the Avalonia project's equivalent namespace:
`using cp2_avalonia.Common;` (where `WorkProgress` was ported in Iteration 3).

**Constructor:** `ScanBlocksProgress(IDiskImage diskImage, AppHook appHook)` — takes
the full disk image, NOT `IChunkAccess`.

**Failure inner class:**
```csharp
public class Failure {
    public uint BlockOrTrack { get; private set; }
    public uint Sector { get; private set; }        // uint.MaxValue = block mode
    public bool IsUnreadable { get; set; }
    public bool IsUnwritable { get; set; }
    public bool IsBlock => Sector == uint.MaxValue;  // distinguishes block vs sector

    // Two constructors: one for track/sector, one for block
    public Failure(uint track, uint sector, bool isUnreadable, bool isUnwritable);
    public Failure(uint block, bool isUnreadable, bool isUnwritable);  // sets Sector=uint.MaxValue
}
```

**`DoWork(BackgroundWorker worker)`** → returns `List<Failure>`:
1. Gets chunks: tries `disk.ChunkAccess` first, if closed falls back to
   `((IFileSystem)disk.Contents).RawAccess`
2. Calls `ScanDisk()` which handles BOTH modes:
   - **`chunks.HasSectors`**: iterates `trk=0..NumTracks`, `sct=0..NumSectorsPerTrack`,
     calls `chunks.TestSector(trk, sct, out bool isWritable)`
   - **`chunks.HasBlocks`**: iterates `blk=0..FormattedLength/BLOCK_SIZE`,
     calls `chunks.TestBlock(blk, out bool isWritable)`
3. `TestSector`/`TestBlock` return `false` for unreadable; `out isWritable` distinguishes
   unwritable blocks (failure can be read-only or read+write failure)
4. Supports cancellation via `bkWorker.CancellationPending`

**`RunWorkerCompleted(object? results)`**: stores `FailureResults = (List<Failure>?)results`

### Step 5: Wire Commands

In `MainController.cs`:

**`OpenPhysicalDrive()`** (from WPF `MainController.OpenPhysicalDrive()`, line ~564):

Must be `async Task` (MessageBox calls are async in Avalonia).

1. `CloseWorkFile()` first — this is synchronous (returns `bool`, never shows a dialog;
   confirmed in Iteration 9). Use `if (!CloseWorkFile()) return;`.
2. Show `SelectPhysicalDrive` dialog via `await dialog.ShowDialog<bool?>(mMainWin)`
   (no `owner` constructor parameter — Avalonia passes owner at call site)
3. If selected, handle privilege escalation **cross-platform**:
   - Use `Environment.IsPrivilegedProcess` (.NET 7+) instead of
     `WinUtil.IsAdministrator()` — works on all platforms (checks root on Linux/macOS,
     admin on Windows)
   - **Windows only**: If not privileged, prompt to restart with `"runas"` verb. On
     confirmation, call
     `((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown()`
     (replaces `Application.Current.Shutdown()`)
   - **Linux/macOS**: If not privileged, show an async `MessageBox` with an informational
     message: "Physical drive access requires elevated privileges. Run the application
     with `sudo` or `pkexec`, or add your user to the `disk` group." Then return early —
     do NOT attempt auto-restart with sudo (too dangerous). This replaces the Windows UAC
     `ProcessStartInfo.Verb = "runas"` path which does not exist on Linux/macOS.
   - **Pre-flight privilege check (G-02)**: Consider test-opening the selected device
     path before proceeding, to catch `UnauthorizedAccessException` early. This provides
     a better UX than failing only after `DoOpenPhysicalDrive`, especially for Linux users
     who have `disk` group membership (where `Environment.IsPrivilegedProcess` returns
     false but access actually succeeds).
4. Call `DoOpenPhysicalDrive(deviceName, readOnly)`

**`DoOpenPhysicalDrive(deviceName, readOnly)`**:

Must be platform-conditional throughout — the entire body needs
`if (OperatingSystem.IsWindows()) { ... } else { ... }`:

- **Boot disk safety check:**
  - **Windows**: block `PhysicalDrive0` (existing behavior)
  - **Linux**: determine the root device by reading `/proc/mounts` for the mount point `/`
    and extracting the device path (e.g., `/dev/sda1` → `/dev/sda`). Block the selected
    device if it matches the root device's base disk. More robust: use `stat /` to get
    `st_dev`, then match against `/sys/block/*/dev` major:minor entries.

- **Device opening:**
  - **Windows**: use `PhysicalDriveAccess.Win.OpenDisk()` returning `SafeFileHandle`
    (existing behavior, guarded by `OperatingSystem.IsWindows()` to prevent P/Invoke
    `DllNotFoundException` on other platforms)
  - **Linux/macOS**: open device file with `new FileStream(devicePath, ...)`.
    Device size cannot come from `stream.Length` (returns 0 for device files); instead use
    `ioctl(BLKGETSIZE64)` on Linux. This requires a small P/Invoke:
    ```csharp
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, out long size);
    // BLKGETSIZE64 = 0x80081272 on most Linux architectures
    ```
    On macOS, use `ioctl(DKIOCGETBLOCKCOUNT)` + `ioctl(DKIOCGETBLOCKSIZE)`.
    Alternatively, read `/sys/block/{name}/size` (sectors × 512) for the device size
    without needing ioctl.

- **Cursor:**
  ```csharp
  mMainWin.Cursor = new Cursor(StandardCursorType.Wait);
  try {
      // ... device open logic ...
  } finally {
      mMainWin.Cursor = null;
  }
  ```
  Replaces `Mouse.OverrideCursor = Cursors.Wait` / `= null`.

- **All `MessageBox.Show()` calls**: Replace with async `MessageBox` helper
  calls. At least four sites:
  - "not running as Administrator" prompt
  - Device handle invalid errors
  - `ScanForBadBlocks` chunk-access error
  Callers must be `async Task`.

**`ScanForBadBlocks()`** (actual WPF name — NOT `DoScanBlocks`):

Must be `async Task` (MessageBox and ShowDialog calls are async):

1. Gets `CurrentWorkObject as IDiskImage` (menu item only enabled when disk image is open)
2. Checks `diskImage.ChunkAccess != null` — if null, shows error via async `MessageBox`
3. Creates `ScanBlocksProgress(diskImage, AppHook)`
4. Shows `WorkProgress` dialog via `await dialog.ShowDialog(mMainWin)`
5. **Results display (critical detail):** Uses `ShowText` dialog, NOT a simple MessageBox:
   ```csharp
   if (results.Count != 0) {
       StringBuilder sb = new StringBuilder();
       sb.Append("Unreadable blocks/sectors: ");
       sb.AppendLine(results.Count.ToString());
       sb.AppendLine();
       foreach (var failure in results) {
           if (failure.IsBlock) {
               sb.Append("Block ");
               sb.Append(failure.BlockOrTrack);
           } else {
               sb.Append("T");
               sb.Append(failure.BlockOrTrack);
               sb.Append(" S");
               sb.Append(failure.Sector);
           }
           if (!failure.IsUnwritable) {
               sb.Append(" (writable)");
           }
           sb.AppendLine();
       }
       ShowText reportDialog = new ShowText();
       reportDialog.Title = "Errors";
       reportDialog.SetText(sb.ToString());
       await reportDialog.ShowDialog(mMainWin);
   } else {
       mMainWin.PostNotification("Scan successful, no errors", true);
   }
   ```
   Note: zero failures → notification bar message, not a dialog.
   Note: `ShowText` constructor takes no owner parameter (Avalonia passes owner via
   `ShowDialog(mMainWin)`).

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Common/PhysicalDriveInfo.cs` |
| **Create** | `cp2_avalonia/SelectPhysicalDrive.axaml` |
| **Create** | `cp2_avalonia/SelectPhysicalDrive.axaml.cs` |
| **Create** | `cp2_avalonia/Actions/ScanBlocksProgress.cs` |
| **Modify** | `cp2_avalonia/MainController.cs` (open drive, scan blocks) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire commands) |

**Command wiring in `MainWindow.axaml.cs`** (not shown elsewhere in this iteration):
```csharp
OpenPhysicalDriveCommand = new RelayCommand(
    async () => { try { await mMainCtrl.OpenPhysicalDrive(); }
                  catch (Exception ex) { Debug.WriteLine("OpenPhysDrive failed: " + ex); } });
    // No CanExecute — always enabled (matches WPF OpenPhysicalDriveCmd)

ScanForBadBlocksCommand = new RelayCommand(
    async () => { try { await mMainCtrl.ScanForBadBlocks(); }
                  catch (Exception ex) { Debug.WriteLine("ScanBlocks failed: " + ex); } },
    () => mMainCtrl.IsFileOpen && mMainCtrl.IsNibbleImageSelected);
```

---

## Platform Testing Notes

- **Linux:** Test with a USB drive inserted. User must have `/dev/sdX` read access
  (typically requires `disk` group or `sudo`). If no removable drives, the list will be
  empty (which is fine).
- **macOS:** Test with external drive. May require Full Disk Access permission in System
  Preferences.
- **Windows:** Test with existing `PhysicalDriveAccess` path — should work as before.

---

## Verification Checklist

- [ ] `dotnet build` succeeds on all platforms
- [ ] File → Open Physical Drive shows drive list (Windows: enumerated, Linux/macOS: enumerated or graceful empty)
- [ ] Drive list shows device label ("Physical disk #N"), type, and size in MB or GB
- [ ] Fixed disks have grayed text and cannot be selected (CanOpen=false)
- [ ] First Removable disk is auto-selected
- [ ] Full device path shown as tooltip on Device column
- [ ] Selecting a drive and clicking Open opens it
- [ ] "Open read-only" checkbox works and setting is persisted
- [ ] Double-click on openable drive→Open without extra click
- [ ] Permission errors show clear messages (Linux: need root/disk group, macOS: Full Disk Access)
- [ ] Boot disk blocked (Windows: PhysicalDrive0, Linux: device containing /)
- [ ] Actions → Scan for Bad Blocks runs on current disk image
- [ ] Scan handles both block mode AND sector mode (track/sector) disk images
- [ ] Scan distinguishes unreadable vs unwritable blocks
- [ ] Scan results shown in ShowText dialog with per-block/sector listing
- [ ] Zero failures → notification bar "Scan successful, no errors" (not a dialog)
- [ ] Scan cancellation via WorkProgress cancel button works
- [ ] "Disk format not recognized" shows error instead of scanning
