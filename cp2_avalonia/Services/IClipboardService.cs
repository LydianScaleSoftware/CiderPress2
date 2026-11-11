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

 namespace cp2_avalonia.Services;

using System.Collections.Generic;
using System.Threading.Tasks;
using AppCommon;
using Models;

public interface IClipboardService {
    /// <summary>True if this process set the clipboard and the data is still pending.</summary>
    bool HasPendingContent { get; }

    /// <summary>
    /// Sets the clipboard. Caches xferEntries for same-process paste, takes ownership of the
    /// temp files that were already written (see <see cref="ClipTransferFiles"/>), and sets
    /// text/uri-list for cross-app paste.  The previous clip's temp files are deleted.
    /// </summary>
    Task SetClipAsync(ClipInfo clipInfo, List<ClipFileEntry> xferEntries,
        ClipTransferFiles.Result files);

    /// <summary>
    /// Gets the ClipInfo from the system clipboard. If the data was set by this process,
    /// ClipInfo.ClipEntries will reference the live cached entries (with stream generators).
    /// Returns null if the clipboard does not contain CP2 data.
    /// </summary>
    Task<ClipInfo?> GetClipAsync();

    /// <summary>
    /// Like <see cref="GetClipAsync"/>, but also returns the raw clipboard text that the
    /// ClipInfo was parsed from, so a caller that needs both reads the clipboard once.
    /// </summary>
    Task<(ClipInfo? Info, string? RawText)> GetClipWithTextAsync();

    /// <summary>Gets the raw text from the system clipboard. Returns null if unavailable.</summary>
    Task<string?> GetRawClipTextAsync();

    /// <summary>
    /// Gets file URIs from the clipboard. Tries text/uri-list,
    /// x-special/gnome-copied-files, and plain-text fallback.
    /// Returns null if no file URIs are found.
    /// </summary>
    Task<string?> GetUriListAsync();

    /// <summary>
    /// Releases the data this process put on the clipboard: forgets the cached entries and
    /// deletes the temp files immediately, then clears the system clipboard only if it still
    /// holds this process's data.  Does nothing if this process has not copied anything, so
    /// another application's clipboard contents are never touched.
    /// </summary>
    Task ClearIfPendingAsync();

    /// <summary>
    /// Sets plain text on the system clipboard. Used by ViewModels that need to
    /// copy formatted text (e.g. hex dump) without the full ClipInfo structure.
    /// </summary>
    Task SetTextAsync(string text);
}