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
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using AppCommon;
using CommonUtil;
using DiskArc;
using DiskArc.Arc;
using DiskArc.Disk;
using DiskArc.FS;
using static DiskArc.Defs;

namespace cp2_avalonia {
    /// <summary>
    /// Gather parameters for disk image creation.
    /// </summary>
    public partial class CreateDiskImage : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private IBrush mDefaultLabelColor = Brushes.Black;
        private IBrush mErrorLabelColor = Brushes.Red;

        /// <summary>
        /// True if the configuration is valid.
        /// </summary>
        public bool IsValid {
            get { return mIsValid; }
            set { mIsValid = value; OnPropertyChanged(); }
        }
        private bool mIsValid;

        /// <summary>
        /// Pathname of created file (set on success).
        /// </summary>
        public string PathName { get; private set; } = string.Empty;

        private AppHook mAppHook;

        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader.
        /// </summary>
        public CreateDiskImage() {
            mAppHook = null!;
            mVolumeNameText = "NEWDISK";
            mVolumeNumText = Defs.DEFAULT_525_VOLUME_NUM.ToString();
            mCustomSizeText = "65535";
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="owner">Parent window (ownership set at call site via ShowDialog).</param>
        /// <param name="appHook">Application hook reference.</param>
        public CreateDiskImage(Window owner, AppHook appHook) {
            mAppHook = appHook;

            // Restore all settings BEFORE InitializeComponent / DataContext (Pitfall #11).
            SettingsHolder settings = AppSettings.Global;
            mDiskSize = settings.GetEnum(AppSettings.NEW_DISK_SIZE, DiskSizeValue.Flop525_140);
            mFilesystem = settings.GetEnum(AppSettings.NEW_DISK_FILESYSTEM, FileSystemType.ProDOS);
            mFileType = settings.GetEnum(AppSettings.NEW_DISK_FILE_TYPE, FileTypeValue.ProDOSBlock);

            mVolumeNameText = settings.GetString(AppSettings.NEW_DISK_VOLUME_NAME, "NEWDISK");
            mVolumeNumText = settings.GetInt(AppSettings.NEW_DISK_VOLUME_NUM,
                Defs.DEFAULT_525_VOLUME_NUM).ToString();
            mCustomSizeText = settings.GetString(AppSettings.NEW_DISK_CUSTOM_SIZE, "65535");
            mIsChecked_ReserveBoot = settings.GetBool(AppSettings.NEW_DISK_RESERVE_BOOT, true);

            InitializeComponent();
            DataContext = this;

            UpdateControls();
        }

        private async void OkButton_Click(object? sender, RoutedEventArgs e) {
            try {
                if (!await CreateImage()) {
                    return;
                }

                // Save current UI state to settings before closing.
                GetVolNum(out int volNum);
                SettingsHolder settings = AppSettings.Global;
                settings.SetEnum(AppSettings.NEW_DISK_SIZE, mDiskSize);
                settings.SetEnum(AppSettings.NEW_DISK_FILESYSTEM, mFilesystem);
                settings.SetEnum(AppSettings.NEW_DISK_FILE_TYPE, mFileType);
                settings.SetString(AppSettings.NEW_DISK_VOLUME_NAME, mVolumeNameText);
                settings.SetInt(AppSettings.NEW_DISK_VOLUME_NUM, volNum);
                settings.SetString(AppSettings.NEW_DISK_CUSTOM_SIZE, mCustomSizeText);
                settings.SetBool(AppSettings.NEW_DISK_RESERVE_BOOT, mIsChecked_ReserveBoot);

                Close(true);
            } catch (Exception ex) {
                Debug.WriteLine("OkButton_Click exception: " + ex.Message);
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }

        /// <summary>
        /// Creates the disk image file as directed.
        /// </summary>
        /// <returns>True on success.</returns>
        private async Task<bool> CreateImage() {
            bool is13Sector = GetNumTracksSectors(out uint chkTracks, out uint chkSectors)
                && chkSectors == 13;

            string? pathName = await SelectOutputFile(this, mFileType, is13Sector);
            if (string.IsNullOrEmpty(pathName)) {
                return false;
            }

            FileStream? stream;
            try {
                stream = new FileStream(pathName, FileMode.Create);
            } catch (Exception ex) {
                await ShowErrorAsync("Unable to create file: " + ex.Message);
                return false;
            }

            MemoryStream? tmpStream = null;
            var waitCursor = new Cursor(StandardCursorType.Wait);
            this.Cursor = waitCursor;

            try {
                IDiskImage diskImage;
                SectorCodec codec;
                MediaKind mediaKind;
                uint blocks, tracks, sectors;

                GetVolNum(out int volNum);

                switch (mFileType) {
                    case FileTypeValue.DOSSector:
                        if (!GetNumTracksSectors(out tracks, out sectors)) {
                            throw new Exception("internal error");
                        }
                        diskImage = UnadornedSector.CreateSectorImage(stream, tracks, sectors,
                            SectorOrder.DOS_Sector, mAppHook);
                        break;
                    case FileTypeValue.ProDOSBlock:
                    case FileTypeValue.SimpleBlock:
                        if (GetNumTracksSectors(out tracks, out sectors)) {
                            diskImage = UnadornedSector.CreateSectorImage(stream, tracks, sectors,
                                SectorOrder.ProDOS_Block, mAppHook);
                        } else if (GetNumBlocks(out blocks)) {
                            diskImage = UnadornedSector.CreateBlockImage(stream, blocks, mAppHook);
                        } else {
                            throw new Exception("internal error");
                        }
                        break;
                    case FileTypeValue.TwoIMG:
                        if (GetNumTracksSectors(out tracks, out sectors)) {
                            diskImage = TwoIMG.CreateDOSSectorImage(stream, tracks, mAppHook);
                        } else if (GetNumBlocks(out blocks)) {
                            diskImage = TwoIMG.CreateProDOSBlockImage(stream, blocks, mAppHook);
                        } else {
                            throw new Exception("internal error");
                        }
                        if (volNum != Defs.DEFAULT_525_VOLUME_NUM) {
                            ((TwoIMG)diskImage).VolumeNumber = volNum;
                        }
                        break;
                    case FileTypeValue.NuFX:
                        if (!GetNumBlocks(out blocks)) {
                            throw new Exception("internal error");
                        }
                        tmpStream = new MemoryStream();
                        if (blocks == 280) {
                            diskImage = UnadornedSector.CreateSectorImage(tmpStream, 35, 16,
                                SectorOrder.ProDOS_Block, mAppHook);
                        } else if (blocks == 1600) {
                            diskImage = UnadornedSector.CreateBlockImage(tmpStream, 1600, mAppHook);
                        } else {
                            throw new Exception("internal error");
                        }
                        break;
                    case FileTypeValue.DiskCopy42:
                        if (!GetMediaKind(out mediaKind)) {
                            throw new Exception("internal error");
                        }
                        diskImage = DiskCopy.CreateDisk(stream, mediaKind, mAppHook);
                        break;
                    case FileTypeValue.Woz:
                        if (IsFlop525) {
                            if (!GetNumTracksSectors(out tracks, out sectors)) {
                                throw new Exception("internal error");
                            }
                            codec = (sectors == 13) ?
                                StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex525.Std_525_13) :
                                StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex525.Std_525_16);
                            diskImage = Woz.CreateDisk525(stream, tracks, codec, (byte)volNum,
                                mAppHook);
                        } else {
                            if (!GetMediaKind(out mediaKind)) {
                                throw new Exception("internal error");
                            }
                            codec = StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex35.Std_35);
                            diskImage = Woz.CreateDisk35(stream, mediaKind, WOZ_IL_35, codec,
                                mAppHook);
                        }
                        Woz woz = (Woz)diskImage;
                        woz.AddMETA();
                        woz.SetCreator("CiderPress II v" + GlobalAppVersion.AppVersion);
                        break;
                    case FileTypeValue.Moof:
                        if (IsFlop525) {
                            throw new Exception("internal error");
                        } else {
                            if (!GetMediaKind(out mediaKind)) {
                                throw new Exception("internal error");
                            }
                            codec = StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex35.Std_35);
                            diskImage = Moof.CreateDisk35(stream, mediaKind, MOOF_IL_35, codec,
                                mAppHook);
                        }
                        Moof moof = (Moof)diskImage;
                        moof.AddMETA();
                        moof.SetCreator("CiderPress II v" + GlobalAppVersion.AppVersion);
                        break;
                    case FileTypeValue.Nib:
                        if (!GetNumTracksSectors(out tracks, out sectors)) {
                            throw new Exception("internal error");
                        }
                        codec = (sectors == 13) ?
                            StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex525.Std_525_13) :
                            StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex525.Std_525_16);
                        diskImage = UnadornedNibble525.CreateDisk(stream, codec, (byte)volNum,
                            mAppHook);
                        break;
                    case FileTypeValue.Trackstar:
                        if (!GetNumTracksSectors(out tracks, out sectors)) {
                            throw new Exception("internal error");
                        }
                        codec = (sectors == 13) ?
                            StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex525.Std_525_13) :
                            StdSectorCodec.GetCodec(StdSectorCodec.CodecIndex525.Std_525_16);
                        diskImage = Trackstar.CreateDisk(stream, codec, (byte)volNum, tracks,
                            mAppHook);
                        break;
                    default:
                        throw new NotImplementedException("Not implemented: " + mFileType);
                }

                // Format the filesystem, if one was chosen.
                using (diskImage) {
                    if (mFilesystem != FileSystemType.Unknown) {
                        FileAnalyzer.CreateInstance(mFilesystem, diskImage.ChunkAccess!, mAppHook,
                            out IDiskContents? contents);
                        if (contents is not IFileSystem) {
                            throw new DAException("Unable to create filesystem");
                        }
                        IFileSystem fs = (IFileSystem)contents;
                        fs.Format(VolumeNameText, volNum, mIsChecked_ReserveBoot);
                        fs.Dispose();
                    }
                }

                // Handle NuFX ".SDK" disk image creation.
                if (mFileType == FileTypeValue.NuFX) {
                    Debug.Assert(tmpStream != null);
                    NuFX archive = NuFX.CreateArchive(mAppHook);
                    archive.StartTransaction();
                    IFileEntry entry = archive.CreateRecord();
                    if (mFilesystem == FileSystemType.ProDOS) {
                        entry.FileName = VolumeNameText;
                    } else {
                        entry.FileName = "DISK";
                    }
                    SimplePartSource source = new SimplePartSource(tmpStream);
                    archive.AddPart(entry, FilePart.DiskImage, source, CompressionFormat.Default);
                    archive.CommitTransaction(stream);
                }

                PathName = pathName;
            } catch (Exception ex) {
                await ShowErrorAsync("Error creating disk: " + ex.Message);
                stream.Close();
                stream = null;
                Debug.WriteLine("Cleanup: removing '" + pathName + "'");
                File.Delete(pathName);
                return false;
            } finally {
                stream?.Close();
                this.Cursor = null;
                waitCursor.Dispose();
            }

            return true;
        }

        private async Task ShowErrorAsync(string message) {
            var dlg = new Window {
                Title = "Error",
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
                        new Avalonia.Controls.Button {
                            Content = "OK",
                            Width = 80,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                        }
                    }
                }
            };
            var sp = (Avalonia.Controls.StackPanel)dlg.Content!;
            var btn = (Avalonia.Controls.Button)sp.Children[1];
            btn.Click += (_, _) => dlg.Close();
            await dlg.ShowDialog(this);
        }

        /// <summary>
        /// Selects the output file using the Avalonia storage provider.
        /// </summary>
        /// <param name="topLevel">The window used for the picker.</param>
        /// <param name="fileType">The disk image file type.</param>
        /// <param name="is13Sector">True if this is a 13-sector disk image.</param>
        /// <returns>Full path, or null/empty if cancelled.</returns>
        internal static async Task<string?> SelectOutputFile(TopLevel topLevel,
                FileTypeValue fileType, bool is13Sector) {
            FilePickerFileType fpft;
            string[] exts;
            switch (fileType) {
                case FileTypeValue.DOSSector:
                    if (is13Sector) {
                        fpft = new FilePickerFileType("DOS 13-Sector") { Patterns = new[] { "*.d13" } };
                        exts = new[] { ".d13" };
                    } else {
                        fpft = new FilePickerFileType("DOS-Order Disk") { Patterns = new[] { "*.do" } };
                        exts = new[] { ".do" };
                    }
                    break;
                case FileTypeValue.ProDOSBlock:
                    fpft = new FilePickerFileType("ProDOS-Order Disk") { Patterns = new[] { "*.po" } };
                    exts = new[] { ".po" };
                    break;
                case FileTypeValue.SimpleBlock:
                    fpft = new FilePickerFileType("Disk Image") { Patterns = new[] { "*.iso", "*.hdv" } };
                    exts = new[] { ".iso", ".hdv" };
                    break;
                case FileTypeValue.TwoIMG:
                    fpft = new FilePickerFileType("2IMG") { Patterns = new[] { "*.2mg" } };
                    exts = new[] { ".2mg" };
                    break;
                case FileTypeValue.NuFX:
                    fpft = new FilePickerFileType("ShrinkIt Disk") { Patterns = new[] { "*.sdk" } };
                    exts = new[] { ".sdk" };
                    break;
                case FileTypeValue.DiskCopy42:
                    fpft = new FilePickerFileType("DiskCopy 4.2") { Patterns = new[] { "*.image" } };
                    exts = new[] { ".image" };
                    break;
                case FileTypeValue.Woz:
                    fpft = new FilePickerFileType("WOZ") { Patterns = new[] { "*.woz" } };
                    exts = new[] { ".woz" };
                    break;
                case FileTypeValue.Moof:
                    fpft = new FilePickerFileType("MOOF") { Patterns = new[] { "*.moof" } };
                    exts = new[] { ".moof" };
                    break;
                case FileTypeValue.Nib:
                    fpft = new FilePickerFileType("Nibble") { Patterns = new[] { "*.nib" } };
                    exts = new[] { ".nib" };
                    break;
                case FileTypeValue.Trackstar:
                    fpft = new FilePickerFileType("Trackstar") { Patterns = new[] { "*.app" } };
                    exts = new[] { ".app" };
                    break;
                default:
                    throw new NotImplementedException("Not implemented: " + fileType);
            }

            string suggestedName = "NewDisk" + exts[0];

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions {
                    Title = "Create File...",
                    SuggestedFileName = suggestedName,
                    FileTypeChoices = new[] {
                        fpft,
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

            if (file == null) {
                return null;
            }

            string pathName = file.Path.LocalPath;
            bool isExtValid = false;
            foreach (string ext in exts) {
                if (pathName.ToLowerInvariant().EndsWith(ext)) {
                    isExtValid = true;
                    break;
                }
            }
            if (!isExtValid) {
                pathName += exts[0];
            }

            return pathName;
        }

        private static readonly string[] sPropList = {
            nameof(IsChecked_Flop525_113),
            nameof(IsChecked_Flop525_140),
            nameof(IsChecked_Flop525_160),
            nameof(IsChecked_Flop35_400),
            nameof(IsChecked_Flop35_800),
            nameof(IsChecked_Flop35_1440),
            nameof(IsChecked_Other_32MB),
            nameof(IsChecked_Other_Custom),

            nameof(IsEnabled_FS_DOS),
            nameof(IsChecked_FS_DOS),
            nameof(IsEnabled_FS_ProDOS),
            nameof(IsChecked_FS_ProDOS),
            nameof(IsEnabled_FS_HFS),
            nameof(IsChecked_FS_HFS),
            nameof(IsEnabled_FS_Pascal),
            nameof(IsChecked_FS_Pascal),
            nameof(IsEnabled_FS_CPM),
            nameof(IsChecked_FS_CPM),
            nameof(IsEnabled_FS_None),
            nameof(IsChecked_FS_None),

            nameof(IsEnabled_FT_DOSSector),
            nameof(IsChecked_FT_DOSSector),
            nameof(IsEnabled_FT_ProDOSBlock),
            nameof(IsChecked_FT_ProDOSBlock),
            nameof(IsEnabled_FT_SimpleBlock),
            nameof(IsChecked_FT_SimpleBlock),
            nameof(IsEnabled_FT_TwoIMG),
            nameof(IsChecked_FT_TwoIMG),
            nameof(IsEnabled_FT_NuFX),
            nameof(IsChecked_FT_NuFX),
            nameof(IsEnabled_FT_DiskCopy42),
            nameof(IsChecked_FT_DiskCopy42),
            nameof(IsEnabled_FT_Woz),
            nameof(IsChecked_FT_Woz),
            nameof(IsEnabled_FT_Moof),
            nameof(IsChecked_FT_Moof),
            nameof(IsEnabled_FT_Nib),
            nameof(IsChecked_FT_Nib),
            nameof(IsEnabled_FT_Trackstar),
            nameof(IsChecked_FT_Trackstar),
        };

        private const long FOUR_GB = 4L * 1024 * 1024 * 1024;

        /// <summary>
        /// Updates all controls, adjusting radio button selections if needed and
        /// validating free-text fields.
        /// </summary>
        private void UpdateControls() {
            bool customSizeSyntaxOk = true;
            bool customSizeLimitOk = true;
            if (mDiskSize == DiskSizeValue.Other_Custom) {
                long customSize = GetVolSize();
                if (customSize < 0) {
                    customSizeSyntaxOk = false;
                } else if (customSize == 0 || customSize > FOUR_GB) {
                    customSizeLimitOk = false;
                }
            }
            SizeDescForeground = customSizeSyntaxOk ? mDefaultLabelColor : mErrorLabelColor;
            SizeLimitForeground = customSizeLimitOk ? mDefaultLabelColor : mErrorLabelColor;

            IsValid = customSizeSyntaxOk && customSizeLimitOk;

            if (IsValid) {
                // Re-select filesystem if current one is now disabled.
                bool needNewFs = false;
                switch (mFilesystem) {
                    case FileSystemType.DOS33:   needNewFs = !IsEnabled_FS_DOS; break;
                    case FileSystemType.ProDOS:  needNewFs = !IsEnabled_FS_ProDOS; break;
                    case FileSystemType.HFS:     needNewFs = !IsEnabled_FS_HFS; break;
                    case FileSystemType.Pascal:  needNewFs = !IsEnabled_FS_Pascal; break;
                    case FileSystemType.CPM:     needNewFs = !IsEnabled_FS_CPM; break;
                    case FileSystemType.Unknown: break;
                    default: Debug.Assert(false); break;
                }
                if (needNewFs) {
                    if (IsEnabled_FS_DOS)
                    {
                        mFilesystem = FileSystemType.DOS33;
                    }
                    else if (IsEnabled_FS_ProDOS)
                    {
                        mFilesystem = FileSystemType.ProDOS;
                    }
                    else if (IsEnabled_FS_HFS)
                    {
                        mFilesystem = FileSystemType.HFS;
                    }
                    else if (IsEnabled_FS_Pascal)
                    {
                        mFilesystem = FileSystemType.Pascal;
                    }
                    else if (IsEnabled_FS_CPM)
                    {
                        mFilesystem = FileSystemType.CPM;
                    }
                    else
                    {
                        mFilesystem = FileSystemType.Unknown;
                    }
                }

                // Re-select file type if current one is now disabled.
                bool needNewType = false;
                switch (mFileType) {
                    case FileTypeValue.DOSSector:   needNewType = !IsEnabled_FT_DOSSector; break;
                    case FileTypeValue.ProDOSBlock:  needNewType = !IsEnabled_FT_ProDOSBlock; break;
                    case FileTypeValue.SimpleBlock:  needNewType = !IsEnabled_FT_SimpleBlock; break;
                    case FileTypeValue.TwoIMG:       needNewType = !IsEnabled_FT_TwoIMG; break;
                    case FileTypeValue.NuFX:         needNewType = !IsEnabled_FT_NuFX; break;
                    case FileTypeValue.DiskCopy42:   needNewType = !IsEnabled_FT_DiskCopy42; break;
                    case FileTypeValue.Woz:          needNewType = !IsEnabled_FT_Woz; break;
                    case FileTypeValue.Moof:         needNewType = !IsEnabled_FT_Moof; break;
                    case FileTypeValue.Nib:          needNewType = !IsEnabled_FT_Nib; break;
                    case FileTypeValue.Trackstar:    needNewType = !IsEnabled_FT_Trackstar; break;
                    default: Debug.Assert(false); break;
                }
                if (needNewType) {
                    if (IsEnabled_FT_ProDOSBlock)
                    {
                        mFileType = FileTypeValue.ProDOSBlock;
                    }
                    else if (IsEnabled_FT_SimpleBlock)
                    {
                        mFileType = FileTypeValue.SimpleBlock;
                    }
                    else if (IsEnabled_FT_DOSSector)
                    {
                        mFileType = FileTypeValue.DOSSector;
                    }
                    else if (IsEnabled_FT_Woz)
                    {
                        mFileType = FileTypeValue.Woz;
                    }
                    else if (IsEnabled_FT_Moof)
                    {
                        mFileType = FileTypeValue.Moof;
                    }
                    else if (IsEnabled_FT_TwoIMG)
                    {
                        mFileType = FileTypeValue.TwoIMG;
                    }
                    else if (IsEnabled_FT_NuFX)
                    {
                        mFileType = FileTypeValue.NuFX;
                    }
                    else if (IsEnabled_FT_DiskCopy42)
                    {
                        mFileType = FileTypeValue.DiskCopy42;
                    }
                    else if (IsEnabled_FT_Nib)
                    {
                        mFileType = FileTypeValue.Nib;
                    }
                    else if (IsEnabled_FT_Trackstar)
                    {
                        mFileType = FileTypeValue.Trackstar;
                    }
                    // else: custom size with no compatible type — leave selection alone
                }
            }

            // Validate volume name for filesystems that use it.
            bool volNameSyntaxOk = true;
            switch (mFilesystem) {
                case FileSystemType.ProDOS:
                    volNameSyntaxOk = ProDOS_FileEntry.IsVolumeNameValid(VolumeNameText);
                    break;
                case FileSystemType.HFS:
                    volNameSyntaxOk = HFS_FileEntry.IsVolumeNameValid(VolumeNameText);
                    break;
                case FileSystemType.Pascal:
                    volNameSyntaxOk = Pascal_FileEntry.IsVolumeNameValid(VolumeNameText);
                    break;
                case FileSystemType.DOS33:
                case FileSystemType.CPM:
                case FileSystemType.Unknown:
                    break;
                default:
                    throw new NotImplementedException("Didn't handle " + mFilesystem);
            }
            VolNameSyntaxForeground = volNameSyntaxOk ? mDefaultLabelColor : mErrorLabelColor;
            IsValid &= volNameSyntaxOk;

            // Validate volume number.
            bool volNumSyntaxOk = GetVolNum(out int _);
            VolNumSyntaxForeground = volNumSyntaxOk ? mDefaultLabelColor : mErrorLabelColor;
            IsValid &= volNumSyntaxOk;

            // Broadcast all property changes.
            foreach (string propName in sPropList) {
                OnPropertyChanged(propName);
            }
        }

        #region Disk Size

        public enum DiskSizeValue {
            Unknown = 0,
            Flop525_114, Flop525_140, Flop525_160,
            Flop35_400, Flop35_800, Flop35_1440,
            Other_32MB, Other_Custom
        }
        public DiskSizeValue mDiskSize;

        private long GetVolSize() {
            switch (mDiskSize) {
                case DiskSizeValue.Flop525_114:  return 35 * 13 * SECTOR_SIZE;
                case DiskSizeValue.Flop525_140:  return 35 * 16 * SECTOR_SIZE;
                case DiskSizeValue.Flop525_160:  return 40 * 16 * SECTOR_SIZE;
                case DiskSizeValue.Flop35_400:   return 400 * 1024;
                case DiskSizeValue.Flop35_800:   return 800 * 1024;
                case DiskSizeValue.Flop35_1440:  return 1440 * 1024;
                case DiskSizeValue.Other_32MB:   return 32 * 1024 * 1024;
                case DiskSizeValue.Other_Custom: return StringToValue.SizeToBytes(mCustomSizeText, true);
                default: throw new NotImplementedException("Didn't handle size=" + mDiskSize);
            }
        }

        private bool GetNumBlocks(out uint blocks) {
            long volSize = GetVolSize();
            if (volSize < 0 || volSize % BLOCK_SIZE != 0) {
                blocks = 0;
                return false;
            }
            blocks = (uint)(volSize / BLOCK_SIZE);
            return blocks * (long)BLOCK_SIZE == volSize;
        }

        private bool GetNumTracksSectors(out uint tracks, out uint sectors) {
            switch (mDiskSize) {
                case DiskSizeValue.Flop525_114:
                    tracks = 35; sectors = 13; return true;
                case DiskSizeValue.Flop525_140:
                    tracks = 35; sectors = 16; return true;
                case DiskSizeValue.Flop525_160:
                    tracks = 40; sectors = 16; return true;
                case DiskSizeValue.Flop35_400:
                case DiskSizeValue.Flop35_800:
                case DiskSizeValue.Flop35_1440:
                case DiskSizeValue.Other_32MB:
                case DiskSizeValue.Other_Custom:
                    long volSize = GetVolSize();
                    if (volSize == 400 * 1024) {
                        tracks = 50; sectors = 32; return true;
                    } else {
                        tracks = sectors = 0; return false;
                    }
                default:
                    throw new NotImplementedException("Didn't handle size=" + mDiskSize);
            }
        }

        private bool GetMediaKind(out MediaKind kind) {
            switch (mDiskSize) {
                case DiskSizeValue.Flop525_114:
                case DiskSizeValue.Flop525_140:
                case DiskSizeValue.Flop525_160:
                    kind = MediaKind.GCR_525; return true;
                case DiskSizeValue.Flop35_400:
                    kind = MediaKind.GCR_SSDD35; return true;
                case DiskSizeValue.Flop35_800:
                    kind = MediaKind.GCR_DSDD35; return true;
                case DiskSizeValue.Flop35_1440:
                    kind = MediaKind.MFM_DSHD35; return true;
                case DiskSizeValue.Other_32MB:
                case DiskSizeValue.Other_Custom:
                    kind = MediaKind.Unknown; return false;
                default:
                    throw new NotImplementedException("Didn't handle size=" + mDiskSize);
            }
        }

        private bool IsFlop525 =>
            mDiskSize == DiskSizeValue.Flop525_114 ||
            mDiskSize == DiskSizeValue.Flop525_140 ||
            mDiskSize == DiskSizeValue.Flop525_160;

        private bool IsFlop35 =>
            mDiskSize == DiskSizeValue.Flop35_400 ||
            mDiskSize == DiskSizeValue.Flop35_800 ||
            mDiskSize == DiskSizeValue.Flop35_1440;

        public bool IsChecked_Flop525_113 {
            get { return mDiskSize == DiskSizeValue.Flop525_114; }
            set { if (value) { mDiskSize = DiskSizeValue.Flop525_114; } UpdateControls(); }
        }
        public bool IsChecked_Flop525_140 {
            get { return mDiskSize == DiskSizeValue.Flop525_140; }
            set { if (value) { mDiskSize = DiskSizeValue.Flop525_140; } UpdateControls(); }
        }
        public bool IsChecked_Flop525_160 {
            get { return mDiskSize == DiskSizeValue.Flop525_160; }
            set { if (value) { mDiskSize = DiskSizeValue.Flop525_160; } UpdateControls(); }
        }
        public bool IsChecked_Flop35_400 {
            get { return mDiskSize == DiskSizeValue.Flop35_400; }
            set { if (value) { mDiskSize = DiskSizeValue.Flop35_400; } UpdateControls(); }
        }
        public bool IsChecked_Flop35_800 {
            get { return mDiskSize == DiskSizeValue.Flop35_800; }
            set { if (value) { mDiskSize = DiskSizeValue.Flop35_800; } UpdateControls(); }
        }
        public bool IsChecked_Flop35_1440 {
            get { return mDiskSize == DiskSizeValue.Flop35_1440; }
            set { if (value) { mDiskSize = DiskSizeValue.Flop35_1440; } UpdateControls(); }
        }
        public bool IsChecked_Other_32MB {
            get { return mDiskSize == DiskSizeValue.Other_32MB; }
            set { if (value) { mDiskSize = DiskSizeValue.Other_32MB; } UpdateControls(); }
        }
        public bool IsChecked_Other_Custom {
            get { return mDiskSize == DiskSizeValue.Other_Custom; }
            set { if (value) { mDiskSize = DiskSizeValue.Other_Custom; } UpdateControls(); }
        }

        public IBrush SizeDescForeground {
            get { return mSizeDescForeground; }
            set { mSizeDescForeground = value; OnPropertyChanged(); }
        }
        private IBrush mSizeDescForeground = Brushes.Black;

        public IBrush SizeLimitForeground {
            get { return mSizeLimitForeground; }
            set { mSizeLimitForeground = value; OnPropertyChanged(); }
        }
        private IBrush mSizeLimitForeground = Brushes.Black;

        public string CustomSizeText {
            get { return mCustomSizeText; }
            set {
                mCustomSizeText = value;
                OnPropertyChanged();
                mDiskSize = DiskSizeValue.Other_Custom;
                UpdateControls();
            }
        }
        private string mCustomSizeText;

        #endregion Disk Size

        #region Filesystem

        private FileSystemType mFilesystem;

        private bool GetVolNum(out int volNum) {
            if (!int.TryParse(VolumeNumText, out volNum)) {
                return false;
            }
            return volNum >= 0 && volNum <= 254;
        }

        public bool IsChecked_FS_None {
            get { return mFilesystem == FileSystemType.Unknown; }
            set { if (value) { mFilesystem = FileSystemType.Unknown; } UpdateControls(); }
        }
        public bool IsEnabled_FS_None => true;

        public bool IsChecked_FS_DOS {
            get { return mFilesystem == FileSystemType.DOS33; }
            set { if (value) { mFilesystem = FileSystemType.DOS33; } UpdateControls(); }
        }
        public bool IsEnabled_FS_DOS => DOS.IsSizeAllowed(GetVolSize()) && !IsFlop35;

        public bool IsChecked_FS_ProDOS {
            get { return mFilesystem == FileSystemType.ProDOS; }
            set { if (value) { mFilesystem = FileSystemType.ProDOS; } UpdateControls(); }
        }
        public bool IsEnabled_FS_ProDOS => ProDOS.IsSizeAllowed(GetVolSize());

        public bool IsChecked_FS_HFS {
            get { return mFilesystem == FileSystemType.HFS; }
            set { if (value) { mFilesystem = FileSystemType.HFS; } UpdateControls(); }
        }
        public bool IsEnabled_FS_HFS => HFS.IsSizeAllowed(GetVolSize());

        public bool IsChecked_FS_Pascal {
            get { return mFilesystem == FileSystemType.Pascal; }
            set { if (value) { mFilesystem = FileSystemType.Pascal; } UpdateControls(); }
        }
        public bool IsEnabled_FS_Pascal => Pascal.IsSizeAllowed(GetVolSize());

        public bool IsChecked_FS_CPM {
            get { return mFilesystem == FileSystemType.CPM; }
            set { if (value) { mFilesystem = FileSystemType.CPM; } UpdateControls(); }
        }
        public bool IsEnabled_FS_CPM => CPM.IsSizeAllowed(GetVolSize());

        public IBrush VolNameSyntaxForeground {
            get { return mVolNameSyntaxForeground; }
            set { mVolNameSyntaxForeground = value; OnPropertyChanged(); }
        }
        private IBrush mVolNameSyntaxForeground = Brushes.Black;

        public string VolumeNameText {
            get { return mVolumeNameText; }
            set { mVolumeNameText = value; OnPropertyChanged(); UpdateControls(); }
        }
        private string mVolumeNameText;

        public IBrush VolNumSyntaxForeground {
            get { return mVolNumSyntaxForeground; }
            set { mVolNumSyntaxForeground = value; OnPropertyChanged(); }
        }
        private IBrush mVolNumSyntaxForeground = Brushes.Black;

        public string VolumeNumText {
            get { return mVolumeNumText; }
            set { mVolumeNumText = value; OnPropertyChanged(); UpdateControls(); }
        }
        private string mVolumeNumText;

        public bool IsChecked_ReserveBoot {
            get { return mIsChecked_ReserveBoot; }
            set { mIsChecked_ReserveBoot = value; OnPropertyChanged(); }
        }
        private bool mIsChecked_ReserveBoot;

        #endregion Filesystem

        #region File Type

        // Interleave for WOZ/MOOF 3.5" floppy disks.
        private const int WOZ_IL_35 = 4;
        private const int MOOF_IL_35 = 2;

        public enum FileTypeValue {
            Unknown = 0,
            DOSSector,
            ProDOSBlock,
            SimpleBlock,
            TwoIMG,
            DiskCopy42,
            NuFX,
            Woz,
            Moof,
            Nib,
            Trackstar
        }
        private FileTypeValue mFileType;

        public bool IsChecked_FT_DOSSector {
            get { return mFileType == FileTypeValue.DOSSector; }
            set { if (value) { mFileType = FileTypeValue.DOSSector; } UpdateControls(); }
        }
        public bool IsEnabled_FT_DOSSector {
            get {
                if (!GetNumTracksSectors(out uint tracks, out uint sectors))
                {
                    return false;
                }

                return UnadornedSector.CanCreateSectorImage(tracks, sectors,
                    SectorOrder.DOS_Sector, out string _);
            }
        }

        public bool IsChecked_FT_ProDOSBlock {
            get { return mFileType == FileTypeValue.ProDOSBlock; }
            set { if (value) { mFileType = FileTypeValue.ProDOSBlock; } UpdateControls(); }
        }
        public bool IsEnabled_FT_ProDOSBlock {
            get {
                if (!GetNumBlocks(out uint blocks))
                {
                    return false;
                }

                return UnadornedSector.CanCreateBlockImage(blocks, out string _);
            }
        }

        public bool IsChecked_FT_SimpleBlock {
            get { return mFileType == FileTypeValue.SimpleBlock; }
            set { if (value) { mFileType = FileTypeValue.SimpleBlock; } UpdateControls(); }
        }
        public bool IsEnabled_FT_SimpleBlock => IsEnabled_FT_ProDOSBlock;

        public bool IsChecked_FT_TwoIMG {
            get { return mFileType == FileTypeValue.TwoIMG; }
            set { if (value) { mFileType = FileTypeValue.TwoIMG; } UpdateControls(); }
        }
        public bool IsEnabled_FT_TwoIMG {
            get {
                if (!GetNumBlocks(out uint blocks))
                {
                    return false;
                }

                return TwoIMG.CanCreateProDOSBlockImage(blocks, out string _);
            }
        }

        public bool IsChecked_FT_NuFX {
            get { return mFileType == FileTypeValue.NuFX; }
            set { if (value) { mFileType = FileTypeValue.NuFX; } UpdateControls(); }
        }
        public bool IsEnabled_FT_NuFX {
            get {
                if (!GetNumBlocks(out uint blocks))
                {
                    return false;
                }

                return blocks == 280 || blocks == 1600;
            }
        }

        public bool IsChecked_FT_DiskCopy42 {
            get { return mFileType == FileTypeValue.DiskCopy42; }
            set { if (value) { mFileType = FileTypeValue.DiskCopy42; } UpdateControls(); }
        }
        public bool IsEnabled_FT_DiskCopy42 {
            get {
                if (!GetNumBlocks(out uint _))
                {
                    return false;
                }

                if (!GetMediaKind(out MediaKind kind))
                {
                    return false;
                }

                return DiskCopy.CanCreateDisk(kind, out string _);
            }
        }

        public bool IsChecked_FT_Woz {
            get { return mFileType == FileTypeValue.Woz; }
            set { if (value) { mFileType = FileTypeValue.Woz; } UpdateControls(); }
        }
        public bool IsEnabled_FT_Woz {
            get {
                if (IsFlop525) {
                    if (!GetNumTracksSectors(out uint tracks, out uint _))
                    {
                        return false;
                    }

                    return Woz.CanCreateDisk525(tracks, out string _);
                } else {
                    if (!GetMediaKind(out MediaKind kind))
                    {
                        return false;
                    }

                    return Woz.CanCreateDisk35(kind, WOZ_IL_35, out string _);
                }
            }
        }

        public bool IsChecked_FT_Moof {
            get { return mFileType == FileTypeValue.Moof; }
            set { if (value) { mFileType = FileTypeValue.Moof; } UpdateControls(); }
        }
        public bool IsEnabled_FT_Moof {
            get {
                if (IsFlop525)
                {
                    return false;
                }

                if (!GetMediaKind(out MediaKind kind))
                {
                    return false;
                }

                return Moof.CanCreateDisk35(kind, MOOF_IL_35, out string _);
            }
        }

        public bool IsChecked_FT_Nib {
            get { return mFileType == FileTypeValue.Nib; }
            set { if (value) { mFileType = FileTypeValue.Nib; } UpdateControls(); }
        }
        public bool IsEnabled_FT_Nib {
            get {
                if (!GetNumTracksSectors(out uint tracks, out uint _))
                {
                    return false;
                }

                return tracks == 35;
            }
        }

        public bool IsChecked_FT_Trackstar {
            get { return mFileType == FileTypeValue.Trackstar; }
            set { if (value) { mFileType = FileTypeValue.Trackstar; } UpdateControls(); }
        }
        public bool IsEnabled_FT_Trackstar {
            get {
                if (!GetNumTracksSectors(out uint tracks, out uint sectors))
                {
                    return false;
                }

                return Trackstar.CanCreateDisk(tracks, sectors, out string _);
            }
        }

        #endregion File Type
    }
}
