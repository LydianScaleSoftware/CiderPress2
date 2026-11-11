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
using System.Threading;
using System.Threading.Tasks;

namespace cp2_avalonia.Common;

/// <summary>
/// A file extraction running on a thread-pool thread, which can be cancelled and waited for.
/// </summary>
/// <remarks>
/// <para>Used by the Linux file-list drag.  Extraction runs in the background so the drag
/// can proceed, but DiskArc is not thread-safe, so nothing else may touch the open archive
/// until it has stopped (the exception to the X1 rule).  The internal-drop handler calls
/// <see cref="CancelAndWaitAsync"/> before it moves files or loads a viewer.</para>
/// </remarks>
/// <typeparam name="T">Type of the extraction result.</typeparam>
internal sealed class CancellableExtraction<T> where T : class
{
    // Not disposed: it has no timer or wait handle, and the internal-drop handler may still
    // call CancelAndWaitAsync after the drag has finished.
    private readonly CancellationTokenSource mCts = new();
    private Task<T?>? mTask;

    /// <summary>
    /// The running extraction, or null if <see cref="Start"/> has not been called.
    /// </summary>
    public Task<T?>? Task => mTask;

    /// <summary>
    /// Starts <paramref name="work"/> on a thread-pool thread.  The work polls the function
    /// it is given and returns null when that returns true.
    /// </summary>
    public Task<T?> Start(Func<Func<bool>, T?> work)
    {
        if (mTask != null) throw new InvalidOperationException("already started");
        CancellationToken token = mCts.Token;
        mTask = System.Threading.Tasks.Task.Run(() => work(() => token.IsCancellationRequested));
        return mTask;
    }

    /// <summary>
    /// Asks the extraction to stop, and completes when it has.  After this returns, no
    /// extraction code is running.  A failure in the extraction is not rethrown (it has
    /// been reported to whoever awaits <see cref="Task"/>).
    /// </summary>
    public async System.Threading.Tasks.Task CancelAndWaitAsync()
    {
        mCts.Cancel();
        if (mTask == null) return;
        try
        {
            await mTask.ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Not this caller's problem.
        }
    }
}
