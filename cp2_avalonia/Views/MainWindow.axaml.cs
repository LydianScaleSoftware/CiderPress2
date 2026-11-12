/*
 * Copyright 2023 faddenSoft
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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

using cp2_avalonia.Common;
using cp2_avalonia.Models;
using cp2_avalonia.Services;
using cp2_avalonia.Tools;
using cp2_avalonia.ViewModels;
using DiskArc;

namespace cp2_avalonia.Views;

public partial class MainWindow : Window, IDialogHost, IViewActions
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public Window GetOwnerWindow() => this;

    // Reference to the native Recent Files submenu item (macOS only, lazy-initialized).
    private NativeMenuItem? mNativeRecentFilesItem;
    private AsciiChartWindow? mAsciiChartWindow;

    // ---- Drag-drop state for file list (internal drag-move) ----
    private const double DRAG_THRESHOLD = 4.0;
    private bool mIsDraggingFileList;
    private readonly List<IFileEntry> mDragMoveList = new List<IFileEntry>();
    private Point mDragStartPosn = new Point(-1, -1);
    private PointerPressedEventArgs? mDragPressArgs;
    private bool mSuppressSort;   // set when pointer pressed on a column resize grip
    // Set when the user presses an already-selected row within a multi-selection (no
    // modifier keys).  We suppress the DataGrid's immediate collapse-to-one-row so a
    // drag can carry the whole selection; if the gesture turns out to be a plain click
    // (released without dragging) we collapse to this item on pointer release.
    private FileListItem? mPendingSingleSelect;

    // Linux XDND receive handler (null on non-Linux).
    private Platform.LinuxDrag? mLinuxDrag;

    private readonly ISettingsService _settings = null!;
    private readonly IClipboardService _clipboard = null!;
    private readonly IDialogServiceFactory _dialogs = null!;
    private readonly IFilePickerServiceFactory _filePicker = null!;
    private readonly IWorkspaceService _workspaceService = null!;

    private const string ELLIPSIS = "…";

    private void ShowHideOptionsButton_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel!.Options.ShowOptionsPanel = !ViewModel.Options.ShowOptionsPanel;
    }

    private void ShowAsciiChartWindow()
    {
        if (mAsciiChartWindow == null)
        {
            var window = new AsciiChartWindow();
            RestoreAsciiChartWindowSize(window);
            window.Closed += (_, _) =>
            {
                SaveAsciiChartWindowSize(window);
                mAsciiChartWindow = null;
            };
            mAsciiChartWindow = window;
        }

        if (!mAsciiChartWindow.IsVisible)
        {
            mAsciiChartWindow.Show(this);
            return;
        }

        if (mAsciiChartWindow.WindowState == WindowState.Minimized)
        {
            mAsciiChartWindow.WindowState = WindowState.Normal;
        }
        mAsciiChartWindow.Activate();
        mAsciiChartWindow.Focus();
    }

    // IViewActions: opens (or re-focuses) the ASCII Chart window. Bound to
    // ShowAsciiChartCommand so the Tools menu item and its Ctrl+Alt+A / Meta+Alt+A
    // accelerators all route here.
    public void ShowAsciiChart() => ShowAsciiChartWindow();

    private void RestoreAsciiChartWindowSize(AsciiChartWindow window)
    {
        int defaultWidth = (int)Math.Round(window.Width);
        int defaultHeight = (int)Math.Round(window.Height);
        int savedWidth = _settings.GetInt(AppSettings.ASCII_CHART_WINDOW_WIDTH, defaultWidth);
        int savedHeight = _settings.GetInt(AppSettings.ASCII_CHART_WINDOW_HEIGHT, defaultHeight);

        window.Width = Math.Max(window.MinWidth, savedWidth);
        window.Height = Math.Max(window.MinHeight, savedHeight);
    }

    private void SaveAsciiChartWindowSize(AsciiChartWindow window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            return;
        }

        _settings.SetInt(AppSettings.ASCII_CHART_WINDOW_WIDTH, (int)Math.Round(window.Width));
        _settings.SetInt(AppSettings.ASCII_CHART_WINDOW_HEIGHT, (int)Math.Round(window.Height));
        _settings.Save();
    }

    // ---- Triptych panel column widths ----
    // Col 0 is set to a fixed pixel value so only the center column stretches on resize.
    // Reading Width.Value (not ActualWidth) is reliable even before the panel is rendered.
    public double LeftPanelWidth
    {
        get => mainTriptychPanel.ColumnDefinitions[0].Width.Value;
        set => mainTriptychPanel.ColumnDefinitions[0].Width = new GridLength(value);
    }


    // Recent-file menu paths longer than this (in characters) get elided from the root
    // (the front of the string) rather than being left for the menu to truncate itself.
    private const int RECENT_FILE_MAX_PATH_CHARS = 40;

    /// <summary>
    /// Populates the File &gt; Recent Files submenu with the current list of recent paths.
    /// </summary>
    public void PopulateRecentFilesMenu(List<string> recentPaths)
    {
        var vm = DataContext as MainViewModel;
        ICommand[] commands = {
            vm!.RecentFile1Command, vm.RecentFile2Command, vm.RecentFile3Command,
            vm.RecentFile4Command, vm.RecentFile5Command, vm.RecentFile6Command
        };

        if (recentFilesMenu != null)
        {
            recentFilesMenu.Items.Clear();
            if (recentPaths.Count == 0)
            {
                var placeholder = new MenuItem { Header = "(none)" };
                recentFilesMenu.Items.Add(placeholder);
            }
            else
            {
                for (int i = 0; i < recentPaths.Count && i < commands.Length; i++)
                {
                    var mi = new MenuItem
                    {
                        Header = $"_{i + 1}: {ElidePathFromRoot(recentPaths[i], RECENT_FILE_MAX_PATH_CHARS)}",
                        Command = commands[i],
                        InputGesture = new KeyGesture(Key.D1 + i,
                            KeyModifiers.Control | KeyModifiers.Shift),
                    };
                    ToolTip.SetTip(mi, new TextBlock {
                        Text = recentPaths[i],
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 600
                    });
                    recentFilesMenu.Items.Add(mi);
                }
            }
        }

        // Update the native menu Recent Files submenu (macOS). The item is
        // lazy-fetched: it lives in Window.NativeMenu > File > Recent Files.
        if (OperatingSystem.IsMacOS())
        {
            if (mNativeRecentFilesItem == null)
            {
                var nm = NativeMenu.GetMenu(this);
                var fileItem = nm?.Items.OfType<NativeMenuItem>().FirstOrDefault();
                mNativeRecentFilesItem = fileItem?.Menu?.Items
                    .OfType<NativeMenuItem>()
                    .FirstOrDefault(i => i.Header == "Recent Files");
            }
            if (mNativeRecentFilesItem?.Menu != null)
            {
                mNativeRecentFilesItem.Menu.Items.Clear();
                if (recentPaths.Count == 0)
                {
                    mNativeRecentFilesItem.Menu.Add(
                        new NativeMenuItem("(none)") { IsEnabled = false });
                }
                else
                {
                    for (int i = 0; i < recentPaths.Count && i < commands.Length; i++)
                    {
                        mNativeRecentFilesItem.Menu.Add(
                            new NativeMenuItem(
                                $"{i + 1}: {ElidePathFromRoot(recentPaths[i], RECENT_FILE_MAX_PATH_CHARS)}")
                            {
                                Command = commands[i],
                                Gesture = new KeyGesture(Key.D1 + i,
                                    KeyModifiers.Meta | KeyModifiers.Shift),
                                ToolTip = recentPaths[i],
                            });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Elides a path down to at most <paramref name="maxChars"/> characters, preferring to
    /// drop whole path segments from the root (the front) so the tail/filename stays intact.
    /// Falls back to a plain front-truncation if even the bare filename doesn't fit.
    /// </summary>
    private static string ElidePathFromRoot(string path, int maxChars)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxChars)
        {
            return path;
        }
        char sep = path.Contains('\\') ? '\\' : '/';
        string[] segments = path.Split(sep);

        string best = ElideCharsFromStart(path, maxChars);
        for (int keep = 1; keep < segments.Length; keep++)
        {
            string candidate = ELLIPSIS + sep + string.Join(sep, segments[^keep..]);
            if (candidate.Length > maxChars)
            {
                break;
            }
            best = candidate;
        }
        return best;
    }

    /// <summary>
    /// Returns <paramref name="text"/> unchanged if it's within <paramref name="maxChars"/>;
    /// otherwise truncates from the front and prefixes with an ellipsis.
    /// </summary>
    private static string ElideCharsFromStart(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }
        int keepChars = Math.Max(0, maxChars - ELLIPSIS.Length);
        return ELLIPSIS + text.Substring(text.Length - keepChars);
    }

    // ---- File list ----
    // public ObservableCollection<FileListItem> FileList { get; } = new();
    // ---- Tree selection helpers (read-only) ----
    public ArchiveTreeItem? SelectedArchiveTreeItem =>
        archiveTree?.SelectedItem as ArchiveTreeItem;



    // The active file list sort lives in FileListViewModel (it sorts as it populates).

    private void SetColumnVisible(string header, bool visible)
    {
        if (fileListDataGrid == null)
        {
            return;
        }
        foreach (DataGridColumn col in fileListDataGrid.Columns)
        {
            if (col.Header?.ToString() == header)
            {
                col.IsVisible = visible;
                return;
            }
        }
    }

    private void ApplyFileListColumnVisibility()
    {
        bool showFileName = ViewModel!.ShowSingleDirFileList;
        SetColumnVisible("Filename", showFileName);
        SetColumnVisible("Pathname", !showFileName);
        SetColumnVisible("Raw Len", ViewModel.FileList.ShowCol_RawLen);
        SetColumnVisible("Data Fmt", ViewModel.FileList.ShowCol_Format);
        SetColumnVisible("Rsrc Len", ViewModel.FileList.ShowCol_RsrcLen);
        SetColumnVisible("Rsrc Fmt", ViewModel.FileList.ShowCol_Format);
        SetColumnVisible("Total Size", ViewModel.FileList.ShowCol_TotalSize);
    }



    // ---- Scroll / focus helpers ----
    public void FileList_ScrollToTop()
    {
        if (ViewModel!.FileList.Items.Count > 0)
        {
            fileListDataGrid.ScrollIntoView(ViewModel!.FileList.Items[0], null);
        }
    }

    public void DirectoryTree_ScrollToTop()
    {
        // TODO: Avalonia TreeView does not expose a direct ScrollToTop() method.
    }

    public MainWindow()
    { // for XAML loader
        InitializeComponent();
    }

    public MainWindow(
        ISettingsService settingsService,
        IClipboardService clipboardService,
        IDialogServiceFactory dialogServiceFactory,
        IFilePickerServiceFactory filePickerServiceFactory,
        IViewerService viewerService,
        IWorkspaceService workspaceService
    ) : this()
    {
        _settings = settingsService;
        _clipboard = clipboardService;
        _dialogs = dialogServiceFactory;
        _filePicker = filePickerServiceFactory;
        _workspaceService = workspaceService;

        // Note: InitializeComponent() was already invoked via `: this()` above.
        // Calling it again would re-run XamlIlPopulate and create a second
        // NativeMenu instance, which causes Avalonia's native menu exporter to
        // throw "The menu being updated does not match" on macOS.

        // Load settings before creating the ViewModel so child VMs (e.g. OptionsPanelViewModel)
        // read persisted values from their constructors. Settings are loaded once in
        // App.Initialize(); this call is now a no-op but kept for clarity.
        _settings.Load();

        var viewModel = new MainViewModel(
            this, // IDialogHost
            this, // IViewActions
            _dialogs,
            _filePicker,
            _settings,
            _clipboard,
            viewerService,
            _workspaceService
        );

        DataContext = viewModel;

        // Wire column-visibility and find-panel focus via PropertyChanged.
        viewModel.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(MainViewModel.ShowSingleDirFileList)) {
                SetColumnVisible("Filename", viewModel.ShowSingleDirFileList);
                SetColumnVisible("Pathname", !viewModel.ShowSingleDirFileList);
            }
        };
        viewModel.FileList.PropertyChanged += (s, e) => {
            switch (e.PropertyName) {
                case nameof(FileListViewModel.ShowCol_Format):
                    SetColumnVisible("Data Fmt", viewModel.FileList.ShowCol_Format);
                    SetColumnVisible("Rsrc Fmt", viewModel.FileList.ShowCol_Format);
                    break;
                case nameof(FileListViewModel.ShowCol_RawLen):
                    SetColumnVisible("Raw Len", viewModel.FileList.ShowCol_RawLen);
                    break;
                case nameof(FileListViewModel.ShowCol_RsrcLen):
                    SetColumnVisible("Rsrc Len", viewModel.FileList.ShowCol_RsrcLen);
                    break;
                case nameof(FileListViewModel.ShowCol_TotalSize):
                    SetColumnVisible("Total Size", viewModel.FileList.ShowCol_TotalSize);
                    break;
            }
        };
        viewModel.FindFileVM.PropertyChanged += (s, e) => {
            // Focus the find text box whenever the find panel becomes visible.
            // The Focus() call is deferred to Input priority so it fires after
            // Avalonia has processed the IsVisible change and laid out the panel.
            if (e.PropertyName == nameof(FindFileViewModel.IsFindPanelVisible)
                    && viewModel.FindFileVM.IsFindPanelVisible) {
                Dispatcher.UIThread.Post(() => {
                    findFileTextBox.SelectAll();
                    findFileTextBox.Focus();
                }, DispatcherPriority.Input);
            }
        };
        ApplyFileListColumnVisibility();

        // Open Physical Drive is hidden on all platforms for now, pending a decision on how
        // to handle physical drive access in general.
        openPhysicalDriveMenuItem.IsVisible = false;

        // On macOS use the native menu bar; hide the in-window menu.
        if (OperatingSystem.IsMacOS())
        {
            mainMenu.IsVisible = false;
        }

        // Restore window placement before the window is shown to avoid a visible jump.
        var earlyPlacement = _settings.GetString(AppSettings.MAIN_WINDOW_PLACEMENT, "");
        if (!string.IsNullOrEmpty(earlyPlacement)) {
            WindowPlacement.Restore(this, earlyPlacement);
        }
        LeftPanelWidth = _settings.GetInt(AppSettings.MAIN_LEFT_PANEL_WIDTH, 300);

        WindowPlacement.TrackNormalBounds(this);
        Loaded += (s, e) =>
        {
            if (DataContext is MainViewModel vm) {
                vm.Initialize();
            }
            ViewModel!.InitImportExportConfig();
            // Register drag-drop on launch panel after the AXAML tree is built.
            launchPanel.AddHandler(DragDrop.DropEvent, LaunchPanel_Drop);
            launchPanel.AddHandler(DragDrop.DragOverEvent, LaunchPanel_DragOver);
            // File list drag-drop (drop from OS file manager + internal move).
            fileListDataGrid.AddHandler(DragDrop.DropEvent, FileListDataGrid_Drop);
            fileListDataGrid.AddHandler(DragDrop.DragOverEvent, FileListDataGrid_DragOver);
            // Use handledEventsToo so that PointerPressed fires even when the column header
            // has already handled the press (it starts its own resize or sort).  This is
            // required for double-click auto-size on the column dividers to work.
            fileListDataGrid.AddHandler(PointerPressedEvent, FileListDataGrid_PointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            fileListDataGrid.AddHandler(PointerMovedEvent, FileListDataGrid_PointerMoved,
                RoutingStrategies.Tunnel);
            // Collapses a preserved multi-selection to a single row when the user
            // clicks (without dragging) on an already-selected row.  See
            // mPendingSingleSelect and FileListDataGrid_PointerPressed.
            fileListDataGrid.AddHandler(PointerReleasedEvent, FileListDataGrid_PointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            // Info-panel DataGrids: same handledEventsToo registration for column divider
            // double-click auto-size and sort suppression.
            partitionLayoutDataGrid.AddHandler(PointerPressedEvent,
                InfoDataGrid_PointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            metadataDataGrid.AddHandler(PointerPressedEvent,
                InfoDataGrid_PointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            // DataGrid manages its PART_HorizontalScrollbar/PART_VerticalScrollbar's
            // AllowAutoHide itself in code (it wins over any Style setter, however high
            // priority, since that's a LocalValue write happening after the template
            // applies). Reach in and override it right after the template is applied so
            // ours is the one left standing; see App.axaml for why this matters.
            fileListDataGrid.TemplateApplied += (_, args) => {
                ScrollBar? hScroll = args.NameScope.Find<ScrollBar>("PART_HorizontalScrollbar");
                if (hScroll != null) {
                    hScroll.AllowAutoHide = false;
                }
                if (args.NameScope.Find<ScrollBar>("PART_VerticalScrollbar") is { } vScroll) {
                    vScroll.AllowAutoHide = false;
                }

                // The DataGrid template lets the rows presenter span the full height,
                // including the row occupied by the (now always-visible) horizontal
                // scrollbar, so the bottom data row renders underneath it. Reserve space
                // by insetting the rows presenter's bottom edge by the scrollbar's height
                // whenever it is visible. Simpler and more version-robust than retemplating.
                Control? rowsPresenter = args.NameScope.Find<Control>("PART_RowsPresenter");
                if (rowsPresenter != null && hScroll != null) {
                    void SyncBottomInset() {
                        double inset = hScroll.IsVisible ? hScroll.Bounds.Height : 0;
                        var m = rowsPresenter.Margin;
                        if (Math.Abs(m.Bottom - inset) > 0.5) {
                            rowsPresenter.Margin = new Thickness(m.Left, m.Top, m.Right, inset);
                        }
                    }
                    // Re-sync whenever the scrollbar shows/hides or its measured height
                    // changes (theme/DPI). Setting the rows-presenter margin cannot change
                    // the horizontal scrollbar's own visibility or bounds, so no feedback loop.
                    hScroll.PropertyChanged += (_, pe) => {
                        if (pe.Property == Visual.IsVisibleProperty ||
                                pe.Property == Visual.BoundsProperty) {
                            SyncBottomInset();
                        }
                    };
                    SyncBottomInset();
                }
            };
            SetupNameColumnAutoSize();
            // Directory tree drag-drop (internal move + drop from OS file manager).
            directoryTree.AddHandler(DragDrop.DropEvent, DirectoryTree_Drop);
            directoryTree.AddHandler(DragDrop.DragOverEvent, DirectoryTree_DragOver);
            // Swallow Enter in the directory tree: it should neither expand/collapse nodes
            // (TreeView default) nor fire the window's Enter -> ViewFiles binding (which would
            // shift focus to the file list).  Tunnel so we mark it Handled before either.
            directoryTree.AddHandler(KeyDownEvent, DirectoryTree_PreviewKeyDown,
                RoutingStrategies.Tunnel);
            // Right-click selects the node under the cursor before the context menu opens,
            // so tree commands act on the clicked node rather than the prior selection.
            // Tunnel so selection happens before the context menu / TreeView handling.
            archiveTree.AddHandler(PointerPressedEvent, Tree_SelectOnRightClick,
                RoutingStrategies.Tunnel);
            directoryTree.AddHandler(PointerPressedEvent, Tree_SelectOnRightClick,
                RoutingStrategies.Tunnel);
            // Alt+Up (Go To Parent).  Handled as a *tunnel* handler so it runs before the
            // focused TreeView/DataGrid, which would otherwise eat the Up arrow (ignoring Alt) and
            // just move their own selection -- masquerading as "go to parent".  NavToParent's
            // selection+focus change also keeps the menu bar out of access-key mode when it
            // actually navigates.  (Edge case: at the top level there's no parent, so no focus
            // change, and Alt+Up there still enters menu mode -- an accepted Avalonia limitation.)
            AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);
            // Dismiss the access-key menu mode that the Alt+Up sequence triggers on Alt release.
            // handledEventsToo so it runs even after AccessKeyHandler processes the Alt key-up.
            AddHandler(KeyUpEvent, Window_AltKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
            // Track focus entering and leaving text boxes (see IsTextInputFocused).
            AddHandler(GotFocusEvent, Window_FocusChanged, RoutingStrategies.Bubble,
                handledEventsToo: true);
            AddHandler(LostFocusEvent, Window_FocusChanged, RoutingStrategies.Bubble,
                handledEventsToo: true);
            // File list panel (parent Grid) catches drops on empty space below rows.
            fileListPanel.AddHandler(DragDrop.DropEvent, FileListPanel_Drop);
            fileListPanel.AddHandler(DragDrop.DragOverEvent, FileListPanel_DragOver);
            fileListPanel.AddHandler(PointerPressedEvent, FileListPanel_PointerPressed,
                RoutingStrategies.Bubble);
            // The panel's context menu is for the empty area below the file rows; suppress it
            // when the center pane is showing disk/partition info rather than the file list.
            fileListPanelContextMenu.Opening += (_, e) =>
            {
                if (ViewModel?.ShowCenterFileList != true)
                {
                    e.Cancel = true;
                }
            };
            if (OperatingSystem.IsLinux())
            {
                InstallLinuxDrag();
            }
            OpenStartupFile();
        };
        Closing += (s, e) =>
        {
            // Write placement before Shutdown() calls _settingsService.Save().
            _settings.SetString(AppSettings.MAIN_WINDOW_PLACEMENT, WindowPlacement.Save(this));
            _settings.SetInt(AppSettings.MAIN_LEFT_PANEL_WIDTH, (int)LeftPanelWidth);
            if (DataContext is MainViewModel vm) {
                vm.Shutdown();
            }
            if (OperatingSystem.IsLinux()) UninstallLinuxDrag();
        };
    }

    /// <summary>
    /// Opens the file passed on the command line, if any.  Deferred to Background priority so
    /// the window is fully shown and laid out before the load's progress dialog appears.
    /// </summary>
    private void OpenStartupFile()
    {
        Common.CommandLineOptions? opts = Program.StartupOptions;
        if (opts?.FilePath == null)
        {
            return;
        }
        OpenFileDeferred(opts.FilePath, opts.ReadOnly);
    }

    /// <summary>
    /// Opens a file handed to an already-running (or just-launched) instance via the OS's
    /// file-activation mechanism.  Currently only reachable on macOS, via Finder's "Open
    /// With"/double-click or a drag onto the Dock/Finder icon; see
    /// <see cref="App.OnMacFileActivated"/>.
    /// </summary>
    public void OpenFileFromActivation(string path)
    {
        // The open itself runs later (Background priority); the window is raised below so a
        // refusal toast (a dialog or operation is in progress) is visible.
        OpenFileDeferred(path, readOnly: false);
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        // Raise the topmost window, not necessarily this one: activating the main window
        // while a modal dialog is open puts it above the dialog.
        GetTopmostWindow().Activate();
    }

    /// <summary>
    /// Returns the window at the top of the modal stack: this window, or the innermost
    /// dialog owned (directly or indirectly) by it.  Modal dialogs are owned by the one
    /// below them, so the last owned window is the one on top.
    /// </summary>
    private Window GetTopmostWindow()
    {
        Window top = this;
        while (top.OwnedWindows.Count > 0)
        {
            top = top.OwnedWindows[top.OwnedWindows.Count - 1];
        }
        return top;
    }

    private void OpenFileDeferred(string path, bool readOnly)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }
        Dispatcher.UIThread.Post(
            // Both callers are OS-delivered requests, which can arrive twice for one launch
            // (command line plus macOS activation), and which must not disturb a dialog.
            () => _ = vm.OpenWorkFileAsync(path, readOnly, fromOs: true),
            DispatcherPriority.Background);
    }

    // ---- Linux XDND receive (external drag from desktop/file manager) ----

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void UninstallLinuxDrag()
    {
        if (mLinuxDrag != null)
            Platform.LinuxDrag.UnregisterMainInstance(mLinuxDrag);
        mLinuxDrag?.Dispose();
        mLinuxDrag = null;
    }

    /// <summary>
    /// Installs the XDND receive proxy after the window is fully realised.
    /// Must only be called on Linux.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void InstallLinuxDrag()
    {
        nint xid = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (xid == IntPtr.Zero)
        {
            Debug.WriteLine("InstallLinuxDrag: could not obtain window XID");
            return;
        }
        mLinuxDrag = Platform.LinuxDrag.Install(xid,
            onImportDrop:    (paths, rx, ry) => OnLinuxFileDrop(paths, rx, ry),
            onImportCp2Drop: (json,  rx, ry) => OnLinuxCp2Drop(json,  rx, ry));
        if (mLinuxDrag != null)
            Platform.LinuxDrag.RegisterMainInstance(mLinuxDrag);
    }

    /// <summary>
    /// Called on the UI thread when a cross-instance CP2 drag delivers rich JSON
    /// (XDND type = <c>com.faddensoft.ciderpress2.clip</c>).  Routes through the
    /// same paste path used by Edit→Paste so fork/attribute data is preserved.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void OnLinuxCp2Drop(string json, int screenX, int screenY)
    {
        if (!Models.ClipInfo.IsClipTextFromCP2(json)) return;
        MainViewModel? vm = ViewModel;
        if (vm == null || !vm.IsFileOpen) return;

        IFileEntry rawHit = HitTestScreenDropTarget(screenX, screenY, out bool inDirTree);
        IFileEntry target = inDirTree ? rawHit : ResolveExternalFileListDropTarget(rawHit);

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(Models.ClipInfo.CrossInstanceDataFormat, json));
        _ = vm.PasteOrDrop(data, target);
    }

    /// <summary>
    /// Called on the Avalonia UI thread when files are dropped from an external
    /// X11/Wayland source (via the XDND protocol).  <paramref name="screenX"/> and
    /// <paramref name="screenY"/> are the X11 root coordinates of the drop point, used
    /// to determine which directory in the UI the user was targeting.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void OnLinuxFileDrop(string[] paths, int screenX, int screenY)
    {
        if (paths.Length == 0) return;
        MainViewModel? vm = ViewModel;
        if (vm == null) return;

        if (!vm.IsFileOpen)
        {
            _ = vm.DropOpenWorkFile(paths[0]);
            return;
        }

        IFileEntry rawHit = HitTestScreenDropTarget(screenX, screenY, out bool inDirTree);
        IFileEntry target = inDirTree
            ? rawHit                                    // tree item is always a directory
            : ResolveExternalFileListDropTarget(rawHit);// handles file→parent-dir, NO_ENTRY→mode default
        _ = vm.AddFileDrop(target, paths);
    }

    // ---- Drag-drop on launch panel ----

    private void LaunchPanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files?.Count == 1)
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
            // Files format available but count is not 1 — reject.
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            // On Linux, DataFormats.Files may not be populated during DragOver
            // (same issue noted in FileListDataGrid_DragOver).  Accept tentatively;
            // the Drop handler validates the actual data.
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Select all items in the file list DataGrid.</summary>
    internal void SelectAllFileListItems() => fileListDataGrid.SelectAll();

    /// <summary>Clear sort tags and the sort indicator on all file list DataGrid columns.</summary>
    internal void ClearColumnSortTags()
    {
        foreach (DataGridColumn col in fileListDataGrid.Columns)
        {
            col.Tag = null;
        }
        SetFileListSortIndicator(null, true);
    }

    // Style classes that show the sort arrow on a file list column header; see the
    // "DataGridColumnHeader.sortAscending" styles in App.axaml.
    private const string SORT_ASC_CLASS = "sortAscending";
    private const string SORT_DESC_CLASS = "sortDescending";
    private DataGridColumnHeader? mSortIndicatorHeader;

    /// <summary>
    /// Shows the ascending/descending sort arrow on the header of the given file list
    /// column, removing it from whichever header had it before.  Pass null to clear it.
    /// </summary>
    /// <remarks>
    /// Avalonia's DataGrid only draws its arrow (via the :sortascending/:sortdescending
    /// pseudo-classes) for sorts it performs itself through its collection view.  We handle
    /// Sorting ourselves (WPF's equivalent is setting DataGridColumn.SortDirection), so we
    /// drive the same SortIcon with our own style classes.  Pseudo-classes can't be used
    /// because the header recomputes them on every pointer enter/exit/press.
    /// </remarks>
    private void SetFileListSortIndicator(DataGridColumn? col, bool ascending)
    {
        if (mSortIndicatorHeader != null)
        {
            mSortIndicatorHeader.Classes.Remove(SORT_ASC_CLASS);
            mSortIndicatorHeader.Classes.Remove(SORT_DESC_CLASS);
            mSortIndicatorHeader = null;
        }
        if (col == null)
        {
            return;
        }
        // The column's header control isn't public; find it by its content, which is bound
        // to the column's (unique) Header string.  Each column keeps one header control for
        // its lifetime, so the classes stick until we remove them.
        DataGridColumnHeader? header = fileListDataGrid.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => Equals(h.Content, col.Header));
        if (header == null)
        {
            return;
        }
        header.Classes.Set(SORT_ASC_CLASS, ascending);
        header.Classes.Set(SORT_DESC_CLASS, !ascending);
        mSortIndicatorHeader = header;
    }



    private void LaunchPanel_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files?.Count == 1)
            {
                string? path = files[0].TryGetLocalPath();
                if (path != null)
                {
                    // Fire-and-forget: DropOpenWorkFile is async but Drop handler is void.
                    if (DataContext is MainViewModel vm) {
                        _ = vm.DropOpenWorkFile(path);
                    }
                }
            }
        }
    }

    // ---- IViewActions implementation ----

    public void ScrollFileListTo(object item) => fileListDataGrid.ScrollIntoView(item, null);

    public void ScrollFileListToTop() => FileList_ScrollToTop();

    public void ScrollDirectoryTreeToTop() => DirectoryTree_ScrollToTop();

    public void FocusFileList() => fileListDataGrid.Focus();

    public void SetFileListSelectionFocus(int index)
    {
        if (index >= 0 && index < ViewModel!.FileList.Items.Count)
        {
            fileListDataGrid.ScrollIntoView(ViewModel!.FileList.Items[index], null);
        }
    }

    void IViewActions.SelectAllFileListItems() => SelectAllFileListItems();

    // True while SetFileListSelection is changing the grid's selection one item at a time;
    // the handler would otherwise snapshot the whole selection after every item.
    private bool mBatchSelecting;

    public void SetFileListSelection(IList<FileListItem> items)
    {
        mBatchSelecting = true;
        try
        {
            fileListDataGrid.SelectedItems.Clear();
            foreach (var item in items)
            {
                fileListDataGrid.SelectedItems.Add(item);
            }
        }
        finally
        {
            mBatchSelecting = false;
        }
        // One snapshot for the whole batch.
        PublishFileListSelection();
    }

    public void ShowToast(string message, bool success, double seconds = 3) =>
        PostNotification(message, success, seconds);

    private bool mNoticeShowing;

    public async void ShowNotice(string message, string caption)
    {
        if (mNoticeShowing)
        {
            return;
        }
        // A box owned by the main window would open behind a dialog.
        Window top = GetTopmostWindow();
        mNoticeShowing = true;
        try
        {
            await PlatformUtil.ShowMessageAsync(top, message, caption, MBButton.OK,
                MBIcon.Information);
        }
        finally
        {
            mNoticeShowing = false;
        }
    }

    // Last value of IsTextInputFocused that the view model was told about.
    private bool mTextInputFocused;

    public bool IsTextInputFocused => TextInputFocus.IsTextInputFocused(this);

    private void Window_FocusChanged(object? sender, FocusChangedEventArgs e)
    {
        bool now = IsTextInputFocused;
        if (now == mTextInputFocused)
        {
            return;
        }
        mTextInputFocused = now;
        ViewModel?.NotifyTextInputFocusChanged();
    }

    public void SetCursorBusy(bool busy)
    {
        Cursor = busy ? new Cursor(StandardCursorType.Wait) : null;
    }

    public void SelectArchiveTreeItem(ArchiveTreeItem item)
    {
        TreeViewHelper.BringIntoView(archiveTree, item);
        archiveTree.SelectedItem = item;
    }

    public void SelectBestArchiveTreeItem(ArchiveTreeItem root)
    {
        ArchiveTreeItem best = ArchiveTreeItem.FindBest(root);
        TreeViewHelper.BringIntoView(archiveTree, best);
        archiveTree.SelectedItem = best;
    }

    public void SelectDirectoryTreeItem(DirectoryTreeItem? item)
    {
        directoryTree.SelectedItem = item;
    }

    public bool SelectDirectoryTreeItemByEntry(
        System.Collections.ObjectModel.ObservableCollection<DirectoryTreeItem> root,
        IFileEntry dirEntry)
    {
        DirectoryTreeItem? item = DirectoryTreeItem.FindItemByEntry(root, dirEntry);
        if (item == null)
        {
            return false;
        }
        TreeViewHelper.BringIntoView(directoryTree, item);
        directoryTree.SelectedItem = item;
        return true;
    }

    public void ScrollFileListItemIntoView(FileListItem item)
    {
        fileListDataGrid.ScrollIntoView(item, null);
    }

    public void SelectFileListItemByEntry(IFileEntry selEntry)
    {
        // Select the row for the entry; if there is none (we changed directories, or selEntry
        // is NO_ENTRY), select the first row, as WPF does.  Callers that must not disturb the
        // selection when the entry is absent check for it first
        // (MainViewModel.SyncFileListToViewerEntry).
        FileListItem? reselItem = null;
        if (selEntry != IFileEntry.NO_ENTRY)
        {
            reselItem = FileListItem.FindItemByEntry(ViewModel!.FileList.Items, selEntry);
        }
        if (reselItem != null)
        {
            fileListDataGrid.SelectedItem = reselItem;
            fileListDataGrid.ScrollIntoView(reselItem, null);
            // TODO: WPF also gives the row keyboard focus (SelectRowByIndex()); here
            // FocusFileList() does it for the callers that want it.
        }
        else if (ViewModel!.FileList.Items.Count > 0)
        {
            fileListDataGrid.SelectedIndex = 0;
        }
    }

    public void PopulateRecentFilesMenu()
    {
        var paths = _workspaceService.RecentFilePaths.ToList();
        PopulateRecentFilesMenu(paths);
    }

    public void ResetFileListSort()
    {
        ClearColumnSortTags();
    }

    public void ScrollDirectoryTreeItemIntoView(DirectoryTreeItem item)
    {
        TreeViewHelper.BringIntoView(directoryTree, item);
    }

    public void FocusDirectoryTree() => directoryTree.Focus();

    // Rooted (field, not a local) so it can't be GC'd before it fires.
    private DispatcherTimer? mFocusDirTreeTimer;

    // Alt+Up menu-dismissal state (see Window_PreviewKeyDown / Window_AltKeyUp).
    private bool mDismissMenuAfterAltUp;
    private DispatcherTimer? mDismissMenuTimer;

    public void FocusDirectoryTreeDeferred()
    {
        // Two problems to beat: (1) the file-list DataGrid re-focuses its selected row
        // asynchronously after the ItemsSource repopulation, so a short timer defers past that
        // settle; (2) focusing the TreeView itself doesn't put keyboard focus on a navigable
        // node, so we focus the selected item's TreeViewItem container instead.
        mFocusDirTreeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        mFocusDirTreeTimer.Stop();
        mFocusDirTreeTimer.Tick -= FocusDirTreeTimer_Tick;
        mFocusDirTreeTimer.Tick += FocusDirTreeTimer_Tick;
        mFocusDirTreeTimer.Start();
    }

    private void FocusDirTreeTimer_Tick(object? sender, EventArgs e)
    {
        mFocusDirTreeTimer?.Stop();
        FocusSelectedDirectoryTreeItem();
    }

    // Focuses the TreeViewItem container for the directory tree's selected item, so keyboard
    // focus actually lands on a navigable node (plain TreeView.Focus() does not).
    private void FocusSelectedDirectoryTreeItem()
    {
        if (directoryTree.SelectedItem is not DirectoryTreeItem sel) {
            directoryTree.Focus();
            return;
        }
        TreeViewItem? tvi = TreeViewHelper.FindContainer(directoryTree, sel);
        if (tvi != null) {
            tvi.Focus();
        } else {
            directoryTree.Focus();
        }
    }

    public void FocusArchiveTree() => archiveTree.Focus();

    // ---- Toast notification (PostNotification) ----

    private DispatcherTimer? mToastTimer;

    /// <summary>
    /// Shows a brief toast notification at the bottom of the window.
    /// </summary>
    /// <param name="msg">Message to display.</param>
    /// <param name="success">True for a success (green) tint, false for error (red).</param>
    /// <param name="seconds">How long the toast stays up.</param>
    public void PostNotification(string msg, bool success, double seconds = 3)
    {
        toastText.Text = msg;
        toastBorder.Background = success
            ? new SolidColorBrush(Color.FromArgb(200, 0, 160, 0))
            : new SolidColorBrush(Color.FromArgb(200, 200, 0, 0));
        toastBorder.IsVisible = true;

        mToastTimer?.Stop();
        mToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        mToastTimer.Tick += (_, _) =>
        {
            toastBorder.IsVisible = false;
            mToastTimer?.Stop();
        };
        mToastTimer.Start();
    }

    private void ArchiveTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm) {
            vm.OnArchiveTreeSelectionChanged(archiveTree.SelectedItem as ArchiveTreeItem);
        }
    }

    private void DirectoryTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm) {
            vm.OnDirectoryTreeSelectionChanged(directoryTree.SelectedItem as DirectoryTreeItem);
        }
    }

    /// <summary>
    /// Selects the tree item under the pointer on a right-button press so the context menu
    /// (which acts on the selected node) targets the clicked item.  Shared by both trees;
    /// left-click selection is handled by the TreeView itself and is unaffected.
    /// </summary>
    private void Tree_SelectOnRightClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TreeView tree) {
            return;
        }
        if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed) {
            return;
        }
        if (e.Source is Visual visual) {
            TreeViewItem? item = visual.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            if (item?.DataContext != null) {
                tree.SelectedItem = item.DataContext;
            }
        }
    }

    // Eat Enter/Return in the directory tree so the TreeView does not toggle the node.  The
    // directory tree's Enter -> View Files key binding (MainWindow.axaml, as in WPF) is tried
    // before any KeyDown handler, so this only sees the key when that command declines (for
    // instance when no file is selected).  Registered as a tunnel handler so it runs before
    // the TreeView's own key handling.
    private void DirectoryTree_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return) {
            e.Handled = true;
        }
    }

    // Tunnel handler for Alt+Up (Go To Parent).  Runs at the window before the event reaches the
    // focused tree/grid, so those controls can't first consume the Up arrow (ignoring Alt) and
    // move their own selection instead of navigating to the parent.
    private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Up || e.KeyModifiers != KeyModifiers.Alt) {
            return;
        }
        // Consume Alt+Up before the focused tree/grid can treat it as a plain Up arrow, then
        // navigate to the parent.
        e.Handled = true;
        // Because we consumed the Up, Avalonia's AccessKeyHandler sees the Alt as a lone press
        // and will toggle the menu bar into access-key mode when Alt is released.  Arm the
        // key-up handler to dismiss it.
        mDismissMenuAfterAltUp = true;
        if (DataContext is MainViewModel vm && vm.NavToParentCommand.CanExecute(null)) {
            vm.NavToParentCommand.Execute(null);
        }
    }

    // Bubble key-up handler (handledEventsToo so it runs even though AccessKeyHandler's tunnel
    // handler already processed the Alt release).  Dismisses the access-key menu mode that the
    // Alt+Up sequence triggers.
    private void Window_AltKeyUp(object? sender, KeyEventArgs e)
    {
        if (!mDismissMenuAfterAltUp || e.Key is not (Key.LeftAlt or Key.RightAlt)) {
            return;
        }
        mDismissMenuAfterAltUp = false;
        // The menu grabs focus asynchronously, so dismiss after a short settle: clear the
        // access-key display and pull keyboard focus back into the directory tree.
        mDismissMenuTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        mDismissMenuTimer.Stop();
        mDismissMenuTimer.Tick -= DismissMenuTimer_Tick;
        mDismissMenuTimer.Tick += DismissMenuTimer_Tick;
        mDismissMenuTimer.Start();
    }

    private void DismissMenuTimer_Tick(object? sender, EventArgs e)
    {
        mDismissMenuTimer?.Stop();
        // TODO(avalonia12): IInputRoot was removed in Avalonia 12 and nothing public replaces
        // ShowAccessKeys.  This used to clear the access-key underline mode that Alt+Up leaves
        // behind, so the underlines may now stay visible after Alt+Up.
        //((IInputRoot)this).ShowAccessKeys = false;
        FocusSelectedDirectoryTreeItem();
    }

    private void FileListDataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Visual;
        var colHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (colHeader != null)
        {
            // Header double-click.  Auto-sizing a column from its divider is done on the
            // second press (FileListDataGrid_PointerPressed), where the pointer position
            // is known.
            mSuppressSort = false;
            return;
        }
        // Only a double-click on a data row opens a viewer, not one on a scrollbar or on
        // blank space below the rows (the selected row would be opened).
        if (source?.FindAncestorOfType<DataGridRow>(includeSelf: true) == null)
        {
            return;
        }
        if (DataContext is MainViewModel vm) {
            bool forceNew = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            vm.HandleFileListDoubleClick(forceNew);
        }
    }

    private void PartitionLayout_DoubleTapped(object? sender, TappedEventArgs e)
    {
        DataGrid? grid = sender as DataGrid;

        // Header double-click: divider auto-size is done on the press (see
        // InfoDataGrid_PointerPressed).
        var source = e.Source as Visual;
        var colHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (colHeader != null)
        {
            mSuppressSort = false;
            return;
        }

        // Only a double-click on a data row opens the partition.
        if (source?.FindAncestorOfType<DataGridRow>(includeSelf: true) == null)
        {
            return;
        }
        if (grid?.SelectedItem is PartitionListItem pli)
        {
            if (DataContext is MainViewModel vm) {
                ArchiveTreeItem? arcTreeSel = archiveTree.SelectedItem as ArchiveTreeItem;
                vm.HandlePartitionLayoutDoubleClick(pli, arcTreeSel);
            }
        }
    }

    private async void MetadataList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Visual;

        // Header double-click: divider auto-size is done on the press (see
        // InfoDataGrid_PointerPressed).
        var colHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (colHeader != null)
        {
            mSuppressSort = false;
            return;
        }

        // Only open the edit dialog when the double-click is on a data row.
        var row = source?.FindAncestorOfType<DataGridRow>();
        if (row == null)
        {
            return;
        }
        DataGrid? dg = sender as DataGrid;
        if (dg?.SelectedItem is MetadataItem item)
        {
            if (DataContext is MainViewModel vm) {
                await vm.HandleMetadataDoubleClick(item, 0, 0);
            }
        }
    }

    private async void Metadata_AddEntryButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) {
            await vm.HandleMetadataAddEntry();
        }
    }

    /// <summary>
    /// Tells whether a press landed on a column divider in a header of <paramref name="grid"/>,
    /// and if so which column the divider resizes: the left divider of column B is at the same
    /// place as the right divider of column A, and both resize A (WPF behavior).
    /// </summary>
    /// <remarks>
    /// The DataGrid draws no grip control (there is no Thumb to hit-test); it resizes when the
    /// pointer is within a few pixels of a header edge, so this tests the pointer position
    /// the same way (see <see cref="DataGridHeaderGrip"/>).
    /// </remarks>
    private static bool TryGetHeaderGrip(DataGrid grid, Visual? hitVisual, PointerEventArgs e,
        out DataGridColumn? target)
    {
        target = null;
        DataGridColumnHeader? header =
            hitVisual?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true);
        if (header == null)
        {
            return false;
        }
        DataGridHeaderGrip.Edge edge =
            DataGridHeaderGrip.Classify(e.GetPosition(header).X, header.Bounds.Width);
        if (edge == DataGridHeaderGrip.Edge.None)
        {
            return false;
        }
        int index = DataGridHeaderGrip.FindColumnIndex(grid.Columns, header.Content);
        target = DataGridHeaderGrip.ResolveTarget(grid.Columns, index, edge);
        return target != null;
    }

    // ---- Name-column pre-sizing (Filename / Pathname) ----
    //
    // The file-list DataGrid is virtualized, so an "Auto" width column only measures the
    // rows currently realized and grows as wider names scroll into view.  To avoid that, we
    // measure the widest name in the whole model up front and pin the column to a pixel
    // width (see PreSizeNameColumn).  Once the user manually resizes a name column we stop
    // managing it and leave their width in place.

    private DataGridColumn? mFileNameColumn;
    private DataGridColumn? mPathNameColumn;
    private bool mFileNameColUserSized;
    private bool mPathNameColUserSized;
    // A name-column resize-grip drag in progress: the column and its width at grip-down.
    // (Avalonia 11's DataGridColumn.Width isn't an AvaloniaProperty, so there's no width-
    // changed notification to hook; we detect a real resize by pointer grip + width delta.)
    private DataGridColumn? mPendingResizeCol;
    private double mPendingResizeStartWidth;

    private void SetupNameColumnAutoSize()
    {
        foreach (DataGridColumn col in fileListDataGrid.Columns) {
            switch (col.SortMemberPath) {
                case nameof(FileListItem.FileName): mFileNameColumn = col; break;
                case nameof(FileListItem.PathName): mPathNameColumn = col; break;
            }
        }
    }

    // Once a name column has been explicitly sized by the user (drag or double-click
    // auto-fit) we stop pre-sizing it and leave their width in place.
    private void MarkNameColumnUserSized(DataGridColumn? col)
    {
        if (col == mFileNameColumn) {
            mFileNameColUserSized = true;
        } else if (col == mPathNameColumn) {
            mPathNameColUserSized = true;
        }
    }

    public void PreSizeNameColumn()
    {
        if (ViewModel == null) {
            return;
        }
        // Only the visible name column matters (Filename in single-dir mode, else Pathname).
        bool showFileName = ViewModel.ShowSingleDirFileList;
        DataGridColumn? col = showFileName ? mFileNameColumn : mPathNameColumn;
        bool userSized = showFileName ? mFileNameColUserSized : mPathNameColUserSized;
        if (col == null || userSized) {
            return;
        }
        var items = ViewModel.FileList.Items;
        if (items.Count == 0) {
            return;
        }

        // Measuring every row is wasteful for large lists.  The widest pixel string is
        // effectively always among the longest by character count, so only measure strings
        // within a few characters of the longest (slack covers proportional-font variation).
        int maxLen = 0;
        foreach (FileListItem it in items) {
            string s = showFileName ? it.FileName : it.PathName;
            if (s != null && s.Length > maxLen) {
                maxLen = s.Length;
            }
        }
        if (maxLen == 0) {
            return;
        }
        int threshold = Math.Max(1, maxLen - 8);

        var measureBlock = new TextBlock {
            FontFamily = fileListDataGrid.FontFamily,
            FontSize = fileListDataGrid.FontSize,
            FontStyle = fileListDataGrid.FontStyle,
            FontWeight = fileListDataGrid.FontWeight,
        };
        // Header width is the floor.
        measureBlock.Text = col.Header?.ToString() ?? string.Empty;
        measureBlock.Measure(Size.Infinity);
        double maxWidth = measureBlock.DesiredSize.Width;

        foreach (FileListItem it in items) {
            string s = showFileName ? it.FileName : it.PathName;
            if (string.IsNullOrEmpty(s) || s.Length < threshold) {
                continue;
            }
            measureBlock.Text = s;
            measureBlock.Measure(Size.Infinity);
            if (measureBlock.DesiredSize.Width > maxWidth) {
                maxWidth = measureBlock.DesiredSize.Width;
            }
        }

        // Same content padding (cell margins/borders + sort indicator) as AutoSizeColumnToContent.
        col.Width = new DataGridLength(maxWidth + 14, DataGridLengthUnitType.Pixel);
    }

    /// <summary>
    /// Auto-sizes a DataGrid column to fit its header and cell content, replicating
    /// the WPF DataGrid double-click-on-resize-grip behavior.
    /// </summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern",
        Justification = "It's not fatal if the property is trimmed away.")]
    private static void AutoSizeColumnToContent(DataGrid grid, DataGridColumn col)
    {
        IEnumerable? items = grid.ItemsSource as IEnumerable;
        if (items == null)
        {
            return;
        }

        // Determine the property name to read from each row item.
        string? propName = null;
        if (col is DataGridBoundColumn boundCol &&
                boundCol.Binding is Avalonia.Data.Binding binding)
        {
            propName = binding.Path;
        }
        else if (col is DataGridTemplateColumn)
        {
            // Template columns have no binding to read; SortMemberPath names the property.
            propName = string.IsNullOrEmpty(col.SortMemberPath) ? null : col.SortMemberPath;
        }

        // Use a temporary TextBlock to measure with the grid's font settings.
        var measureBlock = new TextBlock
        {
            FontFamily = grid.FontFamily,
            FontSize = grid.FontSize,
            FontStyle = grid.FontStyle,
            FontWeight = grid.FontWeight
        };

        // Measure header text.
        measureBlock.Text = col.Header?.ToString() ?? string.Empty;
        measureBlock.Measure(Size.Infinity);
        double maxWidth = measureBlock.DesiredSize.Width;

        // Measure cell content.
        if (propName != null)
        {
            System.Reflection.PropertyInfo? prop = null;
            foreach (object? item in items)
            {
                if (item == null)
                {
                    continue;
                }

                prop ??= item.GetType().GetProperty(propName);      // IL2075 warning here
                if (prop == null)
                {
                    break;
                }

                string? text = prop.GetValue(item)?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    measureBlock.Text = text;
                    measureBlock.Measure(Size.Infinity);
                    if (measureBlock.DesiredSize.Width > maxWidth)
                    {
                        maxWidth = measureBlock.DesiredSize.Width;
                    }
                }
            }
        }

        // Add padding for cell margins, borders, and sort indicator.
        col.Width = new DataGridLength(maxWidth + 14, DataGridLengthUnitType.Pixel);
    }

    /// <summary>
    /// Detects presses on a column divider in info-panel DataGrid headers, so that column
    /// resizing doesn't also trigger a sort, and double-click auto-sizes the column.
    /// </summary>
    private void InfoDataGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Registered with handledEventsToo — skip the Bubble re-invocation.
        if (e.Handled)
        {
            return;
        }

        var hitVisual = e.Source as Visual;
        var colHeader = hitVisual?.FindAncestorOfType<DataGridColumnHeader>();
        if (colHeader != null)
        {
            DataGrid? grid = sender as DataGrid;
            DataGridColumn? target = null;
            bool isOnGrip = grid != null && TryGetHeaderGrip(grid, hitVisual, e, out target);
            mSuppressSort = isOnGrip;
            // Double-click on a divider auto-sizes the column to fit content.
            if (isOnGrip && e.ClickCount == 2 && grid != null && target != null)
            {
                Debug.WriteLine($"Info PointerPressed: divider double-click on " +
                    $"'{colHeader.Content}', ClickCount={e.ClickCount}");
                AutoSizeColumnToContent(grid, target);
            }
        }
        else
        {
            mSuppressSort = false;
        }
    }

    /// <summary>
    /// Generic Sorting handler for info-panel DataGrids (metadata, partition layout, notes).
    /// Suppresses sort when clicking a resize grip; otherwise clears the selected row,
    /// matching WPF behavior.
    /// </summary>
    private void InfoDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        if (mSuppressSort)
        {
            e.Handled = true;
            return;
        }
        if (sender is DataGrid dg)
        {
            dg.SelectedIndex = -1;
        }
    }

    private void FileListDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (mBatchSelecting) {
            return;
        }
        PublishFileListSelection();
    }

    /// <summary>Copies the file list's selection into the view model.</summary>
    private void PublishFileListSelection()
    {
        if (DataContext is MainViewModel vm) {
            // Snapshot the multi-selection into the VM.  As in WPF, selecting entries in the
            // file list never moves the directory tree selection (the tree only drives the
            // list, not the other way around).
            var sel = fileListDataGrid.SelectedItems.Cast<FileListItem>().ToList();
            vm.UpdateFileListSelection(sel);
        }
    }

    /// <summary>
    /// Handles DataGrid column sort clicks.  The view model sorts the rows with our own
    /// comparer (natural name order, secondary sort keys) because Avalonia lacks
    /// ListCollectionView.
    /// </summary>
    private void FileListDataGrid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;   // prevent DataGrid's built-in sort from firing

        // When the click is on a resize grip, suppress sorting so that
        // column resizing doesn't also re-sort the file list.
        if (mSuppressSort)
        {
            return;
        }

        DataGridColumn col = e.Column;

        // Determine new sort direction; default to ascending on first click per column.
        bool wasAscending = col.Tag is System.ComponentModel.ListSortDirection tagDir
            && tagDir == System.ComponentModel.ListSortDirection.Ascending;
        System.ComponentModel.ListSortDirection direction = wasAscending
            ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;

        // Clear sort indicator from all columns, then set on the active one.
        foreach (DataGridColumn c in fileListDataGrid.Columns)
        {
            c.Tag = null;
        }
        col.Tag = direction;

        bool isAscending = (direction == System.ComponentModel.ListSortDirection.Ascending);
        SetFileListSortIndicator(col, isAscending);

        // The view model sorts the rows (in one reset) and remembers the sort, so that
        // repopulating the list keeps it.  The column is identified by SortMemberPath, set
        // in MainWindow.axaml, not by its header text.
        // The selection is kept (WPF sorts through a collection view, which keeps the
        // selected items); the view model puts it back after replacing the rows.
        ViewModel!.FileList.ApplySort(col.SortMemberPath, isAscending);
    }

    // internal void ClearTreesAndLists()
    // {
    //     ArchiveTreeRoot.Clear();
    //     DirectoryTreeRoot.Clear();
    //     FileList.Clear();
    //     IsFullListEnabled = false;
    //     IsDirListEnabled = false;
    // }

    // ---- Drag-drop on file list DataGrid ----

    private void FileListDataGrid_DragOver(object? sender, DragEventArgs e)
    {
        if (mIsDraggingFileList)
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            // Don't check e.DataTransfer.Contains(DataFormat.File) here — on Linux the
            // data formats may not be fully populated during DragOver.  The Drop
            // handler validates the actual data.  (WPF has no DragOver on the file
            // list at all — AllowDrop="True" accepts everything by default.)
            bool canAdd = ViewModel != null && ViewModel.IsFileOpen && ViewModel.CanWrite
                && ViewModel.IsMultiFileItemSelected && ViewModel!.ShowCenterFileList;
            e.DragEffects = canAdd ? DragDropEffects.Copy : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FileListDataGrid_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        IFileEntry dropTarget = GetFileListDropTarget(e);

        if (mIsDraggingFileList)
        {
            // Internal drag: only move if dropped onto a directory entry.
            // Pass a copy of the list — the original is cleared in the finally block
            // of StartFileListDragAsync, which races with the async MoveFiles call.
            IFileEntry moveTarget;
            if (dropTarget != IFileEntry.NO_ENTRY && dropTarget.IsDirectory)
            {
                // Dropped onto a directory — move into it.
                moveTarget = dropTarget;
            }
            else if (dropTarget == IFileEntry.NO_ENTRY)
            {
                // Dropped onto empty space.  The empty-space target is the volume root in
                // full-list mode and the displayed directory in single-directory mode.
                // (GetCurrentDirEntry would be wrong in full-list mode: double-clicking a
                // subdirectory selects it in the tree, so blank space would then mean
                // "move into that subdirectory".)  A file dropped onto itself (hit-test
                // returns NO_ENTRY) is already in the target and is filtered out below.
                moveTarget = GetFileListEmptySpaceTarget();
            }
            else
            {
                // Dropped onto a non-directory file — ignore, nothing sensible to do.
                Debug.WriteLine("FL ignoring internal drop: target is not a directory");
                return;
            }
            if (moveTarget != IFileEntry.NO_ENTRY)
            {
                // Skip items that are already in the target directory — this handles
                // the case where a file is dropped onto itself (or its own row is
                // misidentified as empty space during drag), which would otherwise
                // move it to the root directory unintentionally.
                var itemsToMove = FileListDrag.EntriesToMove(mDragMoveList, moveTarget);
                if (itemsToMove.Count > 0)
                {
                    _ = ViewModel!.MoveFiles(itemsToMove, moveTarget);
                }
            }
            else
            {
                Debug.WriteLine("FL ignoring internal drop: no valid target");
            }
        }
        else if (IsCrossInstanceCp2Drop(e.DataTransfer))
        {
            // Another CP2 instance dragged files here — use the full paste path.
            _ = ViewModel!.PasteOrDrop(e.DataTransfer, ResolveExternalFileListDropTarget(dropTarget));
        }
        else if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files != null)
            {
                string?[] rawPaths = files.Select(f => f.TryGetLocalPath()).ToArray();
                string[] paths = Array.FindAll(rawPaths, p => p != null)!;
                if (paths.Length > 0)
                {
                    _ = ViewModel!.AddFileDrop(ResolveExternalFileListDropTarget(dropTarget), paths);
                }
            }
        }
        else
        {
            Debug.WriteLine("FL no valid drop");
        }
    }

    /// <summary>
    /// Returns the file-list row drop target under the pointer, or NO_ENTRY when the
    /// pointer is over blank DataGrid space below the populated rows.
    /// </summary>
    private IFileEntry GetFileListDropTarget(DragEventArgs e)
    {
        // Use hit-testing to find the DataGridRow under the pointer.  Walking
        // e.Source parents is unreliable for DataGrid drag events because the
        // Source is often the DataGrid itself, not the hovered cell.
        Point point = e.GetPosition(fileListDataGrid);
        var hitVisual = fileListDataGrid.InputHitTest(point) as Visual;
        var row = hitVisual?.FindAncestorOfType<DataGridRow>();
        if (row?.DataContext is not FileListItem fli)
        {
            return IFileEntry.NO_ENTRY;
        }

        // On Windows the DataGrid can report the last realized row even when the
        // pointer is in the blank area below the rows.  Verify that the pointer is
        // actually within the row bounds before treating it as a row drop.
        Point? rowOrigin = row.TranslatePoint(new Point(0, 0), fileListDataGrid);
        if (rowOrigin == null)
        {
            return IFileEntry.NO_ENTRY;
        }

        Rect rowBounds = new Rect(rowOrigin.Value, row.Bounds.Size);
        if (!rowBounds.Contains(point))
        {
            Debug.WriteLine("FL drop in DataGrid empty space");
            return IFileEntry.NO_ENTRY;
        }

        return fli.FileEntry;
    }

    /// <summary>
    /// Called by LinuxDrag when the button is released and no external XDND target accepted
    /// the drop.  Works out which of our windows the pointer was over, if any, stops the
    /// background extraction, and then moves the files or shows the entry in a viewer.
    /// </summary>
    /// <param name="screenX">Root X of the release point.</param>
    /// <param name="screenY">Root Y of the release point.</param>
    /// <param name="chain">X windows under the pointer at release, outermost first.</param>
    /// <param name="itemsToMove">Dragged entries.</param>
    /// <param name="extraction">The extraction started for this drag.</param>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async System.Threading.Tasks.Task HandleLinuxInternalDropAsync(int screenX,
        int screenY, nint[] chain, List<IFileEntry> itemsToMove,
        CancellableExtraction<MainViewModel.DragTransferData> extraction)
    {
        if (ViewModel is not MainViewModel vm)
        {
            return;
        }

        // The window under the pointer, by native handle, so that stacking order is right
        // (a viewer over the main window) and a drop over another program's window, even
        // one that overlaps ours, is not ours.
        var ownWindows = new Dictionary<nint, Window>();
        if (Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
        {
            foreach (Window win in lt.Windows)
            {
                nint handle = win.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                {
                    ownWindows[handle] = win;
                }
            }
        }
        Window? target = Platform.DropWindowPicker.Pick(chain, ownWindows);
        object? workObject = vm.CurrentWorkObject;

        // The extraction reads the same archive that the move or viewer is about to use,
        // and DiskArc is not thread-safe: stop it and wait until it has finished.
        await extraction.CancelAndWaitAsync();

        if (target == null)
        {
            Debug.WriteLine("Linux drop: pointer not over one of our windows; ignored");
            return;
        }
        if (!target.IsVisible || !vm.IsFileOpen || !ReferenceEquals(vm.CurrentWorkObject, workObject))
        {
            Debug.WriteLine("Linux drop: window or work file changed during the drop; ignored");
            return;
        }
        if (itemsToMove.Count == 0)
        {
            return;
        }

        if (target == this)
        {
            IFileEntry hitEntry = HitTestScreenDropTarget(screenX, screenY, out bool inDirTree);
            IFileEntry moveTarget;
            if (inDirTree || (hitEntry != IFileEntry.NO_ENTRY && hitEntry.IsDirectory))
            {
                moveTarget = hitEntry;
            }
            else if (hitEntry != IFileEntry.NO_ENTRY)
            {
                moveTarget = hitEntry.ContainingDir != IFileEntry.NO_ENTRY
                    ? hitEntry.ContainingDir
                    : GetCurrentVolumeDirEntry();
            }
            else
            {
                // Use GetFileListEmptySpaceTarget, not GetCurrentDirEntry.  In full-list mode,
                // selecting a directory syncs the dir tree to that directory, making
                // GetCurrentDirEntry return the dragged directory itself →
                // "Cannot move a directory into itself."
                moveTarget = GetFileListEmptySpaceTarget();
            }
            if (moveTarget != IFileEntry.NO_ENTRY)
            {
                var toMove = FileListDrag.EntriesToMove(itemsToMove, moveTarget);
                if (toMove.Count > 0)
                    _ = vm.MoveFiles(toMove, moveTarget);
            }
        }
        else if (target is FileViewer viewer)
        {
            // Single-entry drag loads the entry in the viewer.
            if (itemsToMove.Count == 1 && workObject != null)
            {
                (viewer.DataContext as FileViewerViewModel)
                    ?.LoadDroppedEntry(workObject, itemsToMove[0], vm.WorkPathName);
            }
        }
        else if (target is DropTarget dropTarget)
        {
            // Debug window: display the dragged entries for diagnostics.
            dropTarget.ShowLinuxInternalDrop(itemsToMove);
        }
        else
        {
            Debug.WriteLine("Linux drop: over " + target.GetType().Name + "; ignored");
        }
    }

    /// <summary>
    /// Converts X11 root screen coordinates to the <see cref="IFileEntry"/> of the
    /// UI element under the pointer.  Returns <see cref="IFileEntry.NO_ENTRY"/> when
    /// the pointer is over empty space (DataGrid or panel) or outside a known control.
    /// <paramref name="inDirectoryTree"/> is set when the hit was in the directory tree
    /// (where the entry is always a directory and should not be passed through
    /// <see cref="ResolveExternalFileListDropTarget"/>).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private IFileEntry HitTestScreenDropTarget(int screenX, int screenY,
        out bool inDirectoryTree)
    {
        inDirectoryTree = false;
        var windowPt = this.PointToClient(new PixelPoint(screenX, screenY));

        // Directory tree (checked first — overlaps the left side of the layout).
        Point? dirPt = this.TranslatePoint(windowPt, directoryTree);
        if (dirPt.HasValue && new Rect(directoryTree.Bounds.Size).Contains(dirPt.Value))
        {
            var hitEl = directoryTree.InputHitTest(dirPt.Value) as StyledElement;
            for (var cur = hitEl; cur != null; cur = cur.Parent as StyledElement)
            {
                if (cur.DataContext is DirectoryTreeItem dti)
                {
                    inDirectoryTree = true;
                    return dti.FileEntry;
                }
            }
        }

        // File list DataGrid.
        Point? gridPt = this.TranslatePoint(windowPt, fileListDataGrid);
        if (gridPt.HasValue && new Rect(fileListDataGrid.Bounds.Size).Contains(gridPt.Value))
        {
            var hitVis = fileListDataGrid.InputHitTest(gridPt.Value) as Visual;
            var row    = hitVis?.FindAncestorOfType<DataGridRow>();
            if (row?.DataContext is FileListItem fli)
            {
                Point? ro = row.TranslatePoint(new Point(0, 0), fileListDataGrid);
                if (ro.HasValue && new Rect(ro.Value, row.Bounds.Size).Contains(gridPt.Value))
                    return fli.FileEntry;
            }
            return IFileEntry.NO_ENTRY; // within the grid but not on a row
        }

        // File list panel (empty space below the populated rows).
        return IFileEntry.NO_ENTRY;
    }

    // ---- Internal drag initiation (pointer events on file list) ----

    private void FileListDataGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Handler is registered with Tunnel|Bubble + handledEventsToo, so it may
        // fire twice per click.  Only process drag-start and auto-resize on the
        // first invocation (Tunnel phase, where Handled is still false).
        Debug.WriteLine($"FL PointerPressed: route={e.Route}, handled={e.Handled}, " +
            $"source={e.Source?.GetType().Name}, click={e.ClickCount}");
        if (e.Handled)
        {
            return;
        }

        var point = e.GetCurrentPoint(fileListDataGrid);
        if (point.Properties.IsLeftButtonPressed)
        {
            // Only start a potential drag from a data row, not from a column header
            // or its resize grip.  Otherwise column-resize drags are misinterpreted
            // as file drag-drop operations.
            var hitVisual = e.Source as Visual;
            var colHeader = hitVisual?.FindAncestorOfType<DataGridColumnHeader>();
            if (colHeader != null)
            {
                // If the press is on a column divider, suppress the next sort event so that
                // resizing doesn't also re-sort the list.
                bool isOnGrip = TryGetHeaderGrip(fileListDataGrid, hitVisual, e,
                    out DataGridColumn? target);
                mSuppressSort = isOnGrip;
                // Double-click on a divider auto-sizes the column to fit content
                // (replicates WPF behavior).  We handle it on the press, where the pointer
                // position is known, rather than in DoubleTapped.
                mPendingResizeCol = null;
                if (isOnGrip && e.ClickCount == 2)
                {
                    Debug.WriteLine($"FL PointerPressed: divider double-click on " +
                        $"'{colHeader.Content}', ClickCount={e.ClickCount}");
                    AutoSizeColumnToContent(fileListDataGrid, target!);
                    // Double-click auto-fit is an explicit user sizing; stop pre-sizing it.
                    MarkNameColumnUserSized(target);
                }
                else if (isOnGrip && e.ClickCount == 1)
                {
                    // Track a possible drag-resize of a name column; confirmed on release
                    // by comparing the width (see FileListDataGrid_PointerReleased).
                    if (target == mFileNameColumn || target == mPathNameColumn)
                    {
                        mPendingResizeCol = target;
                        mPendingResizeStartWidth = target!.ActualWidth;
                    }
                }
                mDragStartPosn = new Point(-1, -1);
                return;
            }
            // Only a press on a data row may arm a drag.  A press on a scrollbar (its thumb,
            // track or arrows) or on blank space below the rows must not, or moving the
            // pointer would start dragging the selected files.
            if (hitVisual?.FindAncestorOfType<DataGridRow>(includeSelf: true) == null)
            {
                mSuppressSort = false;
                mDragStartPosn = new Point(-1, -1);
                return;
            }
            mSuppressSort = false;
            mDragStartPosn = e.GetPosition(fileListDataGrid);
            // DoDragDropAsync needs the press that arms the drag; the drag starts later,
            // from PointerMoved.
            mDragPressArgs = e;

            // If the user presses an already-selected row that is part of a larger
            // selection (no Ctrl/Shift/Cmd held), suppress the DataGrid's default
            // behavior of collapsing the selection to that single row.  This lets a
            // drag carry the whole selection.  We mark the event handled so the
            // DataGrid's selection logic (bubble-phase) is skipped; if the gesture is
            // a plain click rather than a drag, PointerReleased collapses to this row.
            mPendingSingleSelect = null;
            bool noModifiers = (e.KeyModifiers &
                (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) == 0;
            if (noModifiers && fileListDataGrid.SelectedItems.Count > 1)
            {
                var clickedRow = hitVisual?.FindAncestorOfType<DataGridRow>();
                if (clickedRow?.DataContext is FileListItem clickedItem &&
                    fileListDataGrid.SelectedItems.Contains(clickedItem))
                {
                    mPendingSingleSelect = clickedItem;
                    e.Handled = true;
                }
            }
        }
        else
        {
            mDragStartPosn = new Point(-1, -1);
        }
    }

    private void FileListDataGrid_PointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(fileListDataGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            mDragStartPosn = new Point(-1, -1);
            return;
        }
        if (mIsDraggingFileList || mDragStartPosn.X < 0)
        {
            return;
        }
        var posn = e.GetPosition(fileListDataGrid);
        if (Math.Abs(posn.X - mDragStartPosn.X) > DRAG_THRESHOLD ||
                Math.Abs(posn.Y - mDragStartPosn.Y) > DRAG_THRESHOLD)
        {
            mDragStartPosn = new Point(-1, -1);
            // A drag is starting: keep the full selection, don't collapse on release.
            mPendingSingleSelect = null;
            if (mDragPressArgs != null)
            {
                _ = StartFileListDragAsync(e, mDragPressArgs);
            }
        }
    }

    /// <summary>
    /// Releases whatever has captured the pointer, so Avalonia's input is back to normal.
    /// </summary>
    /// <remarks>
    /// Needed after a macOS native drag session: AppKit runs the session and swallows the
    /// mouse-up, so Avalonia never sees the button go up and keeps the pointer captured by
    /// the element the press went to.  Row hover then stops updating, and the next press is
    /// routed to that stale element, which re-selects the previously selected row (so the
    /// wrong file was dragged).
    /// </remarks>
    private static void ReleasePointerCapture(IPointer pointer)
    {
        pointer.Capture(null);
    }

    private void FileListDataGrid_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // The press is over.  A drag needs a fresh press, so a later move with the button
        // down can't start one from a stale position.
        mDragStartPosn = new Point(-1, -1);
        // If a name-column resize grip was grabbed and the width actually changed, lock that
        // column so we stop pre-sizing it (the user has given it an explicit width).
        if (mPendingResizeCol != null)
        {
            DataGridColumn col = mPendingResizeCol;
            mPendingResizeCol = null;
            if (Math.Abs(col.ActualWidth - mPendingResizeStartWidth) > 0.5)
            {
                MarkNameColumnUserSized(col);
            }
        }

        // Registered for both Tunnel and Bubble, so it can fire twice; clearing the
        // field on the first invocation makes the second a no-op.
        if (mPendingSingleSelect == null)
        {
            return;
        }
        FileListItem item = mPendingSingleSelect;
        mPendingSingleSelect = null;
        // The press on an already-selected row was a plain click (no drag), so apply
        // the selection collapse the DataGrid would normally have done on press.
        fileListDataGrid.SelectedItems.Clear();
        fileListDataGrid.SelectedItems.Add(item);
    }

    private async System.Threading.Tasks.Task StartFileListDragAsync(PointerEventArgs e,
        PointerPressedEventArgs pressArgs)
    {
        Debug.Assert(!mIsDraggingFileList);
        mIsDraggingFileList = true;
        mDragMoveList.Clear();
        IPointer pointer = e.Pointer;
        for (int i = 0; i < fileListDataGrid.SelectedItems.Count; i++)
        {
            if (fileListDataGrid.SelectedItems[i] is FileListItem selItem)
            {
                mDragMoveList.Add(selItem.FileEntry);
            }
        }
        MainViewModel.DragTransferData? xferData = null;
        bool nativeDragStarted = false;
        try
        {
            DragDropEffects result = DragDropEffects.None;
            var data = new DataTransfer();
            var dataItem = new DataTransferItem();
            data.Add(dataItem);
            dataItem.Set(FileListDrag.ENTRIES_FORMAT, mDragMoveList);
            if (DataContext is MainViewModel vm)
            {
                if (vm.CurrentWorkObject != null)
                {
                    dataItem.Set(FileListDrag.SOURCE_FORMAT, vm.CurrentWorkObject);
                    dataItem.Set(FileListDrag.WORKPATH_FORMAT, vm.WorkPathName);
                    // For windows that can't get the entries from the data object (a
                    // macOS native drag session has none of ours).
                    FileListDrag.Current = new FileListDrag.State(
                        new List<IFileEntry>(mDragMoveList), vm.CurrentWorkObject,
                        vm.WorkPathName);
                }

                // Linux: arm the XDND drag BEFORE extraction so XdndSelection is claimed
                // immediately and KWin builds its XWayland→Wayland bridge at drag-start.
                // After BeginNativeDrag, BuildDragData runs on the threadpool so the UI
                // stays responsive and the LinuxDrag poll loop can detect button-release
                // while extraction is in progress.
                System.Threading.Tasks.Task<DragDropEffects>? linuxDragTask = null;
                bool linuxNativeCursor = OperatingSystem.IsLinux() && mLinuxDrag != null;
                // The Linux extraction runs on a pool thread while the UI stays live (the
                // one allowed exception to the X1 rule).  The internal-drop handler stops
                // it and waits for it before touching the archive.
                var extraction = new CancellableExtraction<MainViewModel.DragTransferData>();
#pragma warning disable CA1416  // guarded by linuxNativeCursor
                if (linuxNativeCursor)
                {
                    nativeDragStarted = true;
                    var internalDragItems = new List<IFileEntry>(mDragMoveList);
                    linuxDragTask = mLinuxDrag!.BeginNativeDrag(
                        onInternalDrop: (rx, ry, chain) => _ = HandleLinuxInternalDropAsync(
                            rx, ry, chain, internalDragItems, extraction));
                }
#pragma warning restore CA1416

                Cursor = linuxNativeCursor
                    ? new Cursor(StandardCursorType.DragMove)
                    : new Cursor(StandardCursorType.Wait);
                try
                {
                    if (linuxNativeCursor)
                    {
                        // Read the selection here, on the UI thread; only the writing of
                        // the files goes to the pool.  A failure at either step leaves
                        // xferData null, and the drag is told below that there is nothing.
                        try
                        {
                            MainViewModel.DragPlan? plan = vm.PrepareDragData();
                            if (plan != null)
                            {
                                xferData = await extraction.Start(
                                    cancelled => vm.ExtractDragData(plan, cancelled));
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLog.W("File list: preparing the drag data failed", ex);
                            xferData = null;
                        }
                    }
                    else
                    {
                        xferData = vm.BuildDragData();
                    }
                }
                finally
                {
                    if (!linuxNativeCursor) Cursor = null;
                    // Linux: keep DragMove cursor until drag ends in the outer finally.
                }
                if (xferData != null)
                {
                    TempDirectoryTracker.Track(xferData.TempDir);

                    // Private MIME type for cross-CP2-instance drag.
                    dataItem.Set(ClipInfo.CrossInstanceDataFormat, xferData.ClipJsonText);

                    if (OperatingSystem.IsMacOS())
                    {
                        // macOS: start a native NSDraggingSession with NSFilePromiseProvider
                        // items so Finder accepts the drop and receives files via the promise
                        // delivery callback.  A second NSPasteboardItem carries the CP2 JSON
                        // so that same-window and cross-instance CP2 drops still work.
                        //
                        // If the native session starts successfully the onDragEnd callback
                        // owns all cleanup; we skip the DoDragDrop / finally path below.
                        IntPtr nsWin = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                        if (nsWin != IntPtr.Zero)
                        {
                            nativeDragStarted = Platform.MacOSDrag.StartNativeDrag(
                                nsWin,
                                xferData.ExtractedPaths,
                                xferData.ClipJsonText,
                                onDragEnd: () =>
                                {
                                    ReleasePointerCapture(pointer);
                                    EndFileListDragState();
                                });
                            AppLog.D("File list: macOS native drag " +
                                (nativeDragStarted ? "started" : "unavailable, using DoDragDrop"));
                            if (nativeDragStarted)
                            {
                                // AppKit runs the drag session and swallows the mouse-up, so
                                // Avalonia never sees the button go up and keeps the pointer
                                // captured by whatever the press went to.  Hover then stops
                                // updating and the next press is routed to the stale owner.
                                ReleasePointerCapture(pointer);
                                return;
                            }
                        }
                        // Native session unavailable; fall through to DoDragDrop.
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        // Provide extracted paths to the already-armed drag session.
#pragma warning disable CA1416
                        if (linuxDragTask != null)
                        {
                            mLinuxDrag!.SetNativeDragPaths(
                                xferData.ExtractedPaths.ToArray(),
                                xferData.ClipJsonText,
                                xferData.TempDir);
                            result = await linuxDragTask;
                        }
#pragma warning restore CA1416
                    }
                    else
                    {
                        // Windows: DataFormat.File → HDROP shell drag.
                        if (xferData.ExtractedPaths.Count > 0)
                            await AddStorageFilesToDataTransfer(data, xferData.ExtractedPaths);
                    }
                }

                // If extraction returned null, signal SetNativeDragPaths so the
                // ExportOnSelectionRequest spin-wait doesn't block indefinitely.
#pragma warning disable CA1416
                if (linuxDragTask != null && xferData == null)
                {
                    mLinuxDrag!.SetNativeDragPaths(Array.Empty<string>(), string.Empty);
                    result = await linuxDragTask;
                }
#pragma warning restore CA1416
            }
            if (!nativeDragStarted)
            {
                result = await DragDrop.DoDragDropAsync(pressArgs, data,
                    DragDropEffects.Copy | DragDropEffects.Move);
                Debug.WriteLine("FL drag complete, effect=" + result);
            }
        }
        catch (Exception ex)
        {
            AppLog.W("File list: drag operation failed", ex);
        }
        finally
        {
            // Always restore cursor on Linux (set early before BuildDragData).
            if (OperatingSystem.IsLinux()) Cursor = null;

            if (!nativeDragStarted)
            {
                EndFileListDragState();
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux native drag: state cleanup is unconditional (nativeDragStarted=true
                // is set before the extraction, so xferData may be null if it failed).
                EndFileListDragState();
            }
        }
    }

    /// <summary>
    /// Ends the file-list drag state: the flag, the dragged entries, and the state other
    /// windows read.
    /// </summary>
    private void EndFileListDragState()
    {
        mIsDraggingFileList = false;
        mDragMoveList.Clear();
        FileListDrag.Current = null;
    }

    /// <summary>
    /// Converts extracted file paths to <see cref="Avalonia.Platform.Storage.IStorageItem"/>
    /// objects and adds them to <paramref name="data"/> as <see cref="DataFormat.File"/> items.
    /// Builds an explicit <c>file://</c> URI for each path so that
    /// <see cref="Avalonia.Platform.Storage.IStorageProvider.TryGetFileFromPathAsync"/> succeeds
    /// on both Unix (where a bare path has no URI scheme) and Windows.
    /// </summary>
    private async System.Threading.Tasks.Task AddStorageFilesToDataTransfer(
        DataTransfer data, IReadOnlyList<string> paths)
    {
        var items = new List<Avalonia.Platform.Storage.IStorageItem>();
        foreach (string path in paths)
        {
            try
            {
                string uriPath = Path.GetFullPath(path).Replace('\\', '/');
                if (!uriPath.StartsWith('/')) uriPath = "/" + uriPath;
                var fileUri = new Uri("file://" + uriPath);
                if (Directory.Exists(path))
                {
                    var folder = await StorageProvider.TryGetFolderFromPathAsync(fileUri);
                    if (folder != null)
                        items.Add(folder);
                    else
                        Debug.WriteLine("TryGetFolderFromPathAsync null for: " + fileUri);
                }
                else
                {
                    var file = await StorageProvider.TryGetFileFromPathAsync(fileUri);
                    if (file != null)
                        items.Add(file);
                    else
                        Debug.WriteLine("TryGetFileFromPathAsync null for: " + fileUri);
                }
            }
            catch (Exception ex)
            {
                AppLog.W("Storage access failed for '" + path + "'", ex);
            }
        }
        foreach (Avalonia.Platform.Storage.IStorageItem item in items)
            data.Add(DataTransferItem.CreateFile(item));
    }

    /// <summary>
    /// Returns true when the drag data contains CP2 JSON from a different process.
    /// Same-process drags are identified by <see cref="mIsDraggingFileList"/> instead.
    /// </summary>
    private static bool IsCrossInstanceCp2Drop(IDataTransfer data)
    {
        // Try our private UTI first (set by Avalonia DoDragDropAsync on non-macOS, and
        // also by CP2PromiseProvider when Avalonia's custom-UTI lookup works).
        string? text = data.TryGetValue(ClipInfo.CrossInstanceDataFormat);

        // Fallback: on macOS the native drag exposes the JSON as DataFormat.Text
        // (public.utf8-plain-text) to guarantee Avalonia's IDataTransfer can surface it.
        if (text == null)
            text = data.TryGetText();

        Debug.WriteLine($"IsCrossInstanceCp2Drop: formats=[{string.Join(",", data.Formats)}]" +
            $" hasJson={ClipInfo.IsClipTextFromCP2(text)}");

        if (!ClipInfo.IsClipTextFromCP2(text)) return false;
        ClipInfo? info = ClipInfo.FromClipString(text);
        return info != null && info.ProcessId != Environment.ProcessId;
    }

    /// <summary>
    /// Returns the volume directory entry for the currently selected filesystem, or
    /// NO_ENTRY if no filesystem is selected.  Used as fallback drop target when
    /// dropping onto empty space in the file list.
    /// </summary>
    private IFileEntry GetCurrentVolumeDirEntry()
    {
        ArchiveTreeItem? arcTreeSel = SelectedArchiveTreeItem;
        if (arcTreeSel?.WorkTreeNode.DAObject is IFileSystem fs)
        {
            return fs.GetVolDirEntry();
        }
        return IFileEntry.NO_ENTRY;
    }

    /// <summary>
    /// Returns the IFileEntry for the currently-displayed directory in the directory tree,
    /// falling back to the volume root. Used as the drop target for "empty space" drops
    /// so that files dropped onto themselves (where hit-testing returns NO_ENTRY) are
    /// filtered out by the ContainingDir check rather than moved to the root.
    /// </summary>
    private IFileEntry GetCurrentDirEntry()
    {
        IFileEntry? dirEntry = ViewModel?.CachedDirectoryTreeSelection?.FileEntry;
        return dirEntry ?? GetCurrentVolumeDirEntry();
    }

    /// <summary>
    /// Resolves the target for file-list empty-space drops.  In full-list mode the empty space
    /// represents the root level; in single-directory mode it represents the active directory.
    /// </summary>
    private IFileEntry GetFileListEmptySpaceTarget()
    {
        if (ViewModel?.ShowSingleDirFileList == true)
        {
            return GetCurrentDirEntry();
        }
        return GetCurrentVolumeDirEntry();
    }

    /// <summary>
    /// Selects the root directory in the directory tree for the current filesystem.
    /// </summary>
    private void SelectVolumeRootDirectory()
    {
        if (ViewModel?.DirectoryTree.TreeRoot.Count > 0)
        {
            ScrollDirectoryTreeToTop();
            SelectDirectoryTreeItem(ViewModel.DirectoryTree.TreeRoot[0]);
        }
    }

    /// <summary>
    /// Resolves an external drop target from a file-list row.  Dropping onto a file targets the
    /// directory that contains the file; dropping onto a directory targets the directory itself;
    /// dropping onto empty space uses the mode-dependent default.
    /// </summary>
    private IFileEntry ResolveExternalFileListDropTarget(IFileEntry dropTarget)
    {
        if (dropTarget == IFileEntry.NO_ENTRY)
        {
            return GetFileListEmptySpaceTarget();
        }
        if (dropTarget.IsDirectory)
        {
            return dropTarget;
        }
        if (dropTarget.ContainingDir != IFileEntry.NO_ENTRY)
        {
            return dropTarget.ContainingDir;
        }
        return GetCurrentVolumeDirEntry();
    }

    // ---- Drag-drop on file list panel (empty space below rows) ----

    private void FileListPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(fileListPanel);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual visual &&
                (visual == fileListDataGrid || visual.FindAncestorOfType<DataGrid>() == fileListDataGrid))
        {
            return;
        }

        if (ViewModel?.ShowSingleDirFileList != true)
        {
            SelectVolumeRootDirectory();
        }
    }

    private void FileListPanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;     // Already handled by the DataGrid.
        }
        // Same logic as FileListDataGrid_DragOver — accept drops on empty space.
        if (mIsDraggingFileList)
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            bool canAdd = ViewModel != null && ViewModel.IsFileOpen && ViewModel.CanWrite
                && ViewModel.IsMultiFileItemSelected && ViewModel!.ShowCenterFileList;
            e.DragEffects = canAdd ? DragDropEffects.Copy : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FileListPanel_Drop(object? sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;     // Already handled by the DataGrid.
        }
        IFileEntry curDir = GetFileListEmptySpaceTarget();
        if (mIsDraggingFileList)
        {
            if (curDir != IFileEntry.NO_ENTRY)
            {
                if (ViewModel?.ShowSingleDirFileList != true)
                {
                    SelectVolumeRootDirectory();
                }
                // Filter out items already in the target directory (handles self-drop).
                var itemsToMove = mDragMoveList
                    .Where(e => e.ContainingDir != curDir)
                    .ToList();
                if (itemsToMove.Count > 0)
                {
                    _ = ViewModel!.MoveFiles(itemsToMove, curDir);
                }
            }
        }
        else if (IsCrossInstanceCp2Drop(e.DataTransfer))
        {
            if (ViewModel?.ShowSingleDirFileList != true)
            {
                SelectVolumeRootDirectory();
            }
            _ = ViewModel!.PasteOrDrop(e.DataTransfer, curDir);
        }
        else if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files != null)
            {
                string?[] rawPaths = files.Select(f => f.TryGetLocalPath()).ToArray();
                string[] paths = Array.FindAll(rawPaths, p => p != null)!;
                if (paths.Length > 0)
                {
                    if (ViewModel?.ShowSingleDirFileList != true)
                    {
                        SelectVolumeRootDirectory();
                    }
                    _ = ViewModel!.AddFileDrop(curDir, paths);
                }
            }
        }
    }

    // ---- Drag-drop on directory tree ----

    private void DirectoryTree_DragOver(object? sender, DragEventArgs e)
    {
        if (!ViewModel!.CanWrite)
        {
            e.DragEffects = DragDropEffects.None;
        }
        else if (mIsDraggingFileList)
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            // Accept any external drag when writable — see FileListDataGrid_DragOver.
            e.DragEffects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void DirectoryTree_Drop(object? sender, DragEventArgs e)
    {
        // Walk up from the event source to find a DirectoryTreeItem DataContext.
        DirectoryTreeItem? dti = null;
        if (e.Source is StyledElement se)
        {
            var cur = se;
            while (cur != null)
            {
                if (cur.DataContext is DirectoryTreeItem item)
                {
                    dti = item;
                    break;
                }
                cur = cur.Parent;
            }
        }
        if (dti == null)
        {
            Debug.WriteLine("DT drop outside tree item, ignoring");
            return;
        }
        IFileEntry dropTarget = dti.FileEntry;
        Debug.WriteLine("DT drop on item=" + dropTarget);
        if (mIsDraggingFileList)
        {
            // Copy the list — see FileListDataGrid_Drop comment about race condition.
            _ = ViewModel!.MoveFiles(new List<IFileEntry>(mDragMoveList), dropTarget);
        }
        else if (IsCrossInstanceCp2Drop(e.DataTransfer))
        {
            _ = ViewModel!.PasteOrDrop(e.DataTransfer, dropTarget);
        }
        else if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files != null)
            {
                string?[] rawPaths = files.Select(f => f.TryGetLocalPath()).ToArray();
                string[] paths = Array.FindAll(rawPaths, p => p != null)!;
                if (paths.Length > 0)
                {
                    _ = ViewModel!.AddFileDrop(dropTarget, paths);
                }
            }
        }
        else
        {
            Debug.WriteLine("DT no valid drop");
        }
    }
}
