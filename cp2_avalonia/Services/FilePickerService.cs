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
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace cp2_avalonia.Services;

public class FilePickerService(IDialogHost host) : IFilePickerService
{
    // Number of pickers currently showing (they cannot nest, but be safe).
    private int mActiveCount;

    public bool IsPickerActive => mActiveCount > 0;

    private IStorageProvider GetProvider() =>
        TopLevel.GetTopLevel(host.GetOwnerWindow())!.StorageProvider;

    /// <summary>Runs a picker call, keeping <see cref="IsPickerActive"/> set while it is up.</summary>
    private async Task<T> Track<T>(Func<Task<T>> show)
    {
        mActiveCount++;
        try { return await show(); }
        finally { mActiveCount--; }
    }

    private async Task<IStorageFolder?> ResolveStartDir(string? initialDir)
    {
        if (string.IsNullOrEmpty(initialDir)) return null;
        try { return await GetProvider().TryGetFolderFromPathAsync(initialDir); }
        catch (Exception ex) {
            AppLog.W("File picker: couldn't resolve initial directory '" + initialDir + "'", ex);
            return null;
        }
    }

    public async Task<string?> OpenFileAsync(string title,
            IReadOnlyList<FilePickerFileType>? fileTypes = null,
            string? initialDir = null)
    {
        var results = await Track(async () => await GetProvider().OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = fileTypes,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            }));
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title,
            IReadOnlyList<FilePickerFileType>? fileTypes = null,
            string? initialDir = null)
    {
        var results = await Track(async () => await GetProvider().OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = fileTypes,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            }));
        var paths = new List<string>(results.Count);
        foreach (var r in results) {
            string? path = r.TryGetLocalPath();
            if (path != null) paths.Add(path);
        }
        return paths;
    }

    public async Task<string?> SaveFileAsync(string title,
            string? suggestedName = null,
            IReadOnlyList<FilePickerFileType>? fileTypes = null,
            string? initialDir = null)
    {
        string? nameForPanel = PrepareSuggestedName(suggestedName, fileTypes,
            OperatingSystem.IsMacOS(), out string? strippedExt);
        var result = await Track(async () => await GetProvider().SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = nameForPanel,
                FileTypeChoices = fileTypes,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            }));
        string? path = result?.TryGetLocalPath();
        return path == null ? null : FinishSavePath(path, strippedExt);
    }

    /// <summary>
    /// Works out the name to show in the save panel.
    /// </summary>
    /// <remarks>
    /// The macOS save panel (Avalonia's <c>StorageProvider.mm</c>) sets the name field first and
    /// then the allowed content types from the first file type, and the panel then adds that
    /// type's extension itself, so "NewDisk.po" became "NewDisk.po.po".  On macOS a suggested
    /// name that ends with an extension of the first file type is passed without it, and the
    /// panel puts it back.  Other platforms get the name unchanged.
    /// </remarks>
    /// <param name="strippedExt">The extension removed (with its dot), or null.</param>
    internal static string? PrepareSuggestedName(string? suggestedName,
        IReadOnlyList<FilePickerFileType>? fileTypes, bool isMacOS, out string? strippedExt)
    {
        strippedExt = null;
        if (!isMacOS || string.IsNullOrEmpty(suggestedName) || fileTypes == null ||
                fileTypes.Count == 0 || fileTypes[0].Patterns == null) {
            return suggestedName;
        }
        string ext = System.IO.Path.GetExtension(suggestedName);
        if (ext.Length < 2 || ext.Length == suggestedName.Length) {
            return suggestedName;       // no extension, or a name that is only an extension
        }
        foreach (string pattern in fileTypes[0].Patterns!) {
            if (string.Equals(pattern, "*" + ext, StringComparison.OrdinalIgnoreCase)) {
                strippedExt = ext;
                return suggestedName.Substring(0, suggestedName.Length - ext.Length);
            }
        }
        return suggestedName;
    }

    /// <summary>
    /// If the extension was taken off the suggested name and the chosen path came back with
    /// no extension at all (for example the user picked "All Files"), puts it back.  A path
    /// with any extension is returned unchanged.
    /// </summary>
    internal static string FinishSavePath(string path, string? strippedExt)
    {
        if (strippedExt != null && !System.IO.Path.HasExtension(path)) {
            return path + strippedExt;
        }
        return path;
    }

    public async Task<string?> OpenFolderAsync(string title, string? initialDir = null)
    {
        var results = await Track(async () => await GetProvider().OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await ResolveStartDir(initialDir)
            }));
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }
}