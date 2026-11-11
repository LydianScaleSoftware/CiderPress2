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

using AppCommon;
using DiskArc;
using FileConv;
using cp2_avalonia.Services;
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Actions {
    public static class ProgressUtil {
        public const string FINISHING_MSG = "Saving changes...";

        internal enum Choice {
            Unknown = 0, Unset, All, None
        }
        public class PersistentChoices {
            internal Choice Overwrite { get; set; } = Choice.Unset;

            /// <summary>Number of files the user (or a standing choice) chose to skip.</summary>
            public int SkipCount { get; internal set; }
        }

        /// <summary>
        /// Handles a callback from the library code.
        /// </summary>
        /// <param name="what">What happened.</param>
        /// <param name="actionStr">Verb that describes the action being performed.</param>
        /// <param name="choices">Persistent choice values.</param>
        /// <param name="leafNode">Leaf node of tree, if we're making updates.</param>
        /// <param name="bkWorker">Background worker that receives progress updates.</param>
        /// <returns>Result code from user query, if any.</returns>
        public static CallbackFacts.Results HandleCallback(CallbackFacts what, string actionStr,
                PersistentChoices choices, DiskArcNode? leafNode, BackgroundWorker bkWorker) {
            CallbackFacts.Results result = CallbackFacts.Results.Continue;
            switch (what.Reason) {
                case CallbackFacts.Reasons.QueryCancel:
                    if (bkWorker.CancellationPending) {
                        result = CallbackFacts.Results.Cancel;
                    }
                    break;
                case CallbackFacts.Reasons.Progress:
                    if (bkWorker.CancellationPending) {
                        result = CallbackFacts.Results.Cancel;
                        break;
                    }
                    ReportFileProgress(bkWorker, what.ProgressPercent, what.OrigPathName,
                        what.Part);
                    break;
                case CallbackFacts.Reasons.FileNameExists:
                    if (choices.Overwrite == Choice.All) {
                        Debug.WriteLine("Overwrite '" + what.OrigPathName + "' -> overwrite all");
                        return CallbackFacts.Results.Overwrite;
                    } else if (choices.Overwrite == Choice.None) {
                        Debug.WriteLine("Overwrite '" + what.OrigPathName + "' -> overwrite none");
                        choices.SkipCount++;
                        return CallbackFacts.Results.Skip;
                    } else {
                        Debug.Assert(choices.Overwrite == Choice.Unset);
                    }
                    // We're about to wait for the user: flush progress first.  Not needed
                    // when a standing "all"/"none" choice means no dialog will appear.
                    leafNode?.FlushStreams();
                    WorkProgressViewModel.OverwriteQuery query = new WorkProgressViewModel.OverwriteQuery(what);
                    bkWorker.ReportProgress(0, query);
                    result = query.WaitForResult(out bool useForAll);
                    Debug.WriteLine("Overwrite '" + what.OrigPathName + "' -> " + result +
                        ", all=" + useForAll);
                    if (result == CallbackFacts.Results.Skip) {
                        choices.SkipCount++;
                    }
                    if (useForAll) {
                        if (result == CallbackFacts.Results.Overwrite) {
                            choices.Overwrite = Choice.All;
                        } else if (result == CallbackFacts.Results.Skip) {
                            choices.Overwrite = Choice.None;
                        }
                    }
                    break;
                case CallbackFacts.Reasons.ResourceForkIgnored:
                    // TODO: do something with this?
                    break;
                case CallbackFacts.Reasons.PathTooLong:
                case CallbackFacts.Reasons.AttrFailure:
                case CallbackFacts.Reasons.OverwriteFailure:
                case CallbackFacts.Reasons.Failure:
                    AppLog.E(actionStr + ": " + what.Reason + ": " + what.FailMessage);
                    WorkProgressViewModel.MessageBoxQuery failMsg =
                        new WorkProgressViewModel.MessageBoxQuery(what.FailMessage,
                            "Failed", MBButton.OK, MBIcon.Error);
                    bkWorker.ReportProgress(0, failMsg);
                    failMsg.WaitForResult();
                    break;
                case CallbackFacts.Reasons.ConversionFailure:
                    AppLog.E(actionStr + ": conversion failed: " + what.FailMessage);
                    WorkProgressViewModel.MessageBoxQuery cfailMsg =
                        new WorkProgressViewModel.MessageBoxQuery("Conversion failed: " + what.FailMessage,
                            "Failed", MBButton.OK, MBIcon.Error);
                    bkWorker.ReportProgress(0, cfailMsg);
                    cfailMsg.WaitForResult();
                    break;
            }
            return result;
        }

        /// <summary>
        /// Reports the file being processed, and the overall percentage, to the dialog.
        /// </summary>
        public static void ReportFileProgress(BackgroundWorker bkWorker, int percent,
                string pathName, DiskArc.Defs.FilePart part) {
            if (part == DiskArc.Defs.FilePart.RsrcFork) {
                bkWorker.ReportProgress(percent, pathName + " (rsrc)");
            } else {
                bkWorker.ReportProgress(percent, pathName);
            }
        }

        /// <summary>
        /// Adds files to an archive.  Returns true if the operation completed.
        /// </summary>
        internal delegate void AddToArchiveFunc(IArchive arc, out bool isCancelled);

        /// <summary>
        /// Adds files to a disk.  Returns true if the operation completed.
        /// </summary>
        internal delegate void AddToDiskFunc(IFileSystem fs, out bool isCancelled);

        /// <summary>
        /// Runs an "add files" style operation (adding from the file system, or pasting from
        /// the clipboard) against an archive or a disk image, saves the result, and reports
        /// failures.  The caller supplies the two worker calls.
        /// </summary>
        /// <remarks>
        /// THIS RUNS ON THE WORKER THREAD.
        /// </remarks>
        /// <param name="archiveOrFileSystem">Destination.</param>
        /// <param name="leafNode">Leaf node, for saving updates.</param>
        /// <param name="doCompress">True to compress when saving.</param>
        /// <param name="bkWorker">Background worker.</param>
        /// <param name="addToArchive">Worker call for an archive destination.</param>
        /// <param name="addToDisk">Worker call for a disk destination.</param>
        /// <returns>True if the operation completed successfully.</returns>
        internal static bool RunAddOperation(object archiveOrFileSystem, DiskArcNode leafNode,
                bool doCompress, BackgroundWorker bkWorker, AddToArchiveFunc addToArchive,
                AddToDiskFunc addToDisk) {
            switch (archiveOrFileSystem) {
                case IArchive arc:
                    try {
                        arc.StartTransaction();
                        addToArchive(arc, out bool isCancelled);
                        if (isCancelled) {
                            return false;
                        }
                        bkWorker.ReportProgress(100, FINISHING_MSG);
                        leafNode.SaveUpdates(doCompress);
                    } catch (CancelException) {
                        Debug.Assert(bkWorker.CancellationPending);
                        return false;
                    } catch (ConversionException ex) {
                        ShowMessage("Import error: " + ex.Message, true, bkWorker);
                        return false;
                    } catch (Exception ex) {
                        ShowMessage("Error: " + ex.Message, true, bkWorker);
                        return false;
                    } finally {
                        arc.CancelTransaction();    // no effect if transaction isn't open
                    }
                    return true;
                case IFileSystem fs:
                    bool success = true;
                    string? failMsg = null;
                    try {
                        addToDisk(fs, out bool isCancelled);
                        if (isCancelled) {
                            success = false;
                        }
                    } catch (ConversionException ex) {
                        failMsg = "Import error: " + ex.Message;
                        success = false;
                    } catch (Exception ex) {
                        failMsg = "Error: " + ex.Message;
                        success = false;
                    }
                    // Finish writing changes, regardless if the operation succeeded.
                    try {
                        if (failMsg == null) {
                            bkWorker.ReportProgress(100, FINISHING_MSG);
                        }
                        leafNode.SaveUpdates(doCompress);
                    } catch (Exception ex) {
                        ShowMessage("Error: update failed: " + ex.Message, true, bkWorker);
                        return false;
                    }
                    if (failMsg != null) {
                        // Defer failure reporting until after SaveUpdates() to avoid leaving
                        // the disk image in an inconsistent state while the dialog is
                        // displayed.
                        ShowMessage(failMsg, true, bkWorker);
                    }
                    return success;
                default:
                    Debug.Assert(false);
                    return true;
            }
        }

        /// <summary>
        /// Displays a message box with an informative or error message.
        /// </summary>
        public static void ShowMessage(string msg, bool isError, BackgroundWorker bkWorker) {
            if (isError) { AppLog.E(msg); } else { AppLog.I(msg); }
            WorkProgressViewModel.MessageBoxQuery failMsg =
                new WorkProgressViewModel.MessageBoxQuery(msg, isError ? "Failed" : "Info",
                    MBButton.OK,
                    isError ? MBIcon.Error : MBIcon.Information);
            bkWorker.ReportProgress(0, failMsg);
            failMsg.WaitForResult();
        }
    }
}
