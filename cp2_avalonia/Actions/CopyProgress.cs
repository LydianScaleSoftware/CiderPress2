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
using System.ComponentModel;
using System.IO;

using AppCommon;

using cp2_avalonia.Services;
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Actions {
    /// <summary>
    /// Writes the temp files for a Copy or Cut inside a WorkProgress dialog.  Extracting runs
    /// on the worker thread, which is allowed because the progress dialog is modal and
    /// blocks everything else that could touch the open archive.
    /// </summary>
    /// <remarks>
    /// If the user cancels, or extraction fails, the partly written temp directory is deleted
    /// and the clipboard is left as it was: nothing is handed to the clipboard service
    /// until this has completed.
    /// </remarks>
    internal class CopyProgress(
        string tempDir,
        List<ClipFileEntry> xferEntries,
        List<ClipFileEntry> foreignEntries)
        : WorkProgressViewModel.IWorker
    {
        /// <summary>What was written, if the operation completed.</summary>
        public ClipTransferFiles.Result? Result { get; private set; }

        /// <summary>
        /// Does the extraction.
        /// </summary>
        /// <remarks>
        /// THIS RUNS ON THE WORKER THREAD.  Do not try to access GUI objects.
        /// </remarks>
        public object DoWork(BackgroundWorker bkWorker) {
            try {
                ClipTransferFiles.Result? result = ClipTransferFiles.Extract(tempDir,
                    xferEntries, foreignEntries, () => bkWorker.CancellationPending,
                    (percent, name) => bkWorker.ReportProgress(percent, name));
                if (result == null) {
                    DeleteTempDir();
                    return false;
                }
                return result;
            } catch (Exception) {
                // The dialog reports the error; just don't leave the files behind.
                DeleteTempDir();
                throw;
            }
        }

        public bool RunWorkerCompleted(object? results) {
            Result = results as ClipTransferFiles.Result;
            return Result != null;
        }

        private void DeleteTempDir() {
            try {
                if (Directory.Exists(tempDir)) {
                    Directory.Delete(tempDir, true);
                }
            } catch (Exception ex) {
                AppLog.W("Copy: unable to delete partial temp directory", ex);
            }
        }
    }
}
