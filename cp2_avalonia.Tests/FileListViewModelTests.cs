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
using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Avalonia.Headless.XUnit;

using CommonUtil;
using DiskArc;
using DiskArc.Disk;
using DiskArc.FS;

using cp2_avalonia.Common;
using cp2_avalonia.Models;
using cp2_avalonia.ViewModels;
using cp2_avalonia.Views;

namespace cp2_avalonia.Tests;

/// <summary>
/// Records the view calls the file list view model makes.
/// </summary>
internal class FakeViewActions : IViewActions
{
    public List<string> Calls { get; } = new();
    /// <summary>The Items order at the moment SelectFileListItemByEntry was called.</summary>
    public List<string[]> ItemsAtSelect { get; } = new();
    public List<IFileEntry> SelectedEntries { get; } = new();
    public List<IList<FileListItem>> SetSelections { get; } = new();
    public Func<string[]>? SnapshotItems { get; set; }

    public void ScrollFileListTo(object item) { }
    public void ScrollFileListToTop() { }
    public void ScrollDirectoryTreeToTop() { }
    public void FocusFileList() => Calls.Add("FocusFileList");
    public void SetFileListSelectionFocus(int index) { }
    public void SelectAllFileListItems() { }
    public void SetFileListSelection(IList<FileListItem> items) => SetSelections.Add(items);
    public void ShowToast(string message, bool success, double seconds = 3) { }
    public void ShowNotice(string message, string caption) { }
    public void SetCursorBusy(bool busy) { }
    public void SelectArchiveTreeItem(ArchiveTreeItem item) { }
    public void SelectBestArchiveTreeItem(ArchiveTreeItem root) { }
    public void SelectDirectoryTreeItem(DirectoryTreeItem? item) { }
    public bool SelectDirectoryTreeItemByEntry(ObservableCollection<DirectoryTreeItem> root,
        IFileEntry dirEntry) => false;
    public void ScrollFileListItemIntoView(FileListItem item) => Calls.Add("ScrollIntoView");
    public void SelectFileListItemByEntry(IFileEntry selEntry)
    {
        Calls.Add("SelectFileListItemByEntry");
        SelectedEntries.Add(selEntry);
        if (SnapshotItems != null) {
            ItemsAtSelect.Add(SnapshotItems());
        }
    }
    public void PreSizeNameColumn() => Calls.Add("PreSizeNameColumn");
    public void PopulateRecentFilesMenu() { }
    public void ResetFileListSort() => Calls.Add("ResetFileListSort");
    public void ScrollDirectoryTreeItemIntoView(DirectoryTreeItem item) { }
    public void FocusDirectoryTree() { }
    public void FocusArchiveTree() { }
    public void FocusDirectoryTreeDeferred() { }
    public void ShowAsciiChart() { }
}

/// <summary>
/// D2: sorting and refreshing the file list.  Uses a real ProDOS volume in memory.
/// </summary>
public class FileListViewModelTests
{
    private static readonly string[] NAMES = { "FILE10", "FILE2", "B", "A.TXT", "FILE1" };

    private static (IDiskImage disk, IFileSystem fs, AppHook hook) MakeVolume(params string[] names)
    {
        AppHook hook = new AppHook(new SimpleMessageLog());
        MemoryStream stream = new MemoryStream();
        IDiskImage disk = UnadornedSector.CreateBlockImage(stream, 280, hook);
        IFileSystem fs = new ProDOS(disk.ChunkAccess!, hook);
        fs.Format("TEST", 0, false);
        fs.PrepareFileAccess(true);
        foreach (string name in names) {
            fs.CreateFile(fs.GetVolDirEntry(), name, IFileSystem.CreateMode.File);
        }
        return (disk, fs, hook);
    }

    private static FileListViewModel MakeVm(FakeViewActions view, AppHook hook)
    {
        var vm = new FileListViewModel(view, new FakeSettingsService(), hook);
        view.SnapshotItems = () => vm.Items.Select(i => i.FileName).ToArray();
        return vm;
    }

    private static Formatter Fmt() => new Formatter(new Formatter.FormatConfig());

    private static string[] Names(FileListViewModel vm) => vm.Items.Select(i => i.FileName).ToArray();

    private static readonly string[] SORTED = { "A.TXT", "B", "FILE1", "FILE2", "FILE10" };

    private static void Populate(FileListViewModel vm, IFileSystem fs, IFileEntry sel,
        bool focus = true) =>
        vm.PopulateFileList(fs, null, sel, focus, showSingleDir: false, Fmt());

    [AvaloniaFact]
    public void Unsorted_ListFollowsTheFileSystem_AndVerifies()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var vm = MakeVm(new FakeViewActions(), hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            Assert.Equal(NAMES, Names(vm));
            Assert.True(vm.VerifyForObject(fs, null, false));
        }
    }

    [AvaloniaFact]
    public void ApplySort_ReplacesTheRowsInOneReset()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var vm = MakeVm(new FakeViewActions(), hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);

            var events = new List<NotifyCollectionChangedAction>();
            vm.Items.CollectionChanged += (_, e) => events.Add(e.Action);
            vm.ApplySort("FileName", true);

            Assert.Equal(SORTED, Names(vm));
            Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
            Assert.True(vm.IsResetSortEnabled);

            vm.ApplySort("FileName", false);
            Assert.Equal(SORTED.Reverse().ToArray(), Names(vm));
        }
    }

    [AvaloniaFact]
    public void Populate_ReplacesTheRowsInOneReset()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var vm = MakeVm(new FakeViewActions(), hook);
            var events = new List<NotifyCollectionChangedAction>();
            vm.Items.CollectionChanged += (_, e) => events.Add(e.Action);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
        }
    }

    [AvaloniaFact]
    public void SortedList_Verifies_AsASet()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var vm = MakeVm(new FakeViewActions(), hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            vm.ApplySort("FileName", true);

            // The rows are no longer in file system order, but nothing has changed.
            Assert.NotEqual(NAMES, Names(vm));
            Assert.True(vm.VerifyForObject(fs, null, false));
        }
    }

    [AvaloniaFact]
    public void Verify_DetectsAddedAndRemovedEntries_WithAndWithoutSort()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var vm = MakeVm(new FakeViewActions(), hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);

            foreach (bool sorted in new[] { false, true }) {
                if (sorted) {
                    vm.ApplySort("FileName", true);
                }
                IFileEntry added = fs.CreateFile(fs.GetVolDirEntry(), "NEWFILE" + (sorted ? "2" : "1"),
                    IFileSystem.CreateMode.File);
                Assert.False(vm.VerifyForObject(fs, null, false));

                Populate(vm, fs, IFileEntry.NO_ENTRY);
                Assert.True(vm.VerifyForObject(fs, null, false));

                fs.DeleteFile(added);
                Assert.False(vm.VerifyForObject(fs, null, false));
                Populate(vm, fs, IFileEntry.NO_ENTRY);
                Assert.True(vm.VerifyForObject(fs, null, false));
            }
        }
    }

    [AvaloniaFact]
    public void EmptyVolume_Verifies()
    {
        var (disk, fs, hook) = MakeVolume();
        using (disk) using (fs) {
            var vm = MakeVm(new FakeViewActions(), hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            Assert.Empty(vm.Items);
            Assert.True(vm.VerifyForObject(fs, null, false));
        }
    }

    [AvaloniaFact]
    public void Populate_WithSortActive_SortsBeforeRestoringTheSelection()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var view = new FakeViewActions();
            var vm = MakeVm(view, hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            vm.ApplySort("FileName", true);

            IFileEntry sel = fs.GetVolDirEntry().First(e => e.FileName == "FILE2");
            view.Calls.Clear();
            view.ItemsAtSelect.Clear();
            Populate(vm, fs, sel);

            Assert.Equal(SORTED, Names(vm));
            // The selection was restored on the final, sorted rows (a sort after it would
            // have discarded it).
            Assert.Equal(new[] { sel }, view.SelectedEntries.TakeLast(1));
            Assert.Equal(SORTED, view.ItemsAtSelect.Single());
            Assert.True(vm.IsSortActive);
        }
    }

    [AvaloniaFact]
    public void ResetSort_ClearsTheSort_AndSignalsARepopulate()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var view = new FakeViewActions();
            var vm = MakeVm(view, hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            vm.ApplySort("FileName", true);
            int resets = 0;
            vm.SortReset += () => {
                resets++;
                Populate(vm, fs, IFileEntry.NO_ENTRY);
            };

            vm.ResetSortCommand.Execute(null);

            Assert.Equal(1, resets);
            Assert.False(vm.IsSortActive);
            Assert.False(vm.IsResetSortEnabled);
            Assert.Contains("ResetFileListSort", view.Calls);
            Assert.Equal(NAMES, Names(vm));
        }
    }

    [AvaloniaFact]
    public void RestoreSortOrder_ReSortsOutOfOrderRows_AndReselects()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var view = new FakeViewActions();
            var vm = MakeVm(view, hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            vm.ApplySort("FileName", true);

            // Nothing to do while the rows are in order.
            view.SetSelections.Clear();
            vm.RestoreSortOrder();
            Assert.Empty(view.SetSelections);

            // An in-place replacement can leave a row out of place.
            (vm.Items[0], vm.Items[1]) = (vm.Items[1], vm.Items[0]);
            Assert.NotEqual(SORTED, Names(vm));
            FileListItem selected = vm.Items[3];
            vm.SetSelection(new List<FileListItem> { selected });

            vm.RestoreSortOrder();

            Assert.Equal(SORTED, Names(vm));
            Assert.Single(view.SetSelections);
            Assert.Same(selected, view.SetSelections[0].Single());
        }
    }

    [AvaloniaFact]
    public void RestoreSortOrder_Unsorted_DoesNothing()
    {
        var (disk, fs, hook) = MakeVolume(NAMES);
        using (disk) using (fs) {
            var view = new FakeViewActions();
            var vm = MakeVm(view, hook);
            Populate(vm, fs, IFileEntry.NO_ENTRY);
            (vm.Items[0], vm.Items[1]) = (vm.Items[1], vm.Items[0]);
            string[] before = Names(vm);
            vm.RestoreSortOrder();
            Assert.Equal(before, Names(vm));
        }
    }

    [Fact]
    public void ReplaceableCollection_ReplaceAll_RaisesOneResetAndUpdatesCount()
    {
        var coll = new ReplaceableCollection<int> { 1, 2, 3 };
        var events = new List<NotifyCollectionChangedAction>();
        var props = new List<string?>();
        coll.CollectionChanged += (_, e) => events.Add(e.Action);
        ((System.ComponentModel.INotifyPropertyChanged)coll).PropertyChanged +=
            (_, e) => props.Add(e.PropertyName);

        coll.ReplaceAll(new[] { 9, 8 });

        Assert.Equal(new[] { 9, 8 }, coll);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
        Assert.Contains("Count", props);

        coll.ReplaceAll(coll.Reverse());        // source derived from the collection itself
        Assert.Equal(new[] { 8, 9 }, coll);
    }
}
