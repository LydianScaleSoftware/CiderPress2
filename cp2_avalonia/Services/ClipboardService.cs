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

using System.Diagnostics;
using System.Linq;

namespace cp2_avalonia.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using cp2_avalonia.Platform;
using AppCommon;
using Models;

public class ClipboardService : IClipboardService {
    private ClipInfo? mPendingClipInfo;
    private List<ClipFileEntry>? mCachedClipEntries;
    private string?[]? mXferPaths;     // parallel to mCachedClipEntries; same-process fast path
    private string? mClipTempDir;

    public bool HasPendingContent => mPendingClipInfo != null;

    // Resolves the system clipboard lazily at call time — not captured at
    // construction — so the correct window is used in multi-window scenarios.
    private static IClipboard? GetClipboard() {
        if (Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime lifetime) {
            var window = lifetime.MainWindow;
            if (window != null) return TopLevel.GetTopLevel(window)?.Clipboard;
        }
        return null;
    }

    public async Task SetClipAsync(ClipInfo clipInfo, List<ClipFileEntry> xferEntries,
            ClipTransferFiles.Result files) {
        // The new clip's files are already written.  Drop the previous clip's temp files.
        CleanupClipTemp();
        mPendingClipInfo = clipInfo;
        mCachedClipEntries = xferEntries;
        mClipTempDir = files.TempDir;
        // Also register the directory, so it is deleted at exit even if nothing else
        // gets the chance to.
        TempDirectoryTracker.Track(mClipTempDir);
        mXferPaths = files.TempDir != null ? files.XferPaths : null;

        var dataObj = new DataObject();
        // Inject Cp2FileData into the JSON so cross-instance paste can locate temp files.
        string clipJson = clipInfo.ToClipString();
        if (mXferPaths != null)
            clipJson = Cp2ClipUtil.InjectFileData(clipJson, mXferPaths);
        dataObj.Set(DataFormats.Text, clipJson);

        if (files.TopLevelForeignPaths.Count > 0) {
            // Offer only the top-level items, so that pasting a directory in a file manager
            // recreates the whole tree rather than a flat list of its files.
            var uriList = new StringBuilder();
            foreach (string path in files.TopLevelForeignPaths) {
                uriList.Append(new Uri(path).AbsoluteUri);
                uriList.Append("\r\n");
            }
            dataObj.Set("text/uri-list", uriList.ToString());
        }

        var clipboard = GetClipboard();
        if (clipboard != null)
            await clipboard.SetDataObjectAsync(dataObj);
    }

    public async Task<ClipInfo?> GetClipAsync() {
        (ClipInfo? info, _) = await GetClipWithTextAsync();
        return info;
    }

    public async Task<(ClipInfo? Info, string? RawText)> GetClipWithTextAsync() {
        string? text = await GetRawClipTextAsync();
        ClipInfo? info = ClipInfo.FromClipString(text);
        if (info != null && mCachedClipEntries != null &&
                info.ProcessId == Environment.ProcessId &&
                info.ClipEntries?.Count == mCachedClipEntries.Count) {
            // Same-process paste: substitute live cached entries (have stream generators).
            info.ClipEntries = mCachedClipEntries;
        }
        return (info, text);
    }

    public async Task<string?> GetRawClipTextAsync() {
        // On Linux, read clipboard directly via our background X11 connection.
        // Avalonia's GetTextAsync() requests TARGETS first (two round-trips through
        // the Avalonia event loop); our path skips TARGETS and runs on the dedicated
        // LinuxDrag background thread — eliminating the "few seconds" paste delay.
        if (OperatingSystem.IsLinux()) {
#pragma warning disable CA1416
            string? direct = await LinuxDrag.ReadMainWindowClipboardTextAsync();
#pragma warning restore CA1416
            if (direct != null) return direct;
        }
        var clipboard = GetClipboard();
        if (clipboard == null) return null;
        return await clipboard.GetTextAsync();
    }

    public async Task<string?> GetUriListAsync() {
        var clipboard = GetClipboard();
        if (clipboard == null) return null;

        // IClipboard.GetDataAsync(string) is [Obsolete] in Avalonia 11.3 and routes
        // "text/uri-list" through a DataFormat<byte[]> lookup that silently fails on X11
        // (the X11 backend maps "text/uri-list" to DataFormat.File, not a byte format).
        // Use DataFormats.Files instead — it calls dataTransfer.TryGetFilesAsync() and
        // returns IStorageItem[], which we convert to a text/uri-list string.
        object? filesObj = await clipboard.GetDataAsync(DataFormats.Files);
        if (filesObj is IEnumerable<IStorageItem> storageItems) {
            var sb = new StringBuilder();
            foreach (IStorageItem item in storageItems) {
                string? localPath = item.TryGetLocalPath();
                if (localPath != null) {
                    sb.Append(new Uri(localPath).AbsoluteUri);
                    sb.Append("\r\n");
                }
            }
            if (sb.Length > 0) return sb.ToString();
        }

        // Try text/uri-list directly (fallback for platforms where the above doesn't apply).
        string? uriList = await TryGetClipFormat(clipboard, "text/uri-list");
        if (!string.IsNullOrEmpty(uriList)) return uriList;

        // Try x-special/gnome-copied-files (GNOME/KDE Dolphin).
        string? gnomeData = await TryGetClipFormat(clipboard, "x-special/gnome-copied-files");
        if (!string.IsNullOrEmpty(gnomeData)) {
            int newline = gnomeData.IndexOf('\n');
            if (newline >= 0) return gnomeData.Substring(newline + 1);
        }

        // Plain-text fallback: file:// URIs or absolute paths.
        string? plainText = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(plainText)) {
            string firstLine = plainText.Split('\n')[0].Trim('\r', ' ');
            if (firstLine.StartsWith("file://") ||
                    (Path.IsPathRooted(firstLine) && File.Exists(firstLine))) {
                if (!firstLine.StartsWith("file://")) {
                    var sb = new StringBuilder();
                    foreach (string line in plainText.Split('\n')) {
                        string path = line.Trim('\r', ' ');
                        if (!string.IsNullOrEmpty(path) && Path.IsPathRooted(path)) {
                            sb.Append(new Uri(path).AbsoluteUri);
                            sb.Append("\r\n");
                        }
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                return plainText;
            }
        }
        return null;
    }

    public async Task ClearIfPendingAsync() {
        if (mPendingClipInfo == null) {
            return;     // we didn't put anything on the clipboard
        }
        // Release our data and files now.  Do this before awaiting anything, so it is done
        // even if the process is exiting.
        mPendingClipInfo = null;
        mCachedClipEntries = null;
        mXferPaths = null;
        CleanupClipTemp();

        // Clear the system clipboard only if it still holds our data.  If the user has
        // since copied something in another application, leave it alone.
        var clipboard = GetClipboard();
        if (clipboard == null) return;
        try {
            string? text = await GetRawClipTextAsync();
            ClipInfo? onClipboard = ClipInfo.FromClipString(text);
            if (onClipboard != null && onClipboard.ProcessId == Environment.ProcessId) {
                await clipboard.ClearAsync();
            }
        } catch (Exception ex) {
            AppLog.W("Clipboard: failed to clear", ex);
        }
    }

    public async Task SetTextAsync(string text) {
        var clipboard = GetClipboard();
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private void CleanupClipTemp() {
        if (mClipTempDir != null) {
            try { Directory.Delete(mClipTempDir, true); } 
            catch (Exception e)
            {
                AppLog.W("Clipboard: failed to delete temp directory", e);
            }
            mClipTempDir = null;
        }
    }

    private static async Task<string?> TryGetClipFormat(
            IClipboard clipboard, string format) {
        try
        {
            object? data = await clipboard.GetDataAsync(format);
            if (data is string s) return s;
            if (data is byte[] { Length: > 0 } bytes)
                return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e)
        {
            AppLog.W("Clipboard: failed to read format '" + format + "'", e);
        }
        return null;
    }
}