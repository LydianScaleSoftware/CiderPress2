/*
 * Copyright 2026 faddenSoft
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
using System.Collections.ObjectModel;
using System.Diagnostics;

using AppCommon;

using CommonUtil;

using DiskArc;
using static DiskArc.Defs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using cp2_avalonia.Common;
using cp2_avalonia.Models;
using cp2_avalonia.Services;
using cp2_avalonia.Views;

namespace cp2_avalonia.ViewModels;

/// <summary>
/// ViewModel for the file list (center panel, main area).
/// Owns the file entry collection, selection, column visibility, and
/// entry population/verification logic.
/// </summary>
public class FileListViewModel : ObservableObject {
    private readonly IViewActions mViewActions;
    private readonly ISettingsService _settingsService;
    private readonly AppHook mAppHook;

    public FileListViewModel(IViewActions viewActions, ISettingsService settingsService,
            AppHook appHook) {
        mViewActions = viewActions;
        _settingsService = settingsService;
        mAppHook = appHook;

        ResetSortCommand = new RelayCommand(DoResetSort);
    }

    // ---- File list collection ----

    /// <summary>
    /// The rows, in display order (sorted, if a sort is active).  Change the whole contents
    /// with <see cref="ReplaceableCollection{T}.ReplaceAll"/> so the grid sees one reset.
    /// </summary>
    public ReplaceableCollection<FileListItem> Items { get; } = new();

    private FileListItem? mSelectedItem;
    public FileListItem? SelectedItem {
        get => mSelectedItem;
        set => SetProperty(ref mSelectedItem, value);
    }

    /// <summary>
    /// Multi-select tracking. DataGrid multi-select is not directly bindable.
    /// Updated via SetSelection() called from view code-behind.
    /// </summary>
    internal IList<FileListItem> Selection { get; private set; } = new List<FileListItem>();

    /// <summary>
    /// Updates the multi-select collection and the primary SelectedItem.
    /// Called from view code-behind when DataGrid selection changes,
    /// via MainViewModel.UpdateFileListSelection().
    /// </summary>
    public void SetSelection(IList<FileListItem> items) {
        Selection = items;
        SelectedItem = items.Count > 0 ? items[0] : null;
    }

    // ---- Sort state ----

    // The active column sort, as the column's SortMemberPath; null when the list is unsorted
    // (in the file system's natural order).  Kept here, not in the view, so that populating
    // the list sorts it *before* the selection is restored.
    private string? mSortPath;
    private bool mSortAscending;

    /// <summary>True if a column sort is active.</summary>
    internal bool IsSortActive => mSortPath != null;

    private bool mIsResetSortEnabled;
    public bool IsResetSortEnabled {
        get => mIsResetSortEnabled;
        set => SetProperty(ref mIsResetSortEnabled, value);
    }

    /// <summary>
    /// Resets sort and signals MainViewModel to repopulate. The subscribe
    /// hook in WireChildViewModels fires after this executes.
    /// </summary>
    public IRelayCommand ResetSortCommand { get; }

    /// <summary>Fired when sort is reset; MainViewModel wires this to PopulateFileList.</summary>
    public event Action? SortReset;

    private void DoResetSort() {
        mSortPath = null;
        mViewActions.ResetFileListSort();
        IsResetSortEnabled = false;
        SortReset?.Invoke();
    }

    /// <summary>
    /// Sorts the list by a column and remembers the sort, so later repopulation keeps it.
    /// The rows are replaced in one reset.
    /// </summary>
    /// <param name="sortMemberPath">The column's <c>SortMemberPath</c>.</param>
    /// <param name="ascending">True for ascending order.</param>
    public void ApplySort(string? sortMemberPath, bool ascending) {
        mSortPath = sortMemberPath ?? string.Empty;
        mSortAscending = ascending;
        IsResetSortEnabled = true;
        Items.ReplaceAll(Sorted(Items));
    }

    /// <summary>
    /// Returns the items in the active sort order, or unchanged if no sort is active.
    /// </summary>
    private List<FileListItem> Sorted(IEnumerable<FileListItem> items) {
        List<FileListItem> list = new List<FileListItem>(items);
        if (mSortPath != null) {
            var comparer = new FileListItem.ItemComparer(mSortPath, mSortAscending);
            // Stable, so rows that compare equal keep their relative order.
            list = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.OrderBy(list, x => x, comparer));
        }
        return list;
    }

    /// <summary>
    /// Re-sorts the list if a sort is active and the rows are no longer in order, e.g. after
    /// a row was replaced in place because its attributes (the sort key) changed.  The
    /// selection is put back afterwards.  Does nothing when the rows are already in order.
    /// </summary>
    public void RestoreSortOrder() {
        if (mSortPath == null) {
            return;
        }
        var comparer = new FileListItem.ItemComparer(mSortPath, mSortAscending);
        bool ordered = true;
        for (int i = 1; i < Items.Count; i++) {
            if (comparer.Compare(Items[i - 1], Items[i]) > 0) {
                ordered = false;
                break;
            }
        }
        if (ordered) {
            return;
        }
        List<FileListItem> selected = new List<FileListItem>(Selection);
        Items.ReplaceAll(Sorted(Items));
        if (selected.Count > 0) {
            mViewActions.SetFileListSelection(selected);
            mViewActions.ScrollFileListItemIntoView(selected[0]);
        }
    }

    // ---- Column visibility ----

    private bool mShowCol_FileName = true;
    public bool ShowCol_FileName {
        get => mShowCol_FileName;
        set => SetProperty(ref mShowCol_FileName, value);
    }

    private bool mShowCol_PathName;
    public bool ShowCol_PathName {
        get => mShowCol_PathName;
        set => SetProperty(ref mShowCol_PathName, value);
    }

    private bool mShowCol_Format;
    public bool ShowCol_Format {
        get => mShowCol_Format;
        set => SetProperty(ref mShowCol_Format, value);
    }

    private bool mShowCol_RawLen;
    public bool ShowCol_RawLen {
        get => mShowCol_RawLen;
        set => SetProperty(ref mShowCol_RawLen, value);
    }

    private bool mShowCol_RsrcLen;
    public bool ShowCol_RsrcLen {
        get => mShowCol_RsrcLen;
        set => SetProperty(ref mShowCol_RsrcLen, value);
    }

    private bool mShowCol_TotalSize;
    public bool ShowCol_TotalSize {
        get => mShowCol_TotalSize;
        set => SetProperty(ref mShowCol_TotalSize, value);
    }

    // ---- Entry counts (observed by MainViewModel to update StatusBar) ----

    private int mLastDirCount;
    public int LastDirCount {
        get => mLastDirCount;
        set => SetProperty(ref mLastDirCount, value);
    }

    private int mLastFileCount;
    public int LastFileCount {
        get => mLastFileCount;
        set => SetProperty(ref mLastFileCount, value);
    }

    // ---- Focus request flag ----

    private bool mSwitchFocusToFileList;

    /// <summary>
    /// Requests that keyboard focus move to the file list after the next
    /// PopulateFileList() call. Cleared internally after use.
    /// </summary>
    public void RequestFocusAfterPopulate() {
        mSwitchFocusToFileList = true;
    }

    /// <summary>
    /// Returns true if a focus request is pending.
    /// </summary>
    internal bool HasFocusRequest => mSwitchFocusToFileList;

    /// <summary>
    /// Consumes (clears) the pending focus request.
    /// </summary>
    internal void ConsumeFocusRequest() { mSwitchFocusToFileList = false; }

    // =========================================================================
    // Population
    // =========================================================================

    /// <summary>
    /// Populates the file list based on the current work object and directory selection.
    /// </summary>
    /// <param name="currentWorkObject">DiskArc object currently selected in archive tree.</param>
    /// <param name="dirTreeSel">Currently selected directory tree item (may be null).</param>
    /// <param name="selEntry">Entry to pre-select after population.</param>
    /// <param name="focusOnFileList">If true, move keyboard focus to the list after population.</param>
    /// <param name="showSingleDir">If true, show a single directory; otherwise show full disk.</param>
    /// <param name="formatter">Formatter instance for size/date strings.</param>
    public void PopulateFileList(object currentWorkObject, DirectoryTreeItem? dirTreeSel,
            IFileEntry selEntry, bool focusOnFileList, bool showSingleDir,
            Formatter formatter) {
        if (selEntry != IFileEntry.NO_ENTRY) {
            Debug.WriteLine("FileList: Populate current item=" + selEntry.FileName +
                " (focus=" + focusOnFileList + " mSwitch=" + mSwitchFocusToFileList + ")");
        } else {
            Debug.WriteLine("FileList: Populate no selected item" +
                " (focus=" + focusOnFileList + " mSwitch=" + mSwitchFocusToFileList + ")");
        }

        DateTime startWhen = DateTime.Now;

        // Build the new rows off to the side and hand them to the grid in one piece, sorted
        // if a sort is active.  Sorting before the selection is restored (below) is what
        // keeps the selection when a sort is active.
        List<FileListItem> newItems = new List<FileListItem>();
        int dirCount = 0;
        int fileCount = 0;
        if (currentWorkObject is IArchive archive) {
            PopulateEntriesFromArchive(archive, newItems,
                ref dirCount, ref fileCount, formatter);
        } else if (currentWorkObject is IFileSystem system) {
            if (showSingleDir) {
                if (dirTreeSel != null) {
                    PopulateEntriesFromSingleDir(dirTreeSel.FileEntry, newItems,
                        ref dirCount, ref fileCount, formatter);
                } else {
                    Debug.WriteLine("FileList: Can't populate, no dir tree sel");
                }
            } else {
                PopulateEntriesFromFullDisk(
                    system.GetVolDirEntry(), newItems,
                    ref dirCount, ref fileCount, formatter);
            }
        } else {
            Debug.Assert(false, "FileList: work object is " + currentWorkObject);
        }

        Items.ReplaceAll(Sorted(newItems));

        DateTime endWhen = DateTime.Now;
        Debug.WriteLine("FileList: refresh done in " +
            (endWhen - startWhen).TotalMilliseconds + " ms");

        if (Items.Count != 0) {
            if (focusOnFileList || mSwitchFocusToFileList) {
                mViewActions.SelectFileListItemByEntry(selEntry);
            }
        }

        if (mSwitchFocusToFileList) {
            Debug.WriteLine("FileList: focus requested");
            mViewActions.FocusFileList();
            mSwitchFocusToFileList = false;
        }

        // Update observable counts so MainViewModel can relay to StatusBar.
        LastDirCount = dirCount;
        LastFileCount = fileCount;

        // Preallocate the name column to fit the widest value in the whole model, so it
        // doesn't grow row-by-row as the virtualized grid scrolls. Done here (synchronously,
        // before the grid lays out) to avoid a visible width jump.
        mViewActions.PreSizeNameColumn();
    }

    private void PopulateEntriesFromArchive(IArchive arc, List<FileListItem> items,
            ref int dirCount, ref int fileCount, Formatter formatter) {
        bool macZipMode = _settingsService.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
        foreach (IFileEntry entry in arc) {
            IFileEntry adfEntry = IFileEntry.NO_ENTRY;
            FileAttribs? adfAttrs = null;
            if (macZipMode && arc is DiskArc.Arc.Zip) {
                if (entry.IsMacZipHeader()) {
                    continue;
                }
                if (DiskArc.Arc.Zip.HasMacZipHeader(arc, entry, out adfEntry)) {
                    try {
                        using System.IO.Stream adfStream =
                            ArcTemp.ExtractToTemp(arc, adfEntry, FilePart.DataFork);
                        adfAttrs = new FileAttribs(entry);
                        adfAttrs.GetFromAppleSingle(adfStream, mAppHook);
                    } catch (Exception ex) {
                        Debug.WriteLine("FileList: Unable to get ADF attrs for '" +
                            entry.FullPathName + "': " + ex.Message);
                        adfAttrs = null;
                    }
                }
            }
            if (entry.IsDirectory) {
                dirCount++;
            } else {
                fileCount++;
            }
            items.Add(new FileListItem(entry, adfEntry, adfAttrs, formatter));
        }
    }

    private void PopulateEntriesFromSingleDir(IFileEntry dirEntry, List<FileListItem> items,
            ref int dirCount, ref int fileCount, Formatter formatter) {
        foreach (IFileEntry entry in dirEntry) {
            if (entry.IsDirectory) {
                dirCount++;
            } else {
                fileCount++;
            }
            items.Add(new FileListItem(entry, formatter));
        }
    }

    private void PopulateEntriesFromFullDisk(IFileEntry curDirEntry, List<FileListItem> items,
            ref int dirCount, ref int fileCount, Formatter formatter) {
        foreach (IFileEntry entry in curDirEntry) {
            items.Add(new FileListItem(entry, formatter));
            if (entry.IsDirectory) {
                dirCount++;
                PopulateEntriesFromFullDisk(entry, items, ref dirCount, ref fileCount,
                    formatter);
            } else {
                fileCount++;
            }
        }
    }

    // =========================================================================
    // Verification
    // =========================================================================

    /// <summary>
    /// Dispatches to the appropriate VerifyFileList overload based on the work object type.
    /// Returns false if the file list needs to be repopulated.
    /// </summary>
    public bool VerifyForObject(object? workObject, DirectoryTreeItem? dirSel,
            bool showSingleDir) {
        if (workObject is IArchive archive) {
            return VerifyFileList(archive);
        } else if (workObject is IFileSystem system) {
            if (showSingleDir) {
                if (dirSel == null) {
                    Debug.WriteLine("FileList: Can't verify, no dir tree sel");
                    return false;
                }
                return VerifyFileList(dirSel.FileEntry);
            } else {
                return VerifyFileList(system);
            }
        } else if (workObject is IMultiPart ||
                workObject is DiskArc.Multi.Partition ||
                workObject is IDiskImage) {
            Debug.WriteLine("FileList: Skipping re-check for non-FS object");
            return true;
        } else {
            Debug.Assert(false, "FileList: can't verify " + workObject);
            return false;
        }
    }

    private bool VerifyFileList(IArchive arc) {
        return VerifyAgainst(arc);
    }

    private bool VerifyFileList(IFileEntry dirEntry) {
        return VerifyAgainst(EnumerateDir(dirEntry, false));
    }

    private bool VerifyFileList(IFileSystem fs) {
        return VerifyAgainst(EnumerateDir(fs.GetVolDirEntry(), true));
    }

    /// <summary>
    /// Enumerates the entries of a directory in the order the list shows them when it is
    /// unsorted: each entry, followed by its contents if it is a directory (and we recurse).
    /// </summary>
    private static IEnumerable<IFileEntry> EnumerateDir(IFileEntry dirEntry, bool doRecurse) {
        foreach (IFileEntry entry in dirEntry) {
            yield return entry;
            if (doRecurse && entry.IsDirectory) {
                foreach (IFileEntry child in EnumerateDir(entry, doRecurse)) {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// Checks whether the list shows exactly the expected entries.  Unsorted, the order has to
    /// match too.  With a sort active the rows are in sort order, not file system order, so
    /// the entries are compared as a set.
    /// </summary>
    private bool VerifyAgainst(IEnumerable<IFileEntry> expected) {
        if (mSortPath == null) {
            int index = 0;
            foreach (IFileEntry entry in expected) {
                if (index >= Items.Count || Items[index].FileEntry != entry) {
                    return false;
                }
                index++;
            }
            return index == Items.Count;
        }

        HashSet<IFileEntry> shown = new HashSet<IFileEntry>(Items.Count,
            ReferenceEqualityComparer.Instance);
        foreach (FileListItem item in Items) {
            shown.Add(item.FileEntry);
        }
        int count = 0;
        foreach (IFileEntry entry in expected) {
            if (!shown.Contains(entry)) {
                return false;
            }
            count++;
        }
        return count == Items.Count && shown.Count == Items.Count;
    }
}
