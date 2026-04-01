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
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

using DiskArc;
using static DiskArc.IMetadata;

namespace cp2_avalonia {
    /// <summary>
    /// Edit an existing metadata entry.
    /// </summary>
    public partial class EditMetadata : Window, INotifyPropertyChanged {
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

        public bool CanDelete { get; private set; }

        public bool DoDelete { get; private set; } = false;

        public string KeyText { get; set; } = string.Empty;

        public string ValueText {
            get { return mValueText; }
            set { mValueText = value; OnPropertyChanged(); UpdateControls(); }
        }
        private string mValueText = string.Empty;

        public string DescriptionText { get; private set; } = string.Empty;
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

        public IBrush ValueSyntaxForeground {
            get { return mValueSyntaxForeground; }
            set { mValueSyntaxForeground = value; OnPropertyChanged(); }
        }
        private IBrush mValueSyntaxForeground = GetDefaultLabelBrush();

        private IMetadata mMetaObj;
        private MetaEntry mMetaEntry;


        public EditMetadata() {
            // Parameterless constructor for AXAML previewer.
            mMetaObj = null!;
            mMetaEntry = null!;
            InitializeComponent();
            DataContext = this;
        }

        public EditMetadata(IMetadata metaObj, string key) {
            mMetaObj = metaObj;
            MetaEntry? entry = metaObj.GetMetaEntry(key);
            if (entry == null) {
                Debug.Assert(false);
                throw new ArgumentException("couldn't find MetaEntry");
            }
            mMetaEntry = entry;

            KeyText = key;
            mValueText = metaObj.GetMetaValue(key, false)!;
            DescriptionText = string.IsNullOrEmpty(entry.Description)
                ? "User-defined entry." : entry.Description;
            CanDelete = entry.CanDelete;

            if (!entry.CanEdit) {
                ValueSyntaxText = "This entry can't be edited.";
            } else {
                ValueSyntaxText = entry.ValueSyntax;
            }

            InitializeComponent();
            DataContext = this;

            valueTextBox.IsReadOnly = !entry.CanEdit;
            UpdateControls();
        }

        private void Window_Opened(object? sender, EventArgs e) {
            if (mMetaEntry.CanEdit) {
                valueTextBox.SelectAll();
            }
            valueTextBox.Focus();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) {
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }

        private void DeleteButton_Click(object? sender, RoutedEventArgs e) {
            DoDelete = true;
            Close(true);
        }

        private void UpdateControls() {
            IsValid = mMetaObj?.TestMetaValue(mMetaEntry?.Key ?? string.Empty, ValueText) ?? false;
            ValueSyntaxForeground = IsValid ? GetDefaultLabelBrush() : sErrorLabelBrush;
        }
    }
}
