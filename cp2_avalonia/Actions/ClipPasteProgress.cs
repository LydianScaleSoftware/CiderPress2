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
using CommonUtil;
using cp2_avalonia.Models;
using cp2_avalonia.ViewModels;
using DiskArc;
using FileConv;

namespace cp2_avalonia.Actions {
    /// <summary>
    /// This runs a ClipPasteWorker inside a WorkProgress dialog.
    /// </summary>
    internal class ClipPasteProgress(
        object archiveOrFileSystem,
        DiskArcNode leafNode,
        IFileEntry targetDir,
        ClipInfo clipInfo,
        ClipPasteWorker.ClipStreamGenerator streamGen,
        AppHook appHook)
        : WorkProgressViewModel.IWorker
    {
        public bool DoCompress { get; init; }
        public bool EnableMacOSZip { get; init; }
        public bool ConvertDOSText { get; init; }
        public bool StripPaths { get; init; }
        public bool RawMode { get; init; }

        /// <summary>
        /// True if any file was skipped because the user chose not to overwrite an existing
        /// one.  A Cut+Paste must then keep the original files.
        /// </summary>
        public bool AnySkipped { get; private set; }


        /// <summary>
        /// Performs the operation.
        /// </summary>
        /// <remarks>
        /// THIS RUNS ON THE WORKER THREAD.  Do not try to access GUI objects.
        /// </remarks>
        /// <param name="bkWorker">Background worker object.</param>
        /// <returns>Operation results.</returns>
        public object DoWork(BackgroundWorker bkWorker) {
            ProgressUtil.PersistentChoices choices = new ();
            try {
                return DoWorkInternal(bkWorker, choices);
            } finally {
                AnySkipped = choices.SkipCount > 0;
            }
        }

        private object DoWorkInternal(BackgroundWorker bkWorker,
                ProgressUtil.PersistentChoices choices) {

            CallbackFacts.Results CbFunc(CallbackFacts what)
            {
                return ProgressUtil.HandleCallback(what, "paste", choices, leafNode, bkWorker);
            }

            if (clipInfo.ClipEntries == null) {
                throw new InvalidOperationException("The clipboard data has no files to paste.");
            }
            bool isSameProcess = (Environment.ProcessId == clipInfo.ProcessId);
            ClipPasteWorker pasteWorker = new ClipPasteWorker(clipInfo.ClipEntries, streamGen,
                CbFunc, doCompress: DoCompress, macZip: EnableMacOSZip,
                convDosText: ConvertDOSText, stripPaths: StripPaths,
                rawMode: RawMode, isSameProcess: isSameProcess, appHook);

            return ProgressUtil.RunAddOperation(archiveOrFileSystem, leafNode, DoCompress,
                bkWorker,
                (IArchive arc, out bool isCancelled) =>
                    pasteWorker.AddFilesToArchive(arc, out isCancelled),
                (IFileSystem fs, out bool isCancelled) =>
                    pasteWorker.AddFilesToDisk(fs, targetDir, out isCancelled));
        }

        public bool RunWorkerCompleted(object? results) {
            bool success = (results is true);
            Debug.WriteLine("Operation completed, success=" + success);
            return success;
        }
    }
}
