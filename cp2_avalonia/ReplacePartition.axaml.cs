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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

using AppCommon;
using CommonUtil;
using DiskArc;
using DiskArc.Multi;
using cp2_avalonia.Common;
using static DiskArc.Defs;

namespace cp2_avalonia {
    /// <summary>
    /// Replace a partition with the contents of a disk image file.
    /// </summary>
    public partial class ReplacePartition : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public bool IsValid {
            get { return mIsValid; }
            set { mIsValid = value; OnPropertyChanged(); }
        }
        private bool mIsValid;

        public string DstSizeText {
            get { return mDstSizeText; }
            set { mDstSizeText = value; OnPropertyChanged(); }
        }
        private string mDstSizeText = string.Empty;

        public string SrcSizeText {
            get { return mSrcSizeText; }
            set { mSrcSizeText = value; OnPropertyChanged(); }
        }
        private string mSrcSizeText = string.Empty;

        public string SizeDiffText {
            get { return mSizeDiffText; }
            set { mSizeDiffText = value; OnPropertyChanged(); }
        }
        private string mSizeDiffText = string.Empty;

        private static IBrush GetDefaultLabelBrush() {
            if (Application.Current?.TryFindResource("ThemeForegroundBrush",
                    Application.Current.ActualThemeVariant, out var brush) == true
                    && brush is IBrush ib) {
                return ib;
            }
            return Brushes.Black;
        }
        private static readonly IBrush sErrorLabelBrush = Brushes.Red;

        public IBrush SizeDiffForeground {
            get { return mSizeDiffForeground; }
            set { mSizeDiffForeground = value; OnPropertyChanged(); }
        }
        private IBrush mSizeDiffForeground = GetDefaultLabelBrush();

        public bool IsSizeDiffVisible {
            get { return mIsSizeDiffVisible; }
            set { mIsSizeDiffVisible = value; OnPropertyChanged(); }
        }
        private bool mIsSizeDiffVisible = true;

        public delegate bool EnableWriteFunc();
        private EnableWriteFunc mEnableWriteFunc;

        private Partition mDstPartition;
        private IChunkAccess mSrcChunks;
        private AppHook mAppHook;


        public ReplacePartition() {
            // Parameterless constructor for AXAML previewer.
            mEnableWriteFunc = () => false;
            mDstPartition = null!;
            mSrcChunks = null!;
            mAppHook = null!;
            InitializeComponent();
            DataContext = this;
        }

        public ReplacePartition(Partition dstPartition, IChunkAccess srcChunks,
                EnableWriteFunc enableWriteFunc, Formatter formatter, AppHook appHook) {
            mDstPartition = dstPartition;
            mSrcChunks = srcChunks;
            mEnableWriteFunc = enableWriteFunc;
            mAppHook = appHook;

            IChunkAccess dstChunks = dstPartition.ChunkAccess;
            if (dstChunks.HasSectors) {
                mDstSizeText =
                    string.Format("Destination partition has {0} tracks of {1} sectors ({2}).",
                        dstChunks.NumTracks, dstChunks.NumSectorsPerTrack,
                        formatter.FormatSizeOnDisk(dstChunks.FormattedLength, KBLOCK_SIZE));
            } else if (dstChunks.HasBlocks) {
                mDstSizeText =
                    string.Format("Destination partition has {0} blocks ({1}).",
                        dstChunks.FormattedLength / BLOCK_SIZE,
                        formatter.FormatSizeOnDisk(dstChunks.FormattedLength, KBLOCK_SIZE));
            } else {
                throw new NotImplementedException();
            }

            if (srcChunks.HasSectors) {
                mSrcSizeText =
                    string.Format("Source image has {0} tracks of {1} sectors ({2}).",
                        srcChunks.NumTracks, srcChunks.NumSectorsPerTrack,
                        formatter.FormatSizeOnDisk(srcChunks.FormattedLength, KBLOCK_SIZE));
            } else {
                mSrcSizeText =
                    string.Format("Source image has {0} blocks ({1}).",
                        srcChunks.FormattedLength / BLOCK_SIZE,
                        formatter.FormatSizeOnDisk(srcChunks.FormattedLength, KBLOCK_SIZE));
            }

            mIsValid = false;
            if (!(srcChunks.HasSectors && dstChunks.HasSectors) &&
                    !(srcChunks.HasBlocks && dstChunks.HasBlocks)) {
                mSizeDiffText = "Source and destination are not compatible (blocks vs. sectors).";
            } else if (srcChunks.HasSectors && dstChunks.HasSectors &&
                    srcChunks.NumSectorsPerTrack != dstChunks.NumSectorsPerTrack) {
                mSizeDiffText = "Source and destination have differing sectors per track.";
            } else {
                if (srcChunks.FormattedLength == dstChunks.FormattedLength) {
                    mSizeDiffText = string.Empty;
                    mIsSizeDiffVisible = false;
                    mIsValid = true;
                } else if (srcChunks.FormattedLength < dstChunks.FormattedLength) {
                    mSizeDiffText = "NOTE: the source image is smaller than the destination. " +
                        "The leftover space will likely be unusable.";
                    mIsValid = true;
                } else {
                    // Not allowed.
                    mSizeDiffText = "The source image is larger than the destination.";
                }
            }
            if (!mIsValid) {
                mSizeDiffForeground = sErrorLabelBrush;
            }

            InitializeComponent();
            DataContext = this;
        }

        private async void CopyButton_Click(object? sender, RoutedEventArgs e) {
            if (!mEnableWriteFunc()) {
                await PlatformUtil.ShowMessageAsync(this, "Unable to prepare partition for writing",
                    "Failed");
                return;
            }

            SaveAsDisk.CopyDisk(mSrcChunks, mDstPartition.ChunkAccess, out int errorCount);
            if (errorCount != 0) {
                string msg = "Some data could not be read. Total errors: " + errorCount + ".";
                await PlatformUtil.ShowMessageAsync(this, msg, "Partial Copy");
            }

            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }
    }
}
