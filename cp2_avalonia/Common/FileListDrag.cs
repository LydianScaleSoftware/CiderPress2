/*
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
using System.Collections.Generic;

using Avalonia.Input;
using DiskArc;

namespace cp2_avalonia.Common;

/// <summary>
/// The contract between the main window's file-list drag and the windows that accept the
/// entries: the data-transfer formats, and the state of the drag in progress.
/// </summary>
internal static class FileListDrag
{
    /// <summary>Data format: the dragged <c>List&lt;IFileEntry&gt;</c>.</summary>
    public static readonly DataFormat<List<IFileEntry>> ENTRIES_FORMAT =
        DataFormat.CreateInProcessFormat<List<IFileEntry>>("cp2_avalonia/FileListDrag");
    /// <summary>Data format: the archive or file system the entries belong to.</summary>
    public static readonly DataFormat<object> SOURCE_FORMAT =
        DataFormat.CreateInProcessFormat<object>("cp2_avalonia/FileListDragSource");
    /// <summary>Data format: the path of the open work file.</summary>
    public static readonly DataFormat<string> WORKPATH_FORMAT =
        DataFormat.CreateInProcessFormat<string>("cp2_avalonia/FileListDragWorkPath");

    /// <summary>
    /// What is being dragged.
    /// </summary>
    /// <param name="Entries">Dragged entries.</param>
    /// <param name="Source">Archive or file system the entries belong to.</param>
    /// <param name="WorkPath">Path of the open work file.</param>
    public sealed record State(IReadOnlyList<IFileEntry> Entries, object Source,
        string WorkPath);

    /// <summary>
    /// The file-list drag in progress in this process, or null.  Set by the main window for
    /// the length of the drag; UI thread only.
    /// </summary>
    /// <remarks>
    /// The data transfer normally carries the entries, but a macOS native drag session has no
    /// data transfer of ours (the pasteboard holds file promises and the clipboard JSON), so a
    /// window accepting the drag falls back to this.
    /// </remarks>
    public static State? Current { get; set; }

    /// <summary>
    /// Returns the dragged entries that a move into <paramref name="target"/> would actually
    /// change.  Entries already in the target are left out, and so is the target itself (a
    /// directory dropped onto its own row, e.g. by click jitter), which MoveFiles would
    /// otherwise refuse with a "Bad Move" dialog.
    /// </summary>
    public static List<IFileEntry> EntriesToMove(IEnumerable<IFileEntry> entries,
        IFileEntry target)
    {
        List<IFileEntry> result = new List<IFileEntry>();
        foreach (IFileEntry entry in entries)
        {
            if (entry != target && entry.ContainingDir != target)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the dragged entries from a drop's data transfer, or from <see cref="Current"/> if
    /// the data transfer does not have them.
    /// </summary>
    public static bool TryGet(IDataTransfer data, out State state)
    {
        if (data.Contains(ENTRIES_FORMAT) && data.Contains(SOURCE_FORMAT) &&
                data.TryGetValue(ENTRIES_FORMAT) is List<IFileEntry> entries && entries.Count > 0 &&
                data.TryGetValue(SOURCE_FORMAT) is object source)
        {
            string workPath = data.TryGetValue(WORKPATH_FORMAT) ?? string.Empty;
            state = new State(entries, source, workPath);
            return true;
        }
        if (Current is State cur && cur.Entries.Count > 0)
        {
            state = cur;
            return true;
        }
        state = null!;
        return false;
    }
}
