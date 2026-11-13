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
using Avalonia.Headless.XUnit;

using AppCommon;
using CommonUtil;
using DiskArc;
using DiskArc.Arc;
using DiskArc.Disk;
using DiskArc.FS;
using static DiskArc.Defs;

using cp2_avalonia.Models;
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Tests;

/// <summary>
/// View Files with a lone directory selected: which files the viewer gets
/// (<see cref="MainViewModel.CollectDirectoryFiles"/>).
/// </summary>
public class DirectoryViewerSelectionTests
{
    private static Formatter Fmt() => new Formatter(new Formatter.FormatConfig());

    // ProDOS volume:  TOP, DIR/{A, SUB/{B}, Z}, OTHER/{C}
    private static (IDiskImage disk, IFileSystem fs, IFileEntry dir) MakeVolume()
    {
        AppHook hook = new AppHook(new SimpleMessageLog());
        IDiskImage disk = UnadornedSector.CreateBlockImage(new MemoryStream(), 280, hook);
        IFileSystem fs = new ProDOS(disk.ChunkAccess!, hook);
        fs.Format("TEST", 0, false);
        fs.PrepareFileAccess(true);
        IFileEntry root = fs.GetVolDirEntry();
        fs.CreateFile(root, "TOP", IFileSystem.CreateMode.File);
        IFileEntry dir = fs.CreateFile(root, "DIR", IFileSystem.CreateMode.Directory);
        fs.CreateFile(dir, "A", IFileSystem.CreateMode.File);
        IFileEntry sub = fs.CreateFile(dir, "SUB", IFileSystem.CreateMode.Directory);
        fs.CreateFile(sub, "B", IFileSystem.CreateMode.File);
        fs.CreateFile(dir, "Z", IFileSystem.CreateMode.File);
        IFileEntry other = fs.CreateFile(root, "OTHER", IFileSystem.CreateMode.Directory);
        fs.CreateFile(other, "C", IFileSystem.CreateMode.File);
        return (disk, fs, dir);
    }

    // The full list: every entry below the root, depth first (as the file list shows it).
    private static List<FileListItem> FullList(IFileEntry root)
    {
        var items = new List<FileListItem>();
        void Walk(IFileEntry d)
        {
            foreach (IFileEntry child in d) {
                items.Add(new FileListItem(child, Fmt()));
                if (child.IsDirectory) {
                    Walk(child);
                }
            }
        }
        Walk(root);
        return items;
    }

    private static string[] Names(List<IFileEntry> entries) =>
        entries.Select(e => e.FullPathName).ToArray();

    [AvaloniaFact]
    public void SingleDirList_GivesOnlyTheDirectorysOwnFiles()
    {
        var (disk, fs, dir) = MakeVolume();
        using (disk) using (fs) {
            List<IFileEntry> files = MainViewModel.CollectDirectoryFiles(dir, isArchive: false,
                recurse: false, FullList(fs.GetVolDirEntry()));
            Assert.Equal(new[] { "DIR/A", "DIR/Z" }, Names(files));
        }
    }

    [AvaloniaFact]
    public void FullList_GivesEveryFileBelowTheDirectory()
    {
        var (disk, fs, dir) = MakeVolume();
        using (disk) using (fs) {
            List<IFileEntry> files = MainViewModel.CollectDirectoryFiles(dir, isArchive: false,
                recurse: true, FullList(fs.GetVolDirEntry()));
            Assert.Equal(new[] { "DIR/A", "DIR/SUB/B", "DIR/Z" }, Names(files));
        }
    }

    [AvaloniaFact]
    public void FullList_KeepsTheListOrder()
    {
        var (disk, fs, dir) = MakeVolume();
        using (disk) using (fs) {
            List<FileListItem> list = FullList(fs.GetVolDirEntry());
            list.Reverse();         // stands in for a sorted list
            List<IFileEntry> files = MainViewModel.CollectDirectoryFiles(dir, isArchive: false,
                recurse: true, list);
            Assert.Equal(new[] { "DIR/Z", "DIR/SUB/B", "DIR/A" }, Names(files));
        }
    }

    [AvaloniaFact]
    public void DirectoryWithOnlySubdirectories_GivesNothingInTheSingleDirList()
    {
        var (disk, fs, dir) = MakeVolume();
        using (disk) using (fs) {
            IFileEntry sub = fs.FindFileEntry(dir, "SUB");
            IFileEntry empty = fs.CreateFile(sub, "EMPTY", IFileSystem.CreateMode.Directory);
            Assert.Empty(MainViewModel.CollectDirectoryFiles(empty, isArchive: false,
                recurse: false, FullList(fs.GetVolDirEntry())));
        }
    }

    // ZIP archive:  TOP, DIR/, DIR/A, DIR/SUB/, DIR/SUB/B, DIRX/C
    private static (Zip arc, List<FileListItem> items) MakeZip()
    {
        AppHook hook = new AppHook(new SimpleMessageLog());
        Zip arc = Zip.CreateArchive(hook);
        arc.StartTransaction();
        foreach (string name in new[] { "TOP", "DIR/", "DIR/A", "DIR/SUB/", "DIR/SUB/B",
                "DIRX/C" }) {
            IFileEntry entry = arc.CreateRecord();
            entry.FileName = name;
            var data = new MemoryStream();
            if (!name.EndsWith('/')) {
                data.SetLength(4);
            }
            arc.AddPart(entry, FilePart.DataFork, new SimplePartSource(data),
                CompressionFormat.Uncompressed);
        }
        arc.CommitTransaction(new MemoryStream());
        var items = new List<FileListItem>();
        foreach (IFileEntry entry in arc) {
            items.Add(new FileListItem(entry, Fmt()));
        }
        return (arc, items);
    }

    [AvaloniaFact]
    public void Archive_MatchesByPath_AndRecursesEvenWhenAskedNotTo()
    {
        var (arc, items) = MakeZip();
        using (arc) {
            IFileEntry dir = arc.FindFileEntry("DIR/");
            Assert.True(dir.IsDirectory);
            // DIRX/C shares the "DIR" prefix but is not inside DIR/.
            Assert.Equal(new[] { "DIR/A", "DIR/SUB/B" },
                Names(MainViewModel.CollectDirectoryFiles(dir, isArchive: true,
                    recurse: false, items)));
            Assert.Equal(new[] { "DIR/A", "DIR/SUB/B" },
                Names(MainViewModel.CollectDirectoryFiles(dir, isArchive: true,
                    recurse: true, items)));
        }
    }
}
