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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

using Avalonia.Input;

using AppCommon;
using CommonUtil;
using cp2_avalonia.Common;
using DiskArc;
using DiskArc.Arc;
using DiskArc.Disk;
using DiskArc.FS;
using DiskArc.Multi;
using static DiskArc.Defs;
using static DiskArc.IMetadata;

namespace cp2_avalonia {
    public partial class MainController {
        private bool mSwitchFocusToFileList = false;

        /// <summary>
        /// Re-entrancy guard for file list ↔ directory tree selection sync.
        /// </summary>
        private bool mSyncingSelection = false;

        /// <summary>
        /// Cached directory tree selection. Updated in DirectoryTree_SelectionChanged and
        /// SyncDirectoryTreeToFileSelection. Used by NavToParent and CanExecute guards
        /// because Avalonia's TreeView.SelectedItem can return null when focus moves away.
        /// </summary>
        internal DirectoryTreeItem? CachedDirectoryTreeSelection { get; private set; }

        /// <summary>
        /// Cached archive tree selection. Updated in ArchiveTree_SelectionChanged.
        /// </summary>
        internal ArchiveTreeItem? CachedArchiveTreeSelection { get; private set; }

        /// <summary>
        /// Currently-selected DiskArc library object in the archive tree (i.e.
        /// WorkTreeNode.DAObject).  May be IDiskImage, IArchive, IMultiPart, IFileSystem, or
        /// Partition.
        /// </summary>
        private object? CurrentWorkObject { get; set; }

        // True if blocks/sectors are readable.
        public bool CanEditBlocks {
            get { return CanAccessChunk(EditSector.SectorEditMode.Blocks); }
        }
        public bool CanEditSectors {
            get { return CanAccessChunk(EditSector.SectorEditMode.Sectors); }
        }

        public bool HasChunks { get { return GetCurrentWorkChunks() != null; } }

        /// <summary>
        /// Determines whether the current work object can be sector-edited as blocks or sectors.
        /// </summary>
        private bool CanAccessChunk(EditSector.SectorEditMode mode) {
            IChunkAccess? chunks = GetCurrentWorkChunks();
            if (chunks != null) {
                switch (mode) {
                    case EditSector.SectorEditMode.Sectors:
                        return chunks.HasSectors;
                    case EditSector.SectorEditMode.Blocks:
                        return chunks.HasBlocks;
                    case EditSector.SectorEditMode.CPMBlocks:
                        return chunks.HasBlocks && CPM.IsSizeAllowed(chunks.FormattedLength);
                }
            }
            return false;
        }

        /// <summary>
        /// Obtains the IChunkAccess object from CurrentWorkObject.  Returns null if not
        /// applicable.
        /// </summary>
        private IChunkAccess? GetCurrentWorkChunks() {
            if (CurrentWorkObject is IDiskImage) {
                return ((IDiskImage)CurrentWorkObject).ChunkAccess;
            } else if (CurrentWorkObject is Partition) {
                return ((Partition)CurrentWorkObject).ChunkAccess;
            } else {
                return null;
            }
        }

        /// <summary>
        /// True if the currently selected archive tree item is a writable node.
        /// </summary>
        public bool CanWrite {
            get {
                ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
                if (arcTreeSel != null) {
                    return !arcTreeSel.WorkTreeNode.IsReadOnly;
                }
                return false;
            }
        }

        /// <summary>
        /// True if one or more entries are selected.
        /// </summary>
        public bool AreFileEntriesSelected {
            get { return mMainWin.fileListDataGrid.SelectedIndex >= 0; }
        }

        /// <summary>
        /// True if exactly one entry is selected in the file list.
        /// </summary>
        public bool IsSingleEntrySelected {
            get { return mMainWin.fileListDataGrid.SelectedItems.Count == 1; }
        }

        /// <summary>
        /// True if a single file with type ANI $0000 is selected.
        /// </summary>
        public bool IsANISelected {
            get {
                IList sel = mMainWin.fileListDataGrid.SelectedItems;
                if (sel.Count != 1) {
                    return false;
                }
                FileListItem item = (FileListItem)sel[0]!;
                FileAttribs attrs = new FileAttribs(item.FileEntry);
                return (attrs.FileType == FileAttribs.FILE_TYPE_ANI &&
                    attrs.AuxType == 0x0000);
            }
        }

        /// <summary>
        /// True if the item selected in the archive tree is a disk image.
        /// </summary>
        public bool IsDiskImageSelected { get { return CurrentWorkObject is IDiskImage; } }

        /// <summary>
        /// True if the item selected in the archive tree is a partition.
        /// </summary>
        public bool IsPartitionSelected { get { return CurrentWorkObject is Partition; } }

        /// <summary>
        /// True if the item selected in the archive tree is a disk image or a partition.
        /// </summary>
        public bool IsDiskOrPartitionSelected {
            get { return CurrentWorkObject is IDiskImage || CurrentWorkObject is Partition; }
        }

        /// <summary>
        /// True if the item selected in the archive tree is a nibble disk image.
        /// </summary>
        public bool IsNibbleImageSelected { get { return CurrentWorkObject is INibbleDataAccess; } }

        /// <summary>
        /// True if the item selected in the archive tree is a filesystem.
        /// </summary>
        public bool IsFileSystemSelected { get { return CurrentWorkObject is IFileSystem; } }

        /// <summary>
        /// True if the item can hold multiple file entries.
        /// </summary>
        public bool IsMultiFileItemSelected {
            get {
                if (CurrentWorkObject is IFileSystem) {
                    return true;
                } else if (CurrentWorkObject is IArchive) {
                    return !((IArchive)CurrentWorkObject).Characteristics.HasSingleEntry;
                } else {
                    return false;
                }
            }
        }

        public bool IsDefragmentableSelected { get { return CurrentWorkObject is Pascal; } }

        /// <summary>
        /// True if the selected item in the archive tree is a hierarchical filesystem (ProDOS
        /// or HFS).
        /// </summary>
        public bool IsHierarchicalFileSystemSelected {
            get {
                IFileSystem? fs = CurrentWorkObject as IFileSystem;
                if (fs != null) {
                    return fs.Characteristics.IsHierarchical;
                }
                return false;
            }
        }

        /// <summary>
        /// True if the entry selected in the directory tree is at the root (has no parent).
        /// </summary>
        public bool IsSelectedDirRoot {
            get {
                DirectoryTreeItem? dirSel = CachedDirectoryTreeSelection;
                return (dirSel != null && dirSel.Parent == null);
            }
        }

        /// <summary>
        /// True if the entry selected in the archive tree is at the root (the host file).
        /// </summary>
        public bool IsSelectedArchiveRoot {
            get {
                ArchiveTreeItem? arcSel = CachedArchiveTreeSelection;
                return (arcSel != null && arcSel.Parent == null);
            }
        }

        /// <summary>
        /// True if the item selected in the archive tree view is a closable sub-tree.
        /// </summary>
        public bool IsClosableTreeSelected {
            get {
                ArchiveTreeItem? arcSel = CachedArchiveTreeSelection;
                return (arcSel != null && arcSel.CanClose);
            }
        }

        /// <summary>
        /// Handles Actions → Scan for Sub-Volumes.
        /// </summary>
        public void ScanForSubVol() {
            IFileSystem? fs = CurrentWorkObject as IFileSystem;
            if (fs == null) {
                Debug.Assert(false);
                return;
            }
            ArchiveTreeItem? arcTreeSel = CachedArchiveTreeSelection;
            if (arcTreeSel == null) {
                Debug.Assert(false);
                return;
            }
            Debug.Assert(arcTreeSel.WorkTreeNode.DAObject == fs);

            IMultiPart? embeds = fs.FindEmbeddedVolumes();
            if (embeds == null) {
                Debug.WriteLine("No sub-volumes found");
                return;
            }

            // If already open, just select it.
            ArchiveTreeItem? existing =
                ArchiveTreeItem.FindItemByDAObject(mMainWin.ArchiveTreeRoot, embeds);
            if (existing != null) {
                ArchiveTreeItem.SelectItem(mMainWin, existing);
                return;
            }

            // Add to the tree and select the new multi-part node.
            WorkTree.Node? newNode =
                mWorkTree!.TryCreateMultiPart(arcTreeSel.WorkTreeNode, embeds);
            if (newNode != null) {
                ArchiveTreeItem newItem = ArchiveTreeItem.ConstructTree(arcTreeSel, newNode);
                ArchiveTreeItem.SelectItem(mMainWin, newItem);
            }
        }

        /// <summary>
        /// Closes the currently selected sub-tree node.
        /// </summary>
        public void CloseSubTree() {
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            if (arcTreeSel == null)
            {
                return;
            }

            CloseSubTree(arcTreeSel);
        }

        /// <summary>
        /// Closes a sub-tree node, removing it from the archive tree.
        /// </summary>
        internal void CloseSubTree(ArchiveTreeItem item) {
            Debug.Assert(item.CanClose);
            if (item.Parent == null) {
                Debug.Assert(false, "cannot close root");
                return;
            }
            WorkTree.Node workNode = item.WorkTreeNode;
            ArchiveTreeItem parentItem = item.Parent;
            WorkTree.Node parentNode = item.Parent.WorkTreeNode;
            parentNode.CloseChild(workNode);
            bool ok = parentItem.RemoveChild(item);
            Debug.Assert(ok, "failed to remove child tree item");
            parentItem.IsSelected = true;
        }


        /// Should be called after any state change that might affect command availability
        /// (tree selection change, file list selection change, open/close).
        /// </summary>
        internal void RefreshAllCommandStates() {
            // All RelayCommand properties live on mMainWin.  Iterate them explicitly.
            (mMainWin.CloseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.CopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.PasteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.FindCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.SelectAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ViewFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.AddFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ImportFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ExtractFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ExportFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.DeleteFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.TestFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.EditAttributesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.CreateDirectoryCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.EditDirAttributesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.EditSectorsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.EditBlocksCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.SaveAsDiskImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ReplacePartitionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ScanForBadBlocksCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ScanForSubVolCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.DefragmentCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.CloseSubTreeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ShowFullListCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ShowDirListCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ShowInfoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.NavToParentDirCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.NavToParentCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.ResetSortCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (mMainWin.Debug_ConvertANICommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Handles selection change in archive tree view.
        /// </summary>
        internal void ArchiveTree_SelectionChanged(ArchiveTreeItem? newSel) {
            Debug.WriteLine("Archive tree selection now: " + (newSel == null ? "none" : newSel));
            if (newSel != null) {
                CachedArchiveTreeSelection = newSel;
            }
            if (newSel == null) {
                mMainWin.DirectoryTreeRoot.Clear();
                CurrentWorkObject = null;
                return;
            }

            mMainWin.DirectoryTreeRoot.Clear();
            mMainWin.ClearCenterInfo();

            CurrentWorkObject = newSel.WorkTreeNode.DAObject;

            Debug.Assert(mWorkTree != null);
            ConfigureCenterInfo(CurrentWorkObject, newSel.Name);
            if (newSel.WorkTreeNode == mWorkTree.RootNode) {
                if (mWorkTree.ReadWriteOpenFailure == null) {
                    mMainWin.CenterInfoText2 = string.Empty;
                } else {
                    if (mWorkTree.ReadWriteOpenFailure is IOException &&
                            (mWorkTree.ReadWriteOpenFailure.HResult & 0xffff) == 32) {
                        // ERROR_SHARING_VIOLATION
                        mMainWin.CenterInfoText2 = "Unable to open the file for writing, because " +
                            "it's being used by another process.";
                    } else if (mWorkTree.ReadWriteOpenFailure is UnauthorizedAccessException) {
                        mMainWin.CenterInfoText2 = "Unable to open the file for writing, because " +
                            "it's marked read-only.";
                    } else {
                        mMainWin.CenterInfoText2 = "Unable to open the file for writing.";
                    }
                }
            } else {
                mMainWin.CenterInfoText2 = string.Empty;
            }

            if (CurrentWorkObject is IFileSystem) {
                ObservableCollection<DirectoryTreeItem> tvRoot = mMainWin.DirectoryTreeRoot;
                IFileSystem fs = (IFileSystem)CurrentWorkObject;
                Debug.Assert(fs.GetVolDirEntry() != IFileEntry.NO_ENTRY);
                PopulateDirectoryTree(null, tvRoot, fs.GetVolDirEntry());
                mMainWin.directoryTree.SelectedItem = tvRoot[0];
                mMainWin.DirectoryTree_ScrollToTop();
                mMainWin.SetNotesList(fs.Notes);
            } else {
                string title = "Information";
                if (CurrentWorkObject is IArchive) {
                    IArchive arc = (IArchive)CurrentWorkObject;
                    title = "File Archive Entry List";
                    mMainWin.SetNotesList(arc.Notes);
                } else if (CurrentWorkObject is IDiskImage) {
                    IDiskImage disk = (IDiskImage)CurrentWorkObject;
                    title = "Disk Image Information";
                    mMainWin.SetNotesList(disk.Notes);

                    mMainWin.ShowDiskUtilityButtons = true;

                    if (CurrentWorkObject is IMetadata) {
                        mMainWin.SetMetadataList((IMetadata)CurrentWorkObject);
                    }
                } else if (CurrentWorkObject is IMultiPart) {
                    IMultiPart parts = (IMultiPart)CurrentWorkObject;
                    title = "Multi-Partition Information";
                    mMainWin.SetNotesList(parts.Notes);
                    mMainWin.SetPartitionList(parts);
                } else if (CurrentWorkObject is Partition) {
                    // no notes
                    title = "Partition Information";
                    mMainWin.ShowDiskUtilityButtons = true;
                }
                DirectoryTreeItem newItem = new DirectoryTreeItem(null, title, IFileEntry.NO_ENTRY);
                mMainWin.DirectoryTreeRoot.Add(newItem);
                mMainWin.directoryTree.SelectedItem = newItem;
            }

            mMainWin.FileList_ScrollToTop();
            RefreshAllCommandStates();
        }

        /// <summary>
        /// Recursively populates the directory tree view from an IFileSystem object.
        /// </summary>
        private static void PopulateDirectoryTree(DirectoryTreeItem? parent,
                ObservableCollection<DirectoryTreeItem> tvRoot, IFileEntry dirEntry) {
            DirectoryTreeItem newItem = new DirectoryTreeItem(parent, dirEntry.FileName, dirEntry);
            tvRoot.Add(newItem);
            foreach (IFileEntry entry in dirEntry) {
                if (entry.IsDirectory) {
                    PopulateDirectoryTree(newItem, newItem.Items, entry);
                }
            }
        }

        /// <summary>
        /// Handles selection change in directory tree view.
        /// </summary>
        internal void DirectoryTree_SelectionChanged(DirectoryTreeItem? newSel) {
            Debug.WriteLine("Directory tree selection now: " + (newSel == null ? "none" : newSel));
            if (newSel != null) {
                CachedDirectoryTreeSelection = newSel;
            }
            if (newSel == null) {
                mMainWin.FileList.Clear();
                return;
            }
            if (mSyncingSelection) {
                return;
            }

            if (CurrentWorkObject is IArchive) {
                // There isn't really any content in the directory tree, but we'll get this
                // event when the entry representing the entire archive is selected.
                bool hasRsrc = ((IArchive)CurrentWorkObject).Characteristics.HasResourceForks;
                if (CurrentWorkObject is Zip) {
                    // TODO: should reconfigure columns when settings are applied.
                    hasRsrc = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
                }
                mMainWin.ConfigureCenterPanel(isInfoOnly: false, isArchive: true,
                    isHierarchic: true, hasRsrc: hasRsrc, hasRaw: false);
                RefreshDirAndFileList(false);
            } else if (CurrentWorkObject is IFileSystem) {
                bool hasRsrc = ((IFileSystem)CurrentWorkObject).Characteristics.HasResourceForks;
                bool isHier = ((IFileSystem)CurrentWorkObject).Characteristics.IsHierarchical;
                mMainWin.ConfigureCenterPanel(isInfoOnly: false, isArchive: false,
                    isHierarchic: isHier, hasRsrc: hasRsrc, hasRaw: CurrentWorkObject is DOS);
                if (mMainWin.ShowSingleDirFileList) {
                    RefreshDirAndFileList(false);
                } else {
                    RefreshDirAndFileList(false);

                    // Find the directory entry in the full file list.
                    FileListItem? dirItem =
                        FileListItem.FindItemByEntry(mMainWin.FileList, newSel.FileEntry);
                    if (dirItem != null) {
                        mMainWin.SelectedFileListItem = dirItem;
                        mMainWin.fileListDataGrid.ScrollIntoView(dirItem, null);
                    }
                }
            } else {
                mMainWin.ConfigureCenterPanel(isInfoOnly: true, isArchive: false,
                    isHierarchic: false, hasRsrc: false, hasRaw: false);
                ClearEntryCounts();
            }
            RefreshAllCommandStates();
        }

        /// <summary>
        /// When the user selects a file in the full file list, sync the directory tree
        /// to show the containing directory of the selected file.
        /// </summary>
        internal void SyncDirectoryTreeToFileSelection() {
            if (mSyncingSelection || mMainWin.ShowSingleDirFileList ||
                    CurrentWorkObject is not IFileSystem) {
                return;
            }

            FileListItem? selItem = mMainWin.SelectedFileListItem;
            if (selItem == null) {
                return;
            }

            IFileEntry entry = selItem.FileEntry;
            IFileEntry targetDir = entry.IsDirectory ? entry : entry.ContainingDir;
            if (targetDir == IFileEntry.NO_ENTRY) {
                return;
            }

            // Only update if it's actually a different directory.
            DirectoryTreeItem? curDirItem = mMainWin.SelectedDirectoryTreeItem;
            if (curDirItem != null && curDirItem.FileEntry == targetDir) {
                return;
            }

            mSyncingSelection = true;
            try {
                DirectoryTreeItem.SelectItemByEntry(mMainWin, targetDir);
                CachedDirectoryTreeSelection =
                    mMainWin.SelectedDirectoryTreeItem ?? CachedDirectoryTreeSelection;
            } finally {
                mSyncingSelection = false;
            }
        }

        /// <summary>
        /// Refreshes the contents of the directory tree and file list.
        /// </summary>
        internal void RefreshDirAndFileList(bool focusOnFileList = true) {
            mSwitchFocusToFileList |= focusOnFileList;
            if (CurrentWorkObject == null) {
                return;
            }

            // Get item currently selected in file list, if any.
            FileListItem? selectedItem = mMainWin.SelectedFileListItem;
            IFileEntry selFileEntry = IFileEntry.NO_ENTRY;
            if (selectedItem != null) {
                if (selectedItem.FileEntry.IsValid) {
                    selFileEntry = selectedItem.FileEntry;
                } else {
                    Debug.WriteLine("Refresh: selected file entry is no longer valid");
                }
            } else {
                Debug.WriteLine("Refresh: no file list sel");
            }

            if (CurrentWorkObject is IFileSystem) {
                IFileSystem fs = (IFileSystem)CurrentWorkObject;

                IFileEntry curSel = IFileEntry.NO_ENTRY;
                DirectoryTreeItem? dirTreeSel = mMainWin.SelectedDirectoryTreeItem;
                if (dirTreeSel != null) {
                    if (dirTreeSel.FileEntry.IsValid) {
                        curSel = dirTreeSel.FileEntry;
                    } else {
                        Debug.WriteLine("Refresh: selected dir entry is no longer valid");
                    }
                } else {
                    Debug.WriteLine("Refresh: no dir tree sel");
                }

                ObservableCollection<DirectoryTreeItem> rootList = mMainWin.DirectoryTreeRoot;
                IFileEntry volDir = fs.GetVolDirEntry();
                if (!VerifyDirectoryTree(rootList, volDir, 0)) {
                    Debug.WriteLine("Re-populate directory tree");
                    rootList.Clear();
                    PopulateDirectoryTree(null, rootList, volDir);

                    if (curSel == IFileEntry.NO_ENTRY ||
                            !DirectoryTreeItem.SelectItemByEntry(mMainWin, curSel)) {
                        mMainWin.directoryTree.SelectedItem = rootList[0];
                    }
                } else {
                    Debug.WriteLine("Not repopulating directory tree");
                }
            }

            if (!VerifyFileList()) {
                PopulateFileList(selFileEntry, focusOnFileList);
            } else {
                FileListItem? item = FileListItem.FindItemByEntry(mMainWin.FileList, selFileEntry);
                if (item != null) {
                    mMainWin.SelectedFileListItem = item;
                }
                Debug.WriteLine("Not repopulating file list");
            }
            if (mSwitchFocusToFileList) {
                Debug.WriteLine("++ focus to file list requested");
                mMainWin.FileList_SetSelectionFocus();
                mSwitchFocusToFileList = false;
            }
        }

        /// <summary>
        /// Recursively verifies that the directory tree matches the current filesystem layout.
        /// </summary>
        private static bool VerifyDirectoryTree(ObservableCollection<DirectoryTreeItem> tvRoot,
                IFileEntry dirEntry, int index) {
            if (index >= tvRoot.Count) {
                return false;
            }
            DirectoryTreeItem item = tvRoot[index];
            if (item.FileEntry != dirEntry) {
                return false;
            }
            int childIndex = 0;
            foreach (IFileEntry entry in dirEntry) {
                if (entry.IsDirectory) {
                    if (!VerifyDirectoryTree(item.Items, entry, childIndex++)) {
                        return false;
                    }
                }
            }
            if (childIndex != item.Items.Count) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Verifies that the file list matches the current configuration.
        /// </summary>
        private bool VerifyFileList() {
            if (CurrentWorkObject is IArchive) {
                return VerifyFileList(mMainWin.FileList, (IArchive)CurrentWorkObject);
            } else if (CurrentWorkObject is IFileSystem) {
                if (mMainWin.ShowSingleDirFileList) {
                    DirectoryTreeItem? dirTreeSel = mMainWin.SelectedDirectoryTreeItem;
                    if (dirTreeSel == null) {
                        Debug.WriteLine("Can't verify file list, no dir tree sel");
                        return false;
                    }
                    return VerifyFileList(mMainWin.FileList, dirTreeSel.FileEntry);
                } else {
                    return VerifyFileList(mMainWin.FileList, (IFileSystem)CurrentWorkObject);
                }
            } else if (CurrentWorkObject is IMultiPart || CurrentWorkObject is Partition ||
                    CurrentWorkObject is IDiskImage) {
                Debug.WriteLine("Skipping file list re-check");
                return true;
            } else {
                Debug.Assert(false, "can't verify " + CurrentWorkObject);
                return false;
            }
        }

        /// <summary>
        /// Populates the file list, setting a specific item as selected.
        /// </summary>
        internal void PopulateFileList(IFileEntry selEntry, bool focusOnFileList) {
            if (selEntry != IFileEntry.NO_ENTRY) {
                Debug.WriteLine("Populate: current item is " + selEntry.FileName +
                    " (focus=" + focusOnFileList + " mSwitch=" + mSwitchFocusToFileList + ")");
            } else {
                Debug.WriteLine("Populate: no selected item in file list " +
                    " (focus=" + focusOnFileList + " mSwitch=" + mSwitchFocusToFileList + ")");
            }
            ObservableCollection<FileListItem> fileList = mMainWin.FileList;

            DateTime clearWhen = DateTime.Now;
            fileList.Clear();
            DateTime startWhen = DateTime.Now;

            int dirCount = 0;
            int fileCount = 0;
            if (CurrentWorkObject is IArchive) {
                PopulateEntriesFromArchive((IArchive)CurrentWorkObject,
                    ref dirCount, ref fileCount, fileList);
            } else if (CurrentWorkObject is IFileSystem) {
                if (mMainWin.ShowSingleDirFileList) {
                    DirectoryTreeItem? dirTreeSel = mMainWin.SelectedDirectoryTreeItem;
                    if (dirTreeSel != null) {
                        PopulateEntriesFromSingleDir(dirTreeSel.FileEntry,
                            ref dirCount, ref fileCount, fileList);
                    } else {
                        Debug.WriteLine("Can't repopulate file list, no dir tree sel");
                    }
                } else {
                    PopulateEntriesFromFullDisk(((IFileSystem)CurrentWorkObject!).GetVolDirEntry(),
                        ref dirCount, ref fileCount, fileList);
                }
            } else {
                Debug.Assert(false, "work object is " + CurrentWorkObject);
            }

            DateTime endWhen = DateTime.Now;
            AppHook.LogD("File list refresh done in " +
                (endWhen - startWhen).TotalMilliseconds + " ms (clear took " +
                (startWhen - clearWhen).TotalMilliseconds + " ms)");


            if (fileList.Count != 0) {
                if (focusOnFileList || mSwitchFocusToFileList) {
                    FileListItem.SetSelectionFocusByEntry(fileList, mMainWin.fileListDataGrid,
                        selEntry);
                }
            }

            if (mSwitchFocusToFileList) {
                Debug.WriteLine("+ focus to file list requested");
                mMainWin.FileList_SetSelectionFocus();
                mSwitchFocusToFileList = false;
            }

            SetEntryCounts(CurrentWorkObject as IFileSystem, dirCount, fileCount);

            // Reapply any user-chosen column sort that was active before repopulation.
            mMainWin.ReapplyFileListSort();
        }

        private void PopulateEntriesFromArchive(IArchive arc, ref int dirCount,
                ref int fileCount, ObservableCollection<FileListItem> fileList) {
            bool macZipMode = AppSettings.Global.GetBool(AppSettings.MAC_ZIP_ENABLED, true);
            foreach (IFileEntry entry in arc) {
                IFileEntry adfEntry = IFileEntry.NO_ENTRY;
                FileAttribs? adfAttrs = null;
                if (macZipMode && arc is Zip) {
                    if (entry.IsMacZipHeader()) {
                        // Ignore headers we don't explicitly look for.
                        continue;
                    }
                    // Look for paired entry.
                    if (Zip.HasMacZipHeader(arc, entry, out adfEntry)) {
                        try {
                            // Can't use un-seekable archive stream as archive source.
                            using (Stream adfStream = ArcTemp.ExtractToTemp(arc,
                                    adfEntry, FilePart.DataFork)) {
                                adfAttrs = new FileAttribs(entry);
                                adfAttrs.GetFromAppleSingle(adfStream, AppHook);
                            }
                        } catch (Exception ex) {
                            Debug.WriteLine("Unable to get ADF attrs for '" +
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
                fileList.Add(new FileListItem(entry, adfEntry, adfAttrs, mFormatter));
            }
        }

        private void PopulateEntriesFromSingleDir(IFileEntry dirEntry, ref int dirCount,
                ref int fileCount, ObservableCollection<FileListItem> fileList) {
            foreach (IFileEntry entry in dirEntry) {
                if (entry.IsDirectory) {
                    dirCount++;
                } else {
                    fileCount++;
                }
                fileList.Add(new FileListItem(entry, mFormatter));
            }
        }

        private void PopulateEntriesFromFullDisk(IFileEntry curDirEntry, ref int dirCount,
                ref int fileCount, ObservableCollection<FileListItem> fileList) {
            foreach (IFileEntry entry in curDirEntry) {
                fileList.Add(new FileListItem(entry, mFormatter));
                if (entry.IsDirectory) {
                    dirCount++;
                    PopulateEntriesFromFullDisk(entry, ref dirCount, ref fileCount, fileList);
                } else {
                    fileCount++;
                }
            }
        }

        /// <summary>
        /// Verifies that the file list matches the contents of an archive.
        /// </summary>
        private static bool VerifyFileList(ObservableCollection<FileListItem> fileList,
                IArchive arc) {
            if (fileList.Count != arc.Count) {
                return false;
            }
            int index = 0;
            foreach (IFileEntry entry in arc) {
                if (fileList[index].FileEntry != entry) {
                    return false;
                }
                index++;
            }
            return true;
        }

        /// <summary>
        /// Verifies that the file list matches the contents of a single directory.
        /// </summary>
        private static bool VerifyFileList(ObservableCollection<FileListItem> fileList,
                IFileEntry dirEntry) {
            int index = 0;
            bool ok = VerifyFileList(fileList, ref index, dirEntry, false);
            return (ok && index == fileList.Count);
        }

        /// <summary>
        /// Verifies that the file list matches the contents of a full filesystem.
        /// </summary>
        private static bool VerifyFileList(ObservableCollection<FileListItem> fileList,
                IFileSystem fs) {
            int index = 0;
            bool ok = VerifyFileList(fileList, ref index, fs.GetVolDirEntry(), true);
            return (ok && index == fileList.Count);
        }

        /// <summary>
        /// Recursively verifies that disk contents match.
        /// </summary>
        private static bool VerifyFileList(ObservableCollection<FileListItem> fileList,
                ref int index, IFileEntry dirEntry, bool doRecurse) {
            if (index >= fileList.Count) {
                return false;
            }
            foreach (IFileEntry entry in dirEntry) {
                if (index >= fileList.Count || fileList[index].FileEntry != entry) {
                    return false;
                }
                index++;
                if (doRecurse && entry.IsDirectory) {
                    if (!VerifyFileList(fileList, ref index, entry, doRecurse)) {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Sets the file/directory counts for the status bar.
        /// </summary>
        private void SetEntryCounts(IFileSystem? fs, int dirCount, int fileCount) {
            StringBuilder sb = new StringBuilder();
            sb.Append(fileCount);
            if (fileCount == 1) {
                sb.Append(" file, ");
            } else {
                sb.Append(" files, ");
            }
            sb.Append(dirCount);
            if (dirCount == 1) {
                sb.Append(" directory");
            } else {
                sb.Append(" directories");
            }
            if (fs != null) {
                int baseUnit;
                if (fs is DOS || fs is RDOS || fs is Gutenberg) {
                    baseUnit = SECTOR_SIZE;
                } else if (fs is ProDOS || fs is Pascal) {
                    baseUnit = BLOCK_SIZE;
                } else {    // HFS, MFS, CP/M
                    baseUnit = KBLOCK_SIZE;
                }
                sb.Append(", ");
                sb.Append(mFormatter.FormatSizeOnDisk(fs.FreeSpace, baseUnit));
                sb.Append(" free");
            }

            mMainWin.CenterStatusText = sb.ToString();
        }

        /// <summary>
        /// Clears the file/directory count text.
        /// </summary>
        internal void ClearEntryCounts() {
            mMainWin.CenterStatusText = string.Empty;
        }

        /// <summary>
        /// Adds a name/value pair to the center info panel list.
        /// </summary>
        private void AddInfoItem(string name, string value) {
            mMainWin.CenterInfoList.Add(new MainWindow.CenterInfoItem(name + ":", value));
        }

        /// <summary>
        /// Generates a one-line blurb about the specified object, which may be any type that
        /// can be found in the archive tree.
        /// </summary>
        private void ConfigureCenterInfo(object workObj, string selName) {
            mMainWin.CenterInfoList.Clear();
            string infoText;
            if (workObj is IArchive) {
                IArchive arc = (IArchive)workObj;
                infoText = "File archive - " + ThingString.IArchive(arc) + " - " + selName;
                AddInfoItem("Entries", arc.Count.ToString());
            } else if (workObj is IDiskImage) {
                IDiskImage disk = (IDiskImage)workObj;
                infoText = "Disk image - " + ThingString.IDiskImage(disk) + " - " + selName;

                IChunkAccess? chunks = disk.ChunkAccess;
                if (chunks != null) {
                    AddInfoItem("Total size",
                        mFormatter.FormatSizeOnDisk(chunks.FormattedLength, KBLOCK_SIZE));
                    if (chunks.HasSectors) {
                        AddInfoItem("Geometry",
                            chunks.NumTracks.ToString() + " tracks, " +
                            chunks.NumSectorsPerTrack.ToString() + " sectors");
                        if (chunks.NumSectorsPerTrack == 16) {
                            AddInfoItem("File order", ThingString.SectorOrder(chunks.FileOrder));
                        }
                    }
                    if (chunks.NibbleCodec != null) {
                        AddInfoItem("Nibble codec", chunks.NibbleCodec.Name);
                    }
                }
            } else if (workObj is IFileSystem) {
                IFileSystem fs = (IFileSystem)workObj;
                infoText = "Filesystem - " + ThingString.IFileSystem(fs) + " - " + selName;

                IChunkAccess chunks = fs.RawAccess;
                AddInfoItem("Volume size",
                    mFormatter.FormatSizeOnDisk(chunks.FormattedLength, KBLOCK_SIZE));
            } else if (workObj is IMultiPart) {
                IMultiPart partitions = (IMultiPart)workObj;
                infoText = "Multi-partition format - " + ThingString.IMultiPart(partitions) +
                    " - " + selName;
                AddInfoItem("Partition count", partitions.Count.ToString());
            } else if (workObj is Partition) {
                Partition part = (Partition)workObj;
                infoText = "Disk partition - " + ThingString.Partition(part) + " - " + selName;
                AddInfoItem("Start block", (part.StartOffset / BLOCK_SIZE).ToString());
                AddInfoItem("Block count", (part.Length / BLOCK_SIZE).ToString() + " (" +
                    (part.Length / (1024 * 1024.0)).ToString("N1") + " MB)");
            } else {
                infoText = "???";
            }

            mMainWin.CenterInfoText1 = infoText;
        }

        /// <summary>
        /// Scans the currently selected archive for entries that look like disk images or
        /// file archives but have not yet been opened as sub-volumes in the work tree.
        /// Any newly recognized entries are opened and added to the archive tree.
        /// </summary>
        /// <remarks>
        /// <para>This mirrors the auto-open behaviour of the initial WorkTree scan.  It is
        /// intended to be called after add/paste operations so that newly added disk images
        /// appear in the Archive Contents panel without requiring a manual double-click.</para>
        /// </remarks>
        internal void TryOpenNewSubVolumes() {
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            if (arcTreeSel == null || mWorkTree == null) {
                return;
            }
            IArchive? arc = arcTreeSel.WorkTreeNode.DAObject as IArchive;
            if (arc == null) {
                return;     // only applies to file archives
            }

            bool addedAny = false;
            foreach (IFileEntry entry in arc) {
                if (entry.IsDirectory) {
                    continue;
                }
                // Skip entries already open in the tree.
                if (ArchiveTreeItem.FindItemByEntry(mMainWin.ArchiveTreeRoot, entry) != null) {
                    continue;
                }
                try {
                    WorkTree.Node? newNode =
                        mWorkTree.TryCreateSub(arcTreeSel.WorkTreeNode, entry);
                    if (newNode != null) {
                        ArchiveTreeItem.ConstructTree(arcTreeSel, newNode);
                        addedAny = true;
                    }
                } catch (Exception ex) {
                    Debug.WriteLine("TryOpenNewSubVolumes: failed on " +
                        entry.FullPathName + ": " + ex.Message);
                }
            }

            if (addedAny) {
                arcTreeSel.IsExpanded = true;
            }
        }

        /// <summary>
        /// Handles a double-click on an item in the file list grid.
        /// </summary>
        public void HandleFileListDoubleClick() {
            ArchiveTreeItem? arcTreeSel = mMainWin.SelectedArchiveTreeItem;
            DirectoryTreeItem? dirTreeSel = mMainWin.SelectedDirectoryTreeItem;
            if (arcTreeSel == null || dirTreeSel == null) {
                Debug.Assert(false, "tree is missing selection");
                return;
            }

            IArchive? arc = arcTreeSel.WorkTreeNode.DAObject as IArchive;
            IFileSystem? fs = arcTreeSel.WorkTreeNode.DAObject as IFileSystem;
            if (!(arc == null ^ fs == null)) {
                Debug.Assert(false, "Unexpected: arc=" + arc + " fs=" + fs);
                return;
            }

            Avalonia.Controls.DataGrid dg = mMainWin.fileListDataGrid;
            int treeSelIndex = dg.SelectedIndex;
            if (treeSelIndex < 0) {
                Debug.WriteLine("Double-click but no selection");
                return;
            }

            if (dg.SelectedItems.Count == 1) {
                FileListItem fli = (FileListItem)dg.SelectedItems[0]!;
                IFileEntry entry = fli.FileEntry;
                if (entry.IsDirectory) {
                    if (fs != null) {
                        mSwitchFocusToFileList = true;
                        if (!DirectoryTreeItem.SelectItemByEntry(mMainWin, entry)) {
                            Debug.WriteLine("Unable to find dir tree entry for " + entry);
                            mSwitchFocusToFileList = false;
                        }
                    }
                    // else: directory file in a file archive — nothing to do.
                    return;
                }

                // Is it a disk image or file archive that's already open in the tree?
                ArchiveTreeItem? ati =
                    ArchiveTreeItem.FindItemByEntry(mMainWin.ArchiveTreeRoot, entry);
                if (ati != null) {
                    ArchiveTreeItem.SelectBestFrom(mMainWin.archiveTree, ati);
                    mSwitchFocusToFileList = true;
                    return;
                }

                try {
                    mMainWin.Cursor = new Cursor(StandardCursorType.Wait);

                    WorkTree.Node? newNode =
                        mWorkTree!.TryCreateSub(arcTreeSel.WorkTreeNode, entry);
                    if (newNode != null) {
                        ArchiveTreeItem newItem =
                            ArchiveTreeItem.ConstructTree(arcTreeSel, newNode);
                        ArchiveTreeItem.SelectBestFrom(mMainWin.archiveTree, newItem);
                        mSwitchFocusToFileList = true;
                        return;
                    }
                } finally {
                    mMainWin.Cursor = null;
                }
            }

            // View the selection.
            _ = ViewFiles();
        }

        /// <summary>
        /// View selected files.
        /// </summary>
        public async Task ViewFiles() {
            if (!GetFileSelection(omitDir: true, omitOpenArc: true, closeOpenArc: false,
                    oneMeansAll: true, out object? archiveOrFileSystem, out IFileEntry _,
                    out List<IFileEntry>? selected, out int firstSel)) {
                await ShowMessageAsync("GetFileSelection returned false — no selection could " +
                    "be determined.", "ViewFiles Debug");
                return;
            }
            if (selected.Count == 0 || firstSel < 0) {
                await ShowMessageAsync("No viewable files selected (can't view directories " +
                    "or open disks/archives).\nselected.Count=" + selected.Count +
                    " firstSel=" + firstSel, "Empty");
                return;
            }

            try {
                var dialog = new cp2_avalonia.Tools.FileViewer();
                dialog.Init(mMainWin, archiveOrFileSystem, selected, firstSel, AppHook);
                await dialog.ShowDialog<object?>(mMainWin);
            } catch (Exception ex) {
                await ShowMessageAsync("FileViewer threw an exception:\n" + ex.Message +
                    "\n\n" + ex.StackTrace, "ViewFiles Error");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Partition Layout

        /// <summary>
        /// Handles a double-click on a partition layout entry.  If the partition is already
        /// open in the archive tree, select it; otherwise try to open it.
        /// </summary>
        public void HandlePartitionLayoutDoubleClick(MainWindow.PartitionListItem item,
                ArchiveTreeItem arcTreeSel) {
            ArchiveTreeItem? ati = ArchiveTreeItem.FindItemByDAObject(mMainWin.ArchiveTreeRoot,
                item.PartRef);
            if (ati != null) {
                ArchiveTreeItem.SelectBestFrom(mMainWin.archiveTree, ati);
                return;
            }

            try {
                mMainWin.Cursor = new Cursor(StandardCursorType.Wait);

                WorkTree.Node? newNode =
                    mWorkTree!.TryCreatePartition(arcTreeSel.WorkTreeNode, item.Index);
                if (newNode != null) {
                    // Successfully opened.  Update the TreeView.
                    ArchiveTreeItem newItem =
                        ArchiveTreeItem.ConstructTree(arcTreeSel, newNode);
                    // Select something in what we just added.  If it was a disk image, we want
                    // to select the first filesystem, not the disk image itself.
                    ArchiveTreeItem.SelectBestFrom(mMainWin.archiveTree, newItem);
                }
            } finally {
                mMainWin.Cursor = null;
            }
        }

        // -----------------------------------------------------------------------------------------
        // Metadata

        /// <summary>
        /// Handles a double-click on a metadata list entry.
        /// </summary>
        public async Task HandleMetadataDoubleClick(MainWindow.MetadataItem item,
                int row, int col) {
            IMetadata? metaObj = CurrentWorkObject as IMetadata;
            if (metaObj == null) {
                Debug.Assert(false);
                return;
            }
            EditMetadata dialog = new EditMetadata(metaObj, item.Key);
            bool? result = await dialog.ShowDialog<bool?>(mMainWin);
            if (result == true) {
                if (dialog.DoDelete) {
                    metaObj.DeleteMetaEntry(dialog.KeyText);
                    mMainWin.RemoveMetadata(item.Key);
                } else {
                    metaObj.SetMetaValue(item.Key, dialog.ValueText);
                    string? fancyValue = metaObj.GetMetaValue(item.Key, true);
                    if (fancyValue != null) {
                        mMainWin.UpdateMetadata(item.Key, fancyValue);
                    }
                }
                if (metaObj is IDiskImage diskImg) {
                    diskImg.Flush();
                }
            }
        }

        /// <summary>
        /// Handles a click on the "Add Metadata Entry" button.
        /// </summary>
        public async Task HandleMetadataAddEntry() {
            IMetadata? metaObj = CurrentWorkObject as IMetadata;
            if (metaObj == null) {
                Debug.Assert(false);
                return;
            }
            if (metaObj is Woz woz && !woz.HasMeta) {
                woz.AddMETA();
                mMainWin.SetMetadataList(metaObj);
            }
            AddMetadata dialog = new AddMetadata(metaObj);
            bool? result = await dialog.ShowDialog<bool?>(mMainWin);
            if (result == true) {
                metaObj.SetMetaValue(dialog.KeyText, dialog.ValueText);
                if (metaObj is IDiskImage diskImg) {
                    diskImg.Flush();
                }
                IMetadata.MetaEntry? entry = metaObj.GetMetaEntry(dialog.KeyText);
                if (entry != null) {
                    string? value = metaObj.GetMetaValue(dialog.KeyText, true) ?? dialog.ValueText;
                    mMainWin.AddMetadata(entry, value);
                }
            }
        }
    }
}
