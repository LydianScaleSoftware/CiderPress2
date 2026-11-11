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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace cp2_avalonia.Models;

/// <summary>
/// Base for the items of the archive tree and the directory tree: a name, a parent, children,
/// and the expansion state the TreeView binds to.  Holds no UI types, so it can be used
/// from the view models.
/// </summary>
/// <remarks>
/// Selection is not kept here: the TreeView's own SelectedItem is the one source of truth
/// (the main window forwards its SelectionChanged to the view model).
/// </remarks>
/// <typeparam name="T">The concrete item type (curiously recurring).</typeparam>
public abstract class TreeItemBase<T> : INotifyPropertyChanged where T : TreeItemBase<T>
{
    /// <summary>
    /// Reference to parent node.  Null at the root of the tree.
    /// </summary>
    public T? Parent { get; }

    /// <summary>
    /// Children.
    /// </summary>
    public ObservableCollection<T> Items { get; } = new();

    /// <summary>
    /// Name to show in the GUI.
    /// </summary>
    public string Name {
        get => mName;
        set {
            mName = value;
            OnPropertyChanged();
        }
    }
    private string mName = string.Empty;

    /// <summary>
    /// Tied to TreeViewItem.IsExpanded.  Items start out expanded.
    /// </summary>
    public bool IsExpanded {
        get => mIsExpanded;
        set {
            if (value != mIsExpanded) {
                Debug.WriteLine("Tree: expanded '" + Name + "'=" + value);
                mIsExpanded = value;
                OnPropertyChanged();
            }
        }
    }
    private bool mIsExpanded = true;

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected TreeItemBase(T? parent)
    {
        Parent = parent;
    }

    /// <summary>
    /// Returns the items from the root of the tree down to this one (inclusive).
    /// </summary>
    public List<T> GetPathFromRoot()
    {
        List<T> path = new List<T>();
        for (T? cur = (T)this; cur != null; cur = cur.Parent) {
            path.Add(cur);
        }
        path.Reverse();
        return path;
    }
}
