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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using CommonUtil;

namespace cp2_avalonia.Tools {
    /// <summary>
    /// Drop / paste target window, for testing clipboard and drag-drop.
    /// This is a modeless dialog: no Owner, ShowInTaskbar=True, closes via Close().
    /// </summary>
    public partial class DropTarget : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string TextArea {
            get { return mTextArea; }
            set { mTextArea = value; OnPropertyChanged(); }
        }
        private string mTextArea = "Drag or paste stuff here.\n";

        private Formatter mFormatter;

        // Parameterless constructor required for Avalonia previewer (AVLN3001).
        public DropTarget() {
            mFormatter = new Formatter(new Formatter.FormatConfig());
            InitializeComponent();
            DataContext = this;

            // Register TextBox drop handler after InitializeComponent.
            var textBox = this.FindControl<TextBox>("textArea");
            if (textBox != null) {
                textBox.AddHandler(DragDrop.DropEvent, TextArea_Drop);
                textBox.AddHandler(DragDrop.DragOverEvent, TextArea_DragOver);
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) {
            Close();
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control) {
                e.Handled = true;
                _ = DoPasteAsync();
            }
        }

        private void PasteButton_Click(object? sender, RoutedEventArgs e) {
            _ = DoPasteAsync();
        }

        private async System.Threading.Tasks.Task DoPasteAsync() {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) {
                TextArea = "(No clipboard available)\n";
                return;
            }
            string? text = await clipboard.GetTextAsync();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Clipboard paste ===");
            if (text == null) {
                sb.AppendLine("(no text on clipboard)");
            } else {
                sb.AppendLine("Text length: " + text.Length + " chars");
                if (ClipInfo.IsClipTextFromCP2(text)) {
                    sb.AppendLine("Recognized as CiderPress II clipboard data:");
                    DumpClipInfoText(text, sb);
                } else {
                    // Show a truncated preview.
                    string preview = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
                    sb.AppendLine(preview);
                }
            }
            TextArea = sb.ToString();
        }

        private void TextArea_DragOver(object? sender, DragEventArgs e) {
            // Accept any drop so the TextBox doesn't block the event.
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void TextArea_Drop(object? sender, DragEventArgs e) {
            ShowDataObject(e.Data);
        }

        /// <summary>
        /// Dumps the contents of a data object to the text area.
        /// </summary>
        private void ShowDataObject(IDataObject dataObj) {
            StringBuilder sb = new StringBuilder();

            IEnumerable<string> formats = dataObj.GetDataFormats();
            var formatList = new List<string>(formats);

            sb.AppendLine("Found " + formatList.Count + " format(s):");
            foreach (string format in formatList) {
                sb.Append("\u2022 ");
                sb.AppendLine(format);
            }

            // Check for CiderPress II clipboard text.
            if (dataObj.Contains(DataFormats.Text)) {
                string? text = dataObj.Get(DataFormats.Text) as string;
                if (ClipInfo.IsClipTextFromCP2(text)) {
                    sb.AppendLine();
                    sb.AppendLine("=== CiderPress II clipboard data ===");
                    DumpClipInfoText(text, sb);
                } else if (text != null) {
                    sb.AppendLine();
                    sb.AppendLine("Text content preview:");
                    string preview = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
                    sb.AppendLine(preview);
                }
            }

            // List files if present.
            if (dataObj.Contains(DataFormats.Files)) {
                var files = dataObj.GetFiles();
                if (files != null) {
                    sb.AppendLine();
                    sb.AppendLine("=== Files ===");
                    foreach (var item in files) {
                        string? path = item.TryGetLocalPath();
                        sb.AppendLine("  " + (path ?? item.Name));
                    }
                }
            }

            TextArea = sb.ToString();
        }

        /// <summary>
        /// Parses a CiderPress II clip text string and shows the ClipFileEntry list.
        /// </summary>
        private static void DumpClipInfoText(string? clipText, StringBuilder sb) {
            ClipInfo? clipInfo = ClipInfo.FromClipString(clipText);
            if (clipInfo == null) {
                sb.AppendLine("  ERROR: failed to deserialize ClipInfo");
                return;
            }
            sb.AppendLine("  ProcessId=" + clipInfo.ProcessId + " IsExport=" + clipInfo.IsExport);
            sb.AppendLine("  Version=" + clipInfo.AppVersionMajor + "." +
                clipInfo.AppVersionMinor + "." + clipInfo.AppVersionPatch);
            if (clipInfo.ClipEntries == null) {
                sb.AppendLine("  (no ClipEntries)");
            } else {
                sb.AppendLine("  ClipEntries.Count=" + clipInfo.ClipEntries.Count);
                for (int i = 0; i < clipInfo.ClipEntries.Count; i++) {
                    AppCommon.ClipFileEntry entry = clipInfo.ClipEntries[i];
                    sb.AppendLine("  [" + i + "] '" + entry.Attribs.FullPathName +
                        "' part=" + entry.Part + " len=" + entry.OutputLength);
                }
            }
        }
    }
}
