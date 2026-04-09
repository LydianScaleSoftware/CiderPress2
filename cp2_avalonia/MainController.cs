/*
 * Copyright 2023 faddenSoft
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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

using AppCommon;
using CommonUtil;
using cp2_avalonia.Actions;
using cp2_avalonia.Common;
using DiskArc;
using DiskArc.Arc;
using DiskArc.FS;
using DiskArc.Multi;
using FileConv;
using static DiskArc.Defs;

namespace cp2_avalonia {
    /// <summary>
    /// Main GUI controller.  Ported from cp2_wpf/MainController.cs.
    /// Only the file open/close portions are implemented in Iteration 3; the rest is stubbed.
    /// </summary>
    public partial class MainController {
        private const string FILE_ERR_CAPTION = "File error";

        private MainWindow mMainWin;

        private DebugMessageLog mDebugLog;
        public AppHook AppHook { get; private set; }

        private Tools.LogViewer? mDebugLogViewer;
        public bool IsDebugLogOpen => mDebugLogViewer != null;

        private string mWorkPathName = string.Empty;
        private WorkTree? mWorkTree = null;
        public Formatter mFormatter;

        /// <summary>
        /// True when a work file is open.
        /// </summary>
        public bool IsFileOpen { get { return mWorkTree != null; } }

        /// <summary>
        /// List of recently-opened files.
        /// </summary>
        public List<string> RecentFilePaths = new List<string>(MAX_RECENT_FILES);
        public const int MAX_RECENT_FILES = 6;

        /// <summary>
        /// Auto-open behavior.
        /// </summary>
        public enum AutoOpenDepth { Unknown = 0, Shallow, SubVol, Max }


        public MainController(MainWindow mainWin) {
            mMainWin = mainWin;

            Formatter.FormatConfig cfg = new Formatter.FormatConfig();
            mFormatter = new Formatter(cfg);

            mDebugLog = new DebugMessageLog();
            AppHook = new AppHook(mDebugLog);
        }

        /// <summary>
        /// Performs initialization after the window has appeared.
        /// </summary>
        public void WindowLoaded() {
            AppHook.LogI("--- running unit tests ---");
            Debug.Assert(RangeSet.Test());
            Debug.Assert(CommonUtil.Version.Test());
            Debug.Assert(CircularBitBuffer.DebugTest());
            Debug.Assert(Glob.DebugTest());
            Debug.Assert(PathName.DebugTest());
            Debug.Assert(TimeStamp.DebugTestDates());
            Debug.Assert(DiskArc.Disk.TrackInit.DebugCheckInterleave());
            Debug.Assert(DiskArc.Disk.Wozoof_Meta.DebugTest());
            AppHook.LogI("--- unit tests complete ---");

            LoadAppSettings();
            ApplyAppSettings();

            UpdateTitle();
            UpdateRecentLinks();

            // TODO: process command-line arguments (port from WPF Iteration N)
        }

        /// <summary>
        /// Performs cleanup when the window is closing.
        /// </summary>
        public void WindowClosing() {
            mDebugLogViewer?.Close();
            CleanupClipTemp();
            // Force dirty so window placement is always persisted, even if no other
            // settings changed during this session.
            AppSettings.Global.IsDirty = true;
            SaveAppSettings();
        }

        /// <summary>
        /// Handles File → Open.  async because the file picker is async in Avalonia.
        /// </summary>
        public async Task OpenWorkFile() {
            if (!CloseWorkFile()) {
                return;
            }
            string? pathName = await PlatformUtil.AskFileToOpen(mMainWin);
            if (pathName == null) {
                return;
            }
            await DoOpenWorkFile(pathName, false);
        }

        /// <summary>
        /// Handles file-drop open on the launch panel.
        /// </summary>
        public async Task DropOpenWorkFile(string pathName) {
            if (!CloseWorkFile()) {
                return;
            }
            await DoOpenWorkFile(pathName, false);
        }

        private async Task DoOpenWorkFile(string pathName, bool asReadOnly) {
            Debug.Assert(mWorkTree == null);
            if (!File.Exists(pathName)) {
                ShowFileError("File not found: '" + pathName + "'");
                return;
            }
            AppHook.LogI("Opening work file '" + pathName + "' readOnly=" + asReadOnly);

            AutoOpenDepth depth =
                AppSettings.Global.GetEnum(AppSettings.AUTO_OPEN_DEPTH, AutoOpenDepth.SubVol);
            WorkTree.DepthLimiter limiter =
                delegate (WorkTree.DepthParentKind parentKind, WorkTree.DepthChildKind childKind) {
                    return DepthLimit(parentKind, childKind, depth);
                };

            try {
                mMainWin.Cursor = new Cursor(StandardCursorType.Wait);

                OpenProgress prog = new OpenProgress(pathName, limiter, asReadOnly, AppHook);
                WorkProgress workDialog = new WorkProgress(mMainWin, prog, true);
                await workDialog.ShowDialog(mMainWin);
                // ShowDialog returns when the work is done (WorkProgress.Close() is called).
                if (!workDialog.DialogResult) {
                    if (prog.Results.mException != null) {
                        ShowFileError("Error: " + prog.Results.mException.Message);
                    }
                    return;
                }
                Debug.Assert(prog.Results.mWorkTree != null);
                mWorkTree = prog.Results.mWorkTree;

                PopulateArchiveTree();
            } catch (Exception ex) {
                ShowFileError("Unable to open file: " + ex.Message);
                return;
            } finally {
                mMainWin.Cursor = null;
            }
            AppHook.LogI("Load of '" + pathName + "' completed");

            mWorkPathName = pathName;
            UpdateTitle();
            UpdateRecentFilesList(pathName);
            mMainWin.LaunchPanelVisible = false;
            mMainWin.MainPanelVisible = true;
        }

        /// <summary>
        /// Handles File → Close.
        /// </summary>
        /// <returns>True if the file was closed, false if the operation was cancelled.</returns>
        public bool CloseWorkFile() {
            if (mWorkTree == null) {
                return true;
            }
            Debug.WriteLine("Closing " + mWorkPathName);

            mMainWin.ClearTreesAndLists();
            mWorkTree.Dispose();
            mWorkTree = null;
            CachedDirectoryTreeSelection = null;
            CachedArchiveTreeSelection = null;

            UpdateTitle();
            ClearEntryCounts();
            mMainWin.LaunchPanelVisible = true;
            mMainWin.MainPanelVisible = false;

            ClearClipboardIfPending();

            SaveAppSettings();

            GC.Collect();
            return true;
        }

        /// <summary>
        /// Navigates to the parent node. Consults the directory tree first; if already at the
        /// volume root and dirOnly is false, navigates up in the archive tree instead.
        /// </summary>
        public void NavToParent(bool dirOnly) {
            // Step 1: try to go up in the directory tree.
            DirectoryTreeItem? dirSel = CachedDirectoryTreeSelection;
            if (dirSel != null && dirSel.Parent != null) {
                DirectoryTreeItem.BringItemIntoView(mMainWin.directoryTree, dirSel.Parent);
                mMainWin.directoryTree.SelectedItem = dirSel.Parent;
                mMainWin.directoryTree.Focus();
                return;
            }

            // Step 2: at directory root (or no directory tree). If dirOnly, stop here.
            if (dirOnly) {
                return;
            }

            // Step 3: try to go up in the archive tree.
            ArchiveTreeItem? arcSel = CachedArchiveTreeSelection;
            if (arcSel != null && arcSel.Parent != null) {
                ArchiveTreeItem.BringItemIntoView(mMainWin.archiveTree, arcSel.Parent);
                mMainWin.archiveTree.SelectedItem = arcSel.Parent;
                mMainWin.archiveTree.Focus();
            }
        }

        /// <summary>
        /// Populates the archive tree from the work tree.
        /// </summary>
        private bool PopulateArchiveTree() {
            Debug.Assert(mWorkTree != null);

            ObservableCollection<ArchiveTreeItem> tvRoot = mMainWin.ArchiveTreeRoot;
            Debug.Assert(tvRoot.Count == 0);

            AppHook.LogI("Constructing archive trees...");
            DateTime startWhen = DateTime.Now;

            ArchiveTreeItem.ConstructTree(tvRoot, mWorkTree.RootNode);

            AppHook.LogI("Finished archive tree construction in " +
                (DateTime.Now - startWhen).TotalMilliseconds + " ms");

            ArchiveTreeItem.SelectBestFrom(mMainWin.archiveTree, tvRoot[0]);
            return true;
        }

        /// <summary>
        /// Determines the auto-open depth limit for a given parent/child kind pair.
        /// </summary>
        private static bool DepthLimit(WorkTree.DepthParentKind parentKind,
                WorkTree.DepthChildKind childKind, AutoOpenDepth depth) {
            if (depth == AutoOpenDepth.Max) {
                return true;
            }
            if (depth == AutoOpenDepth.Shallow) {
                return false;
            }
            // AutoOpenDepth.SubVol: open sub-volumes but not file archives.
            Debug.Assert(depth == AutoOpenDepth.SubVol);
            return childKind != WorkTree.DepthChildKind.FileArchive;
        }

        /// <summary>
        /// Updates the main window title to reflect the open file.
        /// </summary>
        private void UpdateTitle() {
            StringBuilder sb = new StringBuilder();
            if (mWorkTree != null) {
                string fileName = Path.GetFileName(mWorkPathName);
                if (string.IsNullOrEmpty(fileName)) {
                    fileName = mWorkPathName;
                }
                sb.Append(fileName);
                if (!mWorkTree.CanWrite) {
                    sb.Append(" *READONLY*");
                }
                sb.Append(" - ");
            }
            sb.Append("CiderPress II");
            mMainWin.Title = sb.ToString();
        }

        /// <summary>
        /// Ensures the named file is at the top of the recent files list.
        /// </summary>
        private void UpdateRecentFilesList(string pathName) {
            if (string.IsNullOrEmpty(pathName)) {
                return;
            }
            int index = RecentFilePaths.IndexOf(pathName);
            if (index == 0) {
                return;
            }
            if (index > 0) {
                RecentFilePaths.RemoveAt(index);
            }
            RecentFilePaths.Insert(0, pathName);
            while (RecentFilePaths.Count > MAX_RECENT_FILES) {
                RecentFilePaths.RemoveAt(MAX_RECENT_FILES);
            }

            string cereal = JsonSerializer.Serialize(RecentFilePaths, typeof(List<string>));
            AppSettings.Global.SetString(AppSettings.RECENT_FILES_LIST, cereal);

            UpdateRecentLinks();
        }

        /// <summary>
        /// Unpacks the recent files list from application settings.
        /// </summary>
        private void UnpackRecentFileList() {
            RecentFilePaths.Clear();
            string cereal = AppSettings.Global.GetString(AppSettings.RECENT_FILES_LIST, "");
            if (string.IsNullOrEmpty(cereal)) {
                return;
            }
            try {
                object? parsed = JsonSerializer.Deserialize<List<string>>(cereal);
                if (parsed != null) {
                    RecentFilePaths = (List<string>)parsed;
                }
            } catch (Exception ex) {
                Debug.WriteLine("Failed deserializing recent projects: " + ex.Message);
            }
        }

        /// <summary>
        /// Propagates the recent file list to the main window's binding properties.
        /// </summary>
        private void UpdateRecentLinks() {
            string path1 = RecentFilePaths.Count >= 1 ? RecentFilePaths[0] : string.Empty;
            string path2 = RecentFilePaths.Count >= 2 ? RecentFilePaths[1] : string.Empty;
            mMainWin.RecentFilePath1 = path1;
            mMainWin.RecentFileName1 = string.IsNullOrEmpty(path1) ? string.Empty :
                Path.GetFileName(path1);
            mMainWin.RecentFilePath2 = path2;
            mMainWin.RecentFileName2 = string.IsNullOrEmpty(path2) ? string.Empty :
                Path.GetFileName(path2);
            mMainWin.PopulateRecentFilesMenu(RecentFilePaths);
        }

        /// <summary>
        /// Opens a recent file by index (0 = most recent).
        /// </summary>
        public async Task OpenRecentFile(int recentIndex) {
            if (recentIndex >= RecentFilePaths.Count) {
                return;
            }
            if (!CloseWorkFile()) {
                return;
            }
            await DoOpenWorkFile(RecentFilePaths[recentIndex], false);
        }

        // -----------------------------------------------------------------------------------------
        // Settings

        private void LoadAppSettings() {
            SettingsHolder settings = AppSettings.Global;
            settings.SetBool(AppSettings.MAC_ZIP_ENABLED, true);
            settings.SetEnum(AppSettings.AUTO_OPEN_DEPTH, AutoOpenDepth.SubVol);

            settings.SetBool(AppSettings.ADD_RECURSE_ENABLED, true);
            settings.SetBool(AppSettings.ADD_COMPRESS_ENABLED, true);
            settings.SetBool(AppSettings.ADD_STRIP_PATHS_ENABLED, false);
            settings.SetBool(AppSettings.ADD_STRIP_EXT_ENABLED, true);
            settings.SetBool(AppSettings.ADD_RAW_ENABLED, false);
            settings.SetBool(AppSettings.ADD_PRESERVE_ADF, true);
            settings.SetBool(AppSettings.ADD_PRESERVE_AS, true);
            settings.SetBool(AppSettings.ADD_PRESERVE_NAPS, true);
            settings.SetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.NAPS);
            settings.SetBool(AppSettings.EXT_RAW_ENABLED, false);
            settings.SetBool(AppSettings.EXT_STRIP_PATHS_ENABLED, false);

            settings.SetString(AppSettings.CONV_IMPORT_TAG, "text");

            // Load settings from file and merge them in.
            string settingsPath =
                Path.Combine(PlatformUtil.GetRuntimeDataDir(), AppSettings.SETTINGS_FILE_NAME);
            try {
                string text = File.ReadAllText(settingsPath);
                SettingsHolder? fileSettings = SettingsHolder.Deserialize(text);
                if (fileSettings != null) {
                    AppSettings.Global.MergeSettings(fileSettings);
                }
                Debug.WriteLine("Settings file loaded and merged");
            } catch (Exception ex) {
                Debug.WriteLine("Unable to read settings file: " + ex.Message);
            }
        }

        private void SaveAppSettings() {
            SettingsHolder settings = AppSettings.Global;
            if (!settings.IsDirty) {
                Debug.WriteLine("Settings not dirty, not saving");
                return;
            }

            // Save window placement.
            settings.SetString(AppSettings.MAIN_WINDOW_PLACEMENT,
                WindowPlacement.Save(mMainWin));

            // TODO: port when left/right panel width properties are added in Iteration N
            settings.SetInt(AppSettings.MAIN_LEFT_PANEL_WIDTH, (int)mMainWin.LeftPanelWidth);
            // settings.SetBool(AppSettings.MAIN_RIGHT_PANEL_VISIBLE, mMainWin.ShowOptionsPanel);

            string settingsPath =
                Path.Combine(PlatformUtil.GetRuntimeDataDir(), AppSettings.SETTINGS_FILE_NAME);
            try {
                string cereal = settings.Serialize();
                File.WriteAllText(settingsPath, cereal);
                settings.IsDirty = false;
                Debug.WriteLine("Saved settings to '" + settingsPath + "'");
            } catch (Exception ex) {
                Debug.WriteLine("Failed to save settings: " + ex.Message);
            }
        }

        private void ApplyAppSettings() {
            Debug.WriteLine("Applying app settings...");
            SettingsHolder settings = AppSettings.Global;

            // In debug builds the menu is enabled by default so it's always visible during
            // development; release builds require it to be explicitly enabled in settings.
#if DEBUG
            mMainWin.ShowDebugMenu = settings.GetBool(AppSettings.DEBUG_MENU_ENABLED, true);
#else
            mMainWin.ShowDebugMenu = settings.GetBool(AppSettings.DEBUG_MENU_ENABLED, false);
#endif

            // Restore window position, size, and state from the previous session.
            string? placement = settings.GetString(AppSettings.MAIN_WINDOW_PLACEMENT, "");
            if (!string.IsNullOrEmpty(placement)) {
                WindowPlacement.Restore(mMainWin, placement);
            }

            // Restore left panel width; setting a fixed pixel value means only the center
            // column stretches when the window is resized (right panel is Width=Auto).
            mMainWin.LeftPanelWidth =
                settings.GetInt(AppSettings.MAIN_LEFT_PANEL_WIDTH, 300);

            UnpackRecentFileList();

            if (mWorkTree != null) {
                RefreshDirAndFileList();
            }
            AppHook.SetOptionEnum(DAAppHook.AUDIO_DEC_ALG,
                settings.GetEnum(AppSettings.AUDIO_DECODE_ALG,
                    CassetteDecoder.Algorithm.ZeroCross));
        }

        // -----------------------------------------------------------------------------------------
        // Error helpers

        private void ShowFileError(string msg) {
            Debug.WriteLine("ShowFileError: " + msg);
            AppHook.LogE("ShowFileError: " + msg);
            // Show error dialog fire-and-forget (callers may be in sync context).
            _ = ShowMessageAsync(msg, "Error");
        }

        // -----------------------------------------------------------------------------------------
        // Selection helpers

        /// <summary>
        /// Gets the currently selected archive or filesystem and directory entry.
        /// </summary>
        /// <param name="archiveOrFileSystem">Result: IArchive or IFileSystem, or null.</param>
        /// <param name="daNode">Result: DiskArcNode for the selected item.</param>
        /// <param name="selectionDir">Result: selected directory entry, or NO_ENTRY.</param>
        /// <returns>True on success.</returns>
        public bool GetSelectedArcDir([System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
                out object? archiveOrFileSystem,
                [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DiskArcNode? daNode,
                out IFileEntry selectionDir) {
            archiveOrFileSystem = null;
            daNode = null;
            selectionDir = IFileEntry.NO_ENTRY;

            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            DirectoryTreeItem? dirTreeSel = mMainWin.SelectedDirectoryTreeItem;
            if (arcTreeSel == null || dirTreeSel == null) {
                Debug.WriteLine("Current selection is not archive or filesystem");
                return false;
            }

            object? arcObj = arcTreeSel.WorkTreeNode.DAObject;
            if (arcObj == null) {
                Debug.WriteLine("DAObject was null");
                return false;
            } else if (arcObj is not IArchive && arcObj is not IFileSystem) {
                Debug.WriteLine("Unexpected DAObject type: " + arcObj.GetType());
                return false;
            }
            archiveOrFileSystem = arcObj;
            selectionDir = dirTreeSel.FileEntry;

            daNode = arcTreeSel.WorkTreeNode.FindDANode();
            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Create Directory

        /// <summary>
        /// Handles Actions → Create Directory.
        /// </summary>
        public async Task CreateDirectory() {
            if (!GetSelectedArcDir(out object? archiveOrFileSystem, out DiskArcNode? _,
                    out IFileEntry targetDir)) {
                return;
            }
            IFileSystem? fs = archiveOrFileSystem as IFileSystem;
            if (fs == null) {
                Debug.Assert(false);
                return;
            }

            string rules = "\u2022 " + fs.Characteristics.FileNameSyntaxRules;
            CreateDirectory dialog = new CreateDirectory(mMainWin, fs, targetDir,
                fs.IsValidFileName, rules);
            if (await dialog.ShowDialog<bool?>(mMainWin) != true) {
                return;
            }

            try {
                IFileEntry newEntry = fs.CreateFile(targetDir, dialog.NewFileName,
                    IFileSystem.CreateMode.Directory);
                RefreshDirAndFileList();
                FileListItem.SetSelectionFocusByEntry(mMainWin.FileList,
                    mMainWin.fileListDataGrid, newEntry);
            } catch (Exception ex) {
                AppHook.LogE("CreateDirectory failed: " + ex.Message);
                ShowFileError("Failed to create directory: " + ex.Message);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Delete Files

        /// <summary>
        /// Handles Actions → Delete Files.
        /// </summary>
        public async Task DeleteFiles() {
            if (!GetSelectedArcDir(out object? archiveOrFileSystem, out DiskArcNode? daNode,
                    out IFileEntry unused)) {
                return;
            }

            if (!GetFileSelection(omitDir: false, omitOpenArc: false, closeOpenArc: true,
                    oneMeansAll: false, out archiveOrFileSystem, out IFileEntry unusedDir,
                    out List<IFileEntry>? selected, out int firstSelIndex)) {
                return;
            }
            if (selected.Count == 0) {
                await ShowMessageAsync("No files selected.", "Empty");
                return;
            }

            // We can't undo it, so get confirmation first.
            string msg = string.Format("Delete {0} file {1}?", selected.Count,
                selected.Count == 1 ? "entry" : "entries");
            if (!await ShowConfirmAsync(msg, "Confirm Delete")) {
                return;
            }

            SettingsHolder settings = AppSettings.Global;
            DeleteProgress prog = new DeleteProgress(archiveOrFileSystem, daNode, selected,
                    AppHook) {
                DoCompress = settings.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true),
                EnableMacOSZip = settings.GetBool(AppSettings.MAC_ZIP_ENABLED, true),
            };

            WorkProgress workDialog = new WorkProgress(mMainWin, prog, false);
            await workDialog.ShowDialog(mMainWin);
            bool didCancel = !workDialog.DialogResult;
            if (!didCancel) {
                mMainWin.PostNotification("Deletion successful", true);
            } else {
                mMainWin.PostNotification("Cancelled", false);
            }

            if (!(didCancel && archiveOrFileSystem is IArchive)) {
                // Put the selection on the item above the first one we deleted.
                int selectIdx = Math.Max(0, Math.Min(firstSelIndex - 1,
                    mMainWin.FileList.Count - 1));
                if (mMainWin.FileList.Count > 0) {
                    mMainWin.SelectedFileListItem = mMainWin.FileList[selectIdx];
                }
            }

            RefreshDirAndFileList();
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Test Files

        /// <summary>
        /// Handles Actions → Test Files.
        /// </summary>
        public async Task TestFiles() {
            if (!GetFileSelection(omitDir: false, omitOpenArc: false, closeOpenArc: true,
                    oneMeansAll: false, out object? archiveOrFileSystem,
                    out IFileEntry unusedDir, out List<IFileEntry>? selected, out int unused)) {
                return;
            }
            if (selected.Count == 0) {
                await ShowMessageAsync("No files selected.", "Empty");
                return;
            }

            Actions.TestProgress prog = new Actions.TestProgress(archiveOrFileSystem, selected,
                    AppHook) {
                EnableMacOSZip = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true),
            };
            WorkProgress workDialog = new WorkProgress(mMainWin, prog, false);
            await workDialog.ShowDialog(mMainWin);
            if (workDialog.DialogResult) {
                List<Actions.TestProgress.Failure>? results = prog.FailureResults!;
                if (results.Count != 0) {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("Failures: ");
                    sb.AppendLine(results.Count.ToString());
                    sb.AppendLine();
                    foreach (Actions.TestProgress.Failure failure in results) {
                        sb.Append(failure.Entry.FullPathName);
                        sb.Append(" (");
                        sb.Append(failure.Part);
                        sb.AppendLine(")");
                    }
                    Tools.ShowText reportDialog = new Tools.ShowText(mMainWin, sb.ToString());
                    reportDialog.Title = "Failures";
                    reportDialog.Show();
                } else {
                    mMainWin.PostNotification("Tests successful, no failures", true);
                }
            } else {
                mMainWin.PostNotification("Cancelled", false);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Move Files (called from drag-drop, no menu command)

        /// <summary>
        /// Moves files to a new directory.  Called from drag-drop handlers (Iteration 13).
        /// </summary>
        public async Task MoveFiles(List<IFileEntry> moveList, IFileEntry targetDir) {
            if (CurrentWorkObject is not IFileSystem) {
                Debug.WriteLine("Ignoring move request in " + CurrentWorkObject);
                return;
            }
            if (targetDir == IFileEntry.NO_ENTRY) {
                Debug.Assert(false, "Drag target is NO_FILE");
                return;
            }
            if (!CanWrite) {
                await ShowMessageAsync("Drop target is not writable.", "Not Writable");
                return;
            }
            if (targetDir.IsDubious || targetDir.IsDamaged) {
                await ShowMessageAsync("Destination directory is not writable.", "Not Writable");
                return;
            }
            if (!targetDir.IsDirectory) {
                Debug.Assert(false, "bad move request");
                return;
            }

            // Screen out invalid and no-op requests.
            for (int i = moveList.Count - 1; i >= 0; i--) {
                IFileEntry entry = moveList[i];
                if (entry.ContainingDir == targetDir) {
                    Debug.WriteLine("- ignoring non-move " + entry + " -> " + targetDir);
                    moveList.RemoveAt(i);
                    continue;
                }
                if (entry.IsDirectory) {
                    IFileEntry checkEnt = targetDir;
                    while (checkEnt != IFileEntry.NO_ENTRY) {
                        if (checkEnt == entry) {
                            await ShowMessageAsync(
                                "Cannot move a directory into itself or a descendant.",
                                "Bad Move");
                            return;
                        }
                        checkEnt = checkEnt.ContainingDir;
                    }
                }
            }
            if (moveList.Count == 0) {
                Debug.WriteLine("Nothing to move");
                return;
            }

            if (!GetSelectedArcDir(out object? archiveOrFileSystem, out DiskArcNode? daNode,
                    out IFileEntry unused)) {
                Debug.Assert(false);
                return;
            }

            SettingsHolder settings = AppSettings.Global;
            MoveProgress prog = new MoveProgress(CurrentWorkObject, daNode, moveList, targetDir,
                    AppHook) {
                DoCompress = settings.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true),
            };

            WorkProgress workDialog = new WorkProgress(mMainWin, prog, false);
            await workDialog.ShowDialog(mMainWin);
            if (workDialog.DialogResult) {
                mMainWin.PostNotification("Move successful", true);
            } else {
                mMainWin.PostNotification("Cancelled", false);
            }

            // Clear selection since file list items' pathnames have changed.
            mMainWin.fileListDataGrid.SelectedItems.Clear();

            // Check whether any moved entry is a directory.  If so, we need to rebuild
            // ALL FileListItems because child entries cache their PathName at construction
            // and moving a directory changes the FullPathName of every descendant.
            bool movedDirectory = false;
            foreach (IFileEntry entry in moveList) {
                if (entry.IsDirectory) {
                    movedDirectory = true;
                    break;
                }
            }

            if (movedDirectory) {
                // Rebuild every item in the file list so that descendant paths are
                // refreshed.  Track moved entries so we can re-select them.
                ObservableCollection<FileListItem> fileList = mMainWin.FileList;
                HashSet<IFileEntry> movedSet = new HashSet<IFileEntry>(moveList);
                for (int i = 0; i < fileList.Count; i++) {
                    FileListItem old = fileList[i];
                    FileListItem rebuilt = new FileListItem(old.FileEntry, mFormatter);
                    fileList[i] = rebuilt;
                    if (movedSet.Contains(old.FileEntry)) {
                        mMainWin.fileListDataGrid.SelectedItems.Add(rebuilt);
                    }
                }
            } else {
                // Only files were moved; regenerate just the affected items.
                foreach (IFileEntry entry in moveList) {
                    FileListItem? item = FileListItem.FindItemByEntry(mMainWin.FileList, entry,
                            out int itemIndex);
                    if (item == null) {
                        Debug.Assert(false, "unable to find entry: " + entry);
                        continue;
                    }
                    FileListItem newItem = new FileListItem(entry, mFormatter);
                    mMainWin.FileList.RemoveAt(itemIndex);
                    mMainWin.FileList.Insert(itemIndex, newItem);
                    mMainWin.fileListDataGrid.SelectedItems.Add(newItem);
                }
            }

            RefreshDirAndFileList();
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Edit Attributes / Edit Directory Attributes

        /// <summary>
        /// Handles Actions → Edit Attributes (file list selection).
        /// </summary>
        public async Task EditAttributes() {
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            FileListItem? fileItem = mMainWin.SelectedFileListItem;
            if (arcTreeSel == null || fileItem == null) {
                Debug.Assert(false);
                return;
            }

            DirectoryTreeItem? dirTreeItem = null;
            if (fileItem.FileEntry.IsDirectory) {
                dirTreeItem = DirectoryTreeItem.FindItemByEntry(mMainWin.DirectoryTreeRoot,
                    fileItem.FileEntry);
            }

            await EditAttributesImpl(arcTreeSel.WorkTreeNode, fileItem.FileEntry,
                dirTreeItem, fileItem);
        }

        /// <summary>
        /// Handles Actions → Edit Directory Attributes (directory tree selection).
        /// </summary>
        public async Task EditDirAttributes() {
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            DirectoryTreeItem? dirTreeSel = mMainWin.SelectedDirectoryTreeItem;
            if (arcTreeSel == null || dirTreeSel == null) {
                Debug.Assert(false);
                return;
            }

            FileListItem? fileItem =
                FileListItem.FindItemByEntry(mMainWin.FileList, dirTreeSel.FileEntry);
            await EditAttributesImpl(arcTreeSel.WorkTreeNode, dirTreeSel.FileEntry,
                dirTreeSel, fileItem);
        }

        private async Task EditAttributesImpl(WorkTree.Node workNode, IFileEntry entry,
                DirectoryTreeItem? dirItem, FileListItem? fileItem) {
            Debug.WriteLine("EditAttributes: " + entry);
            object archiveOrFileSystem = workNode.DAObject;

            bool isMacZip = false;
            bool isMacZipEnabled = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
            IFileEntry adfEntry = IFileEntry.NO_ENTRY;
            IFileEntry adfArchiveEntry = IFileEntry.NO_ENTRY;
            FileAttribs curAttribs = new FileAttribs(entry);

            if (isMacZipEnabled && workNode.DAObject is Zip &&
                    Zip.HasMacZipHeader((Zip)workNode.DAObject, entry, out adfEntry)) {
                Zip arc = (Zip)workNode.DAObject;
                // Must create dialog inside the using blocks so adfArchiveEntry remains valid.
                using (Stream adfStream = ArcTemp.ExtractToTemp(arc, adfEntry,
                        DiskArc.Defs.FilePart.DataFork)) {
                    using (IArchive adfArchive = AppleSingle.OpenArchive(adfStream, AppHook)) {
                        adfArchiveEntry = adfArchive.GetFirstEntry();
                        curAttribs.GetFromAppleSingle(adfArchiveEntry);
                        curAttribs.FullPathName = entry.FullPathName;
                        isMacZip = true;

                        bool isReadOnly;
                        if (archiveOrFileSystem is IFileSystem fs) {
                            isReadOnly = fs.IsReadOnly;
                        } else if (archiveOrFileSystem is IArchive ia) {
                            isReadOnly = ia.IsReadOnly;
                        } else {
                            isReadOnly = false;
                        }

                        EditAttributes dialog = new EditAttributes(mMainWin, archiveOrFileSystem,
                            entry, adfArchiveEntry, curAttribs, isReadOnly);
                        if (await dialog.ShowDialog<bool?>(mMainWin) != true) {
                            return;
                        }

                        SettingsHolder settings = AppSettings.Global;
                        EditAttributesProgress prog = new EditAttributesProgress(mMainWin,
                                workNode.DAObject, workNode.FindDANode(), entry, adfEntry,
                                dialog.NewAttribs, AppHook) {
                            DoCompress = settings.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true),
                            EnableMacOSZip = isMacZipEnabled,
                        };

                        if (prog.DoUpdate(isMacZip)) {
                            mMainWin.PostNotification("File attributes edited", true);
                            FinishEditAttributes(entry, adfEntry, dialog.NewAttribs,
                                isMacZip, fileItem, dirItem, archiveOrFileSystem);
                        }
                    }
                }
                RefreshDirAndFileList();
                return;
            }

            // Non-MacZip path.
            if (!isMacZip) {
                bool isReadOnly;
                if (archiveOrFileSystem is IFileSystem fs2) {
                    isReadOnly = fs2.IsReadOnly;
                } else if (archiveOrFileSystem is IArchive ia2) {
                    isReadOnly = ia2.IsReadOnly;
                } else {
                    isReadOnly = false;
                }

                EditAttributes dialog2 = new EditAttributes(mMainWin, archiveOrFileSystem,
                    entry, IFileEntry.NO_ENTRY, curAttribs, isReadOnly);
                if (await dialog2.ShowDialog<bool?>(mMainWin) != true) {
                    return;
                }

                SettingsHolder settings2 = AppSettings.Global;
                EditAttributesProgress prog2 = new EditAttributesProgress(mMainWin,
                        workNode.DAObject, workNode.FindDANode(), entry, IFileEntry.NO_ENTRY,
                        dialog2.NewAttribs, AppHook) {
                    DoCompress = settings2.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true),
                    EnableMacOSZip = isMacZipEnabled,
                };

                if (prog2.DoUpdate(false)) {
                    mMainWin.PostNotification("File attributes edited", true);
                    FinishEditAttributes(entry, IFileEntry.NO_ENTRY, dialog2.NewAttribs,
                        false, fileItem, dirItem, archiveOrFileSystem);
                }
            }

            RefreshDirAndFileList();
        }

        private void FinishEditAttributes(IFileEntry entry, IFileEntry adfEntry,
                FileAttribs newAttribs, bool isMacZip, FileListItem? fileItem,
                DirectoryTreeItem? dirItem, object archiveOrFileSystem) {
            if (fileItem != null) {
                FileListItem newFli;
                if (isMacZip) {
                    newFli = new FileListItem(entry, adfEntry, newAttribs, mFormatter);
                } else {
                    newFli = new FileListItem(entry, mFormatter);
                }
                int index = mMainWin.FileList.IndexOf(fileItem);
                if (index >= 0) {
                    mMainWin.FileList[index] = newFli;
                    mMainWin.SelectedFileListItem = newFli;
                }

                ArchiveTreeItem? ati =
                    ArchiveTreeItem.FindItemByEntry(mMainWin.ArchiveTreeRoot, entry);
                if (ati != null) {
                    ati.Name = entry.FileName;
                }
            }

            if (dirItem != null) {
                dirItem.Name = entry.FileName;

                if (entry.IsDirectory && entry.ContainingDir == IFileEntry.NO_ENTRY) {
                    ArchiveTreeItem? ati =
                        ArchiveTreeItem.FindItemByDAObject(mMainWin.ArchiveTreeRoot,
                        archiveOrFileSystem);
                    if (ati != null) {
                        ati.Name = entry.FileName;
                    }
                }
            }

            // When a directory is renamed, the filesystem updates FullPathName on all
            // child entries, but the cached PathName strings in existing FileListItem
            // objects are stale.  Rebuild every item in-place so the displayed paths
            // update while preserving the current sort order.
            if (entry.IsDirectory) {
                ObservableCollection<FileListItem> fileList = mMainWin.FileList;
                FileListItem? newSelection = null;
                for (int i = 0; i < fileList.Count; i++) {
                    FileListItem old = fileList[i];
                    FileListItem rebuilt = new FileListItem(old.FileEntry, mFormatter);
                    fileList[i] = rebuilt;
                    if (old.FileEntry == entry) {
                        newSelection = rebuilt;
                    }
                }
                if (newSelection != null) {
                    mMainWin.SelectedFileListItem = newSelection;
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // File → New Disk Image / New File Archive

        /// <summary>
        /// Handles File → New Disk Image.
        /// </summary>
        public async Task NewDiskImage() {
            if (!CloseWorkFile()) {
                return;
            }
            CreateDiskImage dialog = new CreateDiskImage(mMainWin, AppHook);
            if (await dialog.ShowDialog<bool?>(mMainWin) != true) {
                return;
            }
            if (!string.IsNullOrEmpty(dialog.PathName)) {
                await DoOpenWorkFile(dialog.PathName, false);
            }
        }

        /// <summary>
        /// Handles File → New File Archive.
        /// </summary>
        public async Task NewFileArchive() {
            CreateFileArchive dialog = new CreateFileArchive(mMainWin);
            if (await dialog.ShowDialog<bool?>(mMainWin) != true) {
                return;
            }
            if (!CloseWorkFile()) {
                return;
            }

            string ext;
            FilePickerFileType fileType;
            switch (dialog.Kind) {
                case DiskArc.Defs.FileKind.Binary2:
                    ext = ".bny";
                    fileType = new FilePickerFileType("Binary II") {
                        Patterns = new[] { "*.bny", "*.bqy" }
                    };
                    break;
                case DiskArc.Defs.FileKind.NuFX:
                    ext = ".shk";
                    fileType = new FilePickerFileType("NuFX Archive") {
                        Patterns = new[] { "*.shk", "*.sdk", "*.bxy" }
                    };
                    break;
                case DiskArc.Defs.FileKind.Zip:
                    ext = ".zip";
                    fileType = new FilePickerFileType("ZIP Archive") {
                        Patterns = new[] { "*.zip" }
                    };
                    break;
                default:
                    Debug.Assert(false);
                    return;
            }

            var topLevel = TopLevel.GetTopLevel(mMainWin);
            var file = await topLevel!.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions {
                    Title = "Create New Archive...",
                    SuggestedFileName = "NewArchive" + ext,
                    FileTypeChoices = new[] {
                        fileType,
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });
            if (file == null) {
                return;
            }

            string pathName = file.Path.LocalPath;
            if (!pathName.ToLowerInvariant().EndsWith(ext)) {
                pathName += ext;
            }

            IArchive archive;
            switch (dialog.Kind) {
                case DiskArc.Defs.FileKind.Binary2:
                    archive = Binary2.CreateArchive(AppHook);
                    break;
                case DiskArc.Defs.FileKind.NuFX:
                    archive = NuFX.CreateArchive(AppHook);
                    break;
                case DiskArc.Defs.FileKind.Zip:
                    archive = Zip.CreateArchive(AppHook);
                    break;
                default:
                    Debug.Assert(false);
                    return;
            }

            try {
                using (FileStream imgStream = new FileStream(pathName, FileMode.CreateNew)) {
                    archive.StartTransaction();
                    archive.CommitTransaction(imgStream);
                }
                await DoOpenWorkFile(pathName, false);
            } catch (IOException ex) {
                ShowFileError("Unable to create archive: " + ex.Message);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Debug log viewer

        /// <summary>
        /// Toggles the debug log viewer window open/closed.
        /// </summary>
        public void Debug_ShowDebugLog() {
            if (mDebugLogViewer == null) {
                Tools.LogViewer dlg = new Tools.LogViewer(mDebugLog);
                dlg.Closing += (sender, e) => {
                    Debug.WriteLine("Debug log viewer closed");
                    mDebugLogViewer = null;
                    mMainWin.IsDebugLogVisible = false;
                };
                dlg.Show();
                mDebugLogViewer = dlg;
            } else {
                mDebugLogViewer.Close();
            }
        }

        // -----------------------------------------------------------------------------------------
        // Debug → Library Tests / Bulk Compress

        /// <summary>
        /// Opens the DiskArc library test runner dialog.
        /// </summary>
        public async Task Debug_DiskArcLibTests() {
            LibTest.TestManager dialog = new LibTest.TestManager("DiskArcTests.dll",
                "DiskArcTests.ITest");
            await dialog.ShowDialog(mMainWin);
        }

        /// <summary>
        /// Opens the FileConv library test runner dialog.
        /// </summary>
        public async Task Debug_FileConvLibTests() {
            LibTest.TestManager dialog = new LibTest.TestManager("FileConvTests.dll",
                "FileConvTests.ITest");
            await dialog.ShowDialog(mMainWin);
        }

        /// <summary>
        /// Opens the bulk compression test dialog.
        /// </summary>
        public async Task Debug_BulkCompressTest() {
            LibTest.BulkCompress dialog = new LibTest.BulkCompress(AppHook);
            await dialog.ShowDialog(mMainWin);
        }

        /// <summary>
        /// Handles Debug → Show System Info.
        /// </summary>
        public void Debug_ShowSystemInfo() {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CiderPress II GUI v" + GlobalAppVersion.AppVersion
#if DEBUG
                + " DEBUG"
#endif
                );
            sb.AppendLine("+ DiskArc Library v" + Defs.LibVersion +
                (string.IsNullOrEmpty(Defs.BUILD_TYPE) ? "" : " (" + Defs.BUILD_TYPE + ")"));
            sb.AppendLine("+ Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription +
                " / " + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
            sb.AppendLine("  E_V=" + Environment.Version);
            sb.AppendLine("  E_OV=" + Environment.OSVersion);
            sb.AppendLine("  E_OV_P=" + Environment.OSVersion.Platform);
            sb.AppendLine("  E_64=" + Environment.Is64BitOperatingSystem + " / " +
                Environment.Is64BitProcess);
            sb.AppendLine("  E_MACH=\"" + Environment.MachineName + "\" cpus=" +
                Environment.ProcessorCount);
            sb.AppendLine("  E_CD=\"" + Environment.CurrentDirectory + "\"");
            sb.AppendLine("  RI_FD=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            sb.AppendLine("  RI_OSA=" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
            sb.AppendLine("  RI_OSD=" + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            sb.AppendLine("  RI_PA=" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
            sb.AppendLine("  RI_RI=" + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
            sb.AppendLine(" " +
                " IsFreeBSD=" + System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.FreeBSD) +
                " IsOSX=" + System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) +
                " IsLinux=" + System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) +
                " IsWindows=" + System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows));

            Tools.ShowText dialog = new Tools.ShowText(mMainWin, sb.ToString());
            dialog.Title = "System Info";
            dialog.ShowDialog(mMainWin);
        }

        /// <summary>
        /// Handles Debug → Convert ANI to GIF.
        /// </summary>
        public async Task Debug_ConvertANI() {
            FileListItem item = (FileListItem)mMainWin.fileListDataGrid.SelectedItems[0]!;
            IFileEntry entry = item.FileEntry;
            FileAttribs attrs = new FileAttribs(entry);
            object? archiveOrFileSystem = CurrentWorkObject;
            if (archiveOrFileSystem == null) {
                return;
            }
            ExportFoundry.GetApplicableConverters(archiveOrFileSystem, entry, attrs,
                false, true, out Stream? dataStream, out Stream? rsrcStream, AppHook);
            rsrcStream?.Close();
            if (dataStream == null) {
                return;
            }
            AnimatedGifEncoder? enc;
            using (dataStream) {
                enc = DoConvertANI(dataStream);
            }
            if (enc == null) {
                await ShowMessageAsync("Unable to parse ANI file.", "Failed");
                return;
            }

            var topLevel = TopLevel.GetTopLevel(mMainWin);
            var file = await topLevel!.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions {
                    Title = "Save Animated GIF...",
                    SuggestedFileName = Path.GetFileName(item.FileName) + ".gif",
                    FileTypeChoices = new[] {
                        new FilePickerFileType("Animated GIF") { Patterns = new[] { "*.gif" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });
            if (file == null) {
                return;
            }
            string pathName = file.Path.LocalPath;
            using (FileStream outStream = new FileStream(pathName, FileMode.Create)) {
                enc.Save(outStream, out int maxWidth, out int maxHeight);
            }
            Debug.WriteLine("Done (frames=" + enc.Count + " " + pathName + ")");
        }

        // Quick & dirty ANI file conversion.  Returns null on failure.
        private static AnimatedGifEncoder? DoConvertANI(Stream dataStream) {
            const int PIC_LEN = 32768;
            if (dataStream.Length < PIC_LEN + 12 || dataStream.Length > 512 * 1024 * 1024) {
                Debug.WriteLine("unsupported file size: " + dataStream.Length);
                return null;
            }
            dataStream.Position = 0;
            byte[] buf = new byte[dataStream.Length];
            dataStream.ReadExactly(buf, 0, buf.Length);
            uint dataLen = RawData.GetU32LE(buf, PIC_LEN);
            ushort vblDelay = RawData.GetU16LE(buf, PIC_LEN + 4);
            uint animLen = RawData.GetU32LE(buf, PIC_LEN + 8);
            if (buf.Length < PIC_LEN + 8 + (int)animLen) {
                Debug.WriteLine("file was cut off");
                return null;
            }
            int delayMsec = (int)(vblDelay * (1000 / 60.0));
            if (delayMsec == 0) {
                delayMsec = 1;
            }
            AnimatedGifEncoder enc = new AnimatedGifEncoder();

            // ANI files have an initial frame, and a series of looping animation frames.  The
            // initial frame isn't really part of the animated sequence, so we don't want to
            // output it.
            int animOff = PIC_LEN + 12;
            int animEnd = PIC_LEN + 8 + (int)animLen;
            if (animEnd <= animOff) {
                // Work around missing anim len by using full data len.
                animEnd = PIC_LEN + 8 + (int)dataLen;
            }
            while (animOff < animEnd) {
                while (animOff < animEnd) {
                    Debug.Assert(animOff >= 0 && animOff < buf.Length);
                    ushort dataOff = RawData.ReadU16LE(buf, ref animOff);
                    ushort value = RawData.ReadU16LE(buf, ref animOff);
                    if (dataOff == 0) {
                        break;
                    }
                    if (dataOff > 0x7ffe) {
                        Debug.WriteLine("found invalid data offset $" + dataOff.ToString("x4"));
                    } else {
                        RawData.SetU16LE(buf, dataOff, value);
                    }
                }
                Bitmap8 rawImage = FileConv.Gfx.SuperHiRes.ConvertBuffer(buf);
                enc.AddFrame(rawImage, delayMsec);
                if (enc.Count > 10000) {
                    Debug.WriteLine("runaway!");
                    break;
                }
            }

            return enc;
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Defragment

        /// <summary>
        /// Handles Actions → Defragment Filesystem. Only applicable to Pascal volumes.
        /// </summary>
        public async Task Defragment() {
            Pascal? fs = CurrentWorkObject as Pascal;
            if (fs == null) {
                Debug.Assert(false);
                return;
            }
            bool ok = false;
            try {
                fs.PrepareRawAccess();
                ok = fs.Defragment();
            } catch (Exception ex) {
                await ShowMessageAsync("Error: " + ex.Message, "Failed");
                return;
            } finally {
                fs.PrepareFileAccess(true);
                RefreshDirAndFileList();
            }
            if (ok) {
                mMainWin.PostNotification("Defragmentation successful", true);
            } else {
                await ShowMessageAsync("Filesystems with errors cannot be defragmented.", "Failed");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Extract / Export Files

        /// <summary>
        /// Handles Actions → Extract Files.
        /// </summary>
        public async Task ExtractFiles() {
            await HandleExtractExport(null);
        }

        /// <summary>
        /// Handles Actions → Export Files.
        /// </summary>
        public async Task ExportFiles() {
            await HandleExtractExport(GetExportSpec());
        }

        private async Task HandleExtractExport(ConvConfig.FileConvSpec? exportSpec) {
            if (!GetFileSelection(omitDir: false, omitOpenArc: false, closeOpenArc: true,
                    oneMeansAll: false, out object? archiveOrFileSystem,
                    out IFileEntry selectionDir, out List<IFileEntry>? selected, out int _)) {
                return;
            }
            if (selected.Count == 0) {
                await ShowMessageAsync("No files selected.", "Empty");
                return;
            }

            // In full-list mode, use the volume dir as the base for path trimming.
            if (archiveOrFileSystem is IFileSystem && !mMainWin.ShowSingleDirFileList) {
                selectionDir = ((IFileSystem)archiveOrFileSystem).GetVolDirEntry();
            }

            string initialDir = AppSettings.Global.GetString(AppSettings.LAST_EXTRACT_DIR,
                Environment.CurrentDirectory);

            var topLevel = TopLevel.GetTopLevel(mMainWin);
            var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions {
                    Title = "Select destination for " +
                        (exportSpec == null ? "extracted" : "exported") + " files",
                    AllowMultiple = false,
                    SuggestedStartLocation = await topLevel.StorageProvider
                        .TryGetFolderFromPathAsync(initialDir)
                });
            if (folders.Count == 0) {
                return;
            }
            string outputDir = folders[0].Path.LocalPath;

            SettingsHolder settings = AppSettings.Global;
            settings.SetString(AppSettings.LAST_EXTRACT_DIR, outputDir);

            ExtractProgress prog = new ExtractProgress(archiveOrFileSystem, selectionDir,
                    selected, outputDir, exportSpec, AppHook) {
                Preserve = settings.GetEnum(AppSettings.EXT_PRESERVE_MODE,
                    ExtractFileWorker.PreserveMode.None),
                AddExportExt = settings.GetBool(AppSettings.EXT_ADD_EXPORT_EXT, true),
                EnableMacOSZip = settings.GetBool(AppSettings.MAC_ZIP_ENABLED, true),
                StripPaths = settings.GetBool(AppSettings.EXT_STRIP_PATHS_ENABLED, false),
                RawMode = settings.GetBool(AppSettings.EXT_RAW_ENABLED, false),
                DefaultSpecs = GetDefaultExportSpecs()
            };
            Debug.WriteLine("Extract: outputDir='" + outputDir +
                "', selectionDir='" + selectionDir + "'");

            WorkProgress workDialog = new WorkProgress(mMainWin, prog, false);
            await workDialog.ShowDialog(mMainWin);
            if (workDialog.DialogResult) {
                mMainWin.PostNotification(exportSpec != null ? "Export successful"
                    : "Extraction successful", true);
            } else {
                mMainWin.PostNotification("Cancelled", false);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Actions → Add / Import Files

        /// <summary>
        /// Handles Actions → Add Files.
        /// </summary>
        public async Task AddFiles() {
            await HandleAddImport(null);
        }

        /// <summary>
        /// Handles Actions → Import Files.
        /// </summary>
        public async Task ImportFiles() {
            await HandleAddImport(GetImportSpec());
        }

        private async Task HandleAddImport(ConvConfig.FileConvSpec? spec) {
            string initialDir = AppSettings.Global.GetString(AppSettings.LAST_ADD_DIR,
                Environment.CurrentDirectory);

            var topLevel = TopLevel.GetTopLevel(mMainWin);
            var files = await topLevel!.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions {
                    Title = spec == null ? "Select files to add" : "Select files to import",
                    AllowMultiple = true,
                    SuggestedStartLocation = await topLevel.StorageProvider
                        .TryGetFolderFromPathAsync(initialDir)
                });
            if (files.Count == 0) {
                return;
            }

            string[] pathNames = new string[files.Count];
            for (int i = 0; i < files.Count; i++) {
                pathNames[i] = files[i].Path.LocalPath;
            }

            // Compute the longest common directory prefix of all selected files.
            // This preserves subdirectory structure when files are selected from
            // different subdirectories (possible with some file pickers, e.g. KDE).
            string basePath = Path.GetDirectoryName(Path.GetFullPath(pathNames[0])) ??
                Path.GetFullPath(pathNames[0]);
            for (int i = 1; i < pathNames.Length; i++) {
                string dir = Path.GetDirectoryName(Path.GetFullPath(pathNames[i])) ??
                    Path.GetFullPath(pathNames[i]);
                basePath = GetCommonPathPrefix(basePath, dir);
            }
            AppSettings.Global.SetString(AppSettings.LAST_ADD_DIR, basePath);

            await AddPaths(pathNames, IFileEntry.NO_ENTRY, spec, basePath);
        }

        /// <summary>
        /// Handles a file-drop (drag from OS file manager) onto the file list.
        /// </summary>
        public async Task AddFileDrop(IFileEntry dropTarget, string[] pathNames) {
            Debug.Assert(pathNames.Length > 0);
            Debug.WriteLine("External file drop (target=" + dropTarget + "):");
            if (!CheckPasteDropOkay()) {
                return;
            }
            await AddPaths(pathNames, dropTarget,
                mMainWin.IsChecked_ImportExport ? GetImportSpec() : null, null);
        }

        /// <param name="explicitBasePath">If non-null, use this as the base path for
        ///   relative storage names.  When null, derives from pathNames[0].</param>
        private async Task AddPaths(string[] pathNames, IFileEntry dropTarget,
                ConvConfig.FileConvSpec? importSpec, string? explicitBasePath) {
            Debug.WriteLine("Add paths (importSpec=" + importSpec + "):");
            foreach (string path in pathNames) {
                Debug.WriteLine("  " + path);
            }

            if (!GetSelectedArcDir(out object? archiveOrFileSystem, out DiskArcNode? daNode,
                    out IFileEntry targetDir)) {
                return;
            }
            if (dropTarget != IFileEntry.NO_ENTRY && dropTarget.IsDirectory) {
                targetDir = dropTarget;
            }

            string basePath;
            if (!string.IsNullOrEmpty(explicitBasePath)) {
                basePath = Path.GetFullPath(explicitBasePath);
            } else {
                // Drag-and-drop: all files typically share the same parent directory.
                basePath = Path.GetDirectoryName(Path.GetFullPath(pathNames[0])) ??
                    Path.GetFullPath(pathNames[0]);
            }

            AddFileSet.AddOpts addOpts = ConfigureAddOpts(importSpec != null);
            AddFileSet fileSet;
            try {
                fileSet = new AddFileSet(basePath, pathNames, addOpts, importSpec, AppHook);
            } catch (IOException ex) {
                ShowFileError(ex.Message);
                return;
            }
            if (fileSet.Count == 0) {
                Debug.WriteLine("File set was empty");
                return;
            }

            SettingsHolder settings = AppSettings.Global;
            AddProgress prog =
                new AddProgress(archiveOrFileSystem, daNode, fileSet, targetDir, AppHook) {
                    DoCompress = settings.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true),
                    EnableMacOSZip = settings.GetBool(AppSettings.MAC_ZIP_ENABLED, true),
                    StripPaths = settings.GetBool(AppSettings.ADD_STRIP_PATHS_ENABLED, false),
                    RawMode = settings.GetBool(AppSettings.ADD_RAW_ENABLED, false),
                };

            WorkProgress workDialog = new WorkProgress(mMainWin, prog, false);
            await workDialog.ShowDialog(mMainWin);
            if (workDialog.DialogResult) {
                mMainWin.PostNotification(importSpec == null ? "Files added" : "Files imported",
                    true);
            } else {
                mMainWin.PostNotification("Cancelled", false);
            }

            // Refresh even if cancelled — partial progress may have occurred.
            RefreshDirAndFileList();
            TryOpenNewSubVolumes();
        }

        /// <summary>
        /// Returns the longest common directory prefix of two absolute directory paths.
        /// For example, "/a/b/c" and "/a/b/d" yields "/a/b".
        /// </summary>
        private static string GetCommonPathPrefix(string path1, string path2) {
            // Split on the directory separator and find the longest matching prefix.
            char sep = Path.DirectorySeparatorChar;
            string[] parts1 = path1.Split(sep);
            string[] parts2 = path2.Split(sep);
            int commonLen = Math.Min(parts1.Length, parts2.Length);
            int lastMatch = 0;
            for (int i = 0; i < commonLen; i++) {
                if (!string.Equals(parts1[i], parts2[i], StringComparison.Ordinal)) {
                    break;
                }
                lastMatch = i + 1;
            }
            if (lastMatch == 0) {
                // No common prefix at all — fall back to root of first path.
                return Path.GetPathRoot(path1) ?? path1;
            }
            string result = string.Join(sep, parts1, 0, lastMatch);
            // Ensure we don't return an empty string for root paths (e.g. "/").
            if (result.Length == 0) {
                result = sep.ToString();
            }
            return result;
        }

        /// <summary>
        /// Performs pre-flight checks before add/paste/drop operations.
        /// </summary>
        internal bool CheckPasteDropOkay() {
            if (!CanWrite) {
                string msg = (CurrentWorkObject is IFileSystem)
                    ? "Can't add files to a read-only filesystem."
                    : "Can't add files to a read-only archive.";
                // Show error via fire-and-forget (this is called from sync context).
                _ = ShowMessageAsync(msg, "Unable to add");
                return false;
            }
            if (!IsMultiFileItemSelected) {
                _ = ShowMessageAsync("Can't add files to a single-file archive.", "Unable to add");
                return false;
            }
            return true;
        }

        private AddFileSet.AddOpts ConfigureAddOpts(bool isImport) {
            SettingsHolder settings = AppSettings.Global;
            AddFileSet.AddOpts addOpts = new AddFileSet.AddOpts();
            if (isImport) {
                // Anything stored with preserved attributes should be added, not imported.
                addOpts.ParseADF = addOpts.ParseAS = addOpts.ParseNAPS = addOpts.CheckNamed =
                    addOpts.CheckFinderInfo = false;
            } else {
                addOpts.ParseADF = settings.GetBool(AppSettings.ADD_PRESERVE_ADF, true);
                addOpts.ParseAS = settings.GetBool(AppSettings.ADD_PRESERVE_AS, true);
                addOpts.ParseNAPS = settings.GetBool(AppSettings.ADD_PRESERVE_NAPS, true);
                addOpts.CheckNamed = false;
                addOpts.CheckFinderInfo = false;
            }
            addOpts.Recurse = settings.GetBool(AppSettings.ADD_RECURSE_ENABLED, true);
            addOpts.StripExt = settings.GetBool(AppSettings.ADD_STRIP_EXT_ENABLED, true);
            return addOpts;
        }

        // -----------------------------------------------------------------------------------------
        // Converter spec helpers

        private ConvConfig.FileConvSpec GetImportSpec() {
            string convTag = AppSettings.Global.GetString(AppSettings.CONV_IMPORT_TAG, "text");
            string settingKey = AppSettings.IMPORT_SETTING_PREFIX + convTag;
            string convSettings = AppSettings.Global.GetString(settingKey, string.Empty);

            ConvConfig.FileConvSpec? spec;
            if (string.IsNullOrEmpty(convSettings)) {
                spec = ConvConfig.CreateSpec(convTag);
            } else {
                spec = ConvConfig.CreateSpec(convTag + "," + convSettings);
            }
            if (spec == null) {
                Debug.Assert(false, "Failed to parse import spec for tag: " + convTag);
                spec = ConvConfig.CreateSpec(convTag) ?? ConvConfig.CreateSpec("text")!;
            }
            return spec;
        }

        private ConvConfig.FileConvSpec GetExportSpec(string? convTag = null) {
            if (convTag == null) {
                bool useBest = AppSettings.Global.GetBool(AppSettings.CONV_EXPORT_BEST, true);
                convTag = useBest ? ConvConfig.BEST
                    : AppSettings.Global.GetString(AppSettings.CONV_EXPORT_TAG, ConvConfig.BEST);
            }
            string settingKey = AppSettings.EXPORT_SETTING_PREFIX + convTag;
            string convSettings = AppSettings.Global.GetString(settingKey, string.Empty);

            ConvConfig.FileConvSpec? spec;
            if (string.IsNullOrEmpty(convSettings)) {
                spec = ConvConfig.CreateSpec(convTag);
            } else {
                spec = ConvConfig.CreateSpec(convTag + "," + convSettings);
            }
            if (spec == null) {
                Debug.Assert(false, "Failed to parse export spec for tag: " + convTag);
                spec = ConvConfig.CreateSpec(ConvConfig.BEST)!;
            }
            return spec;
        }

        private Dictionary<string, ConvConfig.FileConvSpec>? GetDefaultExportSpecs() {
            Dictionary<string, ConvConfig.FileConvSpec> defaults =
                new Dictionary<string, ConvConfig.FileConvSpec>();
            List<string> tags = ExportFoundry.GetConverterTags();
            foreach (string tag in tags) {
                defaults[tag] = GetExportSpec(tag);
            }
            return defaults;
        }

        // -----------------------------------------------------------------------------------------
        // Clipboard copy & paste

        /// <summary>
        /// Cached clip entries from the most recent copy operation, with live stream
        /// generators.  Used for same-process paste since mStreamGen is not serialized.
        /// </summary>
        private List<ClipFileEntry>? mCachedClipEntries;

        /// <summary>
        /// Temp directory holding extracted files for external clipboard paste.
        /// Cleaned up on next copy or when the file is closed.
        /// </summary>
        private string? mClipTempDir;

        /// <summary>
        /// Clears the clipboard and cached state if there is a pending copy from this process.
        /// Called when the user changes the Drag &amp; Copy mode so stale data isn't pasted
        /// with the wrong settings.
        /// </summary>
        public async void ClearClipboardIfPending() {
            if (mCachedClipEntries == null) {
                return;
            }
            CleanupClipTemp();
            mCachedClipEntries = null;
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(mMainWin)?.Clipboard;
            if (clipboard != null) {
                try {
                    await clipboard.ClearAsync();
                } catch (Exception ex) {
                    AppHook.LogW("Failed to clear clipboard: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Cleans up any temp directory created during clipboard copy.
        /// </summary>
        private void CleanupClipTemp() {
            if (mClipTempDir != null && Directory.Exists(mClipTempDir)) {
                try {
                    Directory.Delete(mClipTempDir, true);
                } catch (Exception ex) {
                    AppHook.LogW("Failed to clean up clip temp dir: " + ex.Message);
                }
                mClipTempDir = null;
            }
        }

        /// <summary>
        /// Handles Edit : Copy.  Serializes the selected file entries as JSON text to the
        /// clipboard.  Same-process paste only (cross-process is out of scope).
        /// Also extracts files to a temp directory so they can be pasted externally.
        /// </summary>
        public async Task CopyToClipboard() {
            if (!GetFileSelection(omitDir: false, omitOpenArc: false, closeOpenArc: true,
                    oneMeansAll: false, out object? archiveOrFileSystem,
                    out IFileEntry selectionDir, out List<IFileEntry>? entries, out int unused)) {
                entries = new List<IFileEntry>();
            }

            IFileEntry baseDir = IFileEntry.NO_ENTRY;
            if (CurrentWorkObject is IFileSystem && mMainWin.ShowSingleDirFileList) {
                DirectoryTreeItem? dirItem = mMainWin.SelectedDirectoryTreeItem;
                if (dirItem != null) {
                    baseDir = dirItem.FileEntry;
                }
            }

            SettingsHolder settings = AppSettings.Global;
            ExtractFileWorker.PreserveMode preserve =
                settings.GetEnum(AppSettings.EXT_PRESERVE_MODE,
                    ExtractFileWorker.PreserveMode.None);
            bool addExportExt = settings.GetBool(AppSettings.EXT_ADD_EXPORT_EXT, true);
            bool rawMode = settings.GetBool(AppSettings.EXT_RAW_ENABLED, false);
            bool doStrip = settings.GetBool(AppSettings.EXT_STRIP_PATHS_ENABLED, false);
            bool doMacZip = settings.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
            ConvConfig.FileConvSpec? exportSpec = null;
            Dictionary<string, ConvConfig.FileConvSpec>? defaultSpecs = null;
            if (mMainWin.IsChecked_ImportExport) {
                exportSpec = GetExportSpec();
                defaultSpecs = GetDefaultExportSpecs();
            }

            ClipFileSet clipSet;
            var waitCursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Wait);
            mMainWin.Cursor = waitCursor;
            try {
                clipSet = new ClipFileSet(CurrentWorkObject!, entries, baseDir,
                    preserve, addExportExt: addExportExt, useRawData: rawMode, stripPaths: doStrip,
                    enableMacZip: doMacZip, exportSpec, defaultSpecs, AppHook);
            } finally {
                mMainWin.Cursor = null;
                waitCursor.Dispose();
            }

            // Only serialize direct-transfer (non-export) entries.  Export mode produces no
            // streamable data that a same-process paste could re-read.
            List<ClipFileEntry> xferEntries = clipSet.XferEntries;

            // Buffer file data as base64 into each entry so the JSON contains everything
            // needed for cross-instance paste.
            foreach (ClipFileEntry entry in xferEntries) {
                if (entry.mStreamGen != null && !entry.Attribs.IsDirectory) {
                    using MemoryStream ms = new MemoryStream();
                    entry.mStreamGen.OutputToStream(ms);
                    entry.DataBase64 = Convert.ToBase64String(ms.ToArray());
                }
            }

            ClipInfo clipInfo = new ClipInfo(xferEntries, GlobalAppVersion.AppVersion);
            if (exportSpec != null) {
                clipInfo.IsExport = true;
            }

            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(mMainWin)?.Clipboard;
            if (clipboard != null) {
                // Cache the entries with live stream generators for same-process paste.
                mCachedClipEntries = xferEntries;

                // Extract ForeignEntries to temp directory for pasting to external file
                // managers.  Clean up any previous temp dir first.
                CleanupClipTemp();
                List<ClipFileEntry> foreignEntries = clipSet.ForeignEntries;
                StringBuilder uriListBuilder = new StringBuilder();

                if (foreignEntries.Count > 0) {
                    string tempDir = Path.Combine(Path.GetTempPath(),
                        "cp2clip_" + Environment.ProcessId + "_" +
                        DateTime.UtcNow.Ticks.ToString("x"));
                    Directory.CreateDirectory(tempDir);
                    mClipTempDir = tempDir;

                    foreach (ClipFileEntry clipEntry in foreignEntries) {
                        // Use ExtractPath which includes export extensions and preserves
                        // the correct filename for the current mode.
                        string extractPath = Path.Combine(tempDir, clipEntry.ExtractPath);
                        string? dirName = Path.GetDirectoryName(extractPath);
                        if (dirName != null && !Directory.Exists(dirName)) {
                            Directory.CreateDirectory(dirName);
                        }
                        if (clipEntry.Attribs.IsDirectory) {
                            Directory.CreateDirectory(extractPath);
                            continue;
                        }
                        if (clipEntry.mStreamGen == null) {
                            continue;
                        }
                        try {
                            using (FileStream outStream = new FileStream(extractPath,
                                    FileMode.Create, FileAccess.Write)) {
                                clipEntry.mStreamGen.OutputToStream(outStream);
                            }
                            // Build text/uri-list for X11/Wayland clipboard.
                            Uri fileUri = new Uri(extractPath);
                            uriListBuilder.Append(fileUri.AbsoluteUri);
                            uriListBuilder.Append("\r\n");
                        } catch (Exception ex) {
                            AppHook.LogW("Failed to extract clip entry '" +
                                clipEntry.ExtractPath + "': " + ex.Message);
                        }
                    }
                }

                DataObject dataObj = new DataObject();
                dataObj.Set(DataFormats.Text, clipInfo.ToClipString());
                int extractedCount = 0;
                if (uriListBuilder.Length > 0) {
                    string uriList = uriListBuilder.ToString();
                    // Set text/uri-list so X11-based file managers (KDE, GNOME, etc.)
                    // recognize the clipboard as containing files.
                    dataObj.Set("text/uri-list", uriList);
                    // Count lines (each URI is terminated by \r\n).
                    for (int i = 0; i < uriList.Length; i++) {
                        if (uriList[i] == '\n') {
                            extractedCount++;
                        }
                    }
                }
                await clipboard.SetDataObjectAsync(dataObj);

                AppHook.LogI("Copied " + xferEntries.Count + " entries to clipboard" +
                    (extractedCount > 0 ?
                        " (" + extractedCount + " files extracted)" : ""));
                mMainWin.PostNotification("Copied " + xferEntries.Count + " item(s)", true);
            }
        }

        /// <summary>
        /// Handles Edit : Paste and drag-drop of CP2 data onto the file list.
        /// </summary>
        /// <param name="dropData">Data object from drag-drop, or null for clipboard paste.</param>
        /// <param name="dropTarget">The file entry the data was dropped onto, or NO_ENTRY.</param>
        public async Task PasteOrDrop(Avalonia.Input.IDataObject? dropData,
                IFileEntry dropTarget) {
            if (!CheckPasteDropOkay()) {
                return;
            }

            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(mMainWin)?.Clipboard;
            if (clipboard == null) {
                Debug.WriteLine("Paste: clipboard not available");
                return;
            }

            // First, check for CP2's own clipboard format (same-process paste).
            string? clipText = await clipboard.GetTextAsync();
            ClipInfo? clipInfo = ClipInfo.FromClipString(clipText);

            if (clipInfo == null) {
                // Not CP2 data.  Check for external files from file managers.
                string? uriList = await GetClipboardUriList(clipboard);
                if (!string.IsNullOrEmpty(uriList)) {
                    await PasteExternalFiles(uriList, dropTarget);
                    return;
                }

                await ShowMessageAsync(
                    "No CiderPress II file data found on the clipboard.\n\n" +
                    "Use Edit \u2192 Copy to copy files within CiderPress II,\n" +
                    "or copy files in your file manager to paste them here.",
                    "Nothing to Paste");
                return;
            }

            if (clipInfo.IsExport) {
                await ShowMessageAsync(
                    "The file copy was performed in \"export\" mode.  Please use \"extract\" " +
                    "mode when copying files between CiderPress II windows.",
                    "Can't Paste Exports");
                return;
            }

            if (clipInfo.AppVersionMajor != GlobalAppVersion.AppVersion.Major ||
                    clipInfo.AppVersionMinor != GlobalAppVersion.AppVersion.Minor ||
                    clipInfo.AppVersionPatch != GlobalAppVersion.AppVersion.Patch) {
                await ShowMessageAsync(
                    "Cannot copy and paste between different versions of the application.",
                    "Version Mismatch");
                return;
            }

            if (clipInfo.ClipEntries!.Count == 0) {
                Debug.WriteLine("Pasting empty file set");
                return;
            }
            AppHook.LogI("Paste from clipboard; found " + clipInfo.ClipEntries.Count +
                " files/forks");

            // For same-process paste, use the cached entries which have live stream
            // generators.  The deserialized entries lost mStreamGen during JSON
            // serialization (it's a non-serialized field).
            if (clipInfo.ProcessId == Environment.ProcessId &&
                    mCachedClipEntries != null &&
                    mCachedClipEntries.Count == clipInfo.ClipEntries.Count) {
                clipInfo.ClipEntries = mCachedClipEntries;
            }

            mMainWin.Activate();

            if (!GetSelectedArcDir(out object? archiveOrFileSystem, out DiskArcNode? daNode,
                    out IFileEntry targetDir)) {
                return;
            }
            if (dropTarget != IFileEntry.NO_ENTRY && dropTarget.IsDirectory) {
                targetDir = dropTarget;
            }
            if (clipInfo.ProcessId == Environment.ProcessId &&
                    archiveOrFileSystem is IArchive) {
                await ShowMessageAsync(
                    "Files cannot be copied and pasted from a file archive to itself.",
                    "Conflict");
                return;
            }

            // Stream generator: for same-process paste, use the live StreamGenerator;
            // for cross-instance paste, use the base64 data from the JSON.
            ClipPasteWorker.ClipStreamGenerator streamGen =
                delegate (ClipFileEntry clipEntry) {
                    if (clipEntry.mStreamGen != null) {
                        System.IO.MemoryStream ms = new System.IO.MemoryStream();
                        clipEntry.mStreamGen.OutputToStream(ms);
                        ms.Position = 0;
                        return ms;
                    }
                    if (clipEntry.DataBase64 != null) {
                        return new System.IO.MemoryStream(
                            Convert.FromBase64String(clipEntry.DataBase64));
                    }
                    return null;
                };

            SettingsHolder settings = AppSettings.Global;
            Actions.ClipPasteProgress prog =
                new Actions.ClipPasteProgress(archiveOrFileSystem, daNode, targetDir,
                        clipInfo, streamGen, AppHook) {
                    DoCompress = settings.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true),
                    EnableMacOSZip = settings.GetBool(AppSettings.MAC_ZIP_ENABLED, true),
                    ConvertDOSText = settings.GetBool(AppSettings.DOS_TEXT_CONV_ENABLED, false),
                    StripPaths = settings.GetBool(AppSettings.ADD_STRIP_PATHS_ENABLED, false),
                    RawMode = settings.GetBool(AppSettings.ADD_RAW_ENABLED, false),
                };

            Common.WorkProgress workDialog = new Common.WorkProgress(mMainWin, prog, false);
            if (await workDialog.ShowDialog<bool?>(mMainWin) == true) {
                mMainWin.PostNotification("Files added", true);
            } else {
                mMainWin.PostNotification("Cancelled", false);
            }

            RefreshDirAndFileList();
            TryOpenNewSubVolumes();
        }

        /// <summary>
        /// Handles paste of external files from the system clipboard.  Parses a text/uri-list
        /// string into local file paths and adds them via the standard add-files path.
        /// </summary>
        private async Task PasteExternalFiles(string uriList, IFileEntry dropTarget) {
            List<string> paths = new List<string>();
            foreach (string line in uriList.Split('\n')) {
                string trimmed = line.Trim('\r', ' ');
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) {
                    continue;   // skip comments and blank lines per RFC 2483
                }
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) &&
                        uri.IsFile) {
                    paths.Add(uri.LocalPath);
                }
            }
            if (paths.Count == 0) {
                await ShowMessageAsync(
                    "The clipboard contains file references, but none are local files.",
                    "Nothing to Paste");
                return;
            }
            AppHook.LogI("Paste external files from clipboard: " + paths.Count + " file(s)");
            await AddFileDrop(dropTarget, paths.ToArray());
        }

        /// <summary>
        /// Attempts to retrieve a text/uri-list from the clipboard.  Tries multiple format
        /// names and falls back to interpreting plain text as file:// URIs or bare paths.
        /// </summary>
        private async Task<string?> GetClipboardUriList(
                Avalonia.Input.Platform.IClipboard clipboard) {
            // Log available formats for diagnostic purposes.
            string[]? formats = null;
            try {
                formats = await clipboard.GetFormatsAsync();
            } catch (Exception ex) {
                AppHook.LogW("GetFormatsAsync failed: " + ex.Message);
            }
            if (formats != null) {
                AppHook.LogD("Clipboard formats: " + string.Join(", ", formats));
            }

            // Try text/uri-list first (standard X11/freedesktop format).
            string? uriList = await TryGetClipFormat(clipboard, "text/uri-list");
            if (!string.IsNullOrEmpty(uriList)) {
                return uriList;
            }

            // Try x-special/gnome-copied-files (used by GNOME-based file managers, and
            // also by KDE Dolphin).  Format: "copy\n<uri>\n<uri>..." or "cut\n<uri>...".
            string? gnomeData = await TryGetClipFormat(clipboard, "x-special/gnome-copied-files");
            if (!string.IsNullOrEmpty(gnomeData)) {
                // Strip the "copy" or "cut" prefix line.
                int newline = gnomeData.IndexOf('\n');
                if (newline >= 0) {
                    return gnomeData.Substring(newline + 1);
                }
            }

            // Fall back to plain text: check if it looks like file:// URIs or absolute
            // file paths (one per line).
            string? plainText = await clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(plainText)) {
                string firstLine = plainText.Split('\n')[0].Trim('\r', ' ');
                if (firstLine.StartsWith("file://") ||
                        (Path.IsPathRooted(firstLine) && File.Exists(firstLine))) {
                    // Looks like file references.  If bare paths, convert to file:// URIs.
                    if (!firstLine.StartsWith("file://")) {
                        StringBuilder sb = new StringBuilder();
                        foreach (string line in plainText.Split('\n')) {
                            string path = line.Trim('\r', ' ');
                            if (!string.IsNullOrEmpty(path) && Path.IsPathRooted(path)) {
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
        /// Tries to get clipboard data for a specific format, returning it as a string.
        /// Returns null on failure.
        /// </summary>
        private static async Task<string?> TryGetClipFormat(
                Avalonia.Input.Platform.IClipboard clipboard, string format) {
            try {
                object? data = await clipboard.GetDataAsync(format);
                if (data is string s) {
                    return s;
                }
                if (data is byte[] bytes && bytes.Length > 0) {
                    return Encoding.UTF8.GetString(bytes);
                }
            } catch (Exception ex) {
                Debug.WriteLine("GetDataAsync(\"" + format + "\") failed: " + ex.Message);
            }
            return null;
        }

        // -----------------------------------------------------------------------------------------
        // Debug : Drop/Paste Target

        private Tools.DropTarget? mDebugDropTarget;
        public bool IsDropTargetOpen => mDebugDropTarget != null;

        /// <summary>
        /// Opens or closes the modeless Drop/Paste Target debug window.
        /// </summary>
        public void Debug_ShowDropTarget() {
            if (mDebugDropTarget == null) {
                Tools.DropTarget dlg = new Tools.DropTarget();
                dlg.Closing += (sender, e) => {
                    Debug.WriteLine("Drop target test closed");
                    mDebugDropTarget = null;
                    mMainWin.IsDropTargetVisible = false;
                };
                dlg.Show();
                mDebugDropTarget = dlg;
            } else {
                mDebugDropTarget.Close();
            }
        }

        // -----------------------------------------------------------------------------------------
        // File selection helper

        /// <summary>
        /// Gets the current selection from the file list DataGrid.
        /// </summary>
        public bool GetFileSelection(bool omitDir, bool omitOpenArc, bool closeOpenArc,
                bool oneMeansAll,
                [NotNullWhen(true)] out object? archiveOrFileSystem,
                out IFileEntry selectionDir,
                [NotNullWhen(true)] out List<IFileEntry>? selected,
                out int firstSel) {
            selected = null;
            firstSel = 0;
            if (!GetSelectedArcDir(out archiveOrFileSystem, out DiskArcNode? _,
                    out selectionDir)) {
                return false;
            }

            var dg = mMainWin.fileListDataGrid;
            var listSel = dg.SelectedItems;
            if (listSel == null || listSel.Count == 0) {
                return false;
            }

            // In "one means all" mode with a single selection, expand to all items.
            IEnumerable<FileListItem> itemsToProcess;
            FileListItem? singleSelItem = null;
            if (oneMeansAll && listSel.Count == 1) {
                singleSelItem = listSel[0] as FileListItem;
                itemsToProcess = mMainWin.FileList;
            } else {
                var temp = new List<FileListItem>();
                foreach (object obj in listSel) {
                    if (obj is FileListItem fli)
                    {
                        temp.Add(fli);
                    }
                }
                itemsToProcess = temp;
            }

            selected = new List<IFileEntry>();
            if (archiveOrFileSystem is IArchive) {
                foreach (FileListItem listItem in itemsToProcess) {
                    if (omitDir && listItem.FileEntry.IsDirectory) {
                        continue;
                    }
                    selected.Add(listItem.FileEntry);
                }
            } else {
                // Filesystem: collect entries, descending into subdirectories.
                var knownItems = new Dictionary<IFileEntry, IFileEntry>();
                foreach (FileListItem listItem in itemsToProcess) {
                    if (!knownItems.ContainsKey(listItem.FileEntry)) {
                        knownItems.Add(listItem.FileEntry, listItem.FileEntry);
                    }
                }
                foreach (FileListItem listItem in itemsToProcess) {
                    IFileEntry entry = listItem.FileEntry;
                    if (entry.IsDirectory) {
                        if (!omitDir) {
                            selected.Add(entry);
                            AddDirEntries(entry, knownItems, selected);
                        }
                    } else {
                        selected.Add(entry);
                    }
                }
                if (!omitDir) {
                    selected = ShiftDirectories(selected);
                }
            }

            // Handle open archives.
            if (omitOpenArc || closeOpenArc) {
                ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
                bool hasOpenChildren = arcTreeSel != null && arcTreeSel.Items.Count != 0;
                if (hasOpenChildren) {
                    for (int i = 0; i < selected.Count; i++) {
                        IFileEntry entry = selected[i];
                        if (entry.IsDirectory)
                        {
                            continue;
                        }

                        ArchiveTreeItem? treeItem =
                            ArchiveTreeItem.FindItemByEntry(mMainWin.ArchiveTreeRoot, entry);
                        if (treeItem != null) {
                            if (omitOpenArc) {
                                selected.RemoveAt(i--);
                            } else if (closeOpenArc) {
                                CloseSubTree(treeItem);
                            }
                        }
                    }
                }
            }

            // Find the index of the single originally-selected item.
            if (singleSelItem != null) {
                for (int i = 0; i < selected.Count; i++) {
                    if (selected[i] == singleSelItem.FileEntry) {
                        firstSel = i;
                        break;
                    }
                }
            }
            return true;
        }

        private void AddDirEntries(IFileEntry entry,
                Dictionary<IFileEntry, IFileEntry> excludes, List<IFileEntry> list) {
            foreach (IFileEntry child in entry) {
                if (!excludes.ContainsKey(child)) {
                    list.Add(child);
                }
                if (child.IsDirectory) {
                    AddDirEntries(child, excludes, list);
                }
            }
        }

        private List<IFileEntry> ShiftDirectories(List<IFileEntry> entries) {
            // Ensure parent directories come before their contents by sorting on path length.
            // This is a stable sort: shorter paths (parents) come first.
            var dirs = new List<IFileEntry>();
            var files = new List<IFileEntry>();
            foreach (IFileEntry e in entries) {
                if (e.IsDirectory)
                {
                    dirs.Add(e);
                }
                else
                {
                    files.Add(e);
                }
            }
            dirs.Sort((a, b) =>
                string.Compare(a.FullPathName, b.FullPathName, StringComparison.Ordinal));
            var result = new List<IFileEntry>(dirs.Count + files.Count);
            result.AddRange(dirs);
            result.AddRange(files);
            return result;
        }

        // -----------------------------------------------------------------------------------------
        // EditAppSettings

        /// <summary>
        /// Handles Edit : Application Settings.
        /// </summary>
        public async Task EditAppSettings() {
            EditAppSettings dlg = new EditAppSettings(mMainWin);
            dlg.SettingsApplied += ApplyAppSettings;
            await dlg.ShowDialog<bool?>(mMainWin);
            // Settings are applied via raised event.
        }

        // -----------------------------------------------------------------------------------------
        // Find Files

        /// <summary>
        /// This keeps track of state while we're traversing the tree, trying to find matches.
        /// </summary>
        private class FindFileState {
            public ArchiveTreeItem mCurrentArchive;
            public IFileEntry mCurrentEntry;

            public bool mFoundCurrent;

            public ArchiveTreeItem? mFirstArchive;
            public IFileEntry mFirstEntry = IFileEntry.NO_ENTRY;
            public ArchiveTreeItem? mPrevArchive;
            public IFileEntry mPrevEntry = IFileEntry.NO_ENTRY;
            public ArchiveTreeItem? mNextArchive;
            public IFileEntry mNextEntry = IFileEntry.NO_ENTRY;
            public ArchiveTreeItem? mLastArchive;
            public IFileEntry mLastEntry = IFileEntry.NO_ENTRY;

            public FindFileState(ArchiveTreeItem currentArchive, IFileEntry currentEntry) {
                mCurrentArchive = currentArchive;
                mCurrentEntry = currentEntry;
            }

            public override string ToString() {
                return "[FindRes: current=" + mCurrentEntry +
                    "\r\n  first=" + mFirstEntry +
                    "\r\n  prev=" + mPrevEntry +
                    "\r\n  next=" + mNextEntry +
                    "\r\n  last=" + mLastEntry +
                    "\r\n]";
            }
        }

        /// <summary>
        /// Handles Edit : Find Files.  Displays the modeless Find File dialog.
        /// </summary>
        public async Task FindFiles() {
            FindFile dialog = new FindFile();
            dialog.FindRequested += DoFindFiles;
            await dialog.ShowDialog<bool?>(mMainWin);
        }

        /// <summary>
        /// Does the actual work of finding matching files.
        /// </summary>
        private void DoFindFiles(FindFile.FindFileReq req) {
            Debug.WriteLine("Find Files: " + req);

            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            if (arcTreeSel == null) {
                Debug.WriteLine("No archive entry selected");
                return;
            }

            object? arcObj = arcTreeSel.WorkTreeNode.DAObject;
            if (arcObj is not IArchive && arcObj is not IFileSystem) {
                Debug.WriteLine("Can't start with this archive object: " + arcObj);
                return;
            }

            IFileEntry selEntry;
            IList listSel = mMainWin.fileListDataGrid.SelectedItems;
            if (listSel.Count == 0) {
                var allItems = mMainWin.FileList;
                if (allItems.Count == 0) {
                    Debug.WriteLine("Empty archive/FS selected, can't search");
                    return;
                }
                selEntry = allItems[0].FileEntry;
            } else {
                selEntry = ((FileListItem)listSel[0]!).FileEntry;
            }

            Debug.WriteLine("FIND starting from " + arcTreeSel + " / " + selEntry);
            FindFileState results = new FindFileState(arcTreeSel, selEntry);
            FindInTree(mMainWin.ArchiveTreeRoot, req, results);
            Debug.WriteLine("FIND results: " + results);

            if (results.mFirstArchive == null) {
                mMainWin.PostNotification("No matches found", false);
                return;
            }

            ArchiveTreeItem newTreeItem;
            IFileEntry newEntry;
            if (req.Forward) {
                if (results.mNextArchive != null) {
                    newTreeItem = results.mNextArchive;
                    newEntry = results.mNextEntry;
                } else {
                    newTreeItem = results.mFirstArchive;
                    newEntry = results.mFirstEntry;
                }
            } else {
                if (results.mPrevArchive != null) {
                    newTreeItem = results.mPrevArchive;
                    newEntry = results.mPrevEntry;
                } else {
                    newTreeItem = results.mLastArchive!;
                    newEntry = results.mLastEntry;
                }
            }

            ArchiveTreeItem.SelectItem(mMainWin, newTreeItem);
            if (newEntry.ContainingDir != IFileEntry.NO_ENTRY) {
                DirectoryTreeItem.SelectItemByEntry(mMainWin, newEntry.ContainingDir);
            }
            FileListItem.SelectAndView(mMainWin, newEntry);
        }

        private static void FindInTree(ObservableCollection<ArchiveTreeItem> tvRoot,
                FindFile.FindFileReq req, FindFileState results) {
            foreach (ArchiveTreeItem treeItem in tvRoot) {
                object daObject = treeItem.WorkTreeNode.DAObject;
                if (!req.CurrentArchiveOnly || treeItem == results.mCurrentArchive) {
                    if (daObject is IArchive) {
                        FindInArchive(treeItem, req, results);
                    } else if (daObject is IFileSystem) {
                        FindInFileSystem(treeItem, ((IFileSystem)daObject).GetVolDirEntry(),
                            req, results);
                    }
                }
                FindInTree(treeItem.Items, req, results);
            }
        }

        private static void FindInArchive(ArchiveTreeItem treeItem, FindFile.FindFileReq req,
                FindFileState results) {
            bool macZipEnabled = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
            IArchive arc = (IArchive)treeItem.WorkTreeNode.DAObject;
            foreach (IFileEntry entry in arc) {
                if (macZipEnabled && entry.IsMacZipHeader()) {
                    continue;
                }
                if (entry == results.mCurrentEntry) {
                    results.mFoundCurrent = true;
                }
                if (EntryMatches(entry, req)) {
                    UpdateFindState(treeItem, entry, results);
                }
            }
        }

        private static void FindInFileSystem(ArchiveTreeItem treeItem, IFileEntry dir,
                FindFile.FindFileReq req, FindFileState results) {
            foreach (IFileEntry entry in dir) {
                if (entry == results.mCurrentEntry) {
                    results.mFoundCurrent = true;
                }
                if (EntryMatches(entry, req)) {
                    UpdateFindState(treeItem, entry, results);
                }
                if (entry.IsDirectory) {
                    FindInFileSystem(treeItem, entry, req, results);
                }
            }
        }

        private static bool EntryMatches(IFileEntry entry, FindFile.FindFileReq req) {
            return entry.FileName.Contains(req.FileName,
                    StringComparison.InvariantCultureIgnoreCase);
        }

        private static void UpdateFindState(ArchiveTreeItem treeItem, IFileEntry matchEntry,
                FindFileState results) {
            results.mLastArchive = treeItem;
            results.mLastEntry = matchEntry;
            if (results.mFirstArchive == null) {
                results.mFirstArchive = treeItem;
                results.mFirstEntry = matchEntry;
            }
            if (matchEntry != results.mCurrentEntry) {
                if (!results.mFoundCurrent) {
                    results.mPrevArchive = treeItem;
                    results.mPrevEntry = matchEntry;
                } else if (results.mNextArchive == null) {
                    results.mNextArchive = treeItem;
                    results.mNextEntry = matchEntry;
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // Replace Partition / Save As Disk Image

        /// <summary>
        /// Handles Actions : Replace Partition.
        /// </summary>
        public async Task ReplacePartition() {
            Debug.Assert(mWorkTree != null);
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            if (arcTreeSel == null) {
                Debug.Assert(false);
                return;
            }
            WorkTree.Node workNode = arcTreeSel.WorkTreeNode;
            Debug.Assert(workNode.DAObject == CurrentWorkObject);
            DiskArc.Multi.Partition? dstPartition = workNode.DAObject as DiskArc.Multi.Partition;
            if (dstPartition == null) {
                Debug.Assert(false);
                return;
            }

            // Ask the user to pick a source disk image.
            string? pathName = await PlatformUtil.AskFileToOpen(mMainWin);
            if (string.IsNullOrEmpty(pathName)) {
                return;
            }
            string ext = Path.GetExtension(pathName);

            FileStream stream;
            try {
                stream = new FileStream(pathName, FileMode.Open, FileAccess.Read);
            } catch (Exception ex) {
                await ShowMessageAsync("Unable to open file: " + ex.Message, "I/O Error");
                return;
            }

            using (stream) {
                FileAnalyzer.AnalysisResult result = FileAnalyzer.Analyze(stream, ext, AppHook,
                    out FileKind kind, out SectorOrder orderHint);
                string errMsg;
                switch (result) {
                    case FileAnalyzer.AnalysisResult.DubiousSuccess:
                    case FileAnalyzer.AnalysisResult.FileDamaged:
                        errMsg = "File is not usable due to possible damage.";
                        break;
                    case FileAnalyzer.AnalysisResult.UnknownExtension:
                        errMsg = "File format not recognized.";
                        break;
                    case FileAnalyzer.AnalysisResult.ExtensionMismatch:
                        errMsg = "File appears to have the wrong extension.";
                        break;
                    case FileAnalyzer.AnalysisResult.NotImplemented:
                        errMsg = "Support for this type of file has not been implemented.";
                        break;
                    case FileAnalyzer.AnalysisResult.Success:
                        errMsg = Defs.IsDiskImageFile(kind) ? string.Empty : "File is not a disk image.";
                        break;
                    default:
                        errMsg = "Internal error: unexpected result from analyzer: " + result;
                        break;
                }
                if (!string.IsNullOrEmpty(errMsg)) {
                    await ShowMessageAsync(errMsg, "Unable to use file");
                    return;
                }

                using IDiskImage? diskImage = FileAnalyzer.PrepareDiskImage(stream, kind, AppHook);
                if (diskImage == null) {
                    await ShowMessageAsync(
                        "Unable to prepare disk image, type=" + ThingString.FileKind(kind) + ".",
                        "Unable to use file");
                    return;
                }
                if (!diskImage.AnalyzeDisk(null, orderHint, IDiskImage.AnalysisDepth.ChunksOnly)) {
                    await ShowMessageAsync("Unable to determine format of disk image contents.",
                        "Unable to use file");
                    return;
                }
                Debug.Assert(diskImage.ChunkAccess != null);

                bool wasClosed = false;
                ReplacePartition.EnableWriteFunc func = delegate () {
                    Debug.Assert(mWorkTree.CheckHealth());
                    workNode.CloseChildren();
                    dstPartition.CloseContents();
                    Debug.Assert(mWorkTree.CheckHealth());
                    arcTreeSel.Items.Clear();
                    wasClosed = true;
                    return true;
                };

                ReplacePartition dialog = new ReplacePartition(dstPartition,
                    diskImage.ChunkAccess, func, mFormatter, AppHook);
                bool? dlgResult = await dialog.ShowDialog<bool?>(mMainWin);
                if (dlgResult == true) {
                    mMainWin.PostNotification("Completed", true);
                }

                try {
                    var waitCursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Wait);
                    mMainWin.Cursor = waitCursor;
                    try {
                        if (wasClosed) {
                            mWorkTree.ReprocessPartition(workNode);
                            foreach (WorkTree.Node childNode in workNode) {
                                ArchiveTreeItem.ConstructTree(arcTreeSel, childNode);
                            }
                        }
                    } finally {
                        mMainWin.Cursor = null;
                        waitCursor.Dispose();
                    }
                } catch (Exception ex) {
                    Debug.WriteLine("ReplacePartition post-processing exception: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Handles Actions : Edit Sectors / Blocks / Blocks (CP/M).
        /// </summary>
        public async Task EditBlocksSectors(EditSector.SectorEditMode editMode) {
            Debug.Assert(mWorkTree != null);
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            if (arcTreeSel == null) {
                Debug.Assert(false);
                return;
            }
            WorkTree.Node workNode = arcTreeSel.WorkTreeNode;
            object daObject = workNode.DAObject;
            IChunkAccess? chunks;
            if (daObject is IDiskImage) {
                chunks = ((IDiskImage)daObject).ChunkAccess;
            } else if (daObject is Partition) {
                chunks = ((Partition)daObject).ChunkAccess;
            } else {
                Debug.Assert(false, "unexpected sector edit target: " + daObject);
                return;
            }
            if (chunks == null) {
                await ShowMessageAsync("Disk sector format not recognized", "Trouble");
                return;
            }

            bool writeEnabled = false;
            EditSector.EnableWriteFunc? func = null;
            if (!chunks.IsReadOnly) {
                func = delegate () {
                    Debug.Assert(mWorkTree.CheckHealth());
                    workNode.CloseChildren();
                    if (daObject is IDiskImage) {
                        ((IDiskImage)daObject).CloseContents();
                    } else if (daObject is Partition) {
                        ((Partition)daObject).CloseContents();
                    }
                    Debug.Assert(mWorkTree.CheckHealth());
                    arcTreeSel.Items.Clear();
                    writeEnabled = true;
                    return true;
                };
            }

            EditSector dialog = new EditSector(chunks, editMode, func, mFormatter);
            await dialog.ShowDialog<bool?>(mMainWin);
            Debug.WriteLine("After dialog, enabled=" + writeEnabled);

            if (daObject is IDiskImage) {
                ((IDiskImage)daObject).Flush();
            }

            if (writeEnabled) {
                var waitCursor = new Avalonia.Input.Cursor(StandardCursorType.Wait);
                mMainWin.Cursor = waitCursor;
                try {
                    if (daObject is IDiskImage) {
                        mWorkTree.ReprocessDiskImage(workNode);
                    } else if (daObject is Partition) {
                        mWorkTree.ReprocessPartition(workNode);
                    }
                    foreach (WorkTree.Node childNode in workNode) {
                        ArchiveTreeItem.ConstructTree(arcTreeSel, childNode);
                    }
                } finally {
                    mMainWin.Cursor = null;
                    waitCursor.Dispose();
                }
            }
        }

        /// <summary>
        /// Handles Actions : Save As Disk Image.
        /// </summary>
        public async Task SaveAsDiskImage() {
            IChunkAccess? chunks = GetCurrentWorkChunks();
            if (chunks == null) {
                Debug.Assert(false);
                return;
            }

            SaveAsDisk dialog = new SaveAsDisk(CurrentWorkObject!, chunks, mFormatter, AppHook);
            bool? result = await dialog.ShowDialog<bool?>(mMainWin);
            if (result == true) {
                mMainWin.PostNotification("Saved", true);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Message box helper

        /// <summary>
        /// Shows a simple modal message box with an OK button.
        /// </summary>
        private async Task ShowMessageAsync(string message, string title) {
            var msgWin = new Window {
                Title = title,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Avalonia.Controls.StackPanel {
                    Margin = new Avalonia.Thickness(16),
                    Spacing = 12,
                    Children = {
                        new Avalonia.Controls.TextBlock {
                            Text = message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new Avalonia.Controls.Button {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Width = 80
                        }
                    }
                }
            };
            var sp = (Avalonia.Controls.StackPanel)msgWin.Content!;
            var okBtn = (Avalonia.Controls.Button)sp.Children[1];
            okBtn.Click += (_, _) => msgWin.Close();
            await msgWin.ShowDialog(mMainWin);
        }

        /// <summary>
        /// Shows a confirmation dialog with OK and Cancel buttons.  Returns true if OK was
        /// clicked.
        /// </summary>
        private async Task<bool> ShowConfirmAsync(string message, string title) {
            bool result = false;
            var confirmWin = new Window {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Avalonia.Controls.StackPanel {
                    Margin = new Avalonia.Thickness(16),
                    Spacing = 12,
                    Children = {
                        new Avalonia.Controls.TextBlock {
                            Text = message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new Avalonia.Controls.StackPanel {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Spacing = 8,
                            Children = {
                                new Avalonia.Controls.Button { Content = "OK", Width = 80 },
                                new Avalonia.Controls.Button { Content = "Cancel", Width = 80 }
                            }
                        }
                    }
                }
            };
            var outerSp = (Avalonia.Controls.StackPanel)confirmWin.Content!;
            var btnSp = (Avalonia.Controls.StackPanel)outerSp.Children[1];
            var okBtn = (Avalonia.Controls.Button)btnSp.Children[0];
            var cancelBtn = (Avalonia.Controls.Button)btnSp.Children[1];
            okBtn.Click += (_, _) => { result = true; confirmWin.Close(); };
            cancelBtn.Click += (_, _) => confirmWin.Close();
            await confirmWin.ShowDialog(mMainWin);
            return result;
        }
    }
}
