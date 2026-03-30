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

using AppCommon;
using CommonUtil;
using cp2_avalonia.Common;
using DiskArc;
using DiskArc.Multi;

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
        public ICommand EditBlocksCPMCommand { get; }
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
        private bool mShowDebugMenu = true;  // TODO: read from settings or #if DEBUG
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

        // ---- Toolbar state ----
        private bool mIsChecked_AddExtract = true;
        public bool IsChecked_AddExtract {
            get => mIsChecked_AddExtract;
            set { mIsChecked_AddExtract = value; OnPropertyChanged(); }
        }

        private bool mIsChecked_ImportExport = false;
        public bool IsChecked_ImportExport {
            get => mIsChecked_ImportExport;
            set { mIsChecked_ImportExport = value; OnPropertyChanged(); }
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

        public ObservableCollection<object> MetadataList { get; } = new();
        public void SetMetadataList(IMetadata metaObj) {
            MetadataList.Clear();
            // TODO: implement metadata display in a later iteration
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

            NewDiskImageCommand = new RelayCommand(() => NotImplemented("New Disk Image"));
            NewFileArchiveCommand = new RelayCommand(() => NotImplemented("New File Archive"));
            OpenCommand = new RelayCommand(async () => await mMainCtrl.OpenWorkFile());
            OpenPhysicalDriveCommand = new RelayCommand(() => NotImplemented("Open Physical Drive"));
            CloseCommand = new RelayCommand(
                () => mMainCtrl.CloseWorkFile(),
                () => mMainCtrl?.IsFileOpen ?? false);
            RecentFile1Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(0));
            RecentFile2Command = new RelayCommand(async () => await mMainCtrl.OpenRecentFile(1));
            RecentFile3Command = new RelayCommand(() => NotImplemented("Recent File 3"));
            RecentFile4Command = new RelayCommand(() => NotImplemented("Recent File 4"));
            RecentFile5Command = new RelayCommand(() => NotImplemented("Recent File 5"));
            RecentFile6Command = new RelayCommand(() => NotImplemented("Recent File 6"));

            CopyCommand = new RelayCommand(() => NotImplemented("Copy"), () => false);
            PasteCommand = new RelayCommand(() => NotImplemented("Paste"), () => false);
            FindCommand = new RelayCommand(() => NotImplemented("Find"), () => false);
            SelectAllCommand = new RelayCommand(() => NotImplemented("Select All"), () => false);
            EditAppSettingsCommand = new RelayCommand(() => NotImplemented("Settings"));

            ViewFilesCommand = new RelayCommand(() => NotImplemented("View Files"), () => false);
            AddFilesCommand = new RelayCommand(() => NotImplemented("Add Files"), () => false);
            ImportFilesCommand = new RelayCommand(() => NotImplemented("Import Files"), () => false);
            ExtractFilesCommand = new RelayCommand(() => NotImplemented("Extract Files"), () => false);
            ExportFilesCommand = new RelayCommand(() => NotImplemented("Export Files"), () => false);
            DeleteFilesCommand = new RelayCommand(() => NotImplemented("Delete Files"), () => false);
            TestFilesCommand = new RelayCommand(() => NotImplemented("Test Files"), () => false);
            EditAttributesCommand = new RelayCommand(() => NotImplemented("Edit Attributes"), () => false);
            CreateDirectoryCommand = new RelayCommand(
                async () => await mMainCtrl.CreateDirectory(),
                () => mMainCtrl.IsHierarchicalFileSystemSelected && mMainCtrl.CanWrite);
            EditDirAttributesCommand = new RelayCommand(() => NotImplemented("Edit Directory Attributes"), () => false);
            EditSectorsCommand = new RelayCommand(() => NotImplemented("Edit Sectors"), () => false);
            EditBlocksCommand = new RelayCommand(() => NotImplemented("Edit Blocks"), () => false);
            EditBlocksCPMCommand = new RelayCommand(() => NotImplemented("Edit Blocks (CP/M)"), () => false);
            SaveAsDiskImageCommand = new RelayCommand(() => NotImplemented("Save As Disk Image"), () => false);
            ReplacePartitionCommand = new RelayCommand(() => NotImplemented("Replace Partition Contents"), () => false);
            ScanForBadBlocksCommand = new RelayCommand(() => NotImplemented("Scan for Bad Blocks"), () => false);
            ScanForSubVolCommand = new RelayCommand(() => NotImplemented("Scan for Sub-Volumes"), () => false);
            DefragmentCommand = new RelayCommand(() => NotImplemented("Defragment Filesystem"), () => false);
            CloseSubTreeCommand = new RelayCommand(() => NotImplemented("Close File Source"), () => false);

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

            NavToParentDirCommand = new RelayCommand(() => NotImplemented("Go To Parent Directory"), () => false);
            NavToParentCommand = new RelayCommand(() => NotImplemented("Go To Parent"), () => false);

            Debug_DiskArcLibTestCommand = new RelayCommand(() => NotImplemented("DiskArc Library Tests"));
            Debug_FileConvLibTestCommand = new RelayCommand(() => NotImplemented("FileConv Library Tests"));
            Debug_BulkCompressTestCommand = new RelayCommand(() => NotImplemented("Bulk Compression Test"));
            Debug_ShowSystemInfoCommand = new RelayCommand(() => NotImplemented("System Info"));
            Debug_ShowDebugLogCommand = new RelayCommand(() => {
                mMainCtrl.Debug_ShowDebugLog();
                IsDebugLogVisible = mMainCtrl.IsDebugLogOpen;
            });
            Debug_ShowDropTargetCommand = new RelayCommand(() => NotImplemented("Show Drop/Paste Target"));
            Debug_ConvertANICommand = new RelayCommand(() => NotImplemented("Convert ANI to GIF"), () => false);

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

            mMainCtrl = new MainController(this);
            WindowPlacement.TrackNormalBounds(this);
            Loaded += (s, e) => mMainCtrl.WindowLoaded();
            Closing += (s, e) => mMainCtrl.WindowClosing();
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

        private void FileListDataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            mMainCtrl.RefreshAllCommandStates();
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
