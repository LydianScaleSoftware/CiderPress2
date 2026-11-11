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
using System.Diagnostics;
using System.IO;

using AppCommon;
using CommonUtil;
using DiskArc;
using DiskArc.Arc;
using static DiskArc.Defs;

using cp2_avalonia.Services;

namespace cp2_avalonia.Actions {
    /// <summary>
    /// Manages file entry attribute update.  This will usually be simple enough that executing
    /// it on a background thread inside a WorkProgress dialog is unnecessary.
    /// </summary>
    internal class EditAttributesProgress(
        object archiveOrFileSystem,
        DiskArcNode leafNode,
        IFileEntry fileEntry,
        IFileEntry adfEntry,
        FileAttribs newAttribs,
        AppHook appHook)
    {
        public bool DoCompress { get; init; }
        public bool EnableMacOSZip { get; init; }

        /// <summary>
        /// Message describing why <see cref="DoUpdate"/> returned false, or null if it
        /// succeeded or had nothing to report.  The caller shows it to the user.
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Performs the attribute update.  This runs on the UI thread.  On failure the
        /// reason is left in <see cref="ErrorMessage"/>; nothing is shown here.
        /// </summary>
        /// <param name="updateMacZip">True if we want to update a MacZip header entry.</param>
        /// <returns>True on success.</returns>
        public bool DoUpdate(bool updateMacZip) {
            ErrorMessage = null;
            // For a file system the entry is changed in place, one attribute at a time.  Keep
            // a copy of the original attributes so a failure doesn't leave the entry
            // half-modified in memory.
            FileAttribs? origAttribs = (archiveOrFileSystem is IArchive) ?
                null : new FileAttribs(fileEntry);
            try {
                if (archiveOrFileSystem is IArchive arc) {
                    if (updateMacZip) {
                        try {
                            Debug.Assert(EnableMacOSZip);
                            if (!HandleMacZip(arc, fileEntry, adfEntry, newAttribs, appHook)) {
                                ErrorMessage = "Unable to generate a MacZip header name for '" +
                                    newAttribs.FullPathName + "'.";
                                return false;
                            }
                            leafNode.SaveUpdates(DoCompress);
                        } finally {
                            arc.CancelTransaction();
                        }
                    } else {
                        try {
                            arc.StartTransaction();
                            newAttribs.CopyAttrsTo(fileEntry, false);
                            leafNode.SaveUpdates(DoCompress);
                        } finally {
                            arc.CancelTransaction();    // no effect if transaction isn't open
                        }
                    }
                } else {
                    newAttribs.CopyAttrsTo(fileEntry, false);
                    fileEntry.SaveChanges();
                    leafNode.SaveUpdates(DoCompress);
                }
            } catch (Exception ex) {
                AppLog.E("Edit attributes: update failed", ex);
                ErrorMessage = ex.Message;
                if (origAttribs != null) {
                    RestoreAttributes(origAttribs);
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Puts the original attributes back into a file system entry after a failed update,
        /// so the file list doesn't show values that were never written.
        /// </summary>
        private void RestoreAttributes(FileAttribs origAttribs) {
            try {
                origAttribs.CopyAttrsTo(fileEntry, false);
                fileEntry.SaveChanges();
            } catch (Exception ex) {
                AppLog.E("Edit attributes: unable to restore original attributes", ex);
            }
        }

        /// <summary>
        /// Makes changes to a MacZip header entry.  This opens a transaction in
        /// <paramref name="arc"/>, and leaves it open for the caller to commit.
        /// </summary>
        private static bool HandleMacZip(IArchive arc, IFileEntry mainEntry, IFileEntry adfEntry,
                FileAttribs newAttribs, AppHook appHook) {
            MemoryStream tmpMem;

            // Rewrite the AppleSingle file.
            using (Stream adfStream = ArcTemp.ExtractToTemp(arc, adfEntry, FilePart.DataFork)) {
                using (IArchive adfArchive = AppleSingle.OpenArchive(adfStream, appHook)) {
                    IFileEntry adfArchiveEntry = adfArchive.GetFirstEntry();
                    adfArchive.StartTransaction();
                    newAttribs.CopyAttrsTo(adfArchiveEntry, true);
                    adfArchive.CommitTransaction(tmpMem = new MemoryStream());
                }
            }

            // Replace AppleDouble header entry.
            arc.StartTransaction();
            arc.DeletePart(adfEntry, FilePart.DataFork);
            arc.AddPart(adfEntry, FilePart.DataFork, new SimplePartSource(tmpMem),
                CompressionFormat.Default);

            // Rename both files.
            var hdrName = Zip.GenerateMacZipName(newAttribs.FullPathName);
            if (hdrName == string.Empty) {
                Debug.Assert(false, "can't make name out of '" + newAttribs.FullPathName + "'");
                return false;
            }
            mainEntry.FileName = newAttribs.FullPathName;
            adfEntry.FileName = hdrName;
            return true;
        }
    }
}
