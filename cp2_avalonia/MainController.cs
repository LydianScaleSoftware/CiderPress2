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
using FileConv;

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

            UpdateTitle();
            ClearEntryCounts();
            mMainWin.LaunchPanelVisible = true;
            mMainWin.MainPanelVisible = false;

            // TODO: Avalonia clipboard — check if process owns clipboard and clear it.

            SaveAppSettings();

            GC.Collect();
            return true;
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

            // Restore left panel width; setting a fixed pixel value means only the center
            // column stretches when the window is resized (right panel is Width=Auto).
            mMainWin.LeftPanelWidth =
                settings.GetInt(AppSettings.MAIN_LEFT_PANEL_WIDTH, 300);

            UnpackRecentFileList();
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

            // Regenerate FileListItem for each moved entry (pathname fields may have changed).
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
                    if (obj is FileListItem fli) temp.Add(fli);
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
                        if (entry.IsDirectory) continue;
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
                if (e.IsDirectory) dirs.Add(e); else files.Add(e);
            }
            dirs.Sort((a, b) =>
                string.Compare(a.FullPathName, b.FullPathName, StringComparison.Ordinal));
            var result = new List<IFileEntry>(dirs.Count + files.Count);
            result.AddRange(dirs);
            result.AddRange(files);
            return result;
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
