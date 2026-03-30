/*
 * Copyright 2019 faddenSoft
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
using Avalonia.Input;
using Avalonia.Controls;

namespace cp2_avalonia.Tools {
    /// <summary>
    /// Simple text display dialog.  Can be modal or modeless.
    /// </summary>
    public partial class ShowText : Window, INotifyPropertyChanged {
        /// <summary>
        /// Text to display in the window.  May be updated at any time.
        /// </summary>
        public string DisplayText {
            get { return mDisplayText; }
            set {
                mDisplayText = value;
                OnPropertyChanged();
            }
        }
        private string mDisplayText;

        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader.
        /// </summary>
        public ShowText() {
            InitializeComponent();
            mDisplayText = string.Empty;
            DataContext = this;
        }

        /// <summary>
        /// Constructor.  Pass in an owner for modal dialogs, or null for modeless.
        /// </summary>
        /// <remarks>
        /// For modal use: await dialog.ShowDialog(ownerWindow).
        /// For modeless use: dialog.Show() — ShowInTaskbar is set to true automatically
        /// when owner is null.
        /// Note: In Avalonia 11, Window.Owner has no public setter; ownership is established
        /// by ShowDialog(owner) or Show(owner).
        /// </remarks>
        public ShowText(Window? owner, string initialText) {
            mDisplayText = initialText;

            InitializeComponent();
            DataContext = this;

            if (owner == null) {
                // Modeless dialogs can get lost, so show them in the task bar.
                ShowInTaskbar = true;
            }
        }

        /// <summary>
        /// Catch ESC key to close the window.
        /// </summary>
        private void Window_KeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                Close();
                e.Handled = true;
            }
        }
    }
}
