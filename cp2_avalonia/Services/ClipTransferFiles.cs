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
using System;
using System.Collections.Generic;
using System.IO;

using AppCommon;

namespace cp2_avalonia.Services;

/// <summary>
/// Writes the temporary files for a copy or a drag: the "transfer" files that carry the
/// data to another CiderPress II instance, and the "foreign" files that a file manager
/// receives.  Used by both Copy/Cut and drag-out, so the layout and the rules live in one
/// place.
/// </summary>
/// <remarks>
/// <para>Layout of the temporary directory:</para>
/// <code>
/// &lt;tempDir&gt;/xfer/e0000, e0001, ...   transfer files, one per clip entry
/// &lt;tempDir&gt;/files/&lt;extract path&gt;      files and directories for the file manager
/// </code>
/// <para>The two sets live in separate subdirectories so that a foreign entry with any name
/// (<c>x</c>, <c>X</c>, <c>xfer</c>, ...) can never collide with a transfer file.</para>
/// </remarks>
public static class ClipTransferFiles
{
    /// <summary>Subdirectory holding the numbered transfer files.</summary>
    public const string XFER_SUBDIR = "xfer";

    /// <summary>Subdirectory holding the extracted files offered to the file manager.</summary>
    public const string FOREIGN_SUBDIR = "files";

    /// <summary>Prefix of the temp directory name for a clipboard copy.</summary>
    public const string CLIP_DIR_PREFIX = "cp2clip_";

    /// <summary>Prefix of the temp directory name for a drag.</summary>
    public const string DRAG_DIR_PREFIX = "cp2drag_";

    /// <summary>
    /// What was written.
    /// </summary>
    public sealed class Result
    {
        /// <summary>Temp directory, or null if nothing needed to be written.</summary>
        public string? TempDir { get; init; }

        /// <summary>
        /// Path of the transfer file for each xfer entry, parallel to the entry list;
        /// null for directories, entries without data, and entries that failed.
        /// </summary>
        public string?[] XferPaths { get; init; } = Array.Empty<string?>();

        /// <summary>
        /// Top-level paths of the extracted foreign entries (a directory appears once, not
        /// its contents).  These are what a file manager should be given, so that pasting
        /// a directory recreates its tree.
        /// </summary>
        public List<string> TopLevelForeignPaths { get; init; } = new();
    }

    /// <summary>
    /// Path of the transfer file for xfer entry <paramref name="index"/>.
    /// </summary>
    public static string GetXferPath(string tempDir, int index) =>
        Path.Combine(tempDir, XFER_SUBDIR, "e" + index.ToString("d4"));

    /// <summary>
    /// Path where the foreign entry with the given extract path is written.
    /// </summary>
    public static string GetForeignPath(string tempDir, string extractPath) =>
        Path.Combine(tempDir, FOREIGN_SUBDIR, extractPath);

    /// <summary>
    /// Top-level path (first component of the extract path) for a foreign entry.
    /// </summary>
    public static string GetTopLevelForeignPath(string tempDir, string extractPath)
    {
        int sep = extractPath.IndexOfAny(new[] { Path.DirectorySeparatorChar, '/' });
        string top = sep >= 0 ? extractPath.Substring(0, sep) : extractPath;
        return Path.Combine(tempDir, FOREIGN_SUBDIR, top);
    }

    /// <summary>
    /// Generates a new temp directory path (without creating it).
    /// </summary>
    /// <param name="prefix">One of <see cref="CLIP_DIR_PREFIX"/> or
    ///   <see cref="DRAG_DIR_PREFIX"/>.</param>
    public static string NewTempDirPath(string prefix) =>
        Path.Combine(Path.GetTempPath(),
            prefix + Environment.ProcessId + "_" + DateTime.UtcNow.Ticks.ToString("x"));

    /// <summary>
    /// True if there is anything to write for the clipboard: foreign entries, or transfer
    /// entries that have data.
    /// </summary>
    public static bool NeedsTempDir(List<ClipFileEntry> xferEntries,
            List<ClipFileEntry> foreignEntries)
    {
        if (foreignEntries.Count > 0) return true;
        foreach (ClipFileEntry entry in xferEntries) {
            if (entry.mStreamGen != null && !entry.Attribs.IsDirectory) return true;
        }
        return false;
    }

    /// <summary>
    /// Result for a copy that has nothing to write.
    /// </summary>
    public static Result Empty(int xferCount) => new Result {
        XferPaths = new string?[xferCount],
    };

    /// <summary>
    /// Extracts the entries into <paramref name="tempDir"/>.  Individual files that fail are
    /// logged and left out.
    /// </summary>
    /// <param name="tempDir">Directory to create and fill.</param>
    /// <param name="xferEntries">Entries whose content goes to the transfer files.</param>
    /// <param name="foreignEntries">Entries extracted for the file manager.</param>
    /// <param name="isCancelled">Polled between entries; return true to stop.  May be
    ///   null.</param>
    /// <param name="progress">Called with (percent, path) as entries are written.  May be
    ///   null.</param>
    /// <returns>What was written, or null if cancelled (the caller deletes
    ///   <paramref name="tempDir"/>).</returns>
    public static Result? Extract(string tempDir, List<ClipFileEntry> xferEntries,
            List<ClipFileEntry> foreignEntries, Func<bool>? isCancelled = null,
            Action<int, string>? progress = null)
    {
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, XFER_SUBDIR));
        Directory.CreateDirectory(Path.Combine(tempDir, FOREIGN_SUBDIR));

        int total = xferEntries.Count + foreignEntries.Count;
        int done = 0;
        void Report(string name)
        {
            progress?.Invoke(total == 0 ? 100 : done * 100 / total, name);
        }

        // Transfer files.  Their paths are passed to the destination in a "Cp2FileData"
        // array injected into the clipboard JSON by Cp2ClipUtil, which keeps AppCommon's
        // ClipFileEntry metadata-only.
        string?[] xferPaths = new string?[xferEntries.Count];
        for (int i = 0; i < xferEntries.Count; i++, done++) {
            if (isCancelled?.Invoke() == true) return null;
            ClipFileEntry entry = xferEntries[i];
            if (entry.mStreamGen == null || entry.Attribs.IsDirectory) continue;
            Report(entry.Attribs.FullPathName);
            string tempPath = GetXferPath(tempDir, i);
            try {
                using var outStream = new FileStream(tempPath, FileMode.Create);
                entry.mStreamGen.OutputToStream(outStream);
                xferPaths[i] = tempPath;
            } catch (Exception ex) {
                AppLog.E("Clip: transfer file extraction failed", ex);
            }
        }

        // Foreign files.  The file manager copies or moves these as they are, so they get
        // the original dates (see ClipFileTimestamps).
        var topLevel = new List<string>();
        var seenTopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timestamps = new ClipFileTimestamps();
        for (int i = 0; i < foreignEntries.Count; i++, done++) {
            if (isCancelled?.Invoke() == true) return null;
            ClipFileEntry entry = foreignEntries[i];
            Report(entry.Attribs.FullPathName);
            string extractPath = GetForeignPath(tempDir, entry.ExtractPath);
            try {
                string? dirName = Path.GetDirectoryName(extractPath);
                if (dirName != null) Directory.CreateDirectory(dirName);
                if (entry.Attribs.IsDirectory) {
                    Directory.CreateDirectory(extractPath);
                    timestamps.Add(extractPath, entry.Attribs);
                } else if (entry.mStreamGen != null) {
                    using (var outStream = new FileStream(extractPath, FileMode.Create)) {
                        entry.mStreamGen.OutputToStream(outStream);
                    }
                    timestamps.Add(extractPath, entry.Attribs);
                } else {
                    continue;
                }
            } catch (Exception ex) {
                AppLog.E("Clip: foreign-format extraction failed", ex);
                continue;
            }
            string top = GetTopLevelForeignPath(tempDir, entry.ExtractPath);
            if (seenTopLevel.Add(top)) {
                topLevel.Add(top);
            }
        }
        timestamps.Apply();

        return new Result {
            TempDir = tempDir,
            XferPaths = xferPaths,
            TopLevelForeignPaths = topLevel,
        };
    }
}
