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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using Avalonia.Threading;

using AppCommon;
using CommonUtil;
using cp2_avalonia.Common;
using DiskArc;
using DiskArc.Multi;
using FileConv;

namespace cp2_avalonia {
    public partial class MainWindow : Window, INotifyPropertyChanged {

        private MainController mMainCtrl = null!;

        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ---- File menu commands ----
        public ICommand NewDiskImageCommand { get; }
        public ICommand NewFileArchiveCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand OpenPhysicalDriveCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand RecentFile1Command { get; }
        public ICommand RecentFile2Command { get; }
        public ICommand RecentFile3Command { get; }
        public ICommand RecentFile4Command { get; }
        public ICommand RecentFile5Command { get; }
        public ICommand RecentFile6Command { get; }
        public ICommand ExitCommand { get; }

        // ---- Edit menu commands ----
        public ICommand CopyCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand FindCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand EditAppSettingsCommand { get; }

        // ---- Actions menu commands ----
        public ICommand ViewFilesCommand { get; }
        public ICommand AddFilesCommand { get; }
        public ICommand ImportFilesCommand { get; }
        public ICommand ExtractFilesCommand { get; }
        public ICommand ExportFilesCommand { get; }
        public ICommand DeleteFilesCommand { get; }
        public ICommand TestFilesCommand { get; }
        public ICommand EditAttributesCommand { get; }
        public ICommand CreateDirectoryCommand { get; }
        public ICommand EditDirAttributesCommand { get; }
        public ICommand EditSectorsCommand { get; }
        public ICommand EditBlocksCommand { get; }
        public ICommand SaveAsDiskImageCommand { get; }
        public ICommand ReplacePartitionCommand { get; }
        public ICommand ScanForBadBlocksCommand { get; }
        public ICommand ScanForSubVolCommand { get; }
        public ICommand DefragmentCommand { get; }
        public ICommand CloseSubTreeCommand { get; }

        // ---- View menu commands ----
        public ICommand ShowFullListCommand { get; }
        public ICommand ShowDirListCommand { get; }
        public ICommand ShowInfoCommand { get; }

        // ---- Navigate menu commands ----
        public ICommand NavToParentDirCommand { get; }
        public ICommand NavToParentCommand { get; }

        // ---- Help menu commands ----
        public ICommand HelpCommand { get; }
        public ICommand AboutCommand { get; }

        // ---- Debug menu commands ----
        public ICommand Debug_DiskArcLibTestCommand { get; }
        public ICommand Debug_FileConvLibTestCommand { get; }
        public ICommand Debug_BulkCompressTestCommand { get; }
        public ICommand Debug_ShowSystemInfoCommand { get; }
        public ICommand Debug_ShowDebugLogCommand { get; }
        public ICommand Debug_ShowDropTargetCommand { get; }
        public ICommand Debug_ConvertANICommand { get; }

        // ---- Toolbar-only commands ----
        public ICommand ResetSortCommand { get; }
        public ICommand ToggleInfoCommand { get; }

        // ---- Bindable properties ----
        private bool mShowDebugMenu = false;  // set by ApplyAppSettings()
        public bool ShowDebugMenu {
            get => mShowDebugMenu;
            set { mShowDebugMenu = value; OnPropertyChanged(); }
        }

        private bool mIsDebugLogVisible;
        public bool IsDebugLogVisible {
            get => mIsDebugLogVisible;
            set { mIsDebugLogVisible = value; OnPropertyChanged(); }
        }

        private bool mIsDropTargetVisible;
        public bool IsDropTargetVisible {
            get => mIsDropTargetVisible;
            set { mIsDropTargetVisible = value; OnPropertyChanged(); }
        }

        // ---- Drag-drop state for file list (internal drag-move) ----
        private const string INTERNAL_DRAG_FORMAT = "cp2_avalonia/FileListDrag";
        private const double DRAG_THRESHOLD = 4.0;
        private bool mIsDraggingFileList;
        private List<IFileEntry> mDragMoveList = new List<IFileEntry>();
        private Point mDragStartPosn = new Point(-1, -1);

        // ---- Status bar ----
        private string mCenterStatusText = string.Empty;
        public string CenterStatusText {
            get => mCenterStatusText;
            set { mCenterStatusText = value; OnPropertyChanged(); }
        }

        private string mRightStatusText = string.Empty;
        public string RightStatusText {
            get => mRightStatusText;
            set { mRightStatusText = value; OnPropertyChanged(); }
        }

        // ---- Panel visibility ----
        private bool mLaunchPanelVisible = true;
        public bool LaunchPanelVisible {
            get => mLaunchPanelVisible;
            set { mLaunchPanelVisible = value; OnPropertyChanged(); }
        }

        private bool mMainPanelVisible = false;
        public bool MainPanelVisible {
            get => mMainPanelVisible;
            set { mMainPanelVisible = value; OnPropertyChanged(); }
        }

        // ---- Triptych panel column widths ----
        // Col 0 is set to a fixed pixel value so only the center column stretches on resize.
        // Reading Width.Value (not ActualWidth) is reliable even before the panel is rendered.
        public double LeftPanelWidth {
            get => mainTriptychPanel.ColumnDefinitions[0].Width.Value;
            set => mainTriptychPanel.ColumnDefinitions[0].Width = new GridLength(value);
        }

        // ---- Program version ----
        public string ProgramVersionString => GlobalAppVersion.AppVersion.ToString();

        // ---- Archive and directory trees ----
        public ObservableCollection<ArchiveTreeItem> ArchiveTreeRoot { get; } = new();
        public ObservableCollection<DirectoryTreeItem> DirectoryTreeRoot { get; } = new();

        // ---- Recent files ----
        private string mRecentFileName1 = string.Empty;
        private string mRecentFilePath1 = string.Empty;
        private string mRecentFileName2 = string.Empty;
        private string mRecentFilePath2 = string.Empty;

        public bool ShowRecentFile1 => !string.IsNullOrEmpty(mRecentFileName1);
        public string RecentFileName1 {
            get => mRecentFileName1;
            set { mRecentFileName1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowRecentFile1)); }
        }
        public string RecentFilePath1 {
            get => mRecentFilePath1;
            set { mRecentFilePath1 = value; OnPropertyChanged(); }
        }
        public bool ShowRecentFile2 => !string.IsNullOrEmpty(mRecentFileName2);
        public string RecentFileName2 {
            get => mRecentFileName2;
            set { mRecentFileName2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowRecentFile2)); }
        }
        public string RecentFilePath2 {
            get => mRecentFilePath2;
            set { mRecentFilePath2 = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Populates the File &gt; Recent Files submenu with the current list of recent paths.
        /// </summary>
        public void PopulateRecentFilesMenu(List<string> recentPaths) {
            if (recentFilesMenu == null) {
                return;
            }

            ICommand[] commands = {
                RecentFile1Command, RecentFile2Command, RecentFile3Command,
                RecentFile4Command, RecentFile5Command, RecentFile6Command
            };

            recentFilesMenu.Items.Clear();
            if (recentPaths.Count == 0) {
                var placeholder = new Avalonia.Controls.MenuItem { Header = "(none)" };
                recentFilesMenu.Items.Add(placeholder);
            } else {
                for (int i = 0; i < recentPaths.Count && i < commands.Length; i++) {
                    var mi = new Avalonia.Controls.MenuItem {
                        Header = $"_{i + 1}: {recentPaths[i]}",
                        Command = commands[i]
                    };
                    recentFilesMenu.Items.Add(mi);
                }
            }
        }

        // ---- Toolbar state: Add/Extract vs Import/Export mode ----
        // These are AppSettings-backed toggling properties.  Only write the setting when
        // the property is set to true, to prevent the RadioButton "false" feedback from
        // overwriting the stored value.
        public bool IsChecked_AddExtract {
            get => AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
            set {
                if (value) {
                    AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, true);
                    mMainCtrl.ClearClipboardIfPending();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChecked_ImportExport));
            }
        }
        public bool IsChecked_ImportExport {
            get => !AppSettings.Global.GetBool(AppSettings.DDCP_ADD_EXTRACT, true);
            set {
                if (value) {
                    AppSettings.Global.SetBool(AppSettings.DDCP_ADD_EXTRACT, false);
                    mMainCtrl.ClearClipboardIfPending();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChecked_AddExtract));
            }
        }

        // ---- Add/Import options ----
        public bool IsChecked_AddCompress {
            get => AppSettings.Global.GetBool(AppSettings.ADD_COMPRESS_ENABLED, true);
            set { AppSettings.Global.SetBool(AppSettings.ADD_COMPRESS_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddRaw {
            get => AppSettings.Global.GetBool(AppSettings.ADD_RAW_ENABLED, false);
            set { AppSettings.Global.SetBool(AppSettings.ADD_RAW_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddRecurse {
            get => AppSettings.Global.GetBool(AppSettings.ADD_RECURSE_ENABLED, true);
            set { AppSettings.Global.SetBool(AppSettings.ADD_RECURSE_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddStripExt {
            get => AppSettings.Global.GetBool(AppSettings.ADD_STRIP_EXT_ENABLED, true);
            set { AppSettings.Global.SetBool(AppSettings.ADD_STRIP_EXT_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddStripPaths {
            get => AppSettings.Global.GetBool(AppSettings.ADD_STRIP_PATHS_ENABLED, false);
            set { AppSettings.Global.SetBool(AppSettings.ADD_STRIP_PATHS_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddPreserveADF {
            get => AppSettings.Global.GetBool(AppSettings.ADD_PRESERVE_ADF, true);
            set { AppSettings.Global.SetBool(AppSettings.ADD_PRESERVE_ADF, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddPreserveAS {
            get => AppSettings.Global.GetBool(AppSettings.ADD_PRESERVE_AS, true);
            set { AppSettings.Global.SetBool(AppSettings.ADD_PRESERVE_AS, value); OnPropertyChanged(); }
        }
        public bool IsChecked_AddPreserveNAPS {
            get => AppSettings.Global.GetBool(AppSettings.ADD_PRESERVE_NAPS, true);
            set { AppSettings.Global.SetBool(AppSettings.ADD_PRESERVE_NAPS, value); OnPropertyChanged(); }
        }

        // ---- Extract/Export options ----
        public bool IsChecked_ExtAddExportExt {
            get => AppSettings.Global.GetBool(AppSettings.EXT_ADD_EXPORT_EXT, true);
            set { AppSettings.Global.SetBool(AppSettings.EXT_ADD_EXPORT_EXT, value); OnPropertyChanged(); }
        }
        public bool IsChecked_ExtRaw {
            get => AppSettings.Global.GetBool(AppSettings.EXT_RAW_ENABLED, false);
            set { AppSettings.Global.SetBool(AppSettings.EXT_RAW_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_ExtStripPaths {
            get => AppSettings.Global.GetBool(AppSettings.EXT_STRIP_PATHS_ENABLED, false);
            set { AppSettings.Global.SetBool(AppSettings.EXT_STRIP_PATHS_ENABLED, value); OnPropertyChanged(); }
        }
        public bool IsChecked_ExtPreserveNone {
            get => AppSettings.Global.GetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.None) == ExtractFileWorker.PreserveMode.None;
            set { if (value) AppSettings.Global.SetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.None); OnPropertyChanged(); }
        }
        public bool IsChecked_ExtPreserveAS {
            get => AppSettings.Global.GetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.None) == ExtractFileWorker.PreserveMode.AS;
            set { if (value) AppSettings.Global.SetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.AS); OnPropertyChanged(); }
        }
        public bool IsChecked_ExtPreserveADF {
            get => AppSettings.Global.GetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.None) == ExtractFileWorker.PreserveMode.ADF;
            set { if (value) AppSettings.Global.SetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.ADF); OnPropertyChanged(); }
        }
        public bool IsChecked_ExtPreserveNAPS {
            get => AppSettings.Global.GetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.None) == ExtractFileWorker.PreserveMode.NAPS;
            set { if (value) AppSettings.Global.SetEnum(AppSettings.EXT_PRESERVE_MODE,
                ExtractFileWorker.PreserveMode.NAPS); OnPropertyChanged(); }
        }

        // ---- Export/Import converter configuration ----
        public class ConvItem {
            public string Tag { get; }
            public string Label { get; }
            public ConvItem(string tag, string label) { Tag = tag; Label = label; }
        }
        public List<ConvItem> ImportConverters { get; } = new List<ConvItem>();
        public List<ConvItem> ExportConverters { get; } = new List<ConvItem>();

        private ConvItem? mSelectedImportConverter;
        public ConvItem? SelectedImportConverter {
            get => mSelectedImportConverter;
            set {
                mSelectedImportConverter = value;
                if (value != null) {
                    AppSettings.Global.SetString(AppSettings.CONV_IMPORT_TAG, value.Tag);
                }
                OnPropertyChanged();
            }
        }

        private ConvItem? mSelectedExportConverter;
        public ConvItem? SelectedExportConverter {
            get => mSelectedExportConverter;
            set {
                mSelectedExportConverter = value;
                if (value != null && !IsExportBestChecked) {
                    AppSettings.Global.SetString(AppSettings.CONV_EXPORT_TAG, value.Tag);
                }
                OnPropertyChanged();
            }
        }

        public bool IsExportBestChecked {
            get => AppSettings.Global.GetBool(AppSettings.CONV_EXPORT_BEST, true);
            set {
                if (value) AppSettings.Global.SetBool(AppSettings.CONV_EXPORT_BEST, true);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExportComboChecked));
            }
        }
        public bool IsExportComboChecked {
            get => !AppSettings.Global.GetBool(AppSettings.CONV_EXPORT_BEST, true);
            set {
                if (value) AppSettings.Global.SetBool(AppSettings.CONV_EXPORT_BEST, false);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExportBestChecked));
            }
        }

        /// <summary>
        /// Populates ImportConverters and ExportConverters lists and sets defaults.
        /// </summary>
        private void InitImportExportConfig() {
            for (int i = 0; i < ImportFoundry.GetCount(); i++) {
                ImportFoundry.GetConverterInfo(i, out string tag, out string label,
                    out string _, out _);
                ImportConverters.Add(new ConvItem(tag, label));
            }
            ImportConverters.Sort((a, b) => string.Compare(a.Label, b.Label));

            for (int i = 0; i < ExportFoundry.GetCount(); i++) {
                ExportFoundry.GetConverterInfo(i, out string tag, out string label,
                    out string _, out _);
                ExportConverters.Add(new ConvItem(tag, label));
            }
            ExportConverters.Sort((a, b) => string.Compare(a.Label, b.Label));

            // Set initial selection — may be overwritten when settings are loaded.
            if (ImportConverters.Count > 0) mSelectedImportConverter = ImportConverters[0];
            if (ExportConverters.Count > 0) mSelectedExportConverter = ExportConverters[0];
        }

        /// <summary>
        /// Triggers OnPropertyChanged for all options panel bindings, ensuring the UI
        /// reflects the current AppSettings values.
        /// </summary>
        public void PublishSideOptions() {
            OnPropertyChanged(nameof(IsChecked_AddCompress));
            OnPropertyChanged(nameof(IsChecked_AddRaw));
            OnPropertyChanged(nameof(IsChecked_AddRecurse));
            OnPropertyChanged(nameof(IsChecked_AddStripExt));
            OnPropertyChanged(nameof(IsChecked_AddStripPaths));
            OnPropertyChanged(nameof(IsChecked_AddPreserveADF));
            OnPropertyChanged(nameof(IsChecked_AddPreserveAS));
            OnPropertyChanged(nameof(IsChecked_AddPreserveNAPS));
            OnPropertyChanged(nameof(IsChecked_ExtAddExportExt));
            OnPropertyChanged(nameof(IsChecked_ExtRaw));
            OnPropertyChanged(nameof(IsChecked_ExtStripPaths));
            OnPropertyChanged(nameof(IsChecked_ExtPreserveNone));
            OnPropertyChanged(nameof(IsChecked_ExtPreserveAS));
            OnPropertyChanged(nameof(IsChecked_ExtPreserveADF));
            OnPropertyChanged(nameof(IsChecked_ExtPreserveNAPS));
            OnPropertyChanged(nameof(IsExportBestChecked));
            OnPropertyChanged(nameof(IsExportComboChecked));
            OnPropertyChanged(nameof(IsChecked_AddExtract));
            OnPropertyChanged(nameof(IsChecked_ImportExport));

            // Restore selected import/export converter from settings.
            string importTag = AppSettings.Global.GetString(AppSettings.CONV_IMPORT_TAG, string.Empty);
            if (!string.IsNullOrEmpty(importTag)) {
                var item = ImportConverters.Find(c => c.Tag == importTag);
                if (item != null) mSelectedImportConverter = item;
            }
            string exportTag = AppSettings.Global.GetString(AppSettings.CONV_EXPORT_TAG, string.Empty);
            if (!string.IsNullOrEmpty(exportTag)) {
                var item = ExportConverters.Find(c => c.Tag == exportTag);
                if (item != null) mSelectedExportConverter = item;
            }
            OnPropertyChanged(nameof(SelectedImportConverter));
            OnPropertyChanged(nameof(SelectedExportConverter));
        }

        // TODO (Iteration 15): Replace with a theme-aware accent brush for dark mode support,
        // e.g. Application.Current.Resources["SystemAccentColor"] or a DynamicResource.
        private static readonly IBrush ToolbarHighlightBrush = Brushes.Green;
        private static readonly IBrush ToolbarNohiBrush = Brushes.Transparent;

        private IBrush mFullListBorderBrush = Brushes.Transparent;
        public IBrush FullListBorderBrush {
            get => mFullListBorderBrush;
            set { mFullListBorderBrush = value; OnPropertyChanged(); }
        }

        private IBrush mDirListBorderBrush = Brushes.Transparent;
        public IBrush DirListBorderBrush {
            get => mDirListBorderBrush;
            set { mDirListBorderBrush = value; OnPropertyChanged(); }
        }

        private IBrush mInfoBorderBrush = Brushes.Transparent;
        public IBrush InfoBorderBrush {
            get => mInfoBorderBrush;
            set { mInfoBorderBrush = value; OnPropertyChanged(); }
        }

        // ---- File list ----
        public ObservableCollection<FileListItem> FileList { get; } = new();
        public FileListItem? SelectedFileListItem {
            get => fileListDataGrid?.SelectedItem as FileListItem;
            set { if (fileListDataGrid != null) fileListDataGrid.SelectedItem = value; }
        }

        // ---- Tree selection helpers (read-only) ----
        public ArchiveTreeItem? SelectedArchiveTreeItem =>
            archiveTree?.SelectedItem as ArchiveTreeItem;
        public DirectoryTreeItem? SelectedDirectoryTreeItem =>
            directoryTree?.SelectedItem as DirectoryTreeItem;

        // ---- Center panel toggle ----
        public enum CenterPanelChange { Unknown = 0, Files, Info, Toggle }
        private bool mShowCenterInfo;
        public bool ShowCenterFileList => !mShowCenterInfo;
        public bool ShowCenterInfoPanel => mShowCenterInfo;
        private bool mHasInfoOnly;
        private bool HasInfoOnly {
            get => mHasInfoOnly;
            set { mHasInfoOnly = value; }
        }

        private void SetShowCenterInfo(CenterPanelChange req) {
            if (HasInfoOnly && req != CenterPanelChange.Info) {
                Debug.WriteLine("Ignoring attempt to switch to file list");
                return;
            }
            switch (req) {
                case CenterPanelChange.Info:   mShowCenterInfo = true;  break;
                case CenterPanelChange.Files:  mShowCenterInfo = false; break;
                case CenterPanelChange.Toggle: mShowCenterInfo = !mShowCenterInfo; break;
            }
            OnPropertyChanged(nameof(ShowCenterFileList));
            OnPropertyChanged(nameof(ShowCenterInfoPanel));
            if (mShowCenterInfo) {
                InfoBorderBrush = ToolbarHighlightBrush;
                FullListBorderBrush = DirListBorderBrush = ToolbarNohiBrush;
            } else if (ShowSingleDirFileList) {
                DirListBorderBrush = ToolbarHighlightBrush;
                FullListBorderBrush = InfoBorderBrush = ToolbarNohiBrush;
            } else {
                FullListBorderBrush = ToolbarHighlightBrush;
                DirListBorderBrush = InfoBorderBrush = ToolbarNohiBrush;
            }
        }

        private bool mIsFullListEnabled;
        public bool IsFullListEnabled {
            get => mIsFullListEnabled;
            set { mIsFullListEnabled = value; OnPropertyChanged(); }
        }

        private bool mIsDirListEnabled;
        public bool IsDirListEnabled {
            get => mIsDirListEnabled;
            set { mIsDirListEnabled = value; OnPropertyChanged(); }
        }

        private bool mIsResetSortEnabled;
        public bool IsResetSortEnabled {
            get => mIsResetSortEnabled;
            set { mIsResetSortEnabled = value; OnPropertyChanged(); }
        }

        // ---- Single-dir vs full-list mode ----
        private bool mShowSingleDirFileList;
        public bool ShowSingleDirFileList {
            get => mShowSingleDirFileList;
            set {
                mShowSingleDirFileList = value;
                ShowCol_FileName = value;
                ShowCol_PathName = !value;
            }
        }

        private bool PreferSingleDirList {
            get => AppSettings.Global.GetBool(AppSettings.FILE_LIST_PREFER_SINGLE, true);
            set => AppSettings.Global.SetBool(AppSettings.FILE_LIST_PREFER_SINGLE, value);
        }

        // ---- Column visibility (also set via SetColumnVisible in code-behind) ----
        private bool mShowCol_FileName;
        public bool ShowCol_FileName {
            get => mShowCol_FileName;
            set { mShowCol_FileName = value; OnPropertyChanged(); SetColumnVisible("Filename", value); }
        }

        private bool mShowCol_PathName;
        public bool ShowCol_PathName {
            get => mShowCol_PathName;
            set { mShowCol_PathName = value; OnPropertyChanged(); SetColumnVisible("Pathname", value); }
        }

        private bool mShowCol_Format;
        public bool ShowCol_Format {
            get => mShowCol_Format;
            set { mShowCol_Format = value; OnPropertyChanged(); SetColumnVisible("Data Fmt", value); }
        }

        private bool mShowCol_RawLen;
        public bool ShowCol_RawLen {
            get => mShowCol_RawLen;
            set { mShowCol_RawLen = value; OnPropertyChanged(); SetColumnVisible("Raw Len", value); }
        }

        private bool mShowCol_RsrcLen;
        public bool ShowCol_RsrcLen {
            get => mShowCol_RsrcLen;
            set { mShowCol_RsrcLen = value; OnPropertyChanged(); SetColumnVisible("Rsrc Len", value); }
        }

        private bool mShowCol_TotalSize;
        public bool ShowCol_TotalSize {
            get => mShowCol_TotalSize;
            set { mShowCol_TotalSize = value; OnPropertyChanged(); SetColumnVisible("Total Size", value); }
        }

        /// <summary>
        /// Configures column visibility based on the type of content being shown.
        /// </summary>
        public void ConfigureCenterPanel(bool isInfoOnly, bool isArchive, bool isHierarchic,
                bool hasRsrc, bool hasRaw) {
            ShowSingleDirFileList = !(isArchive || (isHierarchic && !PreferSingleDirList));
            HasInfoOnly = isInfoOnly;
            if (HasInfoOnly) {
                SetShowCenterInfo(CenterPanelChange.Info);
            } else {
                SetShowCenterInfo(CenterPanelChange.Files);
            }
            if (isInfoOnly) {
                IsFullListEnabled = IsDirListEnabled = false;
            } else if (isArchive) {
                IsFullListEnabled = true;
                IsDirListEnabled = false;
            } else if (isHierarchic) {
                IsFullListEnabled = IsDirListEnabled = true;
            } else {
                IsFullListEnabled = false;
                IsDirListEnabled = true;
            }
            ShowCol_Format = isArchive;
            ShowCol_RawLen = hasRaw;
            ShowCol_RsrcLen = hasRsrc;
            ShowCol_TotalSize = !isArchive;
        }

        private void SetColumnVisible(string header, bool visible) {
            if (fileListDataGrid == null) {
                return;
            }
            foreach (DataGridColumn col in fileListDataGrid.Columns) {
                if (col.Header?.ToString() == header) {
                    col.IsVisible = visible;
                    return;
                }
            }
        }

        // ---- Center info panel content ----
        private string mCenterInfoText1 = string.Empty;
        public string CenterInfoText1 {
            get => mCenterInfoText1;
            set { mCenterInfoText1 = value; OnPropertyChanged(); }
        }

        private string mCenterInfoText2 = string.Empty;
        public string CenterInfoText2 {
            get => mCenterInfoText2;
            set { mCenterInfoText2 = value; OnPropertyChanged(); }
        }

        public class CenterInfoItem {
            public string Name { get; }
            public string Value { get; }
            public CenterInfoItem(string name, string value) { Name = name; Value = value; }
        }
        public ObservableCollection<CenterInfoItem> CenterInfoList { get; } = new();

        public void ClearCenterInfo() {
            ShowDiskUtilityButtons = false;
            PartitionList.Clear();
            ShowPartitionLayout = false;
            NotesList.Clear();
            ShowNotes = false;
            MetadataList.Clear();
            ShowMetadata = false;
            CanAddMetadataEntry = false;
        }

        // ---- Info panel sub-sections ----
        private bool mShowDiskUtilityButtons;
        public bool ShowDiskUtilityButtons {
            get => mShowDiskUtilityButtons;
            set { mShowDiskUtilityButtons = value; OnPropertyChanged(); }
        }

        private bool mShowPartitionLayout;
        public bool ShowPartitionLayout {
            get => mShowPartitionLayout;
            set { mShowPartitionLayout = value; OnPropertyChanged(); }
        }

        public class PartitionListItem {
            public int Index { get; }
            public long StartBlock { get; }
            public long BlockCount { get; }
            public string PartName { get; }
            public string PartType { get; }
            public Partition PartRef { get; }
            public PartitionListItem(int index, Partition part) {
                PartRef = part;
                Index = index;
                StartBlock = part.StartOffset / Defs.BLOCK_SIZE;
                BlockCount = part.Length / Defs.BLOCK_SIZE;
                PartName = string.Empty;
                PartType = string.Empty;
            }
            public override string ToString() {
                return "[Part: start=" + StartBlock + " count=" + BlockCount + "]";
            }
        }
        public ObservableCollection<PartitionListItem> PartitionList { get; } = new();

        public void SetPartitionList(IMultiPart parts) {
            PartitionList.Clear();
            for (int i = 0; i < parts.Count; i++) {
                PartitionList.Add(new PartitionListItem(i + 1, parts[i]));
            }
            ShowPartitionLayout = (PartitionList.Count > 0);
        }

        private bool mShowNotes;
        public bool ShowNotes {
            get => mShowNotes;
            set { mShowNotes = value; OnPropertyChanged(); }
        }
        public ObservableCollection<Notes.Note> NotesList { get; } = new();

        public void SetNotesList(Notes notes) {
            NotesList.Clear();
            foreach (Notes.Note note in notes.GetNotes()) {
                NotesList.Add(note);
            }
            ShowNotes = (notes.Count > 0);
        }

        public class MetadataItem : INotifyPropertyChanged {
            public string Key { get; private set; }
            public string Value { get; private set; }
            public string? Description { get; private set; }
            public string? ValueSyntax { get; private set; }
            public bool CanEdit { get; private set; }

            public MetadataItem(string key, string value, string description,
                    string valueSyntax, bool canEdit) {
                Key = key;
                Value = value;
                Description = string.IsNullOrEmpty(description) ? null : description;
                ValueSyntax = string.IsNullOrEmpty(valueSyntax) ? null : valueSyntax;
                CanEdit = canEdit;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            public void SetValue(string value) {
                Value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public ObservableCollection<MetadataItem> MetadataList { get; } = new();

        private bool mShowMetadata;
        public bool ShowMetadata {
            get => mShowMetadata;
            set { mShowMetadata = value; OnPropertyChanged(); }
        }

        private bool mCanAddMetadataEntry;
        public bool CanAddMetadataEntry {
            get => mCanAddMetadataEntry;
            set { mCanAddMetadataEntry = value; OnPropertyChanged(); }
        }

        public void SetMetadataList(IMetadata metaObj) {
            MetadataList.Clear();
            List<IMetadata.MetaEntry> entries = metaObj.GetMetaEntries();
            foreach (IMetadata.MetaEntry met in entries) {
                string? value = metaObj.GetMetaValue(met.Key, true);
                if (value == null) {
                    value = "!NOT FOUND!";
                }
                MetadataList.Add(new MetadataItem(met.Key, value, met.Description,
                    met.ValueSyntax, met.CanEdit));
            }
            ShowMetadata = true;
            CanAddMetadataEntry = metaObj.CanAddNewEntries;
        }

        public void UpdateMetadata(string key, string value) {
            foreach (MetadataItem item in MetadataList) {
                if (item.Key == key) {
                    item.SetValue(value);
                    break;
                }
            }
        }

        public void AddMetadata(IMetadata.MetaEntry met, string value) {
            MetadataList.Add(new MetadataItem(met.Key, value, met.Description,
                met.ValueSyntax, met.CanEdit));
        }

        public void RemoveMetadata(string key) {
            for (int i = 0; i < MetadataList.Count; i++) {
                if (MetadataList[i].Key == key) {
                    MetadataList.RemoveAt(i);
                    return;
                }
            }
            Debug.Assert(false, "Key not found: " + key);
        }

        // ---- Scroll / focus helpers ----
        public void FileList_ScrollToTop() {
            if (FileList.Count > 0) {
                fileListDataGrid.ScrollIntoView(FileList[0], null);
            }
        }

        public void FileList_SetSelectionFocus() {
            int idx = fileListDataGrid.SelectedIndex;
            if (idx >= 0 && idx < FileList.Count) {
                fileListDataGrid.ScrollIntoView(FileList[idx], null);
            }
        }

        public void DirectoryTree_ScrollToTop() {
            // TODO: Avalonia TreeView does not expose a direct ScrollToTop() method.
        }

        public MainWindow() {
            // Initialize commands before InitializeComponent (AXAML bindings need them).
            ExitCommand = new RelayCommand(() => Close());
            HelpCommand = new RelayCommand(() => {
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = "https://ciderpress2.com/gui-manual/",
                        UseShellExecute = true
                    });
                } catch (Exception) {
                    // Ignore failures (e.g., no browser available).
                }
            });
            AboutCommand = new RelayCommand(async () => {
                var dialog = new AboutBox();
                await dialog.ShowDialog(this);
            });

            NewDiskImageCommand = new RelayCommand(async () => {
                try { await mMainCtrl.NewDiskImage(); }
                catch (Exception ex) { Debug.WriteLine("NewDiskImage exception: " + ex.Message); }
            });
            NewFileArchiveCommand = new RelayCommand(async () => {
                try { await mMainCtrl.NewFileArchive(); }
                catch (Exception ex) { Debug.WriteLine("NewFileArchive exception: " + ex.Message); }
            });
            OpenCommand = new RelayCommand(async () => await mMainCtrl.OpenWorkFile());
            OpenPhysicalDriveCommand = new RelayCommand(() => NotImplemented("Open Physical Drive"));
            CloseCommand = new RelayCommand(
                () => mMainCtrl.CloseWorkFile(),
                () => mMainCtrl?.IsFileOpen ?? false);
            RecentFile1Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(0));
            RecentFile2Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(1));
            RecentFile3Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(2));
            RecentFile4Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(3));
            RecentFile5Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(4));
            RecentFile6Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(5));

            CopyCommand = new RelayCommand(
                async () => { try { await mMainCtrl.CopyToClipboard(); }
                              catch (Exception ex) { Debug.WriteLine("Copy failed: " + ex); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                      mMainCtrl.AreFileEntriesSelected && ShowCenterFileList);
            PasteCommand = new RelayCommand(
                async () => { try {
                    // If a directory is selected in the file list, use it as the paste target.
                    IFileEntry pasteTarget = IFileEntry.NO_ENTRY;
                    FileListItem? selItem = SelectedFileListItem;
                    if (selItem != null && selItem.FileEntry.IsDirectory) {
                        pasteTarget = selItem.FileEntry;
                    }
                    await mMainCtrl.PasteOrDrop(null, pasteTarget);
                } catch (Exception ex) { Debug.WriteLine("Paste failed: " + ex); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite
                      && mMainCtrl.IsMultiFileItemSelected);
            FindCommand = new RelayCommand(
                async () => { try { await mMainCtrl.FindFiles(); } catch (Exception ex) {
                    Debug.WriteLine("FindFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.AreFileEntriesSelected);
            SelectAllCommand = new RelayCommand(
                () => fileListDataGrid.SelectAll(),
                () => mMainCtrl?.IsFileOpen ?? false);
            EditAppSettingsCommand = new RelayCommand(
                async () => { try { await mMainCtrl.EditAppSettings(); } catch (Exception ex) {
                    Debug.WriteLine("EditAppSettings exception: " + ex.Message); } });

            ViewFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.ViewFiles(); } catch (Exception ex) {
                    Debug.WriteLine("ViewFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.AreFileEntriesSelected && ShowCenterFileList);
            AddFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.AddFiles(); } catch (Exception ex) {
                    Debug.WriteLine("AddFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
                     mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList);
            ImportFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.ImportFiles(); } catch (Exception ex) {
                    Debug.WriteLine("ImportFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
                     mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList);
            ExtractFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.ExtractFiles(); } catch (Exception ex) {
                    Debug.WriteLine("ExtractFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && ShowCenterFileList &&
                     mMainCtrl.AreFileEntriesSelected);
            ExportFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.ExportFiles(); } catch (Exception ex) {
                    Debug.WriteLine("ExportFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && ShowCenterFileList &&
                     mMainCtrl.AreFileEntriesSelected);
            DeleteFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.DeleteFiles(); } catch (Exception ex) {
                    Debug.WriteLine("DeleteFiles exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite &&
                     mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList &&
                     mMainCtrl.AreFileEntriesSelected);
            TestFilesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.TestFiles(); }
                              catch (Exception ex) { Debug.WriteLine("TestFiles failed: " + ex); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && ShowCenterFileList
                     && mMainCtrl.AreFileEntriesSelected);
            EditAttributesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.EditAttributes(); } catch (Exception ex) {
                    Debug.WriteLine("EditAttributes exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsSingleEntrySelected);
            CreateDirectoryCommand = new RelayCommand(
                async () => await mMainCtrl.CreateDirectory(),
                () => mMainCtrl.IsHierarchicalFileSystemSelected && mMainCtrl.CanWrite);
            EditDirAttributesCommand = new RelayCommand(
                async () => { try { await mMainCtrl.EditDirAttributes(); } catch (Exception ex) {
                    Debug.WriteLine("EditDirAttributes exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsFileSystemSelected);
            EditSectorsCommand = new RelayCommand(
                async () => { try { await mMainCtrl.EditBlocksSectors(EditSector.SectorEditMode.Sectors); }
                    catch (Exception ex) { Debug.WriteLine("EditSectors failed: " + ex); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanEditSectors);
            EditBlocksCommand = new RelayCommand(
                async () => { try { await mMainCtrl.EditBlocksSectors(EditSector.SectorEditMode.Blocks); }
                    catch (Exception ex) { Debug.WriteLine("EditBlocks failed: " + ex); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanEditBlocks);
            SaveAsDiskImageCommand = new RelayCommand(
                async () => { try { await mMainCtrl.SaveAsDiskImage(); } catch (Exception ex) {
                    Debug.WriteLine("SaveAsDiskImage exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsDiskOrPartitionSelected && mMainCtrl.HasChunks);
            ReplacePartitionCommand = new RelayCommand(
                async () => { try { await mMainCtrl.ReplacePartition(); } catch (Exception ex) {
                    Debug.WriteLine("ReplacePartition exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.CanWrite && mMainCtrl.IsPartitionSelected);
            ScanForBadBlocksCommand = new RelayCommand(() => NotImplemented("Scan for Bad Blocks"), () => false);
            ScanForSubVolCommand = new RelayCommand(
                () => mMainCtrl.ScanForSubVol(),
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsFileSystemSelected);
            DefragmentCommand = new RelayCommand(
                async () => { try { await mMainCtrl.Defragment(); }
                              catch (Exception ex) { Debug.WriteLine("Defragment exception: " + ex.Message); } },
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsDefragmentableSelected && mMainCtrl.CanWrite);
            CloseSubTreeCommand = new RelayCommand(
                () => mMainCtrl.CloseSubTree(),
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsClosableTreeSelected);

            ShowFullListCommand = new RelayCommand(
                () => {
                    PreferSingleDirList = false;
                    if (ShowSingleDirFileList) {
                        ShowSingleDirFileList = false;
                        mMainCtrl.PopulateFileList(IFileEntry.NO_ENTRY, false);
                    }
                    SetShowCenterInfo(CenterPanelChange.Files);
                },
                () => IsFullListEnabled);
            ShowDirListCommand = new RelayCommand(
                () => {
                    PreferSingleDirList = true;
                    if (!ShowSingleDirFileList) {
                        ShowSingleDirFileList = true;
                        mMainCtrl.PopulateFileList(IFileEntry.NO_ENTRY, false);
                    }
                    SetShowCenterInfo(CenterPanelChange.Files);
                },
                () => IsDirListEnabled);
            ShowInfoCommand = new RelayCommand(
                () => SetShowCenterInfo(CenterPanelChange.Info),
                () => mMainCtrl?.IsFileOpen ?? false);

            NavToParentDirCommand = new RelayCommand(
                () => mMainCtrl.NavToParent(true),
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     mMainCtrl.IsHierarchicalFileSystemSelected && !mMainCtrl.IsSelectedDirRoot);
            NavToParentCommand = new RelayCommand(
                () => mMainCtrl.NavToParent(false),
                () => mMainCtrl != null && mMainCtrl.IsFileOpen &&
                     ((mMainCtrl.IsHierarchicalFileSystemSelected && !mMainCtrl.IsSelectedDirRoot) ||
                      !mMainCtrl.IsSelectedArchiveRoot));

            Debug_DiskArcLibTestCommand = new RelayCommand(
                async () => { try { await mMainCtrl.Debug_DiskArcLibTests(); }
                              catch (Exception ex) { Debug.WriteLine("DiskArcLibTests failed: " + ex); } });
            Debug_FileConvLibTestCommand = new RelayCommand(
                async () => { try { await mMainCtrl.Debug_FileConvLibTests(); }
                              catch (Exception ex) { Debug.WriteLine("FileConvLibTests failed: " + ex); } });
            Debug_BulkCompressTestCommand = new RelayCommand(
                async () => { try { await mMainCtrl.Debug_BulkCompressTest(); }
                              catch (Exception ex) { Debug.WriteLine("BulkCompressTest failed: " + ex); } });
            Debug_ShowSystemInfoCommand = new RelayCommand(() => mMainCtrl.Debug_ShowSystemInfo());
            Debug_ShowDebugLogCommand = new RelayCommand(() => {
                mMainCtrl.Debug_ShowDebugLog();
                IsDebugLogVisible = mMainCtrl.IsDebugLogOpen;
            });
            Debug_ShowDropTargetCommand = new RelayCommand(() => {
                mMainCtrl.Debug_ShowDropTarget();
                IsDropTargetVisible = mMainCtrl.IsDropTargetOpen;
            });
            Debug_ConvertANICommand = new RelayCommand(
                async () => { try { await mMainCtrl.Debug_ConvertANI(); }
                              catch (Exception ex) { Debug.WriteLine("ConvertANI failed: " + ex); } },
                () => mMainCtrl.IsANISelected);

            ResetSortCommand = new RelayCommand(
                () => {
                    foreach (DataGridColumn col in fileListDataGrid.Columns) {
                        // Avalonia 11 DataGridColumn has no public SortDirection setter;
                        // clear the tracked tag so the next click defaults to ascending.
                        col.Tag = null;
                    }
                    mMainCtrl.PopulateFileList(IFileEntry.NO_ENTRY, false);
                    IsResetSortEnabled = false;
                },
                () => IsResetSortEnabled);
            ToggleInfoCommand = new RelayCommand(
                () => SetShowCenterInfo(CenterPanelChange.Toggle),
                () => mMainCtrl?.IsFileOpen ?? false);

            InitializeComponent();
            DataContext = this;

            // Physical drive access is Windows-only; hide the menu item on other platforms.
            if (!OperatingSystem.IsWindows() && openPhysicalDriveMenuItem != null) {
                openPhysicalDriveMenuItem.IsVisible = false;
            }

            mMainCtrl = new MainController(this);
            WindowPlacement.TrackNormalBounds(this);
            Loaded += (s, e) => {
                mMainCtrl.WindowLoaded();
                InitImportExportConfig();
                // Register drag-drop on launch panel after the AXAML tree is built.
                launchPanel.AddHandler(DragDrop.DropEvent, LaunchPanel_Drop);
                launchPanel.AddHandler(DragDrop.DragOverEvent, LaunchPanel_DragOver);
                // File list drag-drop (drop from OS file manager + internal move).
                fileListDataGrid.AddHandler(DragDrop.DropEvent, FileListDataGrid_Drop);
                fileListDataGrid.AddHandler(DragDrop.DragOverEvent, FileListDataGrid_DragOver);
                fileListDataGrid.AddHandler(PointerPressedEvent, FileListDataGrid_PointerPressed,
                    RoutingStrategies.Tunnel);
                fileListDataGrid.AddHandler(PointerMovedEvent, FileListDataGrid_PointerMoved,
                    RoutingStrategies.Tunnel);
                // Directory tree drag-drop (internal move + drop from OS file manager).
                directoryTree.AddHandler(DragDrop.DropEvent, DirectoryTree_Drop);
                directoryTree.AddHandler(DragDrop.DragOverEvent, DirectoryTree_DragOver);
                // File list panel (parent Grid) catches drops on empty space below rows.
                fileListPanel.AddHandler(DragDrop.DropEvent, FileListPanel_Drop);
                fileListPanel.AddHandler(DragDrop.DragOverEvent, FileListPanel_DragOver);
            };
            Closing += (s, e) => mMainCtrl.WindowClosing();
        }

        // ---- Drag-drop on launch panel ----

        private void LaunchPanel_DragOver(object? sender, DragEventArgs e) {
            if (e.Data.Contains(DataFormats.Files)) {
                var files = e.Data.GetFiles()?.ToList();
                if (files?.Count == 1) {
                    e.DragEffects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }

        private void LaunchPanel_Drop(object? sender, DragEventArgs e) {
            if (e.Data.Contains(DataFormats.Files)) {
                var files = e.Data.GetFiles()?.ToList();
                if (files?.Count == 1) {
                    string? path = files[0].TryGetLocalPath();
                    if (path != null) {
                        // Fire-and-forget: DropOpenWorkFile is async but Drop handler is void.
                        _ = mMainCtrl.DropOpenWorkFile(path);
                    }
                }
            }
        }

        // ---- Toast notification (PostNotification) ----

        private DispatcherTimer? mToastTimer;

        /// <summary>
        /// Shows a brief toast notification at the bottom of the window.
        /// </summary>
        /// <param name="msg">Message to display.</param>
        /// <param name="success">True for a success (green) tint, false for error (red).</param>
        public void PostNotification(string msg, bool success) {
            toastText.Text = msg;
            toastBorder.Background = success
                ? new SolidColorBrush(Color.FromArgb(200, 0, 160, 0))
                : new SolidColorBrush(Color.FromArgb(200, 200, 0, 0));
            toastBorder.IsVisible = true;

            mToastTimer?.Stop();
            mToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            mToastTimer.Tick += (_, _) => {
                toastBorder.IsVisible = false;
                mToastTimer?.Stop();
            };
            mToastTimer.Start();
        }

        private void ArchiveTree_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (archiveTree.SelectedItem is ArchiveTreeItem item) {
                mMainCtrl.ArchiveTree_SelectionChanged(item);
            } else {
                mMainCtrl.ArchiveTree_SelectionChanged(null);
            }
        }

        private void DirectoryTree_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (directoryTree.SelectedItem is DirectoryTreeItem item) {
                mMainCtrl.DirectoryTree_SelectionChanged(item);
            } else {
                mMainCtrl.DirectoryTree_SelectionChanged(null);
            }
        }

        private void FileListDataGrid_DoubleTapped(object? sender, TappedEventArgs e) {
            mMainCtrl.HandleFileListDoubleClick();
        }

        private void PartitionLayout_DoubleTapped(object? sender, TappedEventArgs e) {
            DataGrid? grid = sender as DataGrid;
            if (grid?.SelectedItem is PartitionListItem pli) {
                ArchiveTreeItem? arcTreeSel = archiveTree.SelectedItem as ArchiveTreeItem;
                if (arcTreeSel == null) {
                    Debug.Assert(false, "archive tree is missing selection");
                    return;
                }
                mMainCtrl.HandlePartitionLayoutDoubleClick(pli, arcTreeSel);
            }
        }

        private async void MetadataList_DoubleTapped(object? sender, TappedEventArgs e) {
            DataGrid? grid = sender as DataGrid;
            if (grid?.SelectedItem is MetadataItem item) {
                await mMainCtrl.HandleMetadataDoubleClick(item, 0, 0);
            }
        }

        private async void Metadata_AddEntryButtonClick(object? sender, RoutedEventArgs e) {
            await mMainCtrl.HandleMetadataAddEntry();
        }

        private void FileListDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            mMainCtrl.RefreshAllCommandStates();
            mMainCtrl.SyncDirectoryTreeToFileSelection();
        }

        /// <summary>
        /// Handles DataGrid column sort clicks.  We supply a custom comparer to control secondary
        /// sort keys; the collection is sorted in-place because Avalonia lacks ListCollectionView.
        /// </summary>
        private void FileListDataGrid_Sorting(object? sender, DataGridColumnEventArgs e) {
            DataGridColumn col = e.Column;
            e.Handled = true;   // prevent DataGrid's built-in sort from firing

            // Determine new sort direction; default to ascending on first click per column.
            bool wasAscending = col.Tag is System.ComponentModel.ListSortDirection tagDir
                && tagDir == System.ComponentModel.ListSortDirection.Ascending;
            System.ComponentModel.ListSortDirection direction = wasAscending
                ? System.ComponentModel.ListSortDirection.Descending
                : System.ComponentModel.ListSortDirection.Ascending;

            // Clear sort indicator from all columns, then set on the active one.
            foreach (DataGridColumn c in fileListDataGrid.Columns) {
                c.Tag = null;
            }
            col.Tag = direction;

            bool isAscending = (direction == System.ComponentModel.ListSortDirection.Ascending);
            var comparer = new FileListItem.ItemComparer(col, isAscending);
            List<FileListItem> sorted = FileList.OrderBy(x => x, comparer).ToList();
            FileList.Clear();
            foreach (FileListItem item in sorted) {
                FileList.Add(item);
            }
            IsResetSortEnabled = true;
        }

        internal void ClearTreesAndLists() {
            ArchiveTreeRoot.Clear();
            DirectoryTreeRoot.Clear();
            FileList.Clear();
        }

        // ---- Drag-drop on file list DataGrid ----

        private void FileListDataGrid_DragOver(object? sender, DragEventArgs e) {
            if (mIsDraggingFileList) {
                e.DragEffects = DragDropEffects.Move;
            } else {
                // Don't check e.Data.Contains(DataFormats.Files) here — on Linux the
                // data formats may not be fully populated during DragOver.  The Drop
                // handler validates the actual data.  (WPF has no DragOver on the file
                // list at all — AllowDrop="True" accepts everything by default.)
                bool canAdd = mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite
                    && mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList;
                e.DragEffects = canAdd ? DragDropEffects.Copy : DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void FileListDataGrid_Drop(object? sender, DragEventArgs e) {
            IFileEntry dropTarget = IFileEntry.NO_ENTRY;
            // Use hit-testing to find the DataGridRow under the pointer.  Walking
            // e.Source parents is unreliable for DataGrid drag events because the
            // Source is often the DataGrid itself, not the hovered cell.
            var point = e.GetPosition(fileListDataGrid);
            var hitVisual = fileListDataGrid.InputHitTest(point) as Visual;
            if (hitVisual != null) {
                // Walk up to find the DataGridRow, then get its DataContext.
                var row = hitVisual.FindAncestorOfType<DataGridRow>();
                if (row?.DataContext is FileListItem fli) {
                    dropTarget = fli.FileEntry;
                }
            }

            if (mIsDraggingFileList) {
                // Internal drag: only move if dropped onto a directory entry.
                // Pass a copy of the list — the original is cleared in the finally block
                // of StartFileListDragAsync, which races with the async MoveFiles call.
                IFileEntry moveTarget = (dropTarget != IFileEntry.NO_ENTRY && dropTarget.IsDirectory)
                    ? dropTarget : GetCurrentVolumeDirEntry();
                if (moveTarget != IFileEntry.NO_ENTRY) {
                    _ = mMainCtrl.MoveFiles(new List<IFileEntry>(mDragMoveList), moveTarget);
                } else {
                    Debug.WriteLine("FL ignoring internal drop: no valid target");
                }
            } else if (e.Data.Contains(DataFormats.Files)) {
                var files = e.Data.GetFiles()?.ToList();
                if (files != null) {
                    string?[] rawPaths = files.Select(f => f.TryGetLocalPath()).ToArray();
                    string[] paths = System.Array.FindAll(rawPaths, p => p != null)!;
                    if (paths.Length > 0) {
                        _ = mMainCtrl.AddFileDrop(dropTarget, paths);
                    }
                }
            } else {
                Debug.WriteLine("FL no valid drop");
            }
        }

        // ---- Internal drag initiation (pointer events on file list) ----

        private void FileListDataGrid_PointerPressed(object? sender, PointerPressedEventArgs e) {
            var point = e.GetCurrentPoint(fileListDataGrid);
            if (point.Properties.IsLeftButtonPressed) {
                mDragStartPosn = e.GetPosition(fileListDataGrid);
            } else {
                mDragStartPosn = new Point(-1, -1);
            }
        }

        private void FileListDataGrid_PointerMoved(object? sender, PointerEventArgs e) {
            var point = e.GetCurrentPoint(fileListDataGrid);
            if (!point.Properties.IsLeftButtonPressed) {
                mDragStartPosn = new Point(-1, -1);
                return;
            }
            if (mIsDraggingFileList || mDragStartPosn.X < 0) {
                return;
            }
            var posn = e.GetPosition(fileListDataGrid);
            if (Math.Abs(posn.X - mDragStartPosn.X) > DRAG_THRESHOLD ||
                    Math.Abs(posn.Y - mDragStartPosn.Y) > DRAG_THRESHOLD) {
                mDragStartPosn = new Point(-1, -1);
                _ = StartFileListDragAsync(e);
            }
        }

        private async System.Threading.Tasks.Task StartFileListDragAsync(PointerEventArgs e) {
            Debug.Assert(!mIsDraggingFileList);
            mIsDraggingFileList = true;
            mDragMoveList.Clear();
            for (int i = 0; i < fileListDataGrid.SelectedItems.Count; i++) {
                if (fileListDataGrid.SelectedItems[i] is FileListItem selItem) {
                    mDragMoveList.Add(selItem.FileEntry);
                }
            }
            try {
                // NOTE: Avalonia 11.2.x has no XDND protocol support on X11, so
                // DragDrop.DoDragDrop only works within the same Avalonia process.
                // Dragging files to the desktop, to a file manager, or to another
                // instance of CP2 is not possible.  Use clipboard copy/paste or the
                // menu extract/export commands instead.
                var data = new DataObject();
                data.Set(INTERNAL_DRAG_FORMAT, mDragMoveList);
                DragDropEffects result = await DragDrop.DoDragDrop(e, data,
                    DragDropEffects.Copy | DragDropEffects.Move);
                Debug.WriteLine("FL drag complete, effect=" + result);
            } catch (Exception ex) {
                Debug.WriteLine("FL drag exception: " + ex.Message);
            } finally {
                mIsDraggingFileList = false;
                mDragMoveList.Clear();
            }
        }

        /// <summary>
        /// Returns the volume directory entry for the currently selected filesystem, or
        /// NO_ENTRY if no filesystem is selected.  Used as fallback drop target when
        /// dropping onto empty space in the file list.
        /// </summary>
        private IFileEntry GetCurrentVolumeDirEntry() {
            ArchiveTreeItem? arcTreeSel = SelectedArchiveTreeItem;
            if (arcTreeSel?.WorkTreeNode.DAObject is IFileSystem fs) {
                return fs.GetVolDirEntry();
            }
            return IFileEntry.NO_ENTRY;
        }

        // ---- Drag-drop on file list panel (empty space below rows) ----

        private void FileListPanel_DragOver(object? sender, DragEventArgs e) {
            if (e.Handled) {
                return;     // Already handled by the DataGrid.
            }
            // Same logic as FileListDataGrid_DragOver — accept drops on empty space.
            if (mIsDraggingFileList) {
                e.DragEffects = DragDropEffects.Move;
            } else {
                bool canAdd = mMainCtrl != null && mMainCtrl.IsFileOpen && mMainCtrl.CanWrite
                    && mMainCtrl.IsMultiFileItemSelected && ShowCenterFileList;
                e.DragEffects = canAdd ? DragDropEffects.Copy : DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void FileListPanel_Drop(object? sender, DragEventArgs e) {
            if (e.Handled) {
                return;     // Already handled by the DataGrid.
            }
            // Drop landed on empty space — use the volume directory as target.
            IFileEntry volDir = GetCurrentVolumeDirEntry();
            if (mIsDraggingFileList) {
                if (volDir != IFileEntry.NO_ENTRY) {
                    _ = mMainCtrl.MoveFiles(new List<IFileEntry>(mDragMoveList), volDir);
                }
            } else if (e.Data.Contains(DataFormats.Files)) {
                var files = e.Data.GetFiles()?.ToList();
                if (files != null) {
                    string?[] rawPaths = files.Select(f => f.TryGetLocalPath()).ToArray();
                    string[] paths = System.Array.FindAll(rawPaths, p => p != null)!;
                    if (paths.Length > 0) {
                        _ = mMainCtrl.AddFileDrop(volDir, paths);
                    }
                }
            }
        }

        // ---- Drag-drop on directory tree ----

        private void DirectoryTree_DragOver(object? sender, DragEventArgs e) {
            if (!mMainCtrl.CanWrite) {
                e.DragEffects = DragDropEffects.None;
            } else if (mIsDraggingFileList) {
                e.DragEffects = DragDropEffects.Move;
            } else {
                // Accept any external drag when writable — see FileListDataGrid_DragOver.
                e.DragEffects = DragDropEffects.Copy;
            }
            e.Handled = true;
        }

        private void DirectoryTree_Drop(object? sender, DragEventArgs e) {
            // Walk up from the event source to find a DirectoryTreeItem DataContext.
            DirectoryTreeItem? dti = null;
            if (e.Source is StyledElement se) {
                var cur = se as StyledElement;
                while (cur != null) {
                    if (cur.DataContext is DirectoryTreeItem item) {
                        dti = item;
                        break;
                    }
                    cur = cur.Parent as StyledElement;
                }
            }
            if (dti == null) {
                Debug.WriteLine("DT drop outside tree item, ignoring");
                return;
            }
            IFileEntry dropTarget = dti.FileEntry;
            Debug.WriteLine("DT drop on item=" + dropTarget);
            if (mIsDraggingFileList) {
                // Copy the list — see FileListDataGrid_Drop comment about race condition.
                _ = mMainCtrl.MoveFiles(new List<IFileEntry>(mDragMoveList), dropTarget);
            } else if (e.Data.Contains(DataFormats.Files)) {
                var files = e.Data.GetFiles()?.ToList();
                if (files != null) {
                    string?[] rawPaths = files.Select(f => f.TryGetLocalPath()).ToArray();
                    string[] paths = System.Array.FindAll(rawPaths, p => p != null)!;
                    if (paths.Length > 0) {
                        _ = mMainCtrl.AddFileDrop(dropTarget, paths);
                    }
                }
            } else {
                Debug.WriteLine("DT no valid drop");
            }
        }

        private async void NotImplemented(string featureName) {
            var dialog = new Window {
                Title = "Not Implemented",
                Width = 350, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children = {
                        new TextBlock {
                            Text = $"'{featureName}' has not been ported yet.",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Margin = new Thickness(20)
                        }
                    }
                }
            };
            await dialog.ShowDialog(this);
        }
    }
}
