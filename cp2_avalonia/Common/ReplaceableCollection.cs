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
using System.Collections.Specialized;
using System.ComponentModel;

namespace cp2_avalonia.Common;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> whose whole contents can be replaced with a single
/// change notification.  A bound DataGrid then processes one reset instead of one change per
/// item, which matters for the file list (tens of thousands of rows in full-list mode).
/// </summary>
public class ReplaceableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces every item with <paramref name="items"/>, raising one
    /// <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        // Copy first, in case the source is derived from this collection.
        List<T> copy = new List<T>(items);
        CheckReentrancy();
        Items.Clear();
        foreach (T item in copy) {
            Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
