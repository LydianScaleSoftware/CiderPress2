/*
 * Copyright 2023 faddenSoft
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
using System.Diagnostics;

using DiskArc;

namespace cp2_avalonia.Models {
    /// <summary>
    /// One entry in the directory tree hierarchy.
    /// </summary>
    public class DirectoryTreeItem : TreeItemBase<DirectoryTreeItem> {
        /// <summary>
        /// Directory file entry for this item.
        /// </summary>
        public IFileEntry FileEntry { get; set; }

        public DirectoryTreeItem(DirectoryTreeItem? parent, string name, IFileEntry fileEntry)
                : base(parent) {
            Name = name;
            FileEntry = fileEntry;
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
    }
}
