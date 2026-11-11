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

using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using AppCommon;
using DiskArc;
using DiskArc.Arc;

namespace cp2_avalonia.Models {
    /// <summary>
    /// One entry in the archive tree hierarchy.
    /// </summary>
    public class ArchiveTreeItem : TreeItemBase<ArchiveTreeItem> {
        /// <summary>
        /// Type string to show in the GUI.
        /// </summary>
        public string TypeStr { get; set; }

        public enum Status { None = 0, OK, Dubious, Warning, Error };

        /// <summary>
        /// Status icon shown at left of tree item.  Null when status is OK.
        /// </summary>
        public IImage? StatusIcon { get; set; }

        /// <summary>
        /// Read-only icon shown next to status icon.  Null when read-write.
        /// </summary>
        public IImage? ReadOnlyIcon { get; set; }

        /// <summary>
        /// Reference to corresponding node in the work tree.
        /// </summary>
        public WorkTree.Node WorkTreeNode { get; private set; }

        /// <summary>
        /// True if this item can be closed.  This will be set for disk images and file archives
        /// that are children of IFileSystem or IArchive.
        /// </summary>
        public bool CanClose {
            get {
                if (Parent == null) {
                    return false;
                }
                WorkTree.Node workNode = WorkTreeNode;
                object daObject = workNode.DAObject;
                if (!(daObject is IDiskImage || daObject is IArchive)) {
                    return false;
                }
                WorkTree.Node parentNode = Parent.WorkTreeNode;
                object daParent = parentNode.DAObject;
                if (!(daParent is IFileSystem || daParent is IArchive)) {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public ArchiveTreeItem(ArchiveTreeItem? parent, WorkTree.Node workNode) : base(parent) {
            WorkTreeNode = workNode;

            TypeStr = workNode.TypeStr;
            Name = workNode.Label;

            switch (workNode.NodeStatus) {
                case WorkTree.Node.Status.OK:
                    StatusIcon = null;
                    break;
                case WorkTree.Node.Status.Dubious:
                    StatusIcon = Application.Current!.FindResource("icon_StatusInvalid") as IImage;
                    break;
                case WorkTree.Node.Status.Warning:
                    StatusIcon = Application.Current!.FindResource("icon_StatusWarning") as IImage;
                    break;
                case WorkTree.Node.Status.Error:
                    StatusIcon = Application.Current!.FindResource("icon_StatusError") as IImage;
                    break;
                default:
                    Debug.Assert(false);
                    break;
            }
            if (workNode.IsNodeReadOnly) {
                ReadOnlyIcon =
                    Application.Current!.FindResource("icon_StatusNoNoColor") as IImage;
            }
        }

        /// <summary>
        /// Removes the specified child from the tree.
        /// </summary>
        public bool RemoveChild(ArchiveTreeItem child) {
            return Items.Remove(child);
        }

        /// <summary>
        /// Finds a tree item with a matching file entry object.
        /// </summary>
        public static ArchiveTreeItem? FindItemByEntry(ObservableCollection<ArchiveTreeItem> tvRoot,
                IFileEntry fileEntry) {
            foreach (ArchiveTreeItem treeItem in tvRoot) {
                DiskArcNode? daNode = treeItem.WorkTreeNode.DANode;
                if (daNode != null && daNode.EntryInParent == fileEntry) {
                    return treeItem;
                }
                ArchiveTreeItem? found = FindItemByEntry(treeItem.Items, fileEntry);
                if (found != null) {
                    treeItem.IsExpanded = true;
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Recursively finds a node with a specific DiskArc object (IFileSystem, Partition, etc.).
        /// </summary>
        public static ArchiveTreeItem? FindItemByDAObject(
                ObservableCollection<ArchiveTreeItem> tvRoot, object matchObj) {
            foreach (ArchiveTreeItem treeItem in tvRoot) {
                if (matchObj == treeItem.WorkTreeNode.DAObject) {
                    return treeItem;
                }
                ArchiveTreeItem? found = FindItemByDAObject(treeItem.Items, matchObj);
                if (found != null) {
                    treeItem.IsExpanded = true;
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Constructs the archive tree, starting from the root.
        /// </summary>
        public static ArchiveTreeItem ConstructTree(ObservableCollection<ArchiveTreeItem> tvItems,
                WorkTree.Node workNode) {
            ArchiveTreeItem newItem = new ArchiveTreeItem(null, workNode);
            tvItems.Add(newItem);
            foreach (WorkTree.Node child in workNode) {
                ConstructTree(newItem, child);
            }
            return newItem;
        }

        /// <summary>
        /// Constructs a section of the archive tree.  Called recursively.
        /// </summary>
        public static ArchiveTreeItem ConstructTree(ArchiveTreeItem parent,
                WorkTree.Node workNode) {
            ArchiveTreeItem newItem = new ArchiveTreeItem(parent, workNode);
            parent.Items.Add(newItem);
            foreach (WorkTree.Node child in workNode) {
                ConstructTree(newItem, child);
            }
            return newItem;
        }

        /// <summary>
        /// Finds the "best" thing to select in the specified tree: prefers filesystems and
        /// non-disk archives over raw disk images.  Falls back to the root if nothing
        /// interesting is found.
        /// </summary>
        public static ArchiveTreeItem FindBest(ArchiveTreeItem root) {
            return SelectBestFromR(root) ?? root;
        }

        private static ArchiveTreeItem? SelectBestFromR(ArchiveTreeItem root) {
            foreach (var item in root.Items)
            {
                ArchiveTreeItem? result;
                switch (item.WorkTreeNode.DAObject)
                {
                    case GZip:
                    {
                        break;
                    }
                    case NuFX fx:
                    {
                        IFileEntry firstEntry = fx.GetFirstEntry();
                        if (firstEntry == IFileEntry.NO_ENTRY || !firstEntry.IsDiskImage) {
                            return item;
                        }

                        break;
                    }
                    case IFileSystem:
                    case IArchive:
                        return item;
                }

                if ((result = SelectBestFromR(item)) != null) {
                    return result;
                }
            }
            return null;
        }

        public override string ToString() {
            return "[ArchiveTreeItem: name='" + Name + "' node=" + WorkTreeNode + "]";
        }
    }
}
