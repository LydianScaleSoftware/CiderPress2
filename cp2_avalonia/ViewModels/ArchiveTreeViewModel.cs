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
using cp2_avalonia.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace cp2_avalonia.ViewModels;

/// <summary>
/// ViewModel for the archive tree (left panel, top).  Owns the root collection.  Selection is
/// the TreeView's: MainWindow forwards SelectionChanged to
/// <see cref="MainViewModel.OnArchiveTreeSelectionChanged"/>.
/// </summary>
public class ArchiveTreeViewModel : ObservableObject {
    /// <summary>
    /// Root collection for the archive tree control.
    /// </summary>
    public ObservableCollection<ArchiveTreeItem> TreeRoot { get; } = [];
}
