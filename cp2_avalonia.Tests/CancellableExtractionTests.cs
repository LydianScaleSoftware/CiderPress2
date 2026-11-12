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
using cp2_avalonia.Common;

namespace cp2_avalonia.Tests;

/// <summary>
/// C1: the extraction behind a Linux file-list drag must have stopped before the internal
/// drop touches the archive.
/// </summary>
public class CancellableExtractionTests
{
    [Fact]
    public async Task CancelAndWait_ReturnsOnlyAfterWorkerHasStopped()
    {
        var ex = new CancellableExtraction<string>();
        bool started = false, stopped = false;
        _ = ex.Start(cancelled =>
        {
            started = true;
            while (!cancelled()) Thread.Sleep(5);
            Thread.Sleep(100);              // finishing the current entry takes a while
            stopped = true;
            return null;
        });
        while (!started) await Task.Delay(5, TestContext.Current.CancellationToken);

        await ex.CancelAndWaitAsync();

        Assert.True(stopped);
        Assert.Null(await ex.Task!);
    }

    [Fact]
    public async Task CancelAndWait_AfterCompletionKeepsResult()
    {
        var ex = new CancellableExtraction<string>();
        _ = ex.Start(_ => "done");
        string? result = await ex.Task!;

        await ex.CancelAndWaitAsync();      // already finished: must not disturb the result

        Assert.Equal("done", result);
        Assert.Equal("done", await ex.Task!);
    }

    [Fact]
    public async Task CancelAndWait_NotStarted()
    {
        var ex = new CancellableExtraction<string>();
        await ex.CancelAndWaitAsync();
        Assert.Null(ex.Task);
    }

    [Fact]
    public async Task CancelAndWait_WorkerFailureIsNotRethrown()
    {
        var ex = new CancellableExtraction<string>();
        _ = ex.Start(_ => throw new InvalidOperationException("disk full"));

        await ex.CancelAndWaitAsync();

        // The drag code awaits Task itself and sees the failure there.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ex.Task!);
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        var ex = new CancellableExtraction<string>();
        _ = ex.Start(_ => "a");
        Assert.Throws<InvalidOperationException>(() => { _ = ex.Start(_ => "b"); });
    }
}
