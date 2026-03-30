/*
 * Copyright 2025 faddenSoft
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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

using DiskArc;

namespace cp2_avalonia {
    /// <summary>
    /// One entry in the directory tree hierarchy.
    /// </summary>
    public class DirectoryTreeItem : INotifyPropertyChanged {
        /// <summary>
        /// Reference to parent node.  Will be null at root of tree.
        /// </summary>
        public DirectoryTreeItem? Parent { get; private set; }

        /// <summary>
        /// Name to show in the GUI.
        /// </summary>
        public string Name {
            get { return mName; }
            set { mName = value; OnPropertyChanged(); }
        }
        private string mName = string.Empty;

        /// <summary>
        /// Directory file entry for this item.
        /// </summary>
        public IFileEntry FileEntry { get; set; }

        /// <summary>
        /// List of child items.
        /// </summary>
        public ObservableCollection<DirectoryTreeItem> Items { get; set; }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Tied to TreeViewItem.IsExpanded.
        /// </summary>
        public bool IsExpanded {
            get { return mIsExpanded; }
            set {
                if (value != mIsExpanded) {
                    Debug.WriteLine("Tree: expanded '" + Name + "'=" + value);
                    mIsExpanded = value;
                    OnPropertyChanged();
                }
            }
        }
        private bool mIsExpanded = true;

        /// <summary>
        /// Tied to TreeViewItem.IsSelected.
        /// </summary>
        public bool IsSelected {
            get { return mIsSelected; }
            set {
                if (value != mIsSelected) {
                    mIsSelected = value;
                    OnPropertyChanged();
                }
                if (value) {
                    PurgeSelectionsExcept(this);
                }
            }
        }
        private bool mIsSelected;

        public DirectoryTreeItem(DirectoryTreeItem? parent, string name, IFileEntry fileEntry) {
            Parent = parent;
            Name = name;
            FileEntry = fileEntry;
            Items = new ObservableCollection<DirectoryTreeItem>();
        }

        public override string ToString() {
            return "[DirectoryTreeItem: name='" + Name + "' entry=" + FileEntry + "]";
        }

        /// <summary>
        /// Recursively finds an item in the directory tree.  Expands the nodes leading to
        /// the found item.
        /// </summary>
        public static DirectoryTreeItem? FindItemByEntry(
                ObservableCollection<DirectoryTreeItem> tvRoot, IFileEntry dirEntry) {
            Debug.Assert(dirEntry.IsDirectory);
            foreach (DirectoryTreeItem treeItem in tvRoot) {
                if (treeItem.FileEntry == dirEntry) {
                    return treeItem;
                }
                DirectoryTreeItem? found = FindItemByEntry(treeItem.Items, dirEntry);
                if (found != null) {
                    treeItem.IsExpanded = true;
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds an item in the directory tree, and marks it as selected.
        /// </summary>
        /// <returns>True if found.</returns>
        public static bool SelectItemByEntry(MainWindow mainWin, IFileEntry dirEntry) {
            ObservableCollection<DirectoryTreeItem> tvRoot = mainWin.DirectoryTreeRoot;
            DirectoryTreeItem? item = FindItemByEntry(tvRoot, dirEntry);
            if (item != null) {
                BringItemIntoView(mainWin.directoryTree, item);
                item.IsSelected = true;
                return true;
            } else {
                return false;
            }
        }

        /// <summary>
        /// Scrolls the TreeView so the specified item is visible.
        /// </summary>
        /// <remarks>TODO: implement proper scroll-into-view using Avalonia APIs.</remarks>
        internal static void BringItemIntoView(TreeView treeView, DirectoryTreeItem item) {
            // TODO: Avalonia does not expose VirtualizingStackPanel / ItemContainerGenerator
            // the same way as WPF.  For now this is a no-op; revisit once the tree is
            // functional and we can test scrolling behaviour.
        }

        /// <summary>
        /// Clears the IsSelected flag from all but one item.
        /// </summary>
        private static void PurgeSelectionsExcept(DirectoryTreeItem keep) {
            DirectoryTreeItem top = keep;
            while (top.Parent != null) {
                top = top.Parent;
            }
            PurgeSelections(top.Items, keep);
        }
        private static void PurgeSelections(ObservableCollection<DirectoryTreeItem> items,
                DirectoryTreeItem keep) {
            foreach (DirectoryTreeItem item in items) {
                Debug.Assert(item != keep || item.IsSelected);
                if (item.IsSelected && item != keep) {
                    item.IsSelected = false;
                }
                PurgeSelections(item.Items, keep);
            }
        }
    }
}
