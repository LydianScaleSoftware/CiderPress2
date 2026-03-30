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
using Avalonia.Styling;

using DiskArc;

namespace cp2_avalonia {
    /// <summary>
    /// Create directory UI.  Used for archive filesystem directory creation.
    /// </summary>
    public partial class CreateDirectory : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// New directory name, bound to the TextBox.
        /// </summary>
        public string NewFileName {
            get { return mNewFileName; }
            set {
                mNewFileName = value;
                OnPropertyChanged();
                UpdateControls();
            }
        }
        private string mNewFileName;

        /// <summary>
        /// Set to true when input is valid.  Controls OK button enabled state.
        /// </summary>
        public bool IsValid {
            get { return mIsValid; }
            set { mIsValid = value; OnPropertyChanged(); }
        }
        private bool mIsValid;

        public string SyntaxRulesText {
            get { return mSyntaxRulesText; }
            set { mSyntaxRulesText = value; OnPropertyChanged(); }
        }
        private string mSyntaxRulesText;

        /// <summary>
        /// Default label brush, resolved from the current theme.
        /// </summary>
        private static IBrush GetDefaultLabelBrush() {
            if (Application.Current?.TryFindResource("ThemeForegroundBrush",
                    Application.Current.ActualThemeVariant, out var brush) == true
                    && brush is IBrush ib) {
                return ib;
            }
            return Brushes.Black;
        }
        private static readonly IBrush sErrorLabelBrush = Brushes.Red;

        public IBrush SyntaxRulesForeground {
            get { return mSyntaxRulesForeground; }
            set { mSyntaxRulesForeground = value; OnPropertyChanged(); }
        }
        private IBrush mSyntaxRulesForeground = GetDefaultLabelBrush();

        public IBrush UniqueNameForeground {
            get { return mUniqueNameForeground; }
            set { mUniqueNameForeground = value; OnPropertyChanged(); }
        }
        private IBrush mUniqueNameForeground = GetDefaultLabelBrush();

        public delegate bool IsValidDirNameFunc(string name);

        private IFileSystem mFileSystem;
        private IFileEntry mContainingDir;
        private IsValidDirNameFunc mIsValidFunc;

        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader.
        /// </summary>
        public CreateDirectory() {
            InitializeComponent();
            mNewFileName = string.Empty;
            mSyntaxRulesText = string.Empty;
            mFileSystem = null!;
            mContainingDir = DiskArc.IFileEntry.NO_ENTRY;
            mIsValidFunc = _ => false;
            DataContext = this;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="parent">Parent window (owner); pass for modal ShowDialog.</param>
        /// <param name="fs">Filesystem object.</param>
        /// <param name="containingDir">Directory that will contain the new entry.</param>
        /// <param name="func">Function for evaluating filename correctness.</param>
        /// <param name="syntaxRules">Human-readable syntax rules to display.</param>
        /// <remarks>
        /// In Avalonia 11, Window.Owner has no public setter; ownership is always established
        /// by ShowDialog&lt;T&gt;(owner).  Do not set Owner here.
        /// </remarks>
        public CreateDirectory(Window parent, IFileSystem fs, IFileEntry containingDir,
                IsValidDirNameFunc func, string syntaxRules) {
            mFileSystem = fs;
            mContainingDir = containingDir;
            mIsValidFunc = func;
            mSyntaxRulesText = syntaxRules;
            mNewFileName = "NEW.DIR";

            InitializeComponent();
            DataContext = this;

            UpdateControls();
        }

        /// <summary>
        /// When window opens, focus the text box with all text selected.
        /// </summary>
        private void Window_Opened(object? sender, EventArgs e) {
            newFileNameTextBox.SelectAll();
            newFileNameTextBox.Focus();
        }

        /// <summary>
        /// Validates the current filename and updates control states accordingly.
        /// </summary>
        private void UpdateControls() {
            IBrush defaultBrush = GetDefaultLabelBrush();
            bool nameOkay = mIsValidFunc(mNewFileName);
            SyntaxRulesForeground = nameOkay ? defaultBrush : sErrorLabelBrush;

            bool exists = mFileSystem.TryFindFileEntry(mContainingDir, mNewFileName,
                out IFileEntry _);
            UniqueNameForeground = exists ? sErrorLabelBrush : defaultBrush;

            IsValid = nameOkay && !exists;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) {
            Close(true);
        }
    }
}
