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
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using CommonUtil;
using DiskArc;
using DiskArc.FS;
using static DiskArc.Defs;

namespace cp2_avalonia {
    /// <summary>
    /// Sector edit dialog.
    /// </summary>
    public partial class EditSector : Window, INotifyPropertyChanged {
        private const int NUM_COLS = 16;

        public enum SectorEditMode {
            Unknown = 0, Sectors, Blocks, CPMBlocks
        }

        public enum TxtConvMode { HighASCII, MOR, Latin }
        private static Formatter.CharConvFunc ModeToConverter(TxtConvMode mode) {
            switch (mode) {
                case TxtConvMode.HighASCII:
                default:
                    return Formatter.CharConv_HighASCII;
                case TxtConvMode.MOR:
                    return Formatter.CharConv_MOR;
                case TxtConvMode.Latin:
                    return Formatter.CharConv_Latin;
            }
        }

        // -----------------------------------------------------------------------------------------
        // SectorRow inner class

        public class SectorRow : INotifyPropertyChanged {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            private byte[] mBuffer;
            private int mRowOffset;

            public SectorRow(byte[] buf, int offset, TxtConvMode txtConvMode) {
                mBuffer = buf;
                mRowOffset = offset;
                mConvMode = txtConvMode;
                mConverter = ModeToConverter(txtConvMode);

                RowIndex = mRowOffset / NUM_COLS;
                if (mBuffer.Length == SECTOR_SIZE) {
                    RowLabel = (mRowOffset & 0xf0).ToString("X2");
                } else {
                    RowLabel = (mRowOffset & 0x1f0).ToString("X3");
                }
            }

            public int RowIndex { get; private set; }
            public string RowLabel { get; private set; }

            public string C0 { get { return Get(0x00); }
                set { Set(0x00, value); OnPropertyChanged(); } }
            public string C1 { get { return Get(0x01); }
                set { Set(0x01, value); OnPropertyChanged(); } }
            public string C2 { get { return Get(0x02); }
                set { Set(0x02, value); OnPropertyChanged(); } }
            public string C3 { get { return Get(0x03); }
                set { Set(0x03, value); OnPropertyChanged(); } }
            public string C4 { get { return Get(0x04); }
                set { Set(0x04, value); OnPropertyChanged(); } }
            public string C5 { get { return Get(0x05); }
                set { Set(0x05, value); OnPropertyChanged(); } }
            public string C6 { get { return Get(0x06); }
                set { Set(0x06, value); OnPropertyChanged(); } }
            public string C7 { get { return Get(0x07); }
                set { Set(0x07, value); OnPropertyChanged(); } }
            public string C8 { get { return Get(0x08); }
                set { Set(0x08, value); OnPropertyChanged(); } }
            public string C9 { get { return Get(0x09); }
                set { Set(0x09, value); OnPropertyChanged(); } }
            public string Ca { get { return Get(0x0a); }
                set { Set(0x0a, value); OnPropertyChanged(); } }
            public string Cb { get { return Get(0x0b); }
                set { Set(0x0b, value); OnPropertyChanged(); } }
            public string Cc { get { return Get(0x0c); }
                set { Set(0x0c, value); OnPropertyChanged(); } }
            public string Cd { get { return Get(0x0d); }
                set { Set(0x0d, value); OnPropertyChanged(); } }
            public string Ce { get { return Get(0x0e); }
                set { Set(0x0e, value); OnPropertyChanged(); } }
            public string Cf { get { return Get(0x0f); }
                set { Set(0x0f, value); OnPropertyChanged(); } }

            /// <summary>
            /// Text conversion mode.
            /// </summary>
            public TxtConvMode ConvMode {
                get { return mConvMode; }
                set {
                    mConvMode = value;
                    mConverter = ModeToConverter(value);
                    OnPropertyChanged("AsText");
                }
            }
            private TxtConvMode mConvMode;
            private Formatter.CharConvFunc mConverter;

            private char[] mTextHolder = new char[16];
            public string AsText {
                get {
                    for (int i = 0; i < NUM_COLS; i++) {
                        mTextHolder[i] = mConverter(mBuffer[mRowOffset + i]);
                    }
                    return new string(mTextHolder);
                }
            }

            private string Get(int col) {
                return mBuffer[mRowOffset + col].ToString("X2");
            }
            private void Set(int col, string value) {
                try {
                    byte conv = Convert.ToByte(value, 16);
                    mBuffer[mRowOffset + col] = conv;
                    OnPropertyChanged("AsText");
                } catch {
                    Debug.Assert(false, "invalid byte value '" + value + "'");
                }
            }

            public void PushDigit(int col, byte value) {
                switch (col) {
                    case 0x00: C0 = DoPushDigit(col, value); break;
                    case 0x01: C1 = DoPushDigit(col, value); break;
                    case 0x02: C2 = DoPushDigit(col, value); break;
                    case 0x03: C3 = DoPushDigit(col, value); break;
                    case 0x04: C4 = DoPushDigit(col, value); break;
                    case 0x05: C5 = DoPushDigit(col, value); break;
                    case 0x06: C6 = DoPushDigit(col, value); break;
                    case 0x07: C7 = DoPushDigit(col, value); break;
                    case 0x08: C8 = DoPushDigit(col, value); break;
                    case 0x09: C9 = DoPushDigit(col, value); break;
                    case 0x0a: Ca = DoPushDigit(col, value); break;
                    case 0x0b: Cb = DoPushDigit(col, value); break;
                    case 0x0c: Cc = DoPushDigit(col, value); break;
                    case 0x0d: Cd = DoPushDigit(col, value); break;
                    case 0x0e: Ce = DoPushDigit(col, value); break;
                    case 0x0f: Cf = DoPushDigit(col, value); break;
                    default:
                        Debug.Assert(false);
                        break;
                }
            }

            private string DoPushDigit(int col, byte value) {
                byte curVal = mBuffer[mRowOffset + col];
                mBuffer[mRowOffset + col] = (byte)((curVal << 4) | value);
                return Get(col);
            }

            public void Refresh() {
                OnPropertyChanged(nameof(C0));
                OnPropertyChanged(nameof(C1));
                OnPropertyChanged(nameof(C2));
                OnPropertyChanged(nameof(C3));
                OnPropertyChanged(nameof(C4));
                OnPropertyChanged(nameof(C5));
                OnPropertyChanged(nameof(C6));
                OnPropertyChanged(nameof(C7));
                OnPropertyChanged(nameof(C8));
                OnPropertyChanged(nameof(C9));
                OnPropertyChanged(nameof(Ca));
                OnPropertyChanged(nameof(Cb));
                OnPropertyChanged(nameof(Cc));
                OnPropertyChanged(nameof(Cd));
                OnPropertyChanged(nameof(Ce));
                OnPropertyChanged(nameof(Cf));
                OnPropertyChanged(nameof(AsText));
            }
        }

        // -----------------------------------------------------------------------------------------
        // Window properties

        private IBrush mDefaultLabelColor = Brushes.Black;
        private IBrush mErrorLabelColor = Brushes.Red;

        /// <summary>
        /// Collection of rows that make up the block/sector data.
        /// </summary>
        public List<SectorRow> SectorData { get; set; } = new List<SectorRow>();

        public bool IsSectorDataGridVisible {
            get { return mIsSectorDataGridVisible; }
            set { mIsSectorDataGridVisible = value; OnPropertyChanged(); }
        }
        private bool mIsSectorDataGridVisible = true;

        public string SectorDataLabel {
            get { return mSectorDataLabel; }
            set { mSectorDataLabel = value; OnPropertyChanged(); }
        }
        private string mSectorDataLabel = "LABEL HERE";

        public bool IsSectorVisible { get; private set; }
        public string TrackBlockLabel { get; private set; } = string.Empty;
        public IBrush TrackBlockLabelForeground {
            get { return mTrackBlockLabelForeground; }
            set { mTrackBlockLabelForeground = value; OnPropertyChanged(); }
        }
        private IBrush mTrackBlockLabelForeground;
        public IBrush SectorLabelForeground {
            get { return mSectorLabelForeground; }
            set { mSectorLabelForeground = value; OnPropertyChanged(); }
        }
        private IBrush mSectorLabelForeground;

        public string TrackBlockInfoLabel { get; private set; } = string.Empty;
        public string SectorInfoLabel { get; private set; } = string.Empty;

        public string TrackBlockNumString {
            get { return mTrackBlockNumString; }
            set { mTrackBlockNumString = value; OnPropertyChanged(); TrackSectorUpdated(); }
        }
        private string mTrackBlockNumString = string.Empty;

        public string SectorNumString {
            get { return mSectorNumString; }
            set { mSectorNumString = value; OnPropertyChanged(); TrackSectorUpdated(); }
        }
        private string mSectorNumString = string.Empty;

        public string IOErrorMsg {
            get { return mIOErrorMsg; }
            set { mIOErrorMsg = value; OnPropertyChanged(); }
        }
        private string mIOErrorMsg = "I/O Error";

        public bool IsIOErrorMsgVisible {
            get { return mIsIOErrorMsgVisible; }
            set { mIsIOErrorMsgVisible = value; OnPropertyChanged(); }
        }
        private bool mIsIOErrorMsgVisible = false;

        public delegate bool EnableWriteFunc();
        private EnableWriteFunc? mEnableWriteFunc;

        /// <summary>
        /// Text conversion mode for hex dump.  Set by radio button.
        /// </summary>
        private TxtConvMode mTxtConvMode;

        public bool IsChecked_ConvHighASCII {
            get { return mTxtConvMode == TxtConvMode.HighASCII; }
            set {
                if (value) {
                    mTxtConvMode = TxtConvMode.HighASCII;
                    UpdateTxtConv();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChecked_ConvMOR));
                OnPropertyChanged(nameof(IsChecked_ConvLatin));
            }
        }
        public bool IsChecked_ConvMOR {
            get { return mTxtConvMode == TxtConvMode.MOR; }
            set {
                if (value) {
                    mTxtConvMode = TxtConvMode.MOR;
                    UpdateTxtConv();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChecked_ConvHighASCII));
                OnPropertyChanged(nameof(IsChecked_ConvLatin));
            }
        }
        public bool IsChecked_ConvLatin {
            get { return mTxtConvMode == TxtConvMode.Latin; }
            set {
                if (value) {
                    mTxtConvMode = TxtConvMode.Latin;
                    UpdateTxtConv();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChecked_ConvHighASCII));
                OnPropertyChanged(nameof(IsChecked_ConvMOR));
            }
        }

        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// True if writes have been enabled in the chunk.
        /// </summary>
        public bool WritesEnabled { get; private set; }

        /// <summary>
        /// True if buffer has been modified.
        /// </summary>
        public bool IsDirty {
            get { return mIsDirty; }
            set { mIsDirty = value; }
        }
        private bool mIsDirty;

        /// <summary>
        /// True if the text entered into the track/block/sector fields is valid.
        /// </summary>
        private bool mIsEntryValid;

        /// <summary>
        /// True if the "Write" button should be enabled.
        /// </summary>
        public bool IsWriteButtonEnabled {
            get { return mEnableWriteFunc != null && mIsEntryValid; }
        }

        /// <summary>
        /// True if the "Read" button should be enabled.
        /// </summary>
        public bool IsReadButtonEnabled {
            get { return mIsEntryValid; }
        }

        public bool IsPrevEnabled {
            get { return mIsPrevEnabled; }
            set { mIsPrevEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsPrevEnabled;
        public bool IsNextEnabled {
            get { return mIsNextEnabled; }
            set { mIsNextEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsNextEnabled;

        private IChunkAccess mChunkAccess;
        private SectorEditMode mEditMode;
        private Formatter mFormatter;

        private uint mCurBlockOrTrack;
        private uint mCurSector;
        private int mEnteredBlockOrTrack;
        private int mEnteredSector;

        private byte[] mBuffer;
        private int mCurCol;
        private int mCurRow;
        private int mCurDigit;
        private readonly int mNumRows;

        private SectorOrder mSectorOrder;

        private bool InTextArea { get { return mCurCol == NUM_COLS; } }

        // Keep track of the integer base used for track, sector, and block numbers.
        private static int sTrackNumBase = 10;
        private static int sSectorNumBase = 10;
        private static int sBlockNumBase = 10;

        // Async close guard (Avalonia Window_Closing pattern).
        private bool mUserConfirmedClose;

        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader.
        /// </summary>
        public EditSector() {
            mChunkAccess = null!;
            mFormatter = null!;
            mBuffer = Array.Empty<byte>();
            mTrackBlockLabelForeground = Brushes.Black;
            mSectorLabelForeground = Brushes.Black;
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="chunks">Chunk data source.</param>
        /// <param name="editMode">Edit mode.</param>
        /// <param name="enableWriteFunc">Function to call to enable writes; null if read-only.</param>
        /// <param name="formatter">Text formatter.</param>
        public EditSector(IChunkAccess chunks, SectorEditMode editMode,
                EnableWriteFunc? enableWriteFunc, Formatter formatter) {
            // Initialize bound properties before InitializeComponent / DataContext.
            mDefaultLabelColor = Brushes.Black;
            mErrorLabelColor = Brushes.Red;
            mTrackBlockLabelForeground = mDefaultLabelColor;
            mSectorLabelForeground = mDefaultLabelColor;

            mChunkAccess = chunks;
            mEditMode = editMode;
            mEnableWriteFunc = enableWriteFunc;
            mFormatter = formatter;

            Debug.Assert(mEnableWriteFunc == null || !mChunkAccess.IsReadOnly);

            SettingsHolder settings = AppSettings.Global;
            mTxtConvMode =
                settings.GetEnum(AppSettings.SCTED_TEXT_CONV_MODE, TxtConvMode.HighASCII);

            if (editMode == SectorEditMode.Sectors && !chunks.HasSectors) {
                editMode = SectorEditMode.Blocks;
                mEditMode = editMode;
            }
            bool asSectors = (editMode == SectorEditMode.Sectors);
            switch (editMode) {
                case SectorEditMode.Sectors:
                    Title = "Edit Sectors";
                    mSectorOrder = SectorOrder.DOS_Sector;
                    break;
                case SectorEditMode.Blocks:
                    Title = "Edit Blocks";
                    mSectorOrder = SectorOrder.ProDOS_Block;
                    break;
                case SectorEditMode.CPMBlocks:
                    // Treat as block mode with CP/M ordering; the block order combo
                    // in the editor allows switching between ProDOS and CP/M.
                    Title = "Edit Blocks";
                    mEditMode = SectorEditMode.Blocks;
                    mSectorOrder = SectorOrder.CPM_KBlock;
                    break;
                default:
                    Debug.Assert(false);
                    mSectorOrder = SectorOrder.Physical;
                    break;
            }

            mBuffer = new byte[asSectors ? SECTOR_SIZE : BLOCK_SIZE];
            mNumRows = mBuffer.Length / NUM_COLS;

            for (int i = 0; i < mBuffer.Length; i += NUM_COLS) {
                SectorRow row = new SectorRow(mBuffer, i, mTxtConvMode);
                SectorData.Add(row);
            }
            mCurBlockOrTrack = 0;
            mCurSector = 0;
            SetPrevNextEnabled();
            UpdateTxtConv();
            ReadFromDisk();

            if (asSectors) {
                IsSectorVisible = true;
                TrackBlockLabel = "Track:";
                TrackBlockInfoLabel = string.Format("\u2022 Track is 0-{0} (${1:X})",
                    mChunkAccess.NumTracks - 1, mChunkAccess.NumTracks - 1);
                SectorInfoLabel = string.Format("\u2022 Sector is 0-{0} (${1:X})",
                    mChunkAccess.NumSectorsPerTrack - 1, mChunkAccess.NumSectorsPerTrack - 1);
            } else {
                IsSectorVisible = false;
                TrackBlockLabel = "Block:";
                int numBlocks = (int)(mChunkAccess.FormattedLength / BLOCK_SIZE);
                TrackBlockInfoLabel = string.Format("\u2022 Block is 0-{0} (${1:X})",
                    numBlocks - 1, numBlocks - 1);
                SectorInfoLabel = string.Empty;
            }
            CopyNumToTextFields();

            mCurRow = mCurCol = 0;
            mCurDigit = 0;

            InitializeComponent();
            DataContext = this;

            // InitializeComponent() resets Title from the AXAML default; re-apply it.
            Title = mEditMode switch {
                SectorEditMode.Sectors   => "Edit Sectors",
                SectorEditMode.Blocks    => "Edit Blocks",
                _                        => "Edit Blocks",
            };

            PrepareBlockOrder();
            PrepareSectorOrder();
            PrepareSectorCodec();
        }

        private void Window_Opened(object? sender, EventArgs e) {
            // Focus the track/block input box so the user can immediately type a location.
            trackBlockNumBox.Focus();
            trackBlockNumBox.SelectAll();
        }

        // Window-level KeyDown — intercept Ctrl+C to copy to clipboard.
        private void Window_KeyDown(object? sender, KeyEventArgs e) {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C) {
                e.Handled = true;
                _ = CopyToClipboardAsync();
            }
        }

        private async void Window_Closing(object? sender, WindowClosingEventArgs e) {
            if (mUserConfirmedClose) {
                return;
            }
            if (mIsDirty) {
                e.Cancel = true;
                bool discard = await ConfirmDiscardChangesAsync();
                if (discard) {
                    mUserConfirmedClose = true;
                    Close();
                }
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) {
            Close();
        }

        /// <summary>
        /// If there are unwritten modifications, ask the user to confirm.
        /// Returns true if it's okay to continue.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> ConfirmDiscardChangesAsync() {
            if (!mIsDirty) {
                return true;
            }
            return await ShowConfirmAsync("Discard unwritten modifications?", "Confirm");
        }

        // -----------------------------------------------------------------------------------------
        // Read / Write

        /// <summary>
        /// Reads a block or sector of data from the current position.
        /// </summary>
        private void ReadFromDisk() {
            // Synchronous version used during construction and button clicks.
            // Full async version is in ReadFromDiskAsync (called when dirty).
            try {
                switch (mEditMode) {
                    case SectorEditMode.Sectors:
                        mChunkAccess.ReadSector(mCurBlockOrTrack, mCurSector, mBuffer, 0,
                            mSectorOrder);
                        break;
                    case SectorEditMode.Blocks:
                    case SectorEditMode.CPMBlocks:
                        mChunkAccess.ReadBlock(mCurBlockOrTrack, mBuffer, 0, mSectorOrder);
                        break;
                    default:
                        Debug.Assert(false);
                        return;
                }
                IsSectorDataGridVisible = true;
                IsIOErrorMsgVisible = false;
            } catch (BadBlockException) {
                RawData.MemSet(mBuffer, 0, mBuffer.Length, 0xcc);
                IsSectorDataGridVisible = false;
                IsIOErrorMsgVisible = true;
            }
            foreach (SectorRow row in SectorData) {
                row.Refresh();
            }
            IsDirty = false;
            SetSectorDataLabel();
        }

        /// <summary>
        /// Async version of ReadFromDisk — used when dirty state must be checked first.
        /// </summary>
        private async System.Threading.Tasks.Task ReadFromDiskAsync() {
            if (!await ConfirmDiscardChangesAsync()) {
                return;
            }
            ReadFromDisk();
        }

        private async System.Threading.Tasks.Task WriteToDiskAsync() {
            if (!TryEnableWrites()) {
                await ShowMessageAsync("Unable to write to this disk", "Not Possible");
                return;
            }
            try {
                switch (mEditMode) {
                    case SectorEditMode.Sectors:
                        mChunkAccess.WriteSector(mCurBlockOrTrack, mCurSector, mBuffer, 0,
                            mSectorOrder);
                        break;
                    case SectorEditMode.Blocks:
                    case SectorEditMode.CPMBlocks:
                        mChunkAccess.WriteBlock(mCurBlockOrTrack, mBuffer, 0, mSectorOrder);
                        break;
                    default:
                        Debug.Assert(false);
                        return;
                }
                IsDirty = false;
            } catch (Exception ex) {
                await ShowMessageAsync("Write failed: " + ex.Message, "Error");
            }
            SetSectorDataLabel();
        }

        private void SetSectorDataLabel() {
            string dirtyStr = IsDirty ? " [*]" : string.Empty;
            if (mEditMode == SectorEditMode.Sectors) {
                SectorDataLabel = string.Format("Track {0} (${0:X2}), Sector {1} (${1:X}) {2}",
                    mCurBlockOrTrack, mCurSector, dirtyStr);
            } else if (mSectorOrder == SectorOrder.CPM_KBlock) {
                uint allocBlock = DiskArc.FS.CPM.DiskBlockToAllocBlock(mCurBlockOrTrack,
                        mChunkAccess.FormattedLength, out uint offset);
                if (allocBlock != uint.MaxValue) {
                    SectorDataLabel =
                        string.Format("Block {0} (${0:X}) / Alloc {1}.{2} (${1:X2}) {3}",
                            mCurBlockOrTrack, allocBlock, offset / BLOCK_SIZE, dirtyStr);
                } else {
                    SectorDataLabel = string.Format("Block {0} (${0:X2}) / Alloc --.- ($--) {1}",
                        mCurBlockOrTrack, dirtyStr);
                }
            } else {
                SectorDataLabel = string.Format("Block {0} (${0:X2}) {1}",
                    mCurBlockOrTrack, dirtyStr);
            }
        }

        private void SetPrevNextEnabled() {
            bool hasPrev, hasNext;
            if (mEditMode == SectorEditMode.Sectors) {
                hasPrev = (mCurBlockOrTrack != 0 || mCurSector != 0);
                hasNext = (mCurBlockOrTrack != mChunkAccess.NumTracks - 1 ||
                    mCurSector != mChunkAccess.NumSectorsPerTrack - 1);
            } else {
                long numBlocks = mChunkAccess.FormattedLength / BLOCK_SIZE;
                hasPrev = (mCurBlockOrTrack != 0);
                hasNext = (mCurBlockOrTrack != numBlocks - 1);
            }
            IsPrevEnabled = hasPrev;
            IsNextEnabled = hasNext;
        }

        private void CopyNumToTextFields() {
            string fmtStr = "{0:D}";
            if (mEditMode == SectorEditMode.Sectors) {
                if (sTrackNumBase == 16) {
                    fmtStr = "${0:X2}";
                }
            } else {
                if (sBlockNumBase == 16) {
                    fmtStr = "${0:X4}";
                }
            }
            TrackBlockNumString = string.Format(fmtStr, mCurBlockOrTrack);

            fmtStr = "{0:D}";
            if (sSectorNumBase == 16) {
                fmtStr = "${0:X2}";
            }
            SectorNumString = string.Format(fmtStr, mCurSector);
        }

        private void UpdateTxtConv() {
            AppSettings.Global.SetEnum(AppSettings.SCTED_TEXT_CONV_MODE, mTxtConvMode);
            foreach (SectorRow row in SectorData) {
                row.ConvMode = mTxtConvMode;
            }
            Formatter.FormatConfig config = mFormatter.Config;
            config.HexDumpConvFunc = ModeToConverter(mTxtConvMode);
            mFormatter = new Formatter(config);
        }

        private void TrackSectorUpdated() {
            int val, intBase;

            if (StringToValue.TryParseInt(TrackBlockNumString, out val, out intBase)) {
                mEnteredBlockOrTrack = val;
                if (mEditMode == SectorEditMode.Sectors) {
                    sTrackNumBase = intBase;
                } else {
                    sBlockNumBase = intBase;
                }
            } else {
                mEnteredBlockOrTrack = -1;
            }
            if (StringToValue.TryParseInt(SectorNumString, out val, out intBase)) {
                mEnteredSector = val;
                sSectorNumBase = intBase;
            } else {
                mEnteredSector = -1;
            }

            bool trackBlockValid, sectorValid;

            if (mEditMode == SectorEditMode.Sectors) {
                trackBlockValid = mEnteredBlockOrTrack >= 0 &&
                    mEnteredBlockOrTrack < mChunkAccess.NumTracks;
                sectorValid = mEnteredSector >= 0 &&
                    mEnteredSector < mChunkAccess.NumSectorsPerTrack;
            } else {
                long numBlocks = mChunkAccess.FormattedLength / BLOCK_SIZE;
                trackBlockValid = mEnteredBlockOrTrack >= 0 && mEnteredBlockOrTrack < numBlocks;
                sectorValid = true;
            }

            TrackBlockLabelForeground = trackBlockValid ? mDefaultLabelColor : mErrorLabelColor;
            SectorLabelForeground = sectorValid ? mDefaultLabelColor : mErrorLabelColor;

            mIsEntryValid = trackBlockValid && sectorValid;
            OnPropertyChanged(nameof(IsReadButtonEnabled));
            OnPropertyChanged(nameof(IsWriteButtonEnabled));
        }

        private bool TryEnableWrites() {
            if (mEnableWriteFunc == null) {
                return false;
            }
            if (WritesEnabled) {
                return true;
            }
            if (!mEnableWriteFunc()) {
                // Show error asynchronously; caller already expects false return.
                _ = ShowMessageAsync("Failed to enable write access", "Whoops");
                return false;
            }
            WritesEnabled = true;
            Debug.WriteLine("Writes enabled");
            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Button click handlers

        private async void ReadButton_Click(object? sender, RoutedEventArgs e) {
            mCurBlockOrTrack = (uint)mEnteredBlockOrTrack;
            mCurSector = (uint)mEnteredSector;
            SetPrevNextEnabled();
            await ReadFromDiskAsync();
        }

        private async void PrevButton_Click(object? sender, RoutedEventArgs e) {
            if (mEditMode == SectorEditMode.Sectors) {
                if (mCurSector != 0) {
                    mCurSector--;
                } else {
                    mCurSector = mChunkAccess.NumSectorsPerTrack - 1;
                    mCurBlockOrTrack--;
                }
            } else {
                mCurBlockOrTrack--;
            }
            SetPrevNextEnabled();
            CopyNumToTextFields();
            await ReadFromDiskAsync();
        }

        private async void NextButton_Click(object? sender, RoutedEventArgs e) {
            if (mEditMode == SectorEditMode.Sectors) {
                mCurSector++;
                if (mCurSector == mChunkAccess.NumSectorsPerTrack) {
                    mCurSector = 0;
                    mCurBlockOrTrack++;
                }
            } else {
                mCurBlockOrTrack++;
            }
            SetPrevNextEnabled();
            CopyNumToTextFields();
            await ReadFromDiskAsync();
        }

        private async void WriteButton_Click(object? sender, RoutedEventArgs e) {
            Debug.Assert(!mChunkAccess.IsReadOnly);
            if (mCurBlockOrTrack != mEnteredBlockOrTrack || mCurSector != mEnteredSector) {
                bool ok = await ShowConfirmAsync(
                    "This will write to a different sector than was read from. Proceed?",
                    "Confirm");
                if (!ok) {
                    return;
                }
            }
            mCurBlockOrTrack = (uint)mEnteredBlockOrTrack;
            mCurSector = (uint)mEnteredSector;
            SetPrevNextEnabled();
            await WriteToDiskAsync();
        }

        private void CopyButton_Click(object? sender, RoutedEventArgs e) {
            _ = CopyToClipboardAsync();
        }

        // -----------------------------------------------------------------------------------------
        // DataGrid interaction

        /// <summary>
        /// Event that fires when the current cell changes.
        /// </summary>
        private void SectorDataGrid_CurrentCellChanged(object? sender, EventArgs e) {
            var col = sectorDataGrid.CurrentColumn;
            if (col == null)
            {
                return;
            }

            int displayIndex = col.DisplayIndex;
            if (displayIndex == 0)
            {
                return;  // RowLabel column — ignore
            }

            if (sectorDataGrid.SelectedItem is not SectorRow row)
            {
                return;
            }

            mCurRow = row.RowIndex;
            mCurCol = displayIndex - 1;     // 0..15 = hex columns, 16 = text
            mCurDigit = 0;
            Debug.WriteLine("Select: posn=$" + (mCurRow * NUM_COLS + mCurCol).ToString("x3"));
        }

        private void SectorDataGrid_KeyDown(object? sender, KeyEventArgs e) {
            bool posnChanged = false;
            bool digitPushed = false;

            if (e.KeyModifiers != KeyModifiers.None) {
                return;
            }

            switch (e.Key) {
                case Key.Left:
                    posnChanged = MoveLeft();
                    e.Handled = true;
                    break;
                case Key.Right:
                    posnChanged = MoveRight();
                    e.Handled = true;
                    break;
                case Key.Up:
                    posnChanged = MoveUp();
                    e.Handled = true;
                    break;
                case Key.Down:
                    posnChanged = MoveDown();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    e.Handled = true;
                    break;
                case Key.Tab:
                    trackBlockNumBox.Focus();
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.D1:
                case Key.D2:
                case Key.D3:
                case Key.D4:
                case Key.D5:
                case Key.D6:
                case Key.D7:
                case Key.D8:
                case Key.D9:
                    if (!InTextArea) {
                        if (TryEnableWrites()) {
                            SectorData[mCurRow].PushDigit(mCurCol,
                                (byte)((int)e.Key - (int)Key.D0));
                            digitPushed = true;
                        }
                        e.Handled = true;
                    }
                    break;
                case Key.A:
                case Key.B:
                case Key.C:
                case Key.D:
                case Key.E:
                case Key.F:
                    if (!InTextArea) {
                        if (TryEnableWrites()) {
                            SectorData[mCurRow].PushDigit(mCurCol,
                                (byte)((int)e.Key - (int)Key.A + 10));
                            digitPushed = true;
                        }
                        e.Handled = true;
                    }
                    break;
                default:
                    break;
            }

            if (digitPushed) {
                mCurDigit++;
                if (mCurDigit > 1) {
                    mCurDigit = 0;
                    posnChanged = MoveRight();
                }
                IsDirty = true;
                SetSectorDataLabel();
            }
            if (posnChanged) {
                SetPosition(mCurCol, mCurRow);
            }
        }

        private void SetPosition(int col, int row) {
            // col is the logical hex column (0..15); add 1 for the RowLabel column offset.
            int gridCol = col + 1;
            if (row < 0 || row >= SectorData.Count)
            {
                return;
            }

            object item = SectorData[row];
            DataGridColumn? dgCol = sectorDataGrid.Columns.Count > gridCol
                ? sectorDataGrid.Columns[gridCol] : null;
            if (dgCol == null)
            {
                return;
            }

            sectorDataGrid.SelectedItem = item;
            sectorDataGrid.CurrentColumn = dgCol;
            sectorDataGrid.ScrollIntoView(item, dgCol);
            sectorDataGrid.Focus();
        }

        private bool MoveLeft() {
            mCurCol--;
            if (mCurCol < 0) {
                mCurCol = NUM_COLS - 1;
                mCurRow--;
                if (mCurRow < 0) {
                    mCurRow = mNumRows - 1;
                }
            }
            return true;
        }

        private bool MoveRight() {
            mCurCol++;
            if (mCurCol >= NUM_COLS) {
                mCurCol = 0;
                mCurRow++;
                if (mCurRow == mNumRows) {
                    mCurRow = 0;
                }
            }
            return true;
        }

        private bool MoveUp() {
            mCurRow--;
            if (mCurRow < 0) {
                mCurRow = mNumRows - 1;
            }
            return true;
        }

        private bool MoveDown() {
            mCurRow++;
            if (mCurRow == mNumRows) {
                mCurRow = 0;
            }
            return true;
        }

        private async System.Threading.Tasks.Task CopyToClipboardAsync() {
            if (IsIOErrorMsgVisible) {
                return;
            }
            string dumpText = mFormatter.FormatHexDump(mBuffer).ToString();
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) {
                await clipboard.SetTextAsync(dumpText);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Block order (ProDOS vs CP/M)

        public bool IsBlockOrderVisible { get; private set; }
        public bool IsBlockOrderEnabled { get; private set; }

        public class BlockOrderItem {
            public string Label { get; }
            public SectorOrder Order { get; }

            public BlockOrderItem(string label, SectorOrder order) {
                Label = label;
                Order = order;
            }

            public override string ToString() => Label;
        }

        public List<BlockOrderItem> BlockOrderList { get; } = new List<BlockOrderItem>();

        private void PrepareBlockOrder() {
            if (mEditMode == SectorEditMode.Sectors) {
                IsBlockOrderVisible = false;
                OnPropertyChanged(nameof(IsBlockOrderVisible));
                return;
            }
            // Always show the combo in block mode.
            IsBlockOrderVisible = true;
            OnPropertyChanged(nameof(IsBlockOrderVisible));

            // Always offer ProDOS ordering.
            BlockOrderList.Add(new BlockOrderItem("ProDOS", SectorOrder.ProDOS_Block));
            // Only offer CP/M if the disk size is CP/M-compatible and the chunk access
            // has 16-sector tracks (so the sector-order remapping actually changes data).
            // Block-only images (e.g. 800K .po) ignore the SectorOrder parameter in
            // ReadBlock, so offering CP/M order there would be misleading.
            if (CPM.IsSizeAllowed(mChunkAccess.FormattedLength) &&
                    mChunkAccess.NumSectorsPerTrack == 16) {
                BlockOrderList.Add(new BlockOrderItem("CP/M", SectorOrder.CPM_KBlock));
            }
            IsBlockOrderEnabled = BlockOrderList.Count > 1;
            OnPropertyChanged(nameof(IsBlockOrderEnabled));

            blockOrderCombo.ItemsSource = BlockOrderList;
            mInitializingAdv = true;
            int match = 0;
            for (int i = 0; i < BlockOrderList.Count; i++) {
                if (BlockOrderList[i].Order == mSectorOrder) {
                    match = i;
                    break;
                }
            }
            blockOrderCombo.SelectedIndex = match;
            mInitializingAdv = false;
        }

        private async void BlockOrderCombo_SelectionChanged(object? sender,
                SelectionChangedEventArgs e) {
            if (mInitializingAdv) {
                return;
            }
            int idx = ((ComboBox)sender!).SelectedIndex;
            if (idx < 0 || idx >= BlockOrderList.Count) {
                return;
            }
            BlockOrderItem item = BlockOrderList[idx];
            if (item.Order == mSectorOrder) {
                return;
            }

            if (mIsDirty) {
                bool discard = await ShowConfirmAsync(
                    "Changing the block order will abandon your pending changes. Continue?",
                    "Pending Changes");
                if (!discard) {
                    // Revert the combo to the previous block order.
                    mInitializingAdv = true;
                    for (int i = 0; i < BlockOrderList.Count; i++) {
                        if (BlockOrderList[i].Order == mSectorOrder) {
                            blockOrderCombo.SelectedIndex = i;
                            break;
                        }
                    }
                    mInitializingAdv = false;
                    return;
                }
            }

            mSectorOrder = item.Order;
            ReadFromDisk();
        }

        // -----------------------------------------------------------------------------------------
        // Sector order

        private bool mInitializingAdv;

        public bool IsSectorOrderEnabled { get; private set; }

        public class SectorOrderItem {
            public string Label { get; }
            public SectorOrder Order { get; }

            public SectorOrderItem(string label, SectorOrder order) {
                Label = label;
                Order = order;
            }

            public override string ToString() => Label;
        }

        public List<SectorOrderItem> SectorOrderList { get; } = new List<SectorOrderItem>() {
            new SectorOrderItem("DOS 3.3", SectorOrder.DOS_Sector),
            new SectorOrderItem("ProDOS", SectorOrder.ProDOS_Block),
            new SectorOrderItem("CP/M", SectorOrder.CPM_KBlock),
            new SectorOrderItem("Physical", SectorOrder.Physical)
        };

        private void PrepareSectorOrder() {
            // Sector skew only applies to 16-sector nibble-based floppy images where the
            // raw sectors can be reinterpreted in different orders.  Block-based chunks
            // that synthesize sector access (e.g. ChunkSubset for DOS-in-ProDOS) only
            // support their native FileOrder and will assert if a different order is used.
            if (!mChunkAccess.HasSectors || mChunkAccess.NumSectorsPerTrack != 16 ||
                    (mChunkAccess.HasBlocks && mChunkAccess.NibbleCodec == null)) {
                IsSectorOrderEnabled = false;
                OnPropertyChanged(nameof(IsSectorOrderEnabled));
                sectorOrderCombo.SelectedItem = null;
                return;
            }
            IsSectorOrderEnabled = true;
            OnPropertyChanged(nameof(IsSectorOrderEnabled));

            sectorOrderCombo.ItemsSource = SectorOrderList;
            int match;
            for (match = 0; match < SectorOrderList.Count; match++) {
                if (SectorOrderList[match].Order == mSectorOrder) {
                    break;
                }
            }
            if (match == SectorOrderList.Count) {
                match = 0;
            }
            mInitializingAdv = true;
            sectorOrderCombo.SelectedIndex = match;
            mInitializingAdv = false;
        }

        private async void SectorOrderCombo_SelectionChanged(object? sender,
                SelectionChangedEventArgs e) {
            if (mInitializingAdv) {
                return;
            }
            int idx = ((ComboBox)sender!).SelectedIndex;
            if (idx < 0 || idx >= SectorOrderList.Count) {
                return;
            }
            SectorOrderItem item = SectorOrderList[idx];
            if (item.Order == mSectorOrder) {
                return;
            }
            mSectorOrder = item.Order;
            await ReadFromDiskAsync();
        }

        // -----------------------------------------------------------------------------------------
        // Sector codec

        public string SectorCodecName { get; private set; } = string.Empty;

        private void PrepareSectorCodec() {
            if (mChunkAccess.NibbleCodec == null) {
                SectorCodecName = "N/A";
            } else {
                SectorCodecName = mChunkAccess.NibbleCodec.Name;
            }
            OnPropertyChanged(nameof(SectorCodecName));
        }

        // -----------------------------------------------------------------------------------------
        // Message box helpers (scoped to this window)

        private async System.Threading.Tasks.Task ShowMessageAsync(string message, string title) {
            var msgWin = new Window {
                Title = title,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Avalonia.Controls.StackPanel {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children = {
                        new TextBlock {
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
            await msgWin.ShowDialog(this);
        }

        private async System.Threading.Tasks.Task<bool> ShowConfirmAsync(
                string message, string title) {
            bool result = false;
            var confirmWin = new Window {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Avalonia.Controls.StackPanel {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children = {
                        new TextBlock {
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
            await confirmWin.ShowDialog(this);
            return result;
        }
    }
}
