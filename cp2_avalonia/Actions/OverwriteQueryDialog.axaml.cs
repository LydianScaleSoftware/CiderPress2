/*
 * Copyright 2024 faddenSoft
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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AppCommon;
using CommonUtil;

namespace cp2_avalonia.Actions {
    /// <summary>
    /// File overwrite confirmation dialog.
    /// </summary>
    public partial class OverwriteQueryDialog : Window, INotifyPropertyChanged {
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// User selection.  Will be Overwrite or Skip.  The caller sets Cancel if the
        /// dialog was cancelled (Close() without a result).
        /// </summary>
        public CallbackFacts.Results Result { get; private set; }

        private bool mUseForAll;
        public bool UseForAll {
            get => mUseForAll;
            set { mUseForAll = value; OnPropertyChanged(); }
        }

        public string NewFileName { get; }
        public string NewDirName { get; }
        public string NewModWhen { get; }
        public string ExistFileName { get; }
        public string ExistDirName { get; }
        public string ExistModWhen { get; }

        public OverwriteQueryDialog() {
            // Parameterless constructor required for AXAML previewer.
            NewFileName = NewDirName = NewModWhen = string.Empty;
            ExistFileName = ExistDirName = ExistModWhen = string.Empty;
            InitializeComponent();
            DataContext = this;
        }

        public OverwriteQueryDialog(CallbackFacts facts) {
            NewFileName = PathName.GetFileName(facts.NewPathName, facts.NewDirSep);
            string ndn = PathName.GetDirectoryName(facts.NewPathName, facts.NewDirSep);
            NewDirName = string.IsNullOrEmpty(ndn) ? string.Empty : "Directory: " + ndn;
            NewModWhen = TimeStamp.IsValidDate(facts.NewModWhen)
                ? "Modified: " + facts.NewModWhen.ToString()
                : "Modified: (unknown)";

            ExistFileName = PathName.GetFileName(facts.OrigPathName, facts.OrigDirSep);
            string edn = PathName.GetDirectoryName(facts.OrigPathName, facts.OrigDirSep);
            ExistDirName = string.IsNullOrEmpty(edn) ? string.Empty : "Directory: " + edn;
            ExistModWhen = TimeStamp.IsValidDate(facts.OrigModWhen)
                ? "Modified: " + facts.OrigModWhen.ToString()
                : "Modified: (unknown)";

            InitializeComponent();
            DataContext = this;
        }

        private void Replace_Click(object? sender, RoutedEventArgs e) {
            Result = CallbackFacts.Results.Overwrite;
            Close(true);
        }

        private void Skip_Click(object? sender, RoutedEventArgs e) {
            Result = CallbackFacts.Results.Skip;
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }
    }
}
