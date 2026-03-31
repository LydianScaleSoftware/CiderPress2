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
using System.Text.RegularExpressions;

using Avalonia.Controls;
using Avalonia.Media;

using AppCommon;
using CommonUtil;
using DiskArc;
using DiskArc.Arc;
using DiskArc.FS;

namespace cp2_avalonia {
    /// <summary>
    /// File entry attribute editor.
    /// </summary>
    public partial class EditAttributes : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Set to true when input is valid.  Controls whether the OK button is enabled.
        /// </summary>
        public bool IsValid {
            get { return mIsValid; }
            set { mIsValid = value; OnPropertyChanged(); }
        }
        private bool mIsValid;

        /// <summary>
        /// Set to true when attributes can't be edited.
        /// </summary>
        public bool IsAllReadOnly {
            get { return mIsAllReadOnly; }
            set { mIsAllReadOnly = value; OnPropertyChanged(); }
        }
        private bool mIsAllReadOnly;

        private IBrush mDefaultLabelColor = Brushes.Black;
        private IBrush mErrorLabelColor = Brushes.Red;

        /// <summary>
        /// File attributes after edits.  The filename will always be in FullPathName, even
        /// for disk images.
        /// </summary>
        public FileAttribs NewAttribs { get; private set; } = new FileAttribs();

        private object mArchiveOrFileSystem;
        private IFileEntry mFileEntry;
        private IFileEntry mADFEntry;
        private FileAttribs mOldAttribs;


        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader.
        /// </summary>
        public EditAttributes() {
            mArchiveOrFileSystem = null!;
            mFileEntry = IFileEntry.NO_ENTRY;
            mADFEntry = IFileEntry.NO_ENTRY;
            mOldAttribs = new FileAttribs();
            mIsValidFunc = _ => false;
            mSyntaxRulesText = string.Empty;
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="parent">Parent window.</param>
        /// <param name="archiveOrFileSystem">IArchive or IFileSystem object.</param>
        /// <param name="entry">File entry object.</param>
        /// <param name="adfEntry">For MacZip, the ADF header entry; otherwise NO_ENTRY.</param>
        /// <param name="attribs">Current file attributes, from entry or MacZip header contents.</param>
        /// <param name="isReadOnly">True if the source is read-only.</param>
        public EditAttributes(Window parent, object archiveOrFileSystem, IFileEntry entry,
                IFileEntry adfEntry, FileAttribs attribs, bool isReadOnly) {
            mArchiveOrFileSystem = archiveOrFileSystem;
            mFileEntry = entry;
            mADFEntry = adfEntry;
            mOldAttribs = attribs;
            IsAllReadOnly = isReadOnly;

            if (archiveOrFileSystem is IArchive) {
                IArchive arc = (IArchive)archiveOrFileSystem;
                mIsValidFunc = arc.IsValidFileName;
                mSyntaxRulesText = "\u2022 " + arc.Characteristics.FileNameSyntaxRules;
                if (arc.Characteristics.DefaultDirSep != IFileEntry.NO_DIR_SEP) {
                    DirSepText = string.Format(DIR_SEP_CHAR_FMT, arc.Characteristics.DefaultDirSep);
                    mIsDirSepTextVisible = true;
                } else {
                    DirSepText = string.Empty;
                    mIsDirSepTextVisible = false;
                }
            } else if (archiveOrFileSystem is IFileSystem) {
                IFileSystem fs = (IFileSystem)archiveOrFileSystem;
                if (entry.IsDirectory && entry.ContainingDir == IFileEntry.NO_ENTRY) {
                    // Volume Directory.
                    mIsValidFunc = fs.IsValidVolumeName;
                    mSyntaxRulesText = "\u2022 " + fs.Characteristics.VolumeNameSyntaxRules;
                } else {
                    mIsValidFunc = fs.IsValidFileName;
                    mSyntaxRulesText = "\u2022 " + fs.Characteristics.FileNameSyntaxRules;
                }
                DirSepText = string.Empty;
                mIsDirSepTextVisible = false;
            } else {
                throw new NotImplementedException("Can't edit " + archiveOrFileSystem);
            }

            NewAttribs = new FileAttribs(mOldAttribs);

            PrepareFileName();

            PrepareProTypeList();
            ProTypeDescString = FileTypes.GetDescription(attribs.FileType, attribs.AuxType);
            ProAuxString = attribs.AuxType.ToString("X4");

            PrepareHFSTypes();

            PrepareTimestamps();

            PrepareAccess();

            PrepareComment();

            // All Prepare*() calls must run BEFORE InitializeComponent() / DataContext = this.
            InitializeComponent();
            Owner = parent;
            DataContext = this;

            UpdateControls();
        }

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);
            Loaded_FileType();
            fileNameTextBox.SelectAll();
            fileNameTextBox.Focus();
        }

        /// <summary>
        /// Updates the validation-state-dependent controls.
        /// </summary>
        private void UpdateControls() {
            SyntaxRulesForeground = mIsFileNameValid ? mDefaultLabelColor : mErrorLabelColor;
            UniqueNameForeground = mIsFileNameUnique ? mDefaultLabelColor : mErrorLabelColor;

            ProAuxForeground = mProAuxValid ? mDefaultLabelColor : mErrorLabelColor;
            if (mProAuxValid) {
                ProTypeDescString =
                    FileTypes.GetDescription(NewAttribs.FileType, NewAttribs.AuxType);
            } else {
                ProTypeDescString = string.Empty;
            }

            HFSTypeForeground = mHFSTypeValid ? mDefaultLabelColor : mErrorLabelColor;
            HFSCreatorForeground = mHFSCreatorValid ? mDefaultLabelColor : mErrorLabelColor;

            CreateWhenForeground = mCreateWhenValid ? mDefaultLabelColor : mErrorLabelColor;
            ModWhenForeground = mModWhenValid ? mDefaultLabelColor : mErrorLabelColor;

            if (IsAllReadOnly) {
                IsValid = false;
            } else {
                IsValid = mIsFileNameValid && mIsFileNameUnique && mProAuxValid &&
                    mHFSTypeValid && mHFSCreatorValid && mCreateWhenValid && mModWhenValid;
            }
        }

        private void OkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
            Close(true);
        }

        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
            Close(false);
        }


        #region Filename

        public string SyntaxRulesText {
            get { return mSyntaxRulesText; }
            set { mSyntaxRulesText = value; OnPropertyChanged(); }
        }
        private string mSyntaxRulesText = string.Empty;

        public IBrush SyntaxRulesForeground {
            get { return mSyntaxRulesForeground; }
            set { mSyntaxRulesForeground = value; OnPropertyChanged(); }
        }
        private IBrush mSyntaxRulesForeground = Brushes.Black;

        public IBrush UniqueNameForeground {
            get { return mUniqueNameForeground; }
            set { mUniqueNameForeground = value; OnPropertyChanged(); }
        }
        private IBrush mUniqueNameForeground = Brushes.Black;

        public bool IsUniqueTextVisible { get; private set; } = true;

        public string DirSepText { get; private set; } = string.Empty;
        public bool IsDirSepTextVisible {
            get { return mIsDirSepTextVisible; }
            private set { mIsDirSepTextVisible = value; OnPropertyChanged(); }
        }
        private bool mIsDirSepTextVisible = false;

        private const string DIR_SEP_CHAR_FMT = "\u2022 Directory separator character is '{0}'.";

        private delegate bool IsValidFileNameFunc(string name);
        private IsValidFileNameFunc mIsValidFunc = _ => true;

        private bool mIsFileNameValid;
        private bool mIsFileNameUnique;

        /// <summary>
        /// Filename string.
        /// </summary>
        public string FileName {
            get { return NewAttribs.FullPathName; }
            set {
                NewAttribs.FullPathName = value;
                OnPropertyChanged();
                CheckFileNameValidity(out mIsFileNameValid, out mIsFileNameUnique);
                UpdateControls();
            }
        }

        private void CheckFileNameValidity(out bool isValid, out bool isUnique) {
            if (IsAllReadOnly) {
                isValid = isUnique = true;
                return;
            }
            isValid = mIsValidFunc(NewAttribs.FullPathName);
            isUnique = true;
            if (mArchiveOrFileSystem is IArchive) {
                IArchive arc = (IArchive)mArchiveOrFileSystem;
                if (arc.TryFindFileEntry(NewAttribs.FullPathName, out IFileEntry entry) &&
                        entry != mFileEntry) {
                    isUnique = false;
                }
            } else {
                IFileSystem fs = (IFileSystem)mArchiveOrFileSystem;
                if (mFileEntry.ContainingDir != IFileEntry.NO_ENTRY) {
                    if (fs.TryFindFileEntry(mFileEntry.ContainingDir, NewAttribs.FullPathName,
                            out IFileEntry entry) && entry != mFileEntry) {
                        isUnique = false;
                    }
                }
            }
        }

        private void PrepareFileName() {
            if (mFileEntry is DOS_FileEntry && mFileEntry.IsDirectory) {
                // The DOS volume name is formatted as "DOS-nnn", but we just want the number.
                NewAttribs.FullPathName = ((DOS)mArchiveOrFileSystem).VolumeNum.ToString("D3");
            } else if (mArchiveOrFileSystem is IArchive) {
                NewAttribs.FullPathName = mOldAttribs.FullPathName;
            } else {
                NewAttribs.FullPathName = mOldAttribs.FileNameOnly;
            }
            CheckFileNameValidity(out mIsFileNameValid, out mIsFileNameUnique);
        }

        #endregion Filename

        #region File Type

        public bool IsProTypeVisible { get; private set; } = true;
        public bool IsHFSTypeVisible { get; private set; } = true;

        public class ProTypeListItem {
            public string Label { get; private set; }
            public byte Value { get; private set; }

            public ProTypeListItem(string label, byte value) {
                Label = label;
                Value = value;
            }
        }

        /// <summary>
        /// List of suitable types from the ProDOS type list.  ItemsSource for the ComboBox.
        /// </summary>
        public List<ProTypeListItem> ProTypeList { get; } = new List<ProTypeListItem>();

        /// <summary>
        /// True if the ProDOS type list is enabled.
        /// </summary>
        public bool IsProTypeListEnabled { get; private set; } = true;

        /// <summary>
        /// True if the ProDOS aux type entry field is enabled.
        /// </summary>
        public bool IsProAuxEnabled { get; private set; } = true;

        public string ProTypeDescString {
            get { return mProTypeDescString; }
            set { mProTypeDescString = value; OnPropertyChanged(); }
        }
        private string mProTypeDescString = string.Empty;

        /// <summary>
        /// Aux type input field (0-4 hex chars).  Must be a valid hex value or empty string.
        /// </summary>
        public string ProAuxString {
            get { return mProAuxString; }
            set {
                mProAuxString = value;
                mProAuxValid = true;
                OnPropertyChanged();
                if (string.IsNullOrEmpty(value)) {
                    NewAttribs.AuxType = 0;
                } else {
                    try {
                        NewAttribs.AuxType = Convert.ToUInt16(value, 16);
                    } catch (Exception) {
                        mProAuxValid = false;
                    }
                }
                UpdateControls();
            }
        }
        private string mProAuxString = string.Empty;
        private bool mProAuxValid = true;

        public IBrush ProAuxForeground {
            get { return mProAuxForeground; }
            set { mProAuxForeground = value; OnPropertyChanged(); }
        }
        private IBrush mProAuxForeground = Brushes.Black;

        public string HFSTypeCharsString {
            get { return mHFSTypeCharsString; }
            set {
                mHFSTypeCharsString = value;
                OnPropertyChanged();
                mHFSTypeHexString = SetHexFromChars(value, out uint newNum, out mHFSTypeValid);
                OnPropertyChanged(nameof(HFSTypeHexString));
                NewAttribs.HFSFileType = newNum;
                UpdateControls();
            }
        }
        private string mHFSTypeCharsString = string.Empty;

        public string HFSTypeHexString {
            get { return mHFSTypeHexString; }
            set {
                mHFSTypeHexString = value;
                OnPropertyChanged();
                mHFSTypeCharsString = SetCharsFromHex(value, out uint newNum, out mHFSTypeValid);
                OnPropertyChanged(nameof(HFSTypeCharsString));
                NewAttribs.HFSFileType = newNum;
                UpdateControls();
            }
        }
        private string mHFSTypeHexString = string.Empty;
        private bool mHFSTypeValid = true;

        public IBrush HFSTypeForeground {
            get { return mHFSTypeForeground; }
            set { mHFSTypeForeground = value; OnPropertyChanged(); }
        }
        private IBrush mHFSTypeForeground = Brushes.Black;

        public string HFSCreatorCharsString {
            get { return mHFSCreatorCharsString; }
            set {
                mHFSCreatorCharsString = value;
                OnPropertyChanged();
                mHFSCreatorHexString =
                    SetHexFromChars(value, out uint newNum, out mHFSCreatorValid);
                OnPropertyChanged(nameof(HFSCreatorHexString));
                NewAttribs.HFSCreator = newNum;
                UpdateControls();
            }
        }
        private string mHFSCreatorCharsString = string.Empty;

        public string HFSCreatorHexString {
            get { return mHFSCreatorHexString; }
            set {
                mHFSCreatorHexString = value;
                OnPropertyChanged();
                mHFSCreatorCharsString =
                    SetCharsFromHex(value, out uint newNum, out mHFSCreatorValid);
                OnPropertyChanged(nameof(HFSCreatorCharsString));
                NewAttribs.HFSCreator = newNum;
                UpdateControls();
            }
        }
        private string mHFSCreatorHexString = string.Empty;
        private bool mHFSCreatorValid = true;

        public IBrush HFSCreatorForeground {
            get { return mHFSCreatorForeground; }
            set { mHFSCreatorForeground = value; OnPropertyChanged(); }
        }
        private IBrush mHFSCreatorForeground = Brushes.Black;

        private static string SetHexFromChars(string charValue, out uint newNum, out bool isValid) {
            string newHexStr;
            isValid = true;
            if (string.IsNullOrEmpty(charValue)) {
                newNum = 0;
                newHexStr = string.Empty;
            } else if (charValue.Length == 4) {
                newNum = MacChar.IntifyMacConstantString(charValue);
                newHexStr = newNum.ToString("X8");
            } else {
                newNum = 0;
                newHexStr = string.Empty;
                isValid = false;
            }
            return newHexStr;
        }

        private static string SetCharsFromHex(string hexStr, out uint newNum, out bool isValid) {
            string newCharStr;
            isValid = true;
            if (string.IsNullOrEmpty(hexStr)) {
                newCharStr = string.Empty;
                newNum = 0;
            } else {
                try {
                    newNum = Convert.ToUInt32(hexStr, 16);
                    newCharStr = MacChar.StringifyMacConstant(newNum);
                } catch (Exception) {
                    isValid = false;
                    newNum = 0;
                    newCharStr = string.Empty;
                }
            }
            return newCharStr;
        }

        private static readonly byte[] DOS_TYPES = {
            FileAttribs.FILE_TYPE_TXT,
            FileAttribs.FILE_TYPE_INT,
            FileAttribs.FILE_TYPE_BAS,
            FileAttribs.FILE_TYPE_BIN,
            FileAttribs.FILE_TYPE_F2,
            FileAttribs.FILE_TYPE_REL,
            FileAttribs.FILE_TYPE_F3,
            FileAttribs.FILE_TYPE_F4
        };
        private static readonly byte[] PASCAL_TYPES = {
            FileAttribs.FILE_TYPE_NON,
            FileAttribs.FILE_TYPE_BAD,
            FileAttribs.FILE_TYPE_PCD,
            FileAttribs.FILE_TYPE_PTX,
            FileAttribs.FILE_TYPE_F3,
            FileAttribs.FILE_TYPE_PDA,
            FileAttribs.FILE_TYPE_F4,
            FileAttribs.FILE_TYPE_FOT,
            FileAttribs.FILE_TYPE_F5
        };

        private void PrepareProTypeList() {
            if (mFileEntry is DOS_FileEntry) {
                if (mFileEntry.IsDirectory) {
                    IsProTypeListEnabled = false;
                    IsProAuxEnabled = false;
                    IsProTypeVisible = false;
                } else {
                    foreach (byte type in DOS_TYPES) {
                        string abbrev = FileTypes.GetDOSTypeAbbrev(type);
                        ProTypeList.Add(new ProTypeListItem(abbrev, type));
                    }
                }
            } else if (mFileEntry is Pascal_FileEntry) {
                IsProAuxEnabled = false;
                if (mFileEntry.IsDirectory) {
                    IsProTypeListEnabled = false;
                    IsProTypeVisible = false;
                } else {
                    foreach (byte type in PASCAL_TYPES) {
                        string abbrev = FileTypes.GetPascalTypeName(type);
                        ProTypeList.Add(new ProTypeListItem(abbrev, type));
                    }
                }
            } else if (mFileEntry.HasProDOSTypes || mADFEntry != IFileEntry.NO_ENTRY) {
                for (int type = 0; type < 256; type++) {
                    string abbrev = FileTypes.GetFileTypeAbbrev(type);
                    if (abbrev[0] == '$') {
                        abbrev = "???";
                    }
                    string label = abbrev + " $" + type.ToString("X2");
                    ProTypeList.Add(new ProTypeListItem(label, (byte)type));
                }

                IsProTypeListEnabled = !mFileEntry.IsDirectory;
                if (mArchiveOrFileSystem is IFileSystem) {
                    IsProAuxEnabled = mFileEntry.ContainingDir != IFileEntry.NO_ENTRY;
                } else {
                    IsProAuxEnabled = true;
                }
            } else {
                IsProTypeListEnabled = IsProAuxEnabled = false;
                IsProTypeVisible = false;
            }

            if (mFileEntry.IsDirectory && mFileEntry.ContainingDir == IFileEntry.NO_ENTRY) {
                IsUniqueTextVisible = false;
            }

            if (IsAllReadOnly) {
                IsProTypeListEnabled = false;
            }
        }

        private void PrepareHFSTypes() {
            if (mADFEntry == IFileEntry.NO_ENTRY && !mFileEntry.HasHFSTypes) {
                IsHFSTypeVisible = false;
            }

            if (NewAttribs.HFSFileType == 0) {
                HFSTypeHexString = string.Empty;
            } else {
                HFSTypeHexString = NewAttribs.HFSFileType.ToString("X8");
            }
            if (NewAttribs.HFSCreator == 0) {
                HFSCreatorHexString = string.Empty;
            } else {
                HFSCreatorHexString = NewAttribs.HFSCreator.ToString("X8");
            }
        }

        private void Loaded_FileType() {
            for (int i = 0; i < ProTypeList.Count; i++) {
                if (ProTypeList[i].Value == NewAttribs.FileType) {
                    proTypeCombo.SelectedIndex = i;
                    break;
                }
            }
            if (ProTypeList.Count != 0 && proTypeCombo.SelectedIndex < 0) {
                Debug.Assert(mFileEntry is DOS_FileEntry, "no ProDOS type matched");
                proTypeCombo.SelectedIndex = 0;
            }
        }

        private void ProTypeCombo_SelectionChanged(object? sender,
                SelectionChangedEventArgs e) {
            int selIndex = proTypeCombo.SelectedIndex;
            if (selIndex >= 0) {
                NewAttribs.FileType = ProTypeList[selIndex].Value;
                Debug.WriteLine("ProDOS file type: $" + NewAttribs.FileType.ToString("x2"));
            }
            UpdateControls();
        }

        #endregion File Type

        #region Timestamps

        public bool IsTimestampVisible { get; private set; } = true;

        // Avalonia DatePicker uses DateTimeOffset? for SelectedDate.
        public DateTimeOffset TimestampStart { get; set; }
        public DateTimeOffset TimestampEnd { get; set; }

        // String versions for the info text block (DateTimeOffset doesn't format as nicely).
        public string TimestampStartStr { get; set; } = string.Empty;
        public string TimestampEndStr { get; set; } = string.Empty;

        public DateTimeOffset? CreateDate {
            get { return mCreateDate; }
            set {
                mCreateDate = value;
                OnPropertyChanged();
                NewAttribs.CreateWhen = DateTimeUpdated(mCreateDate, mCreateTimeString,
                    out mCreateWhenValid);
                UpdateControls();
            }
        }
        private DateTimeOffset? mCreateDate;

        public string CreateTimeString {
            get { return mCreateTimeString; }
            set {
                mCreateTimeString = value;
                OnPropertyChanged();
                NewAttribs.CreateWhen = DateTimeUpdated(mCreateDate, mCreateTimeString,
                    out mCreateWhenValid);
                UpdateControls();
            }
        }
        private string mCreateTimeString = string.Empty;
        private bool mCreateWhenValid = true;

        public bool CreateWhenEnabled { get; private set; } = true;
        public bool ModWhenEnabled { get; private set; } = true;

        public DateTimeOffset? ModDate {
            get { return mModDate; }
            set {
                mModDate = value;
                OnPropertyChanged();
                NewAttribs.ModWhen = DateTimeUpdated(mModDate, mModTimeString,
                    out mModWhenValid);
                UpdateControls();
            }
        }
        private DateTimeOffset? mModDate;

        public string ModTimeString {
            get { return mModTimeString; }
            set {
                mModTimeString = value;
                OnPropertyChanged();
                NewAttribs.ModWhen = DateTimeUpdated(mModDate, mModTimeString,
                    out mModWhenValid);
                UpdateControls();
            }
        }
        private string mModTimeString = string.Empty;
        private bool mModWhenValid = true;

        public IBrush CreateWhenForeground {
            get { return mCreateWhenForeground; }
            set { mCreateWhenForeground = value; OnPropertyChanged(); }
        }
        private IBrush mCreateWhenForeground = Brushes.Black;

        public IBrush ModWhenForeground {
            get { return mModWhenForeground; }
            set { mModWhenForeground = value; OnPropertyChanged(); }
        }
        private IBrush mModWhenForeground = Brushes.Black;

        private const string TIME_PATTERN = @"^(\d{1,2}):(\d\d)(?>:(\d\d))?$";
        private static Regex sTimeRegex = new Regex(TIME_PATTERN);

        private DateTime DateTimeUpdated(DateTimeOffset? ndt, string timeStr, out bool isValid) {
            isValid = true;
            if (ndt == null) {
                return TimeStamp.NO_DATE;
            }
            DateTime dt = ndt.Value.DateTime;
            DateTime newWhen;
            if (!string.IsNullOrEmpty(timeStr)) {
                MatchCollection matches = sTimeRegex.Matches(timeStr);
                if (matches.Count != 1) {
                    isValid = false;
                    return TimeStamp.NO_DATE;
                }
                int hours = int.Parse(matches[0].Groups[1].Value);
                int minutes = int.Parse(matches[0].Groups[2].Value);
                int seconds = 0;
                if (!string.IsNullOrEmpty(matches[0].Groups[3].Value)) {
                    seconds = int.Parse(matches[0].Groups[3].Value);
                }
                if (hours >= 24 || minutes >= 60 || seconds >= 60) {
                    isValid = false;
                    return TimeStamp.NO_DATE;
                }

                newWhen = new DateTime(dt.Year, dt.Month, dt.Day, hours, minutes, seconds,
                    DateTimeKind.Local);
            } else {
                DateTime newDt = new DateTime(dt.Year, dt.Month, dt.Day);
                newWhen = DateTime.SpecifyKind(newDt, DateTimeKind.Local);
            }

            isValid = newWhen >= TimestampStart.DateTime && newWhen <= TimestampEnd.DateTime;
            return newWhen;
        }

        private void PrepareTimestamps() {
            DateTime tsStart, tsEnd;
            if (mArchiveOrFileSystem is IArchive) {
                if (mADFEntry == IFileEntry.NO_ENTRY) {
                    IArchive arc = (IArchive)mArchiveOrFileSystem;
                    tsStart = arc.Characteristics.TimeStampStart;
                    tsEnd = arc.Characteristics.TimeStampEnd;
                } else {
                    tsStart = AppleSingle.SCharacteristics.TimeStampStart;
                    tsEnd = AppleSingle.SCharacteristics.TimeStampEnd;
                }
            } else {
                IFileSystem fs = (IFileSystem)mArchiveOrFileSystem;
                tsStart = fs.Characteristics.TimeStampStart;
                tsEnd = fs.Characteristics.TimeStampEnd;
            }

            TimestampStart = new DateTimeOffset(tsStart);
            TimestampEnd = new DateTimeOffset(tsEnd);
            TimestampStartStr = tsStart.ToShortDateString();
            TimestampEndStr = tsEnd.ToShortDateString();

            if (tsStart == tsEnd) {
                IsTimestampVisible = false;
            }

            if ((mArchiveOrFileSystem is Zip && mADFEntry == IFileEntry.NO_ENTRY) ||
                    mArchiveOrFileSystem is GZip || mArchiveOrFileSystem is Pascal) {
                CreateWhenEnabled = false;
            }

            if (TimeStamp.IsValidDate(NewAttribs.CreateWhen)) {
                mCreateDate = new DateTimeOffset(NewAttribs.CreateWhen);
                mCreateTimeString = NewAttribs.CreateWhen.ToString("HH:mm:ss");
            } else {
                mCreateDate = null;
                mCreateTimeString = string.Empty;
            }
            if (TimeStamp.IsValidDate(NewAttribs.ModWhen)) {
                mModDate = new DateTimeOffset(NewAttribs.ModWhen);
                mModTimeString = NewAttribs.ModWhen.ToString("HH:mm:ss");
            } else {
                mModDate = null;
                mModTimeString = string.Empty;
            }
            mCreateWhenValid = mModWhenValid = true;

            if (IsAllReadOnly) {
                CreateWhenEnabled = false;
                ModWhenEnabled = false;
            }
        }

        #endregion Timestamps

        #region Access Flags

        public bool IsAccessVisible { get; private set; } = true;

        public bool IsLockedOnlyVisible { get; private set; } = false;
        public bool IsAllFlagsVisible { get; private set; } = false;

        private const byte FILE_ACCESS_TOGGLE = (byte)
            (FileAttribs.AccessFlags.Write |
            FileAttribs.AccessFlags.Rename |
            FileAttribs.AccessFlags.Delete);

        public bool AccessLocked {
            get { return mAccessLocked; }
            set {
                mAccessLocked = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access = (byte)(NewAttribs.Access & ~FILE_ACCESS_TOGGLE);
                } else {
                    NewAttribs.Access |= FILE_ACCESS_TOGGLE;
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Read;
                }
            }
        }
        private bool mAccessLocked;
        public bool AccessLockedEnabled { get; private set; } = true;

        public bool AccessRead {
            get { return mAccessRead; }
            set {
                mAccessRead = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Read;
                } else {
                    NewAttribs.Access =
                        (byte)(NewAttribs.Access & (byte)~FileAttribs.AccessFlags.Read);
                }
            }
        }
        private bool mAccessRead;
        public bool AccessReadEnabled { get; private set; } = true;

        public bool AccessWrite {
            get { return mAccessWrite; }
            set {
                mAccessWrite = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Write;
                } else {
                    NewAttribs.Access =
                        (byte)(NewAttribs.Access & (byte)~FileAttribs.AccessFlags.Write);
                }
            }
        }
        private bool mAccessWrite;
        public bool AccessWriteEnabled { get; private set; } = true;

        public bool AccessRename {
            get { return mAccessRename; }
            set {
                mAccessRename = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Rename;
                } else {
                    NewAttribs.Access =
                        (byte)(NewAttribs.Access & (byte)~FileAttribs.AccessFlags.Rename);
                }
            }
        }
        private bool mAccessRename;
        public bool AccessRenameEnabled { get; private set; } = true;

        public bool AccessDelete {
            get { return mAccessDelete; }
            set {
                mAccessDelete = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Delete;
                } else {
                    NewAttribs.Access =
                        (byte)(NewAttribs.Access & (byte)~FileAttribs.AccessFlags.Delete);
                }
            }
        }
        private bool mAccessDelete;
        public bool AccessDeleteEnabled { get; private set; } = true;

        public bool AccessBackup {
            get { return mAccessBackup; }
            set {
                mAccessBackup = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Backup;
                } else {
                    NewAttribs.Access =
                        (byte)(NewAttribs.Access & (byte)~FileAttribs.AccessFlags.Backup);
                }
            }
        }
        private bool mAccessBackup;
        public bool AccessBackupEnabled { get; private set; } = true;

        public bool AccessInvisible {
            get { return mAccessInvisible; }
            set {
                mAccessInvisible = value;
                OnPropertyChanged();
                if (value) {
                    NewAttribs.Access |= (byte)FileAttribs.AccessFlags.Invisible;
                } else {
                    NewAttribs.Access =
                        (byte)(NewAttribs.Access & (byte)~FileAttribs.AccessFlags.Invisible);
                }
            }
        }
        private bool mAccessInvisible;
        public bool AccessInvisibleEnabled { get; private set; } = true;

        private void PrepareAccess() {
            if ((mArchiveOrFileSystem is Zip && mADFEntry == IFileEntry.NO_ENTRY) ||
                    mArchiveOrFileSystem is GZip || mArchiveOrFileSystem is Pascal) {
                IsAccessVisible = false;
                return;
            }
            if (mFileEntry.IsDirectory && mFileEntry.ContainingDir == IFileEntry.NO_ENTRY) {
                IsAccessVisible = false;
                return;
            }

            if (mArchiveOrFileSystem is ProDOS || mArchiveOrFileSystem is NuFX ||
                    mArchiveOrFileSystem is CPM) {
                IsAllFlagsVisible = true;
                IsLockedOnlyVisible = false;
                if (mArchiveOrFileSystem is CPM) {
                    AccessReadEnabled = AccessRenameEnabled = AccessDeleteEnabled =
                        AccessInvisibleEnabled = false;
                }
                mAccessRead = (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Read) != 0;
                mAccessWrite = (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Write) != 0;
                mAccessRename = (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Rename) != 0;
                mAccessBackup = (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Backup) != 0;
                mAccessDelete = (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Delete) != 0;
                mAccessInvisible =
                    (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Invisible) != 0;
            } else {
                IsAllFlagsVisible = false;
                IsLockedOnlyVisible = true;

                mAccessLocked = (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Write) == 0;

                AccessInvisibleEnabled =
                    mArchiveOrFileSystem is AppleSingle ||
                    mArchiveOrFileSystem is Binary2 ||
                    mADFEntry != IFileEntry.NO_ENTRY;
                mAccessInvisible =
                    (NewAttribs.Access & (byte)FileAttribs.AccessFlags.Invisible) != 0;
            }

            if (IsAllReadOnly) {
                AccessLockedEnabled = AccessInvisibleEnabled = AccessReadEnabled =
                    AccessWriteEnabled = AccessRenameEnabled = AccessBackupEnabled =
                    AccessDeleteEnabled = false;
            }
        }

        #endregion Access Flags

        #region Comment

        public bool IsCommentVisible { get; private set; } = true;

        public string CommentText {
            get { return mCommentText; }
            set {
                mCommentText = value;
                OnPropertyChanged();
                NewAttribs.Comment = value;
            }
        }
        private string mCommentText = string.Empty;

        private void PrepareComment() {
            if ((mArchiveOrFileSystem is not Zip || mADFEntry != IFileEntry.NO_ENTRY) &&
                    mArchiveOrFileSystem is not NuFX) {
                IsCommentVisible = false;
                return;
            }

            mCommentText = NewAttribs.Comment;
        }

        #endregion Comment
    }
}
