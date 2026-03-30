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
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using CommonUtil;

namespace cp2_avalonia.Tools {
    /// <summary>
    /// Debug log viewer.  Always modeless.
    /// </summary>
    public partial class LogViewer : Window {
        /// <summary>
        /// Log entry collection, bound to the ListBox.
        /// </summary>
        public ObservableCollection<LogEntry> LogEntries { get; set; }

        /// <summary>
        /// True if we are auto-scrolling to keep the bottom visible.
        /// </summary>
        private bool mAutoScroll = true;

        private DebugMessageLog mLog;

        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader.
        /// </summary>
        public LogViewer() {
            LogEntries = new ObservableCollection<LogEntry>();
            mLog = null!;

            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="log">Log to display.</param>
        public LogViewer(DebugMessageLog log) {
            LogEntries = new ObservableCollection<LogEntry>();

            InitializeComponent();
            DataContext = this;

            // Always modeless — show in taskbar so it's not lost.
            ShowInTaskbar = true;

            mLog = log;
            mLog.RaiseLogEvent += HandleLogEvent;

            // Pull all stored logs in.
            List<DebugMessageLog.LogEntry> logs = mLog.GetLogs();
            foreach (DebugMessageLog.LogEntry entry in logs) {
                LogEntries.Add(new LogEntry(entry));
            }

            // Wire scroll-changed event.  The ScrollChangedEvent bubbles from the ListBox's
            // internal ScrollViewer, so we can use AddHandler on the ListBox.
            logListBox.AddHandler(ScrollViewer.ScrollChangedEvent, ScrollViewer_ScrollChanged);
        }

        /// <summary>
        /// Unsubscribes from the log event when the window closes.
        /// </summary>
        private void Window_Closed(object? sender, EventArgs e) {
            mLog.RaiseLogEvent -= HandleLogEvent;
        }

        /// <summary>
        /// Handles the arrival of a new log message.
        /// </summary>
        private void HandleLogEvent(object? sender, DebugMessageLog.LogEventArgs e) {
            LogEntries.Add(new LogEntry(e.Entry));
        }

        /// <summary>
        /// Handles scroll events.  Engages or disengages auto-scroll based on position.
        /// </summary>
        private void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e) {
            if (sender is not ScrollViewer sv) {
                return;
            }
            if (e.ExtentDelta.Y == 0) {
                // User-initiated scroll: engage if at bottom, disengage otherwise.
                bool atBottom = sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 1;
                mAutoScroll = atBottom;
            } else {
                // Content changed: scroll to bottom if auto-scroll is engaged.
                if (mAutoScroll) {
                    sv.ScrollToEnd();
                }
            }
        }

        private async void SaveLog_Click(object? sender, RoutedEventArgs e) {
            var topLevel = TopLevel.GetTopLevel(this);
            var file = await topLevel!.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions {
                    Title = "Save Debug Log...",
                    SuggestedFileName = "cp2-log.txt",
                    FileTypeChoices = new[] {
                        new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });
            if (file == null) {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            StringBuilder sb = new StringBuilder(128);
            foreach (LogEntry entry in LogEntries) {
                sb.Clear();
                sb.Append(entry.When.ToString(@"hh\:mm\:ss\.fff"));
                sb.Append(' ');
                sb.Append(entry.Priority);
                sb.Append(' ');
                sb.AppendLine(entry.Message);
                writer.Write(sb.ToString());
            }
        }
    }

    /// <summary>
    /// Wrapper for DebugMessageLog.LogEntry that is visible to AXAML.
    /// </summary>
    public class LogEntry {
        private static readonly string[] sSingleLetter = { "V", "D", "I", "W", "E", "S" };

        public int Index { get; private set; }
        public DateTime When { get; private set; }
        public string Priority { get; private set; }
        public string Message { get; private set; }

        public LogEntry(DebugMessageLog.LogEntry entry) {
            Index = entry.Index;
            When = entry.When;
            Priority = sSingleLetter[(int)entry.Priority];
            Message = entry.Message;
        }
    }
}
