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
using Avalonia.Input;
using DiskArc;

using cp2_avalonia.Common;

namespace cp2_avalonia.Tests;

/// <summary>
/// C10: how a window gets the entries of a file-list drag.
/// </summary>
public class FileListDragTests : IDisposable
{
    private static readonly object Source = new();

    public FileListDragTests()
    {
        FileListDrag.Current = null;
    }

    public void Dispose()
    {
        FileListDrag.Current = null;
    }

    private static DataTransfer MakeData(string workPath = "/disks/a.po")
    {
        var item = new DataTransferItem();
        item.Set(FileListDrag.ENTRIES_FORMAT, new List<IFileEntry> { IFileEntry.NO_ENTRY });
        item.Set(FileListDrag.SOURCE_FORMAT, Source);
        item.Set(FileListDrag.WORKPATH_FORMAT, workPath);
        var data = new DataTransfer();
        data.Add(item);
        return data;
    }

    [Fact]
    public void FormatNamesMatchWhatWasShipped()
    {
        // Other builds and the viewer must agree on these strings.
        Assert.Equal("cp2_avalonia/FileListDrag", FileListDrag.ENTRIES_FORMAT.Identifier);
        Assert.Equal("cp2_avalonia/FileListDragSource", FileListDrag.SOURCE_FORMAT.Identifier);
        Assert.Equal("cp2_avalonia/FileListDragWorkPath", FileListDrag.WORKPATH_FORMAT.Identifier);
    }

    [Fact]
    public void FromDataObject()
    {
        Assert.True(FileListDrag.TryGet(MakeData(), out FileListDrag.State state));
        Assert.Single(state.Entries);
        Assert.Same(Source, state.Source);
        Assert.Equal("/disks/a.po", state.WorkPath);
    }

    [Fact]
    public void NothingAvailable()
    {
        Assert.False(FileListDrag.TryGet(new DataTransfer(), out _));
    }

    [Fact]
    public void FallsBackToCurrentWhenDataObjectHasNone()
    {
        // A macOS native drag session: nothing of ours in the data object.
        var cur = new FileListDrag.State(new List<IFileEntry> { IFileEntry.NO_ENTRY },
            Source, "/disks/b.po");
        FileListDrag.Current = cur;

        Assert.True(FileListDrag.TryGet(new DataTransfer(), out FileListDrag.State state));
        Assert.Same(cur, state);
    }

    [Fact]
    public void DataObjectWinsOverCurrent()
    {
        FileListDrag.Current = new FileListDrag.State(
            new List<IFileEntry> { IFileEntry.NO_ENTRY }, new object(), "/disks/other.po");

        Assert.True(FileListDrag.TryGet(MakeData(), out FileListDrag.State state));
        Assert.Equal("/disks/a.po", state.WorkPath);
    }

    [Fact]
    public void EmptyCurrentIsIgnored()
    {
        FileListDrag.Current = new FileListDrag.State(new List<IFileEntry>(), Source, "x");
        Assert.False(FileListDrag.TryGet(new DataTransfer(), out _));
    }
}
