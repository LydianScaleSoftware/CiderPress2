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
using cp2_avalonia.ViewModels;
using DiskArc;
using FileConv;

namespace cp2_avalonia.Actions {
    /// <summary>
    /// This runs an AddFileWorker inside a WorkProgress dialog.
    /// </summary>
    internal class AddProgress(
        object archiveOrFileSystem,
        DiskArcNode leafNode,
        AddFileSet addSet,
        IFileEntry targetDir,
        AppHook appHook)
        : WorkProgressViewModel.IWorker
    {
        public bool DoCompress { get; init; }
        public bool EnableMacOSZip { get; init; }
        public bool StripPaths { get; init; }
        public bool RawMode { get; init; }


        /// <summary>
        /// Performs the operation.
        /// </summary>
        /// <remarks>
        /// THIS RUNS ON THE WORKER THREAD.  Do not try to access GUI objects.
        /// </remarks>
        public object DoWork(BackgroundWorker bkWorker) {
            ProgressUtil.PersistentChoices choices = new();

            CallbackFacts.Results CbFunc(CallbackFacts what)
            {
                return ProgressUtil.HandleCallback(what, "add", choices, leafNode, bkWorker);
            }

            AddFileWorker addWorker = new AddFileWorker(addSet, CbFunc,
                doCompress: DoCompress, macZip: EnableMacOSZip, stripPaths: StripPaths,
                rawMode: RawMode, appHook);

            return ProgressUtil.RunAddOperation(archiveOrFileSystem, leafNode, DoCompress,
                bkWorker,
                (IArchive arc, out bool isCancelled) =>
                    addWorker.AddFilesToArchive(arc, out isCancelled),
                (IFileSystem fs, out bool isCancelled) =>
                    addWorker.AddFilesToDisk(fs, targetDir, out isCancelled));
        }

        public bool RunWorkerCompleted(object? results) {
            bool success = (results is true);
            Debug.WriteLine("AddProgress completed, success=" + success);
            return success;
        }
    }
}
