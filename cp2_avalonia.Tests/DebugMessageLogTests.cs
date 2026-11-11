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
using CommonUtil;

using cp2_avalonia.Models;
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Tests;

public class DebugMessageLogTests
{
    [Fact]
    public void Clear_AfterWrap_ReturnsNewEntriesInOrder()
    {
        var log = new DebugMessageLog();
        // 100,001 lines (plus the "Log started" line) wraps the 100,000-line ring buffer.
        for (int i = 0; i < 100001; i++) {
            log.LogI("line " + i);
        }
        log.Clear();
        log.LogI("a");
        log.LogI("b");
        log.LogI("c");

        List<DebugMessageLog.LogEntry> entries = log.GetLogs();
        Assert.Equal(new[] { "a", "b", "c" }, entries.Select(e => e.Message));
        Assert.True(entries[0].Index < entries[1].Index && entries[1].Index < entries[2].Index);
    }

    [Fact]
    public void LogViewer_DropsDuplicatesAndKeepsOrder()
    {
        var log = new DebugMessageLog();
        log.LogI("one");
        log.LogI("two");
        var snapshot = log.GetLogs();
        var vm = new LogViewerViewModel(log, new FakeSettingsService());
        Assert.Equal(snapshot.Count, vm.LogEntries.Count);

        // An event for an entry already in the snapshot must not be added twice.
        vm.HandleLogEvent(log, new DebugMessageLog.LogEventArgs(snapshot[^1]));
        Assert.Equal(snapshot.Count, vm.LogEntries.Count);

        int lastIndex = snapshot[^1].Index;
        var next = new DebugMessageLog.LogEntry(lastIndex + 1, DateTime.Now,
            MessageLog.Priority.Info, "three");
        vm.HandleLogEvent(log, new DebugMessageLog.LogEventArgs(next));
        Assert.Equal(snapshot.Count + 1, vm.LogEntries.Count);
        Assert.Equal("three", vm.LogEntries[^1].Message);
        Assert.Equal(lastIndex + 1, vm.LogEntries[^1].Index);
    }

    [Fact]
    public void LogViewer_IgnoresEventsAfterClose()
    {
        var log = new DebugMessageLog();
        var vm = new LogViewerViewModel(log, new FakeSettingsService());
        int count = vm.LogEntries.Count;
        vm.OnWindowClosed();

        var late = new DebugMessageLog.LogEntry(1000, DateTime.Now,
            MessageLog.Priority.Info, "late");
        vm.HandleLogEvent(log, new DebugMessageLog.LogEventArgs(late));
        Assert.Equal(count, vm.LogEntries.Count);
    }

    [Fact]
    public void LogViewer_Clear_EmptiesTheLogAndTheView_AndIgnoresQueuedOldEntries()
    {
        var log = new DebugMessageLog();
        log.LogI("one");
        log.LogI("two");
        var before = log.GetLogs();
        var vm = new LogViewerViewModel(log, new FakeSettingsService());
        Assert.Equal(before.Count, vm.LogEntries.Count);

        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.LogEntries);
        Assert.Empty(log.GetLogs());
        Assert.Equal(before[^1].Index, log.ClearedThroughIndex);

        // An event for an entry logged before the clear, still queued, is ignored.
        vm.HandleLogEvent(log, new DebugMessageLog.LogEventArgs(before[^1]));
        Assert.Empty(vm.LogEntries);

        // New entries after the clear are shown.
        log.LogI("three");
        var after = log.GetLogs();
        Assert.Single(after);
        vm.HandleLogEvent(log, new DebugMessageLog.LogEventArgs(after[0]));
        Assert.Single(vm.LogEntries);
        Assert.Equal("three", vm.LogEntries[0].Message);
    }

    [Fact]
    public void DropTarget_Clear_EmptiesTheText()
    {
        var vm = new DropTargetViewModel { TextArea = "dropped stuff" };
        vm.ClearCommand.Execute(null);
        Assert.Equal(string.Empty, vm.TextArea);
    }
}
