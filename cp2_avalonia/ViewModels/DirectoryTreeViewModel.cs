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
using System.Collections.ObjectModel;
using DiskArc;
using cp2_avalonia.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace cp2_avalonia.ViewModels;

/// <summary>
/// ViewModel for the directory tree (left panel, bottom).  Owns the root collection and the
/// population/verification logic.  Selection is the TreeView's: MainWindow forwards
/// SelectionChanged to <see cref="MainViewModel.OnDirectoryTreeSelectionChanged"/>.
/// </summary>
public class DirectoryTreeViewModel : ObservableObject {
    /// <summary>
    /// Root collection for the directory tree control.
    /// </summary>
    public ObservableCollection<DirectoryTreeItem> TreeRoot { get; } = [];

    /// <summary>
    /// Recursively populates the directory tree from an IFileSystem root entry.
    /// </summary>
    public static void PopulateTree(DirectoryTreeItem? parent,
            ObservableCollection<DirectoryTreeItem> tvRoot, IFileEntry dirEntry) {
        DirectoryTreeItem newItem = new DirectoryTreeItem(parent, dirEntry.FileName, dirEntry);
        tvRoot.Add(newItem);
        foreach (IFileEntry entry in dirEntry) {
            if (entry.IsDirectory) {
                PopulateTree(newItem, newItem.Items, entry);
            }
        }
    }

    /// <summary>
    /// Verifies that the directory tree matches the file system's actual structure.
    /// Returns false if the tree needs to be repopulated.
    /// </summary>
    public static bool VerifyTree(ObservableCollection<DirectoryTreeItem> tvRoot,
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
                if (!VerifyTree(item.Items, entry, childIndex++)) {
                    return false;
                }
            }
        }
        if (childIndex != item.Items.Count) {
            return false;
        }
        return true;
    }
}
