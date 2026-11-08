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
using System.Linq;

using CommonUtil;
using DiskArc;

namespace cp2_avalonia.Services;

/// <summary>
/// Applies the original file dates to host files written for drag-out and clipboard copy,
/// so the OS file manager receives the same timestamps "Extract files" would produce.
/// </summary>
/// <remarks>
/// <para>The WPF version hands the shell virtual files whose descriptors carry the dates.
/// Here the files are extracted to a temp directory first, so the dates must be set on what
/// we write; the file manager then carries them across when it copies or moves the files.</para>
/// <para>The rules mirror <c>ExtractFileWorker.SetFileAttributes</c>/<c>SetDirectoryAttributes</c>:
/// only valid dates are applied, creation time is set before modification time, and the
/// values are interpreted as local time.  The ordering matters on Linux, where .NET has no
/// birth time to set and maps <c>SetCreationTime</c> onto the modification time.</para>
/// </remarks>
internal sealed class ClipFileTimestamps
{
    private readonly Dictionary<string, FileAttribs> mFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileAttribs> mDirs = new(StringComparer.Ordinal);

    /// <summary>
    /// Records a path that was written for a clip entry.  Call <see cref="Apply"/> once all
    /// output has been written, because writing a later fork or a child file would otherwise
    /// overwrite the modification date again.
    /// </summary>
    /// <param name="hostPath">Full path of the file or directory that was written.</param>
    /// <param name="attrs">Attributes of the entry, with the original dates.</param>
    public void Add(string hostPath, FileAttribs attrs)
    {
        if (attrs.IsDirectory) {
            mDirs[hostPath] = attrs;
            return;
        }
        // Host-preservation mode writes the resource fork to "FILE/..namedfork/rsrc";
        // the dates belong to the file itself.
        string? parent = Path.GetDirectoryName(hostPath);
        if (Path.GetFileName(hostPath) == "rsrc" && parent != null &&
                Path.GetFileName(parent) == "..namedfork") {
            string? baseFile = Path.GetDirectoryName(parent);
            if (baseFile != null) {
                hostPath = baseFile;
            }
        }
        mFiles[hostPath] = attrs;
    }

    /// <summary>
    /// Sets the dates on every recorded path: files first, then directories from the deepest
    /// up, so that creating children doesn't disturb a directory's modification date.
    /// Failures are logged and skipped.
    /// </summary>
    public void Apply()
    {
        foreach (KeyValuePair<string, FileAttribs> kvp in mFiles) {
            SetTimes(kvp.Key, isDirectory: false, kvp.Value.CreateWhen, kvp.Value.ModWhen);
        }
        foreach (KeyValuePair<string, FileAttribs> kvp in
                mDirs.OrderByDescending(kvp => kvp.Key.Length)) {
            SetTimes(kvp.Key, isDirectory: true, kvp.Value.CreateWhen, kvp.Value.ModWhen);
        }
    }

    /// <summary>
    /// Sets the creation and modification dates on a host file or directory.  Invalid
    /// dates (e.g. entries from filesystems without timestamps) are left alone.
    /// </summary>
    public static void SetTimes(string hostPath, bool isDirectory, DateTime createWhen,
            DateTime modWhen)
    {
        try {
            if (TimeStamp.IsValidDate(createWhen)) {
                if (isDirectory) {
                    Directory.SetCreationTime(hostPath, createWhen);
                } else {
                    File.SetCreationTime(hostPath, createWhen);
                }
            }
            if (TimeStamp.IsValidDate(modWhen)) {
                if (isDirectory) {
                    Directory.SetLastWriteTime(hostPath, modWhen);
                } else {
                    File.SetLastWriteTime(hostPath, modWhen);
                }
            }
        } catch (Exception ex) {
            AppLog.W("Unable to set file dates on '" + hostPath + "'", ex);
        }
    }

    /// <summary>
    /// Copies the creation and modification dates from one host file or directory to
    /// another.  Used where we copy already-stamped temp files to their destination.
    /// </summary>
    public static void CopyTimes(string srcPath, string destPath, bool isDirectory)
    {
        try {
            DateTime create, mod;
            if (isDirectory) {
                create = Directory.GetCreationTime(srcPath);
                mod = Directory.GetLastWriteTime(srcPath);
            } else {
                create = File.GetCreationTime(srcPath);
                mod = File.GetLastWriteTime(srcPath);
            }
            SetTimes(destPath, isDirectory, create, mod);
        } catch (Exception ex) {
            AppLog.W("Unable to copy file dates to '" + destPath + "'", ex);
        }
    }
}
