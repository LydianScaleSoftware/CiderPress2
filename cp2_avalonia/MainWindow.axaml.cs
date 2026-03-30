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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using cp2_avalonia.Common;

namespace cp2_avalonia {
    public partial class MainWindow : Window, INotifyPropertyChanged {

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
            OpenCommand = new RelayCommand(() => NotImplemented("Open"));
            OpenPhysicalDriveCommand = new RelayCommand(() => NotImplemented("Open Physical Drive"));
            CloseCommand = new RelayCommand(() => NotImplemented("Close"), () => false);
            RecentFile1Command = new RelayCommand(() => NotImplemented("Recent File 1"));
            RecentFile2Command = new RelayCommand(() => NotImplemented("Recent File 2"));
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
            CreateDirectoryCommand = new RelayCommand(() => NotImplemented("Create Directory"), () => false);
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

            ShowFullListCommand = new RelayCommand(() => NotImplemented("Show Full List"), () => false);
            ShowDirListCommand = new RelayCommand(() => NotImplemented("Show Directory List"), () => false);
            ShowInfoCommand = new RelayCommand(() => NotImplemented("Show Information"), () => false);

            NavToParentDirCommand = new RelayCommand(() => NotImplemented("Go To Parent Directory"), () => false);
            NavToParentCommand = new RelayCommand(() => NotImplemented("Go To Parent"), () => false);

            Debug_DiskArcLibTestCommand = new RelayCommand(() => NotImplemented("DiskArc Library Tests"));
            Debug_FileConvLibTestCommand = new RelayCommand(() => NotImplemented("FileConv Library Tests"));
            Debug_BulkCompressTestCommand = new RelayCommand(() => NotImplemented("Bulk Compression Test"));
            Debug_ShowSystemInfoCommand = new RelayCommand(() => NotImplemented("System Info"));
            Debug_ShowDebugLogCommand = new RelayCommand(() => NotImplemented("Show Debug Log"));
            Debug_ShowDropTargetCommand = new RelayCommand(() => NotImplemented("Show Drop/Paste Target"));
            Debug_ConvertANICommand = new RelayCommand(() => NotImplemented("Convert ANI to GIF"), () => false);

            ResetSortCommand = new RelayCommand(() => NotImplemented("Reset Sort"), () => false);
            ToggleInfoCommand = new RelayCommand(() => NotImplemented("Toggle Information"), () => false);

            InitializeComponent();
            DataContext = this;
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
