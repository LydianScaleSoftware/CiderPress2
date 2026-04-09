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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using AvaloniaEdit.Document;

using AppCommon;
using CommonUtil;
using cp2_avalonia.Common;
using DiskArc;
using DiskArc.FS;
using FileConv;
using static cp2_avalonia.Tools.ConfigOptCtrl;

namespace cp2_avalonia.Tools {
    /// <summary>
    /// File viewer dialog.  Ported from cp2_wpf/FileViewer.xaml.cs.
    /// </summary>
    public partial class FileViewer : Window, INotifyPropertyChanged {

        #region Properties

        public bool IsDataTabEnabled {
            get { return mIsDataTabEnabled; }
            set { mIsDataTabEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsDataTabEnabled;

        public bool IsRsrcTabEnabled {
            get { return mIsRsrcTabEnabled; }
            set { mIsRsrcTabEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsRsrcTabEnabled;

        public bool IsNoteTabEnabled {
            get { return mIsNoteTabEnabled; }
            set { mIsNoteTabEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsNoteTabEnabled;

        /// <summary>True when the text editor should be visible (simple or fancy text).</summary>
        public bool IsTextVisible {
            get { return mIsTextVisible; }
            set { mIsTextVisible = value; OnPropertyChanged(); }
        }
        private bool mIsTextVisible;

        /// <summary>True when the bitmap viewer should be visible.</summary>
        public bool IsBitmapVisible {
            get { return mIsBitmapVisible; }
            set { mIsBitmapVisible = value; OnPropertyChanged(); }
        }
        private bool mIsBitmapVisible;

        /// <summary>Bitmap shown in the image preview panel.</summary>
        public Bitmap? PreviewBitmap {
            get { return mPreviewBitmap; }
            set { mPreviewBitmap = value; OnPropertyChanged(); }
        }
        private Bitmap? mPreviewBitmap;

        public bool IsOptionsBoxEnabled {
            get { return mIsOptionsBoxEnabled; }
            set { mIsOptionsBoxEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsOptionsBoxEnabled;

        public bool IsSaveDefaultsEnabled {
            get { return mIsSaveDefaultsEnabled; }
            set { mIsSaveDefaultsEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsSaveDefaultsEnabled;

        public bool IsDOSRaw {
            get { return AppSettings.Global.GetBool(AppSettings.VIEW_RAW_ENABLED, false); }
            set {
                AppSettings.Global.SetBool(AppSettings.VIEW_RAW_ENABLED, value);
                OnPropertyChanged();
                if (mArchiveOrFileSystem != null) {
                    ShowFile(true);
                }
            }
        }

        public bool IsDOSRawEnabled {
            get { return mIsDOSRawEnabled; }
            set { mIsDOSRawEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsDOSRawEnabled;

        public bool IsFindEnabled {
            get { return mIsFindEnabled; }
            set { mIsFindEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsFindEnabled;

        public bool IsFindButtonsEnabled {
            get { return mIsFindButtonsEnabled; }
            set { mIsFindButtonsEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsFindButtonsEnabled;

        public bool IsExportEnabled {
            get { return mIsExportEnabled; }
            set { mIsExportEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsExportEnabled;

        public bool HasPrevFile {
            get { return mHasPrevFile; }
            set { mHasPrevFile = value; OnPropertyChanged(); }
        }
        private bool mHasPrevFile;

        public string PrevFileTip {
            get { return mPrevFileTip; }
            set { mPrevFileTip = value; OnPropertyChanged(); }
        }
        private string mPrevFileTip = string.Empty;

        public bool HasNextFile {
            get { return mHasNextFile; }
            set { mHasNextFile = value; OnPropertyChanged(); }
        }
        private bool mHasNextFile;

        public string NextFileTip {
            get { return mNextFileTip; }
            set { mNextFileTip = value; OnPropertyChanged(); }
        }
        private string mNextFileTip = string.Empty;

        public string GraphicsZoomStr {
            get { return mGraphicsZoomStr; }
            set { mGraphicsZoomStr = value; OnPropertyChanged(); }
        }
        private string mGraphicsZoomStr = "1X";

        public string SearchString {
            get { return mSearchString; }
            set { mSearchString = value; OnPropertyChanged(); UpdateFindControls(); }
        }
        private string mSearchString = string.Empty;

        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion Properties

        // Find commands; wired in Window.KeyBindings.
        public ICommand FindNextCommand { get; }
        public ICommand FindPrevCommand { get; }

        private enum DisplayItemType { Unknown = 0, SimpleText, FancyText, Bitmap }
        private DisplayItemType mDataDisplayType = DisplayItemType.Unknown;

        private void SetDisplayType(DisplayItemType type) {
            mDataDisplayType = type;
            IsTextVisible = (type == DisplayItemType.SimpleText || type == DisplayItemType.FancyText);
            IsBitmapVisible = (type == DisplayItemType.Bitmap);
        }

        private enum Tab { Unknown = 0, Data, Rsrc, Note }
        private void ShowTab(Tab tab) {
            switch (tab) {
                case Tab.Data: tabControl.SelectedItem = dataTabItem; break;
                case Tab.Rsrc: tabControl.SelectedItem = rsrcTabItem; break;
                case Tab.Note: tabControl.SelectedItem = noteTabItem; break;
                default: Debug.Assert(false); break;
            }
        }

        // List of temporary files created for host-format files (PDF, RTF, Word).
        private List<string> mTmpFiles = new List<string>();
        private const string TEMP_FILE_PREFIX = "cp2tmp_";

        // These are set by Init().
        private object? mArchiveOrFileSystem;
        private List<IFileEntry> mSelected = new List<IFileEntry>();
        private int mCurIndex;
        private Dictionary<string, string> mConvOptions = new Dictionary<string, string>();
        private Stream? mDataFork;
        private Stream? mRsrcFork;
        private IConvOutput? mCurDataOutput;
        private IConvOutput? mCurRsrcOutput;
        private AppHook? mAppHook;

        private List<ControlMapItem> mCustomCtrls = new List<ControlMapItem>();
        private bool mIsConfiguring;

        private const int MAX_FANCY_TEXT = 1024 * 1024 * 2;
        private const int MAX_SIMPLE_TEXT = 1024 * 1024 * 2;

        /// <summary>
        /// Converter combo box item.
        /// </summary>
        private class ConverterComboItem {
            public string Name { get; private set; }
            public Converter Converter { get; private set; }

            public ConverterComboItem(string name, Converter converter) {
                Name = name;
                Converter = converter;
            }
            public override string ToString() { return Name; }
        }

        /// <summary>
        /// Constructor.  Called by the previewer and by Init().
        /// </summary>
        public FileViewer() {
            // Initialize all bound properties before InitializeComponent (Pitfall #11).
            IsDataTabEnabled = IsRsrcTabEnabled = IsNoteTabEnabled = false;
            IsTextVisible = true;
            IsBitmapVisible = false;
            IsOptionsBoxEnabled = false;
            IsSaveDefaultsEnabled = false;
            IsDOSRawEnabled = false;
            IsFindEnabled = IsFindButtonsEnabled = false;
            IsExportEnabled = false;
            HasPrevFile = HasNextFile = false;

            FindNextCommand = new RelayCommand(() => DoFind(true), () => IsFindButtonsEnabled);
            FindPrevCommand = new RelayCommand(() => DoFind(false), () => IsFindButtonsEnabled);

            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Initializes the dialog for viewing the specified files.  Call this after construction
        /// and before ShowDialog().
        /// </summary>
        public void Init(Window owner, object archiveOrFileSystem, List<IFileEntry> selected,
                int firstSel, AppHook appHook) {
            Debug.Assert(selected.Count > 0);
            Debug.Assert(firstSel >= 0 && firstSel < selected.Count);

            mArchiveOrFileSystem = archiveOrFileSystem;
            mSelected = selected;
            mCurIndex = firstSel;
            mAppHook = appHook;
            mConvOptions = new Dictionary<string, string>();

            CreateControlMap();

            // Equivalent of WPF's Window_SourceInitialized: set slider value, then show file.
            magnificationSlider.Value = 1;
            UpdatePrevNextControls();
            ShowFile(true);
        }

        /// <summary>
        /// Catches the window-closed event to clean up streams and temp files.
        /// </summary>
        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            CloseStreams();
            DeleteTempFiles();
        }

        private void UpdatePrevNextControls() {
            if (mCurIndex > 0) {
                HasPrevFile = true;
                PrevFileTip = mSelected[mCurIndex - 1].FullPathName;
            } else {
                HasPrevFile = false;
                PrevFileTip = string.Empty;
            }
            if (mCurIndex < mSelected.Count - 1) {
                HasNextFile = true;
                NextFileTip = mSelected[mCurIndex + 1].FullPathName;
            } else {
                HasNextFile = false;
                NextFileTip = string.Empty;
            }
        }

        private void PrevFile_Click(object? sender, RoutedEventArgs e) {
            Debug.Assert(mCurIndex > 0);
            mCurIndex--;
            UpdatePrevNextControls();
            ShowFile(true);
        }

        private void NextFile_Click(object? sender, RoutedEventArgs e) {
            Debug.Assert(mCurIndex < mSelected.Count - 1);
            mCurIndex++;
            UpdatePrevNextControls();
            ShowFile(true);
        }

        private void DoneButton_Click(object? sender, RoutedEventArgs e) {
            Close();
        }

        private void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (tabControl == null || dataTabItem == null) {
                return;     // fired during XAML initialization, controls not yet resolved
            }
            IsOptionsBoxEnabled = (tabControl.SelectedItem == dataTabItem);
        }

        /// <summary>
        /// Configures the dialog for the current file entry.
        /// </summary>
        private void ShowFile(bool fileChanged) {
            if (mArchiveOrFileSystem == null || mAppHook == null) {
                return;
            }

            IsDOSRawEnabled = (mArchiveOrFileSystem is DOS);

            // Reset scrolling.
            dataForkTextEditor.ScrollToHome();
            rsrcForkTextEditor.ScrollToHome();
            noteTextEditor.ScrollToHome();

            CloseStreams();
            IsExportEnabled = IsFindEnabled = false;

            IFileEntry entry = mSelected[mCurIndex];
            FileAttribs attrs = new FileAttribs(entry);
            List<Converter>? applics;
            try {
                bool macZipEnabled = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
                applics = ExportFoundry.GetApplicableConverters(mArchiveOrFileSystem,
                    entry, attrs, IsDOSRaw, macZipEnabled, out mDataFork, out mRsrcFork, mAppHook);
                Debug.Assert(applics.Count > 0);
            } catch (Exception ex) {
                Debug.WriteLine("conv failed: " + ex);
                ShowErrorMessage(ex.Message);
                applics = null;
            }

            string oldName = string.Empty;
            if (convComboBox.SelectedItem != null) {
                oldName = ((ConverterComboItem)convComboBox.SelectedItem).Name;
            }

            convComboBox.Items.Clear();
            if (applics != null) {
                if (applics.Count > 0) {
                    mAppHook.LogD("Best converter is " + applics[0].Label +
                        ", rating=" + applics[0].Applic);
                }

                int newIndex = 0;
                for (int i = 0; i < applics.Count; i++) {
                    Converter conv = applics[i];
                    if (conv.Applic == Converter.Applicability.NotSelectable) {
                        continue;
                    }
                    ConverterComboItem item = new ConverterComboItem(conv.Label, conv);
                    convComboBox.Items.Add(item);
                    if (!fileChanged && conv.Label == oldName) {
                        newIndex = i;
                    }
                }
                convComboBox.SelectedIndex = newIndex;
            } else {
                ConfigOptCtrl.HideConvControls(mCustomCtrls);
                noOptions.IsVisible = true;
            }

            Title = entry.FileName + " - File Viewer";
            UpdateFindControls();
        }

        private void CloseStreams() {
            mDataFork?.Close();
            mDataFork = null;
            mRsrcFork?.Close();
            mRsrcFork = null;
        }

        private void ShowErrorMessage(string msg) {
            dataForkTextEditor.Document = new TextDocument("Viewer error: " + msg);
            SetDisplayType(DisplayItemType.SimpleText);
            ShowTab(Tab.Data);
        }

        private void ConvComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            ConverterComboItem? item = convComboBox.SelectedItem as ConverterComboItem;
            if (item == null) {
                return;
            }
            ConfigureControls(item.Converter);
            FormatFile();
        }

        private void SelectPlainText_Click(object? sender, RoutedEventArgs e) {
            SelectConversion(typeof(FileConv.Generic.PlainText));
        }

        private void SelectHexDump_Click(object? sender, RoutedEventArgs e) {
            SelectConversion(typeof(FileConv.Generic.HexDump));
        }

        private void SelectBest_Click(object? sender, RoutedEventArgs e) {
            convComboBox.SelectedIndex = 0;
        }

        private void SelectConversion(Type convType) {
            foreach (object? obj in convComboBox.Items) {
                if (obj is not ConverterComboItem item)
                {
                    continue;
                }

                if (item.Converter.GetType() == convType) {
                    convComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        /// <summary>
        /// Formats the current file with the selected converter and updates the display.
        /// </summary>
        private void FormatFile() {
            ConverterComboItem? item = convComboBox.SelectedItem as ConverterComboItem;
            if (item == null) {
                return;
            }

            DateTime startWhen = DateTime.Now;
            try {
                mCurDataOutput = item.Converter.ConvertFile(mConvOptions);
            } catch (Exception ex) {
                if (ex is BadBlockException) {
                    mCurDataOutput = new SimpleText("Error: bad disk block encountered: " +
                        ex.Message);
                } else {
                    mCurDataOutput = new SimpleText("Error: converter (" +
                        item.Converter.GetType().Name + ") crashed:\r\n" + ex);
                }
            }

            mCurRsrcOutput = item.Converter.FormatResources(mConvOptions);

            if (mAppHook != null) {
                mAppHook.LogD(item.Converter.Label + " conv took " +
                    (DateTime.Now - startWhen).TotalMilliseconds + " ms");
            }

            IsFindEnabled = IsExportEnabled = false;

            if (mCurDataOutput is ErrorText) {
                string msg = ((SimpleText)mCurDataOutput).Text.ToString();
                dataForkTextEditor.Document = new TextDocument(msg);
                SetDisplayType(DisplayItemType.SimpleText);
            } else if (mCurDataOutput is FancyText && !((FancyText)mCurDataOutput).PreferSimple) {
                FancyText fancy = (FancyText)mCurDataOutput;
                StringBuilder sb = fancy.Text;
                if (sb.Length > MAX_FANCY_TEXT) {
                    int oldLen = sb.Length;
                    sb.Clear();
                    sb.Append("[ output too long for viewer (length=");
                    sb.Append(oldLen);
                    sb.Append(") ]");
                }
                FancyTextHelper.Apply(dataForkTextEditor, fancy);
                SetDisplayType(DisplayItemType.FancyText);
                IsFindEnabled = IsExportEnabled = true;
            } else if (mCurDataOutput is SimpleText) {
                StringBuilder sb = ((SimpleText)mCurDataOutput).Text;
                if (sb.Length > MAX_SIMPLE_TEXT) {
                    int oldLen = sb.Length;
                    sb.Clear();
                    sb.Append("[ output too long for viewer (length=");
                    sb.Append(oldLen);
                    sb.Append(") ]");
                }
                dataForkTextEditor.Document = new TextDocument(sb.ToString());
                // Clear any lingering FancyText highlighting.
                dataForkTextEditor.TextArea.TextView.LineTransformers.Clear();
                SetDisplayType(DisplayItemType.SimpleText);
                IsFindEnabled = IsExportEnabled = true;
            } else if (mCurDataOutput is CellGrid) {
                StringBuilder sb = new StringBuilder();
                CSVGenerator.GenerateString((CellGrid)mCurDataOutput, false, sb);
                if (sb.Length > MAX_SIMPLE_TEXT) {
                    int oldLen = sb.Length;
                    sb.Clear();
                    sb.Append("[ output too long for viewer (length=");
                    sb.Append(oldLen);
                    sb.Append(") ]");
                }
                dataForkTextEditor.Document = new TextDocument(sb.ToString());
                dataForkTextEditor.TextArea.TextView.LineTransformers.Clear();
                SetDisplayType(DisplayItemType.SimpleText);
                IsFindEnabled = IsExportEnabled = true;
            } else if (mCurDataOutput is IBitmap) {
                ConfigureMagnification();
                SetDisplayType(DisplayItemType.Bitmap);
                IsExportEnabled = true;
            } else if (mCurDataOutput is HostConv) {
                HostConv.FileKind kind = ((HostConv)mCurDataOutput).Kind;
                switch (kind) {
                    case HostConv.FileKind.GIF:
                    case HostConv.FileKind.JPEG:
                    case HostConv.FileKind.PNG:
                        Bitmap? bmp = PrepareHostImage(mDataFork);
                        if (bmp == null) {
                            dataForkTextEditor.Document =
                                new TextDocument("Unable to decode " + kind + " image.");
                            dataForkTextEditor.TextArea.TextView.LineTransformers.Clear();
                            SetDisplayType(DisplayItemType.SimpleText);
                        } else {
                            PreviewBitmap = bmp;
                            ConfigureMagnification();
                            SetDisplayType(DisplayItemType.Bitmap);
                        }
                        break;
                    case HostConv.FileKind.PDF:
                    case HostConv.FileKind.RTF:
                    case HostConv.FileKind.Word:
                        string msg;
                        if (LaunchExternalViewer(mDataFork, kind)) {
                            msg = "(Displaying with external command)";
                        } else {
                            msg = "Failed to launch external viewer";
                        }
                        dataForkTextEditor.Document = new TextDocument(msg);
                        dataForkTextEditor.TextArea.TextView.LineTransformers.Clear();
                        SetDisplayType(DisplayItemType.SimpleText);
                        break;
                }
            } else {
                Debug.Assert(false, "unknown IConvOutput impl " + mCurDataOutput);
            }

            // Determine tab enabled states.
            IsDataTabEnabled = (mDataFork != null);

            // If data tab is enabled but showing only empty plain text, disable it when
            // there's also a resource fork to show.
            if (IsDataTabEnabled && mCurRsrcOutput != null &&
                    mDataDisplayType == DisplayItemType.SimpleText) {
                string docText = dataForkTextEditor.Document.Text;
                if (docText.Length == 0) {
                    IsDataTabEnabled = false;
                }
            }

            if (mCurRsrcOutput == null) {
                IsRsrcTabEnabled = false;
                rsrcForkTextEditor.Document = new TextDocument(string.Empty);
            } else {
                IsRsrcTabEnabled = true;
                rsrcForkTextEditor.Document =
                    new TextDocument(((SimpleText)mCurRsrcOutput).Text.ToString());
            }

            Notes comboNotes = new Notes();
            if (mCurDataOutput != null && mCurDataOutput.Notes.Count > 0) {
                comboNotes.MergeFrom(mCurDataOutput.Notes);
            }
            if (mCurRsrcOutput != null && mCurRsrcOutput.Notes.Count > 0) {
                comboNotes.MergeFrom(mCurRsrcOutput.Notes);
            }
            if (comboNotes.Count > 0) {
                noteTextEditor.Document = new TextDocument(comboNotes.ToString());
                IsNoteTabEnabled = true;
            } else {
                noteTextEditor.Document = new TextDocument(string.Empty);
                IsNoteTabEnabled = false;
            }

            SelectEnabledTab();
        }

        private void SelectEnabledTab() {
            if ((tabControl.SelectedItem == dataTabItem && IsDataTabEnabled) ||
                    (tabControl.SelectedItem == rsrcTabItem && IsRsrcTabEnabled) ||
                    (tabControl.SelectedItem == noteTabItem && IsNoteTabEnabled)) {
                // Already on a valid tab — SelectionChanged won't fire, so update manually.
                IsOptionsBoxEnabled = (tabControl.SelectedItem == dataTabItem);
                return;
            }
            if (IsDataTabEnabled) {
                ShowTab(Tab.Data);
            } else if (IsRsrcTabEnabled) {
                ShowTab(Tab.Rsrc);
            } else {
                ShowTab(Tab.Note);
            }
        }

        private static Bitmap? PrepareHostImage(Stream? stream) {
            if (stream == null) {
                return null;
            }
            stream.Position = 0;
            MemoryStream tmpStream = new MemoryStream();
            stream.CopyTo(tmpStream);
            tmpStream.Position = 0;
            try {
                return new Bitmap(tmpStream);
            } catch (Exception ex) {
                Debug.WriteLine("Bitmap decode failed: " + ex.Message);
                return null;
            }
        }

        private bool LaunchExternalViewer(Stream? stream, HostConv.FileKind kind) {
            if (stream == null) {
                return false;
            }

            string ext = kind switch {
                HostConv.FileKind.GIF  => ".gif",
                HostConv.FileKind.JPEG => ".jpg",
                HostConv.FileKind.PNG  => ".png",
                HostConv.FileKind.PDF  => ".pdf",
                HostConv.FileKind.RTF  => ".rtf",
                HostConv.FileKind.Word => ".doc",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(ext)) {
                Debug.Assert(false, "Unhandled kind: " + kind);
                return false;
            }

            string tmpFileName = Path.GetTempPath() +
                TEMP_FILE_PREFIX + Guid.NewGuid().ToString() + ext;
            try {
                using (Stream tmpFile = new FileStream(tmpFileName, FileMode.Create)) {
                    stream.Position = 0;
                    stream.CopyTo(tmpFile);
                }
                mAppHook?.LogI("Created temp file '" + tmpFileName + "'");
                mTmpFiles.Add(tmpFileName);
            } catch (IOException ex) {
                mAppHook?.LogW("Failed to create temp file '" + tmpFileName + "': " + ex.Message);
                return false;
            }

            try {
                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                proc.StartInfo = new System.Diagnostics.ProcessStartInfo(tmpFileName);
                proc.StartInfo.UseShellExecute = true;
                return proc.Start();
            } catch (Exception ex) {
                mAppHook?.LogW("Failed to launch external viewer: " + ex.Message);
                return false;
            }
        }

        private void DeleteTempFiles() {
            foreach (string path in mTmpFiles) {
                try {
                    File.Delete(path);
                    mAppHook?.LogI("Removed temp '" + path + "'");
                } catch (Exception ex) {
                    mAppHook?.LogW("Unable to remove temp: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Scans for stale temporary files left by previous viewer sessions.
        /// </summary>
        public static List<string> FindStaleTempFiles() {
            List<string> staleTemps = new List<string>();
            string pattern = TEMP_FILE_PREFIX + "*";
            string[] allFiles = Directory.GetFiles(Path.GetTempPath(), pattern);
            foreach (string path in allFiles) {
                try {
                    using (FileStream stream = new FileStream(path, FileMode.Open,
                            FileAccess.Read, FileShare.None)) {
                        staleTemps.Add(path);
                    }
                } catch {
                    // Can't open = can't delete; skip.
                }
            }
            return staleTemps;
        }

        #region Magnification

        private void MagnificationSlider_ValueChanged(object? sender,
                Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) {
            if (mCurDataOutput == null) {
                return;
            }
            ConfigureMagnification();
        }

        /// <summary>
        /// Configures the magnification for the current bitmap display.
        /// </summary>
        private void ConfigureMagnification() {
            int tick = (int)magnificationSlider.Value;
            double mult = (tick == 0) ? 0.5 : tick;
            GraphicsZoomStr = mult.ToString() + "X";

            if (mCurDataOutput is HostConv) {
                // HostConv: resize the existing previewImage width/height; let Avalonia scale.
                Bitmap? bmp = PreviewBitmap;
                if (bmp == null) {
                    return;
                }
                previewImage.Width = Math.Floor(bmp.PixelSize.Width * mult);
                previewImage.Height = Math.Floor(bmp.PixelSize.Height * mult);
                return;
            }

            if (mCurDataOutput is not IBitmap) {
                return;
            }

            IBitmap ibm = (IBitmap)mCurDataOutput;
            if (mult < 1.0) {
                // Scale down: convert at full size, let Avalonia shrink it.
                PreviewBitmap = BitmapUtil.ConvertToBitmap(ibm);
                previewImage.Width = Math.Floor(ibm.Width * mult);
                previewImage.Height = Math.Floor(ibm.Height * mult);
            } else if (mult == 1.0) {
                // Exact size + 1px trick to avoid NearestNeighbor edge artifacts.
                PreviewBitmap = BitmapUtil.ConvertToBitmap(ibm);
                previewImage.Width = ibm.Width + 1;
                previewImage.Height = ibm.Height + 1;
            } else {
                // Scale up by pre-scaling the bitmap, then display at native size.
                IBitmap scaled = ibm.ScaleUp((int)mult);
                PreviewBitmap = BitmapUtil.ConvertToBitmap(scaled);
                previewImage.Width = scaled.Width;
                previewImage.Height = scaled.Height;
            }
        }

        #endregion Magnification

        #region Export and Copy

        private void ExportButton_Click(object? sender, RoutedEventArgs e) {
            // Identify what we're exporting.
            IConvOutput? convOut;
            string prefix;
            if (mCurRsrcOutput != null && tabControl.SelectedItem == rsrcTabItem) {
                convOut = mCurRsrcOutput;
                prefix = ".r";
            } else {
                convOut = mCurDataOutput;
                prefix = "";
            }
            if (convOut == null) {
                return;
            }

            // Determine the file extension and filter.
            string ext;
            List<FilePickerFileType> fileTypes;
            if (convOut is FancyText && !((FancyText)convOut).PreferSimple) {
                ext = prefix + RTFGenerator.FILE_EXT;
                fileTypes = new List<FilePickerFileType> {
                    new FilePickerFileType("RTF Document") { Patterns = new[] { "*.rtf" } },
                    FilePickerFileTypes.All
                };
            } else if (convOut is SimpleText) {
                ext = prefix + TXTGenerator.FILE_EXT;
                fileTypes = new List<FilePickerFileType> {
                    new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } },
                    FilePickerFileTypes.All
                };
            } else if (convOut is CellGrid) {
                ext = prefix + CSVGenerator.FILE_EXT;
                fileTypes = new List<FilePickerFileType> {
                    new FilePickerFileType("CSV File") { Patterns = new[] { "*.csv" } },
                    FilePickerFileTypes.All
                };
            } else if (convOut is IBitmap) {
                ext = prefix + PNGGenerator.FILE_EXT;
                fileTypes = new List<FilePickerFileType> {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                    FilePickerFileTypes.All
                };
            } else {
                Debug.Assert(false, "not handling " + convOut.GetType().Name);
                return;
            }

            // Run the save dialog and write the file.
            _ = DoExport(convOut, ext, fileTypes);
        }

        private async Task DoExport(IConvOutput convOut, string ext,
                List<FilePickerFileType> fileTypes) {
            string fileName = (mSelected.Count > 0) ? mSelected[mCurIndex].FileName : "export";
            if (!fileName.ToLowerInvariant().EndsWith(ext)) {
                fileName += ext;
            }

            var topLevel = GetTopLevel(this);
            if (topLevel == null) {
                return;
            }
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions {
                    Title = "Export File...",
                    SuggestedFileName = fileName,
                    FileTypeChoices = fileTypes
                });
            if (file == null) {
                return;
            }

            try {
                using (Stream outStream = await file.OpenWriteAsync()) {
                    CopyViewToStream(convOut, outStream);
                }
            } catch (Exception ex) {
                Debug.WriteLine("Export failed: " + ex.Message);
                await ShowExportError("Export failed: " + ex.Message);
            }
        }

        private async Task ShowExportError(string msg) {
            var dialog = new Window {
                Title = "Export Failed",
                Width = 350, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBlock {
                    Text = msg,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(20),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            };
            await dialog.ShowDialog(this);
        }

        private void CopyViewToStream(IConvOutput convOut, Stream outStream) {
            if (convOut is FancyText && !((FancyText)convOut).PreferSimple) {
                RTFGenerator.Generate((FancyText)convOut, outStream);
            } else if (convOut is SimpleText) {
                using (StreamWriter sw = new StreamWriter(outStream, leaveOpen: true)) {
                    sw.Write(((SimpleText)convOut).Text);
                }
            } else if (convOut is CellGrid) {
                StringBuilder sb = new StringBuilder();
                CSVGenerator.GenerateString((CellGrid)convOut, false, sb);
                using (StreamWriter sw = new StreamWriter(outStream, leaveOpen: true)) {
                    sw.Write(sb);
                }
            } else if (convOut is IBitmap) {
                PNGGenerator.Generate((IBitmap)convOut, outStream);
            } else {
                throw new NotImplementedException("Can't export " + convOut.GetType().Name);
            }
        }

        private void CopyButton_Click(object? sender, RoutedEventArgs e) {
            _ = DoCopy();
        }

        private async Task DoCopy() {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard == null) {
                return;
            }

            // Notes tab doesn't have an IConvOutput.
            if (tabControl.SelectedItem == noteTabItem) {
                await clipboard.SetTextAsync(noteTextEditor.Document.Text);
                return;
            }

            IConvOutput? convOut;
            if (mCurRsrcOutput != null && tabControl.SelectedItem == rsrcTabItem) {
                convOut = mCurRsrcOutput;
            } else {
                convOut = mCurDataOutput;
            }
            if (convOut == null) {
                return;
            }

            try {
                if (convOut is FancyText && !((FancyText)convOut).PreferSimple) {
                    // Avalonia portable clipboard is text-only; copy plain text.
                    string plainText = ((FancyText)convOut).Text.ToString();
                    await clipboard.SetTextAsync(plainText);
                } else if (convOut is SimpleText) {
                    await clipboard.SetTextAsync(((SimpleText)convOut).Text.ToString());
                } else if (convOut is CellGrid) {
                    StringBuilder sb = new StringBuilder();
                    CSVGenerator.GenerateString((CellGrid)convOut, false, sb);
                    await clipboard.SetTextAsync(sb.ToString());
                } else if (convOut is IBitmap) {
                    // Avalonia portable clipboard doesn't have a standard image API.
                    // Copy a PNG-encoded base64 string as a fallback.
                    using (MemoryStream ms = new MemoryStream()) {
                        PNGGenerator.Generate((IBitmap)convOut, ms);
                        await clipboard.SetTextAsync(
                            "[PNG image - copy to file using Export]");
                    }
                }
            } catch (Exception ex) {
                Debug.WriteLine("Copy failed: " + ex.Message);
            }
        }

        #endregion Export and Copy

        #region Find

        private void UpdateFindControls() {
            IsFindButtonsEnabled = IsFindEnabled && !string.IsNullOrEmpty(mSearchString);
            (FindNextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FindPrevCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void SearchStringTextBox_KeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Return || e.Key == Key.Enter) {
                DoFind(true);
                e.Handled = true;
            }
        }

        private void DoFind(bool forward) {
            if (string.IsNullOrEmpty(SearchString)) {
                return;
            }

            // Select the editor for the currently visible tab.
            AvaloniaEdit.TextEditor editor;
            if (tabControl.SelectedItem == rsrcTabItem) {
                editor = rsrcForkTextEditor;
            } else if (tabControl.SelectedItem == noteTabItem) {
                editor = noteTextEditor;
            } else {
                if (mDataDisplayType == DisplayItemType.Bitmap) {
                    return;
                }
                editor = dataForkTextEditor;
            }

            string text = editor.Document.Text;
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            int startPos = forward
                ? editor.CaretOffset
                : Math.Max(0, editor.SelectionStart - 1);

            int index;
            if (forward) {
                index = text.IndexOf(SearchString, startPos,
                    StringComparison.OrdinalIgnoreCase);
                if (index < 0) {
                    index = text.IndexOf(SearchString, 0, StringComparison.OrdinalIgnoreCase);
                }
            } else {
                if (startPos < 0) {
                    startPos = text.Length - 1;
                }
                index = text.LastIndexOf(SearchString, startPos,
                    StringComparison.OrdinalIgnoreCase);
                if (index < 0 && text.Length > 0) {
                    index = text.LastIndexOf(SearchString, text.Length - 1,
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            if (index >= 0) {
                editor.Select(index, SearchString.Length);
                editor.CaretOffset = index + SearchString.Length;
                editor.ScrollTo(editor.TextArea.Caret.Line, editor.TextArea.Caret.Column);
            }
        }

        #endregion Find

        #region Configure

        private void CreateControlMap() {
            mCustomCtrls.Clear();
            mCustomCtrls.Add(new ToggleButtonMapItem(UpdateOption, checkBox1));
            mCustomCtrls.Add(new ToggleButtonMapItem(UpdateOption, checkBox2));
            mCustomCtrls.Add(new ToggleButtonMapItem(UpdateOption, checkBox3));
            mCustomCtrls.Add(new TextBoxMapItem(UpdateOption, stringInput1,
                stringInput1_Label, stringInput1_Box));
            mCustomCtrls.Add(new RadioButtonGroupItem(UpdateOption, multiGroup1,
                new Avalonia.Controls.RadioButton[] {
                    radioButton1_1, radioButton1_2, radioButton1_3, radioButton1_4
                }));
            mCustomCtrls.Add(new RadioButtonGroupItem(UpdateOption, multiGroup2,
                new Avalonia.Controls.RadioButton[] {
                    radioButton2_1, radioButton2_2, radioButton2_3, radioButton2_4
                }));
        }

        private void ConfigureControls(Converter conv) {
            mIsConfiguring = true;

            mConvOptions = ConfigOptCtrl.LoadExportOptions(conv.OptionDefs,
                AppSettings.VIEW_SETTING_PREFIX, conv.Tag);

            ConfigOptCtrl.HideConvControls(mCustomCtrls);

            noOptions.IsVisible = (conv.OptionDefs.Count == 0);

            ConfigOptCtrl.ConfigureControls(mCustomCtrls, conv.OptionDefs, mConvOptions);

            // Avalonia fires TextChanged asynchronously via the dispatcher, so we must
            // defer resetting the flag until after those queued events have been processed.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => mIsConfiguring = false);
        }

        private void UpdateOption(string tag, string newValue) {
            if (mIsConfiguring) {
                return;
            }

            ConverterComboItem? item = convComboBox.SelectedItem as ConverterComboItem;
            if (item == null) {
                return;
            }
            string settingKey = AppSettings.VIEW_SETTING_PREFIX + item.Converter.Tag;

            if (string.IsNullOrEmpty(newValue)) {
                mConvOptions.Remove(tag);
            } else {
                mConvOptions[tag] = newValue;
            }
            string optStr = ConvConfig.GenerateOptString(mConvOptions);

            IsSaveDefaultsEnabled = !AppSettings.Global.GetString(settingKey, string.Empty)
                .Equals(optStr, StringComparison.InvariantCultureIgnoreCase);

            FormatFile();
        }

        private void SaveDefaultsButton_Click(object? sender, RoutedEventArgs e) {
            ConverterComboItem? item = convComboBox.SelectedItem as ConverterComboItem;
            if (item == null) {
                return;
            }
            string optStr = ConvConfig.GenerateOptString(mConvOptions);
            string settingKey = AppSettings.VIEW_SETTING_PREFIX + item.Converter.Tag;
            AppSettings.Global.SetString(settingKey, optStr);
            IsSaveDefaultsEnabled = false;
        }

        #endregion Configure
    }
}
