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
using System;
using System.Collections.Generic;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Threading;

using cp2_avalonia.Models;

namespace cp2_avalonia.Views;

/// <summary>
/// Finds, scrolls to and focuses the TreeViewItem for a tree item.  Shared by the archive
/// tree and the directory tree.
/// </summary>
internal static class TreeViewHelper
{
    // A TreeViewItem's children get their containers when the item is laid out, so a newly
    // added (or just expanded) node can take a few layout passes to be fully realized.
    private const int MAX_RETRIES = 3;

    /// <summary>
    /// Walks from the root of the TreeView down to the item's container, expanding each
    /// ancestor so that its children are realized.
    /// </summary>
    /// <returns>The container, or null if a container on the path does not exist yet (the
    ///   tree has not been laid out since the item was added or its ancestor expanded).</returns>
    public static TreeViewItem? FindContainer<T>(TreeView tree, T item)
        where T : TreeItemBase<T>
    {
        List<T> path = item.GetPathFromRoot();
        ItemsControl container = tree;
        TreeViewItem? tvi = null;
        for (int i = 0; i < path.Count; i++) {
            tvi = container.ContainerFromItem(path[i]) as TreeViewItem;
            if (tvi == null) {
                return null;
            }
            if (i < path.Count - 1) {
                tvi.IsExpanded = true;      // realize children before descending
            }
            container = tvi;
        }
        return tvi;
    }

    /// <summary>
    /// Scrolls the TreeView so that the item is visible.  If its container does not exist
    /// yet, which is the normal case for an item that was just added to the tree, the walk
    /// is repeated after layout.
    /// </summary>
    /// <param name="done">Called with the container once the item has been scrolled to.</param>
    public static void BringIntoView<T>(TreeView tree, T item, Action<TreeViewItem>? done = null)
        where T : TreeItemBase<T>
    {
        BringIntoView(tree, item, done, MAX_RETRIES);
    }

    private static void BringIntoView<T>(TreeView tree, T item, Action<TreeViewItem>? done,
        int retriesLeft) where T : TreeItemBase<T>
    {
        TreeViewItem? tvi = FindContainer(tree, item);
        if (tvi != null) {
            tvi.BringIntoView();
            done?.Invoke(tvi);
            return;
        }
        if (retriesLeft <= 0) {
            Debug.WriteLine("Tree: gave up scrolling to " + item);
            return;
        }
        // Background is below Layout and Render, so the pending layout pass has run by then.
        Dispatcher.UIThread.Post(() => BringIntoView(tree, item, done, retriesLeft - 1),
            DispatcherPriority.Background);
    }
}
