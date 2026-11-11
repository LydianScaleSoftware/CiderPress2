/*
 * Copyright 2026 faddenSoft
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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommonUtil;
using DiskArc;
using DiskArc.Multi;
using cp2_avalonia.Services;
using static DiskArc.Defs;

namespace cp2_avalonia.ViewModels;

/// <summary>
/// ViewModel for the Replace Partition dialog.
/// </summary>
public class ReplacePartitionViewModel : ObservableObject
{
    /// <summary>
    /// Delegate that prepares the partition for writing (closes sub-volumes, etc.).
    /// Returns true on success, false on failure.
    /// </summary>
    public delegate bool EnableWriteFunc();

    private readonly EnableWriteFunc mEnableWriteFunc;
    private readonly Partition mDstPartition;
    private readonly IChunkAccess mSrcChunks;
    private readonly IDialogService mDialogService;

    public event Action<bool>? CloseRequested;

    public IRelayCommand CopyCommand { get; }
    public IRelayCommand CancelCommand { get; }

    // ── Display properties ────────────────────────────────────────────────────
    public string DstSizeText { get; }
    public string SrcSizeText { get; }

    private string mSizeDiffText;
    public string SizeDiffText {
        get => mSizeDiffText;
        private set => SetProperty(ref mSizeDiffText, value);
    }

    /// <summary>True when sizes are compatible; false causes foreground to turn red.</summary>
    private bool mIsSizeCompatible;
    public bool IsSizeCompatible {
        get => mIsSizeCompatible;
        private set => SetProperty(ref mIsSizeCompatible, value);
    }

    private bool mIsSizeDiffVisible;
    public bool IsSizeDiffVisible {
        get => mIsSizeDiffVisible;
        private set => SetProperty(ref mIsSizeDiffVisible, value);
    }

    private bool mIsValid;
    public bool IsValid {
        get => mIsValid;
        private set => SetProperty(ref mIsValid, value);
    }

    /// <summary>True while the copy is running; Copy and Cancel are disabled.</summary>
    private bool mIsBusy;
    public bool IsBusy {
        get => mIsBusy;
        private set {
            if (SetProperty(ref mIsBusy, value)) {
                CopyCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ReplacePartitionViewModel(
        Partition dstPartition,
        IChunkAccess srcChunks,
        EnableWriteFunc enableWriteFunc,
        Formatter formatter,
        AppHook appHook,
        IDialogService dialogService)
    {
        mDstPartition = dstPartition;
        mSrcChunks = srcChunks;
        mEnableWriteFunc = enableWriteFunc;
        mDialogService = dialogService;

        IChunkAccess dstChunks = dstPartition.ChunkAccess;

        // Build destination size text.
        if (dstChunks.HasSectors) {
            DstSizeText = string.Format(
                "Destination partition has {0} tracks of {1} sectors ({2}).",
                dstChunks.NumTracks, dstChunks.NumSectorsPerTrack,
                formatter.FormatSizeOnDisk(dstChunks.FormattedLength, KBLOCK_SIZE));
        } else if (dstChunks.HasBlocks) {
            DstSizeText = string.Format(
                "Destination partition has {0} blocks ({1}).",
                dstChunks.FormattedLength / BLOCK_SIZE,
                formatter.FormatSizeOnDisk(dstChunks.FormattedLength, KBLOCK_SIZE));
        } else {
            DstSizeText = "Destination partition has unknown geometry.";
        }

        // Build source size text.
        if (srcChunks.HasSectors) {
            SrcSizeText =
                $"Source image has {srcChunks.NumTracks} tracks of {srcChunks.NumSectorsPerTrack} sectors ({formatter.FormatSizeOnDisk(srcChunks.FormattedLength, KBLOCK_SIZE)}).";
        } else {
            SrcSizeText = string.Format("Source image has {0} blocks ({1}).",
                srcChunks.FormattedLength / BLOCK_SIZE,
                formatter.FormatSizeOnDisk(srcChunks.FormattedLength, KBLOCK_SIZE));
        }

        // Determine compatibility and diff text.
        bool valid = false;
        string diffText = string.Empty;
        bool diffVisible = true;
        bool sizeOk = true;

        if (!(srcChunks.HasSectors && dstChunks.HasSectors) &&
                !(srcChunks.HasBlocks && dstChunks.HasBlocks)) {
            diffText = "Source and destination are not compatible (blocks vs. sectors).";
            sizeOk = false;
        } else if (srcChunks.HasSectors && dstChunks.HasSectors &&
                srcChunks.NumSectorsPerTrack != dstChunks.NumSectorsPerTrack) {
            diffText = "Source and destination have differing sectors per track.";
            sizeOk = false;
        } else {
            if (srcChunks.FormattedLength == dstChunks.FormattedLength) {
                diffText = string.Empty;
                diffVisible = false;
                valid = true;
            } else if (srcChunks.FormattedLength < dstChunks.FormattedLength) {
                diffText = "NOTE: the source image is smaller than the destination. " +
                    "The leftover space will likely be unusable.";
                valid = true;
            } else {
                diffText = "The source image is larger than the destination.";
                sizeOk = false;
            }
        }

        mSizeDiffText = diffText;
        mIsSizeDiffVisible = diffVisible;
        mIsSizeCompatible = sizeOk;
        mIsValid = valid;

        CopyCommand = new AsyncRelayCommand(DoCopy, () => IsValid && !IsBusy);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false), () => !IsBusy);
    }

    private async Task DoCopy()
    {
        if (!mEnableWriteFunc()) {
            await mDialogService.ShowMessageAsync(
                "Unable to prepare partition for writing", "Failed");
            return;
        }

        // The copy runs off the UI thread so the dialog stays responsive.  This is allowed
        // under the X1 rule because this dialog is modal (the main window is disabled) and
        // Copy and Cancel are disabled, and the window refuses to close, while it runs.
        int errorCount = 0;
        IsBusy = true;
        try {
            await Task.Run(() => SaveAsDiskViewModel.CopyDisk(mSrcChunks,
                mDstPartition.ChunkAccess, out errorCount));
        } catch (Exception ex) {
            AppLog.E("Replace partition: copy failed", ex);
            IsBusy = false;
            await mDialogService.ShowMessageAsync(
                "The partition could not be replaced: " + ex.Message +
                "  The partition contents may be incomplete.", "Failed");
            CloseRequested?.Invoke(false);
            return;
        }
        IsBusy = false;

        if (errorCount != 0) {
            string msg = "Some data could not be read. Total errors: " + errorCount + ".";
            await mDialogService.ShowMessageAsync(msg, "Partial Copy");
        }

        CloseRequested?.Invoke(true);
    }
}
