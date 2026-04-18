# Iteration 3A Blueprint: Service Interfaces, Implementations & DI Container

> **First:** Read `cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md` for context,
> conventions, and coding rules before proceeding.
>
> **Reference:** `cp2_avalonia/MVVM_Project/MVVM_Notes.md` §4, §7.14, §7.15,
> §7.17, §7.19.

---

## Goal

Create all service interfaces, their concrete implementations, the
`IDialogHost` abstraction, and the DI container setup in `App.axaml.cs`.
After this iteration the services are registered and injectable but not yet
consumed — command bodies still call `mController`. Phase 3B will dissolve
the controller and wire commands through services.

---

## Prerequisites

- Iteration 2 is complete (all 51 commands on `MainViewModel` as
  `ReactiveCommand`, no `RelayCommand` on `MainWindow`).
- The application builds and runs correctly.

---

## Step-by-Step Instructions

### Step 1: Create the `Services/` Directory

```
cp2_avalonia/Services/
```

All interfaces and implementations live here.

> **License header:** Every new `.cs` file must include the Apache 2.0
> license header as specified in Pre-Iteration-Notes. The code snippets
> below omit the header for brevity.

### Step 2: Create `IDialogHost`

```csharp
// cp2_avalonia/Services/IDialogHost.cs
namespace cp2_avalonia.Services;

using Avalonia.Controls;

/// <summary>
/// Provides the owner Window for dialogs and file pickers.
/// Implemented by MainWindow (or any future top-level window).
/// </summary>
public interface IDialogHost {
    Window GetOwnerWindow();
}
```

`MainWindow` implements this:

```csharp
// Add IDialogHost to the existing MainWindow.axaml.cs class declaration.
// Preserve the existing INotifyPropertyChanged interface.
public partial class MainWindow : Window, INotifyPropertyChanged, IDialogHost {
    public Window GetOwnerWindow() => this;
    // ... existing code ...
}
```

### Step 3: Create `IDialogService` / `DialogService`

**Interface:**

```csharp
// cp2_avalonia/Services/IDialogService.cs
namespace cp2_avalonia.Services;

using System.Threading.Tasks;

public enum MBButton { OK, OKCancel, YesNo, YesNoCancel }
public enum MBIcon { None, Info, Warning, Error, Question }
public enum MBResult { OK, Cancel, Yes, No }

public interface IDialogService {
    /// <summary>
    /// Show a modal dialog whose View is resolved from the ViewModel type.
    /// Returns true if the dialog result was accepted, false/null otherwise.
    /// </summary>
    Task<bool?> ShowDialogAsync<TViewModel>(TViewModel viewModel)
        where TViewModel : class;

    /// <summary>
    /// Show a standard message box.
    /// </summary>
    Task<MBResult> ShowMessageAsync(string text, string caption,
        MBButton buttons = MBButton.OK, MBIcon icon = MBIcon.None);

    /// <summary>
    /// Convenience: show a yes/no confirmation dialog.
    /// </summary>
    Task<bool> ShowConfirmAsync(string text, string caption);

    /// <summary>
    /// Show a modeless (non-blocking) window for the given ViewModel.
    /// </summary>
    void ShowModeless<TViewModel>(TViewModel viewModel)
        where TViewModel : class;

    /// <summary>
    /// Register a ViewModel→View mapping. Called once at startup.
    /// </summary>
    void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Window, new();
}
```

**Implementation:**

```csharp
// cp2_avalonia/Services/DialogService.cs
namespace cp2_avalonia.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

public class DialogService : IDialogService {
    private readonly IDialogHost mHost;

    // ViewModel type → factory that creates the View
    private readonly Dictionary<Type, Func<Window>> mMap = new();

    public DialogService(IDialogHost host) {
        mHost = host;
    }

    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Window, new() {
        mMap[typeof(TViewModel)] = () => new TView();
    }

    public async Task<bool?> ShowDialogAsync<TViewModel>(TViewModel viewModel)
        where TViewModel : class {
        if (!mMap.TryGetValue(typeof(TViewModel), out var factory))
            throw new InvalidOperationException(
                $"No view registered for {typeof(TViewModel).Name}");

        Window view = factory();
        view.DataContext = viewModel;
        var result = await view.ShowDialog<bool?>(mHost.GetOwnerWindow());
        return result;
    }

    public async Task<MBResult> ShowMessageAsync(string text, string caption,
        MBButton buttons = MBButton.OK, MBIcon icon = MBIcon.None) {
        MBResult dialogResult = MBResult.OK;

        // Build button panel based on requested buttons.
        var buttonPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12
        };

        Window? msgBox = null;

        void AddButton(string label, MBResult result) {
            var btn = new Button { Content = label, MinWidth = 80 };
            btn.Click += (_, _) => {
                dialogResult = result;
                msgBox!.Close();
            };
            buttonPanel.Children.Add(btn);
        }

        switch (buttons) {
            case MBButton.OK:
                AddButton("OK", MBResult.OK);
                break;
            case MBButton.OKCancel:
                AddButton("OK", MBResult.OK);
                AddButton("Cancel", MBResult.Cancel);
                break;
            case MBButton.YesNo:
                AddButton("Yes", MBResult.Yes);
                AddButton("No", MBResult.No);
                break;
            case MBButton.YesNoCancel:
                AddButton("Yes", MBResult.Yes);
                AddButton("No", MBResult.No);
                AddButton("Cancel", MBResult.Cancel);
                break;
        }

        msgBox = new Window {
            Title = caption,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel {
                Margin = new Thickness(20),
                Spacing = 16,
                Children = {
                    new TextBlock {
                        Text = text,
                        TextWrapping = TextWrapping.Wrap
                    },
                    buttonPanel
                }
            }
        };
        await msgBox.ShowDialog(mHost.GetOwnerWindow());
        return dialogResult;
    }

    public async Task<bool> ShowConfirmAsync(string text, string caption) {
        var result = await ShowMessageAsync(text, caption,
            MBButton.YesNo, MBIcon.Question);
        return result == MBResult.Yes;
    }

    public void ShowModeless<TViewModel>(TViewModel viewModel)
        where TViewModel : class {
        if (!mMap.TryGetValue(typeof(TViewModel), out var factory))
            throw new InvalidOperationException(
                $"No view registered for {typeof(TViewModel).Name}");

        Window view = factory();
        view.DataContext = viewModel;
        view.Show(mHost.GetOwnerWindow());
    }
}
```

### Step 4: Create `IFilePickerService` / `FilePickerService`

**Interface:**

```csharp
// cp2_avalonia/Services/IFilePickerService.cs
namespace cp2_avalonia.Services;

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

public interface IFilePickerService {
    Task<string?> OpenFileAsync(string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDir = null);

    Task<IReadOnlyList<string>> OpenFilesAsync(string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDir = null);

    Task<string?> SaveFileAsync(string title,
        string? suggestedName = null,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDir = null);

    Task<string?> OpenFolderAsync(string title,
        string? initialDir = null);
}
```

**Implementation:**

```csharp
// cp2_avalonia/Services/FilePickerService.cs
namespace cp2_avalonia.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

public class FilePickerService : IFilePickerService {
    private readonly IDialogHost mHost;

    public FilePickerService(IDialogHost host) {
        mHost = host;
    }

    private IStorageProvider GetProvider() =>
        TopLevel.GetTopLevel(mHost.GetOwnerWindow())!.StorageProvider;

    private async Task<IStorageFolder?> ResolveStartDir(string? initialDir) {
        if (initialDir == null)
            return null;
        return await GetProvider().TryGetFolderFromPathAsync(initialDir);
    }

    public async Task<string?> OpenFileAsync(string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDir = null) {
        var results = await GetProvider().OpenFilePickerAsync(
            new FilePickerOpenOptions {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            });
        return results.Count > 0 ? results[0].Path.LocalPath : null;
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDir = null) {
        var results = await GetProvider().OpenFilePickerAsync(
            new FilePickerOpenOptions {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = filters,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            });
        return results.Select(r => r.Path.LocalPath).ToList();
    }

    public async Task<string?> SaveFileAsync(string title,
        string? suggestedName = null,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDir = null) {
        var result = await GetProvider().SaveFilePickerAsync(
            new FilePickerSaveOptions {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = filters,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            });
        return result?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderAsync(string title,
        string? initialDir = null) {
        var results = await GetProvider().OpenFolderPickerAsync(
            new FolderPickerOpenOptions {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            });
        return results.Count > 0 ? results[0].Path.LocalPath : null;
    }
}
```

### Step 5: Create `ISettingsService` / `SettingsService`

**Interface:**

```csharp
// cp2_avalonia/Services/ISettingsService.cs
namespace cp2_avalonia.Services;

using System;

public interface ISettingsService {
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
    int GetInt(string key, int defaultValue);
    void SetInt(string key, int value);
    string GetString(string key, string defaultValue);
    void SetString(string key, string value);
    T GetEnum<T>(string key, T defaultValue) where T : struct, System.Enum;
    void SetEnum<T>(string key, T value) where T : struct, System.Enum;
    IObservable<string> SettingChanged { get; }
    void Load();
    void Save();
}
```

**Implementation:**

```csharp
// cp2_avalonia/Services/SettingsService.cs
namespace cp2_avalonia.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Subjects;
using CommonUtil;
using cp2_avalonia;          // AppSettings
using cp2_avalonia.Common;  // PlatformUtil

/// <summary>
/// Thin wrapper around AppSettings.Global (SettingsHolder).
/// Coexists with direct AppSettings.Global access during migration.
/// </summary>
public class SettingsService : ISettingsService {
    private readonly SettingsHolder mHolder;
    private readonly Subject<string> mSettingChanged = new();

    public IObservable<string> SettingChanged => mSettingChanged;

    public SettingsService() {
        mHolder = AppSettings.Global;
    }

    public bool GetBool(string key, bool defaultValue) =>
        mHolder.GetBool(key, defaultValue);
    public void SetBool(string key, bool value) {
        mHolder.SetBool(key, value);
        mSettingChanged.OnNext(key);
    }

    public int GetInt(string key, int defaultValue) =>
        mHolder.GetInt(key, defaultValue);
    public void SetInt(string key, int value) {
        mHolder.SetInt(key, value);
        mSettingChanged.OnNext(key);
    }

    public string GetString(string key, string defaultValue) =>
        mHolder.GetString(key, defaultValue);
    public void SetString(string key, string value) {
        mHolder.SetString(key, value);
        mSettingChanged.OnNext(key);
    }

    public T GetEnum<T>(string key, T defaultValue)
        where T : struct, System.Enum =>
        mHolder.GetEnum(key, defaultValue);
    public void SetEnum<T>(string key, T value)
        where T : struct, System.Enum {
        mHolder.SetEnum(key, value);
        mSettingChanged.OnNext(key);
    }

    /// <summary>
    /// Load settings from the JSON file on disk and merge into the
    /// current holder. SettingsHolder exposes Serialize()/Deserialize(),
    /// not Load()/Save() — file I/O is the caller's responsibility.
    /// Default-setting (e.g. AUTO_OPEN_DEPTH) remains in the controller
    /// until Phase 3B migrates it here.
    /// </summary>
    public void Load() {
        string settingsPath = Path.Combine(
            PlatformUtil.GetRuntimeDataDir(), AppSettings.SETTINGS_FILE_NAME);
        try {
            string text = File.ReadAllText(settingsPath);
            SettingsHolder? loaded = SettingsHolder.Deserialize(text);
            if (loaded != null) {
                mHolder.MergeSettings(loaded);
            }
            Debug.WriteLine("Settings file loaded and merged");
        } catch (Exception ex) {
            Debug.WriteLine("Unable to read settings file: " + ex.Message);
        }
    }

    /// <summary>
    /// Serialize current settings to the JSON file on disk.
    /// Window-placement saving remains in the controller/view until
    /// Phase 3B (it requires a Window reference).
    /// </summary>
    public void Save() {
        if (!mHolder.IsDirty) {
            Debug.WriteLine("Settings not dirty, not saving");
            return;
        }
        string settingsPath = Path.Combine(
            PlatformUtil.GetRuntimeDataDir(), AppSettings.SETTINGS_FILE_NAME);
        try {
            File.WriteAllText(settingsPath, mHolder.Serialize());
            mHolder.IsDirty = false;
            Debug.WriteLine("Saved settings to '" + settingsPath + "'");
        } catch (Exception ex) {
            Debug.WriteLine("Failed to save settings: " + ex.Message);
        }
    }
}
```

### Step 6: Create `IClipboardService` / `ClipboardService`

**Interface:**

```csharp
// cp2_avalonia/Services/IClipboardService.cs
namespace cp2_avalonia.Services;

using System.Collections.Generic;
using System.Threading.Tasks;
using AppCommon;
using cp2_avalonia;          // ClipInfo

public interface IClipboardService {
    /// <summary>
    /// Place clip data on the clipboard for same-process paste, and
    /// optionally set text/uri-list for foreign-process paste.
    /// </summary>
    Task SetClipAsync(ClipInfo clipInfo, List<ClipFileEntry> cachedEntries,
        string? clipTempDir);

    /// <summary>
    /// Retrieve clip data from the clipboard (same-process only).
    /// Returns null info/cached if no pending content.
    /// </summary>
    Task<(ClipInfo? info, List<ClipFileEntry>? cached)> GetClipAsync();

    /// <summary>
    /// Read raw CP2 JSON text placed by another CP2 process.
    /// Used for cross-process same-app paste.
    /// </summary>
    Task<string?> GetRawClipTextAsync();

    /// <summary>
    /// Read text/uri-list placed by an external file manager
    /// (KDE Dolphin, GNOME Nautilus, etc.).
    /// </summary>
    Task<string?> GetUriListAsync();

    /// <summary>
    /// Clear the clipboard if this process owns it (pending paste).
    /// </summary>
    Task ClearIfPendingAsync();

    /// <summary>
    /// True if this service placed content on the clipboard.
    /// </summary>
    bool HasPendingContent { get; }
}
```

> **Prerequisite:** `ClipInfo` is currently declared `internal class ClipInfo`
> in `ClipInfo.cs`. Because the public `IClipboardService` interface exposes
> `ClipInfo` in method signatures, change it to `public class ClipInfo` to
> avoid CS0051 ("Inconsistent accessibility").

**Implementation:**

```csharp
// cp2_avalonia/Services/ClipboardService.cs
namespace cp2_avalonia.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AppCommon;
using cp2_avalonia;          // ClipInfo
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

public class ClipboardService : IClipboardService {
    private ClipInfo? mPendingClipInfo;
    private List<ClipFileEntry>? mCachedClipEntries;
    private string? mClipTempDir;

    public bool HasPendingContent => mPendingClipInfo != null;

    /// <summary>
    /// Resolve the system clipboard from the currently active top-level window.
    /// Not captured at construction time — safe for multi-window.
    /// </summary>
    private static IClipboard? GetClipboard() {
        if (Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes
                    .IClassicDesktopStyleApplicationLifetime desktop) {
            var window = desktop.MainWindow;
            if (window != null)
                return TopLevel.GetTopLevel(window)?.Clipboard;
        }
        return null;
    }

    public async Task SetClipAsync(ClipInfo clipInfo,
        List<ClipFileEntry> cachedEntries, string? clipTempDir) {
        mPendingClipInfo = clipInfo;
        mCachedClipEntries = cachedEntries;
        mClipTempDir = clipTempDir;

        var clipboard = GetClipboard();
        if (clipboard != null) {
            // Build a DataObject with CP2 JSON text and (optionally)
            // text/uri-list for external file-manager paste.
            var dataObj = new Avalonia.Input.DataObject();
            dataObj.Set(Avalonia.Input.DataFormats.Text,
                clipInfo.ToClipString());

            if (clipTempDir != null) {
                // Build text/uri-list from extracted temp files.
                var uriBuilder = new System.Text.StringBuilder();
                foreach (string file in System.IO.Directory.GetFiles(
                        clipTempDir, "*", System.IO.SearchOption.AllDirectories)) {
                    Uri fileUri = new Uri(file);
                    uriBuilder.Append(fileUri.AbsoluteUri);
                    uriBuilder.Append("\r\n");
                }
                if (uriBuilder.Length > 0) {
                    dataObj.Set("text/uri-list", uriBuilder.ToString());
                }
            }

            await clipboard.SetDataObjectAsync(dataObj);
        }
    }

    public Task<(ClipInfo? info, List<ClipFileEntry>? cached)> GetClipAsync() {
        return Task.FromResult((mPendingClipInfo, mCachedClipEntries));
    }

    public async Task<string?> GetRawClipTextAsync() {
        var clipboard = GetClipboard();
        if (clipboard == null)
            return null;
        return await clipboard.GetTextAsync();
    }

    public async Task<string?> GetUriListAsync() {
        var clipboard = GetClipboard();
        if (clipboard == null)
            return null;

        // 1. Standard X11/freedesktop format.
        string? uriList = await TryGetFormat(clipboard, "text/uri-list");
        if (!string.IsNullOrEmpty(uriList))
            return uriList;

        // 2. GNOME/KDE Dolphin format — strip the "copy\n" or "cut\n" prefix.
        string? gnomeData = await TryGetFormat(clipboard,
            "x-special/gnome-copied-files");
        if (!string.IsNullOrEmpty(gnomeData)) {
            int nl = gnomeData.IndexOf('\n');
            return nl >= 0 ? gnomeData[(nl + 1)..] : gnomeData;
        }

        // 3. Plain-text fallback: bare file:// URIs or absolute paths.
        string? plainText = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(plainText)) {
            string firstLine = plainText.Split('\n')[0].Trim('\r', ' ');
            if (firstLine.StartsWith("file://") ||
                    (System.IO.Path.IsPathRooted(firstLine) &&
                     System.IO.File.Exists(firstLine))) {
                // Convert bare paths to file:// URIs if needed.
                if (!firstLine.StartsWith("file://")) {
                    var sb = new System.Text.StringBuilder();
                    foreach (string line in plainText.Split('\n')) {
                        string path = line.Trim('\r', ' ');
                        if (!string.IsNullOrEmpty(path) &&
                                System.IO.Path.IsPathRooted(path)) {
                            sb.Append(new Uri(path).AbsoluteUri);
                            sb.Append("\r\n");
                        }
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                return plainText;
            }
        }
        return null;
    }

    /// <summary>
    /// Try to read a specific clipboard format, handling both string and
    /// byte[] results.
    /// </summary>
    private static async Task<string?> TryGetFormat(
            Avalonia.Input.Platform.IClipboard clipboard, string format) {
        try {
            object? data = await clipboard.GetDataAsync(format);
            if (data is string s) return s;
            if (data is byte[] b && b.Length > 0)
                return System.Text.Encoding.UTF8.GetString(b);
        } catch { /* format not available */ }
        return null;
    }

    public async Task ClearIfPendingAsync() {
        if (mPendingClipInfo != null) {
            mPendingClipInfo = null;
            mCachedClipEntries = null;
            // Clean up temp directory if present.
            if (mClipTempDir != null && System.IO.Directory.Exists(mClipTempDir)) {
                try {
                    System.IO.Directory.Delete(mClipTempDir, true);
                } catch { /* best effort */ }
                mClipTempDir = null;
            }
            var clipboard = GetClipboard();
            if (clipboard != null) {
                await clipboard.ClearAsync();
            }
        }
    }
}
```

### Step 7a: Promote `AutoOpenDepth` to a Standalone Type

`AutoOpenDepth` is currently a nested enum inside `MainController`.
Promote it to its own file so that `IWorkspaceService` can reference it
without depending on the controller.

```csharp
// cp2_avalonia/Models/AutoOpenDepth.cs
namespace cp2_avalonia.Models;

/// <summary>
/// How deeply to auto-open nested archives/disk images when opening
/// a work file.
/// </summary>
public enum AutoOpenDepth {
    Unknown = 0,
    Shallow,
    SubVol,
    Max
}
```

Update `MainController.cs` to reference the canonical type:

```csharp
using cp2_avalonia.Models;
// Remove the nested enum definition; existing code continues to compile
// because the using brings AutoOpenDepth into scope.
```

Update `EditAppSettings.axaml.cs` — it qualifies every reference as
`MainController.AutoOpenDepth`. Add `using cp2_avalonia.Models;` at the
top of the file and replace all 8 occurrences of
`MainController.AutoOpenDepth` with the unqualified `AutoOpenDepth`
(lines 99, 106, 108–109, 112–113, 116–117).

### Step 7b: Create `IWorkspaceService` (Interface Only)

The full implementation moves here in Phase 3B when the controller is
dissolved. For now, define only the interface.

```csharp
// cp2_avalonia/Services/IWorkspaceService.cs
namespace cp2_avalonia.Services;

using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using AppCommon;
using CommonUtil;
using cp2_avalonia;
using cp2_avalonia.Models;

public interface IWorkspaceService {
    /// <summary>Current WorkTree (null when no file is open).</summary>
    WorkTree? WorkTree { get; }

    bool IsFileOpen { get; }

    /// <summary>Full path of the currently open work file.</summary>
    string WorkPathName { get; }

    DebugMessageLog DebugLog { get; }

    Formatter Formatter { get; }
    AppHook AppHook { get; }

    /// <summary>Recent file paths, most-recent first.</summary>
    ObservableCollection<string> RecentFilePaths { get; }

    Task<WorkTree> OpenAsync(string path, bool readOnly, AutoOpenDepth depth);
    bool Close();

    /// <summary>Fires after any operation that modifies the workspace
    /// (add, delete, move, set-attr, etc.).</summary>
    IObservable<Unit> WorkspaceModified { get; }
}
```

### Step 8: Create `IViewerService` (Interface Only)

Full implementation in Phase 4A/4B.

```csharp
// cp2_avalonia/Services/IViewerService.cs
namespace cp2_avalonia.Services;

using System.Collections.Generic;
using cp2_avalonia.ViewModels;

public interface IViewerService {
    IReadOnlyList<FileViewerViewModel> ActiveViewers { get; }
    void Register(FileViewerViewModel viewer);
    void Unregister(FileViewerViewModel viewer);

    /// <summary>
    /// Close all viewers associated with the given work path (called on
    /// file close).
    /// </summary>
    void CloseViewersForSource(string workPathName);
}
```

> **Note:** `FileViewerViewModel` does not exist yet. Create a minimal stub
> class in `ViewModels/FileViewerViewModel.cs`:
>
> ```csharp
> // cp2_avalonia/ViewModels/FileViewerViewModel.cs
> namespace cp2_avalonia.ViewModels;
>
> using System;
> using ReactiveUI;
>
> /// <summary>
> /// Stub — Phase 4A will flesh this out with full file viewer logic.
> /// Implements IDisposable because it holds file data/handles and must
> /// self-deregister from IViewerService (see Pre-Iteration-Notes §4).
> /// </summary>
> public class FileViewerViewModel : ReactiveObject, IDisposable {
>     // Phase 4A: Dispose() must call _viewerService.Unregister(this).
>     public void Dispose() { }
> }
> ```

### Step 9: Configure the DI Container in `App.axaml.cs`

Merge the following additions into the existing `App.axaml.cs`. Do **not**
replace the file — the existing `App.axaml` requires `public partial class App`.

```csharp
// Add these usings to App.axaml.cs:
using Microsoft.Extensions.DependencyInjection;
using cp2_avalonia.Services;

// Existing class — merge additions into it:
public partial class App : Application {
    /// <summary>Application-wide service provider.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            // Build the DI container FIRST — before MainWindow() is
            // constructed, because its constructor resolves services
            // from App.Services.
            var sc = new ServiceCollection();

            // Singletons (container-managed)
            sc.AddSingleton<ISettingsService, SettingsService>();
            sc.AddSingleton<IClipboardService, ClipboardService>();

            // IDialogHost is NOT registered — MainWindow passes itself
            // directly as IDialogHost to MainViewModel's constructor.
            // IDialogService and IFilePickerService are NOT registered
            // in the container. MainViewModel constructs its own instances
            // passing IDialogHost (see Step 10 and Pre-Iteration-Notes §4).

            Services = sc.BuildServiceProvider();

            // Now construct MainWindow — App.Services is valid.
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ... existing code (ApplyTheme, native menu handlers) unchanged ...
}
```

### Step 10: Inject Services into MainViewModel

`IDialogService` and `IFilePickerService` are **manually constructed** by
`MainViewModel` — they are not resolved from the container (see
Pre-Iteration-Notes §4 DI table). `MainViewModel` receives `IDialogHost`
and creates its own instances, which keeps each window's dialog/picker
bound to the correct owner window.

Add to `MainViewModel.cs`:

```csharp
using cp2_avalonia.Services;
```

Add constructor parameters for services:

```csharp
public class MainViewModel : ReactiveObject {
    // Manually constructed (not DI-injected) — use mCamelCase per convention.
    private readonly IDialogService mDialogService;
    private readonly IFilePickerService mFilePickerService;

    // Container-managed singletons — use _camelCase per convention.
    private readonly ISettingsService _settingsService;
    private readonly IClipboardService _clipboardService;

    public MainViewModel(
        IDialogHost dialogHost,
        ISettingsService settingsService,
        IClipboardService clipboardService) {
        // Manually construct host-dependent services (not container-managed).
        mDialogService = new DialogService(dialogHost);
        mFilePickerService = new FilePickerService(dialogHost);

        // Container-managed singletons.
        _settingsService = settingsService;
        _clipboardService = clipboardService;

        // Register ViewModel→View mappings on our DialogService instance.
        RegisterDialogMappings();

        // ... existing command initialization ...
    }

    /// <summary>
    /// One-time registration of all ViewModel→View pairs used by
    /// IDialogService.ShowDialogAsync. Add entries as dialog ViewModels
    /// are created in Phases 4A/4B.
    /// </summary>
    private void RegisterDialogMappings() {
        // Dialog registrations will be added in Phase 4A/4B, e.g.:
        // mDialogService.Register<EditSectorViewModel, EditSector>();
        // mDialogService.Register<EditAppSettingsViewModel, EditAppSettings>();
    }
}
```

Update `MainWindow` to construct the ViewModel. Add the following
usings to `MainWindow.axaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using cp2_avalonia.Services;
```

```csharp
public MainWindow() {
    InitializeComponent();

    var vm = new MainViewModel(
        this,   // IDialogHost
        App.Services.GetRequiredService<ISettingsService>(),
        App.Services.GetRequiredService<IClipboardService>());

    DataContext = vm;

    // Temporary: wire controller to VM (removed in Phase 3B)
    mMainCtrl = new MainController(this);
    vm.SetController(mMainCtrl);
}
```

### Step 11: Build and Validate

1. Run `dotnet build` — verify zero errors.
2. Launch the application — verify it starts normally.
3. Services are registered but not yet used by commands. Existing command
   logic still delegates to `mController`. No functional changes expected.
4. Optionally, confirm DI resolution works by adding a temporary debug line:

```csharp
var ss = App.Services.GetRequiredService<ISettingsService>();
Debug.Assert(ss != null, "SettingsService not resolved");
```

---

## Inventory of Files Created

| File | Purpose |
|---|---|
| `Services/IDialogHost.cs` | Owner-window abstraction |
| `Services/IDialogService.cs` | Modal/modeless dialog service |
| `Services/DialogService.cs` | Dialog service with ViewModel→View map |
| `Services/IFilePickerService.cs` | File open/save/folder picker |
| `Services/FilePickerService.cs` | Wraps Avalonia StorageProvider |
| `Services/ISettingsService.cs` | Settings read/write |
| `Services/SettingsService.cs` | Wraps AppSettings.Global |
| `Services/IClipboardService.cs` | Clipboard abstraction |
| `Services/ClipboardService.cs` | Same-process + cross-process clipboard |
| `Models/AutoOpenDepth.cs` | Promoted enum (was nested in MainController) |
| `Services/IWorkspaceService.cs` | Interface only (impl in Phase 3B) |
| `Services/IViewerService.cs` | Interface only (impl in Phase 4) |
| `ViewModels/FileViewerViewModel.cs` | Stub class (fleshed out in Phase 4A) |

---

## What This Enables

- Phase 3B can now inject services into command bodies, replacing all
  direct `mMainWin.StorageProvider`, `new DialogName(mMainWin, ...)`,
  and `AppSettings.Global` calls.
- The `mController` coupling is the only remaining link; Phase 3B dissolves
  it by inlining controller logic into the ViewModel + services.
