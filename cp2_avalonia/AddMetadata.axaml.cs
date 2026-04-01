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
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

using DiskArc;

namespace cp2_avalonia {
    /// <summary>
    /// Add a new metadata entry.
    /// </summary>
    public partial class AddMetadata : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public bool IsValid {
            get { return mIsValid; }
            set { mIsValid = value; OnPropertyChanged(); }
        }
        private bool mIsValid;

        public string KeyText {
            get { return mKeyText; }
            set { mKeyText = value; OnPropertyChanged(); UpdateControls(); }
        }
        private string mKeyText = string.Empty;

        public string ValueText {
            get { return mValueText; }
            set { mValueText = value; OnPropertyChanged(); UpdateControls(); }
        }
        private string mValueText = string.Empty;

        public string KeySyntaxText { get; private set; } = string.Empty;
        public string ValueSyntaxText { get; private set; } = string.Empty;

        private static IBrush GetDefaultLabelBrush() {
            if (Application.Current?.TryFindResource("ThemeForegroundBrush",
                    Application.Current.ActualThemeVariant, out var brush) == true
                    && brush is IBrush ib) {
                return ib;
            }
            return Brushes.Black;
        }
        private static readonly IBrush sErrorLabelBrush = Brushes.Red;

        public IBrush KeySyntaxForeground {
            get { return mKeySyntaxForeground; }
            set { mKeySyntaxForeground = value; OnPropertyChanged(); }
        }
        private IBrush mKeySyntaxForeground = GetDefaultLabelBrush();

        public IBrush ValueSyntaxForeground {
            get { return mValueSyntaxForeground; }
            set { mValueSyntaxForeground = value; OnPropertyChanged(); }
        }
        private IBrush mValueSyntaxForeground = GetDefaultLabelBrush();

        public bool IsNonUniqueVisible {
            get { return mIsNonUniqueVisible; }
            set { mIsNonUniqueVisible = value; OnPropertyChanged(); }
        }
        private bool mIsNonUniqueVisible;

        private IMetadata mMetaObj;


        public AddMetadata() {
            // Parameterless constructor for AXAML previewer.
            mMetaObj = null!;
            InitializeComponent();
            DataContext = this;
        }

        public AddMetadata(IMetadata metaObj) {
            mMetaObj = metaObj;
            mKeyText = "meta:new_key";
            KeySyntaxText =
                "Keys are comprised of ASCII letters, numbers, and the underscore ('_')." +
                Environment.NewLine +
                "WOZ metadata keys are prefixed with \"meta:\".";
            ValueSyntaxText = "WOZ values may have any characters except linefeed and tab.";

            InitializeComponent();
            DataContext = this;

            UpdateControls();
        }

        private void Window_Opened(object? sender, EventArgs e) {
            keyTextBox.SelectionStart = 5;
            keyTextBox.SelectionEnd = mKeyText.Length;
            keyTextBox.Focus();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) {
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }

        private void UpdateControls() {
            bool isOkay = mMetaObj?.TestMetaValue(KeyText, ValueText) ?? false;
            bool isUnique = mMetaObj?.GetMetaValue(KeyText, false) == null;
            IsValid = isOkay && isUnique;
            KeySyntaxForeground = isOkay ? GetDefaultLabelBrush() : sErrorLabelBrush;
            IsNonUniqueVisible = !isUnique;
        }
    }
}
