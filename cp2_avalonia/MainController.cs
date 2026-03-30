/*
 * Copyright 2025 faddenSoft
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Input;

using AppCommon;
using CommonUtil;
using cp2_avalonia.Actions;
using cp2_avalonia.Common;
using DiskArc;
using DiskArc.Arc;
using DiskArc.FS;

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
        /// Handles selection change in the archive tree view.  Called from MainWindow event
        /// handler.
        /// </summary>
        internal void ArchiveTree_SelectionChanged(ArchiveTreeItem? newSel) {
            Debug.WriteLine("Archive tree selection now: " + (newSel == null ? "none" : newSel));
            if (newSel == null) {
                mMainWin.DirectoryTreeRoot.Clear();
                return;
            }

            mMainWin.DirectoryTreeRoot.Clear();

            object currentWorkObject = newSel.WorkTreeNode.DAObject;

            if (currentWorkObject is IFileSystem) {
                IFileSystem fs = (IFileSystem)currentWorkObject;
                Debug.Assert(fs.GetVolDirEntry() != IFileEntry.NO_ENTRY);
                PopulateDirectoryTree(null, mMainWin.DirectoryTreeRoot, fs.GetVolDirEntry());
                if (mMainWin.DirectoryTreeRoot.Count > 0) {
                    mMainWin.DirectoryTreeRoot[0].IsSelected = true;
                }
            } else {
                // Non-filesystem (archive, disk image, partition, etc.) — show a single
                // placeholder entry in the directory tree.
                string title;
                if (currentWorkObject is IArchive) {
                    title = "File Archive Entry List";
                } else if (currentWorkObject is DiskArc.IDiskImage) {
                    title = "Disk Image Information";
                } else if (currentWorkObject is DiskArc.IMultiPart) {
                    title = "Multi-Partition Information";
                } else if (currentWorkObject is DiskArc.Multi.Partition) {
                    title = "Partition Information";
                } else {
                    title = "Information";
                }
                DirectoryTreeItem newItem = new DirectoryTreeItem(null, title, IFileEntry.NO_ENTRY);
                mMainWin.DirectoryTreeRoot.Add(newItem);
                newItem.IsSelected = true;
            }
        }

        /// <summary>
        /// Populates the directory tree recursively from a filesystem volume directory.
        /// </summary>
        private void PopulateDirectoryTree(DirectoryTreeItem? parent,
                ObservableCollection<DirectoryTreeItem> items, IFileEntry dirEntry) {
            string name = dirEntry.FileName;
            DirectoryTreeItem newItem = new DirectoryTreeItem(parent, name, dirEntry);
            items.Add(newItem);

            foreach (IFileEntry child in dirEntry) {
                if (child.IsDirectory) {
                    PopulateDirectoryTree(newItem, newItem.Items, child);
                }
            }
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

        private void ClearEntryCounts() {
            mMainWin.CenterStatusText = string.Empty;
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
            // settings.SetInt(AppSettings.MAIN_LEFT_PANEL_WIDTH, (int)mMainWin.LeftPanelWidth);
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

            mMainWin.ShowDebugMenu = settings.GetBool(AppSettings.DEBUG_MENU_ENABLED, false);

            UnpackRecentFileList();
        }

        // -----------------------------------------------------------------------------------------
        // Error helpers

        private void ShowFileError(string msg) {
            // TODO: Iteration 5 — show Avalonia MessageBox.  For now just log it.
            Debug.WriteLine("ShowFileError: " + msg);
            AppHook.LogE("ShowFileError: " + msg);
        }
    }
}
