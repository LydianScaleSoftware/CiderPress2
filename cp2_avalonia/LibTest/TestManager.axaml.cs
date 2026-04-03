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
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace cp2_avalonia.LibTest {
    /// <summary>
    /// Executes the DiskArc/FileConv library tests.  The test code is loaded dynamically.
    /// </summary>
    public partial class TestManager : Window, INotifyPropertyChanged {
        // String constants replacing WPF FindResource() pattern.
        private const string STR_RUN_TEST = "Run Test";
        private const string STR_CANCEL_TEST = "Cancel";

        // Full set of results returned by previous test run.
        private List<TestRunner.TestResult> mLastResults;

        private BackgroundWorker mWorker;
        private string mTestLibName;
        private string mTestIfaceName;

        // AvaloniaEdit colored-text support: transformer holds (start, length, color) spans.
        // Thread-safety note (G-01): document mutations must happen on the UI thread.
        // ProgressChanged fires on the UI thread, so appends there are safe.
        private ProgressTextTransformer mProgressTransformer = new ProgressTextTransformer();

        // Default text color for un-colored messages.
        private static readonly Color sDefaultColor = Colors.Black;

        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// True when we're not running.  Used to enable the "run test" button.
        /// </summary>
        public bool IsNotRunning {
            get { return mIsNotRunning; }
            set { mIsNotRunning = value; OnPropertyChanged(); }
        }
        private bool mIsNotRunning;

        public bool IsOutputRetained {
            get { return mIsOutputRetained; }
            set { mIsOutputRetained = value; OnPropertyChanged(); }
        }
        private bool mIsOutputRetained;

        public string RunButtonLabel {
            get { return mRunButtonLabel; }
            set { mRunButtonLabel = value; OnPropertyChanged(); }
        }
        private string mRunButtonLabel = string.Empty;

        /// <summary>
        /// Per-test results for the output ComboBox.  Populated after tests complete.
        /// </summary>
        public ObservableCollection<TestRunner.TestResult> OutputItems { get; } =
            new ObservableCollection<TestRunner.TestResult>();

        /// <summary>
        /// True when there are failure results to browse.  Enables the ComboBox.
        /// </summary>
        public bool IsOutputSelectEnabled {
            get { return mIsOutputSelectEnabled; }
            set { mIsOutputSelectEnabled = value; OnPropertyChanged(); }
        }
        private bool mIsOutputSelectEnabled;

        private const string STR_NO_RESULTS =
            "(No test results yet. Run the tests first.\r\n" +
            "If all tests pass, no results will appear here.\r\n" +
            "Failures will be listed in the drop-down above.)";

        private const string STR_ALL_PASSED =
            "All tests passed. No failures to report.";


        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader (AVLN3001).
        /// </summary>
        public TestManager() {
            mTestLibName = string.Empty;
            mTestIfaceName = string.Empty;
            mLastResults = new List<TestRunner.TestResult>(0);
            mWorker = new BackgroundWorker();
            InitializeComponent();
        }

        public TestManager(string testLibName, string testIfaceName) {
            InitializeComponent();
            DataContext = this;

            mTestLibName = testLibName;
            mTestIfaceName = testIfaceName;

            mLastResults = new List<TestRunner.TestResult>(0);

            // Configure the AvaloniaEdit transformer for colored progress output.
            progressTextEditor.TextArea.TextView.LineTransformers.Add(mProgressTransformer);

            // Create and configure the BackgroundWorker.
            mWorker = new BackgroundWorker();
            mWorker.WorkerReportsProgress = true;
            mWorker.WorkerSupportsCancellation = true;
            mWorker.DoWork += BackgroundWorker_DoWork;
            mWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            mWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;

            IsNotRunning = true;
            RunButtonLabel = STR_RUN_TEST;
            IsOutputSelectEnabled = false;
            outputTextBox.Text = STR_NO_RESULTS;
        }

        /// <summary>
        /// Handles a click on the "run test" button, which becomes a "cancel test" button once
        /// the test has started.
        /// </summary>
        private void RunCancelButton_Click(object sender, RoutedEventArgs e) {
            if (mWorker.IsBusy) {
                IsNotRunning = false;
                mWorker.CancelAsync();
            } else {
                ResetDialog();
                RunButtonLabel = STR_CANCEL_TEST;
                mWorker.RunWorkerAsync(mIsOutputRetained);
            }
        }

        /// <summary>
        /// Cancels the test if the user closes the window.
        /// </summary>
        private void CloseButton_Click(object? sender, RoutedEventArgs e) {
            Close();
        }

        private void Window_Closing(object sender, Avalonia.Controls.WindowClosingEventArgs e) {
            if (mWorker.IsBusy) {
                mWorker.CancelAsync();
            }
        }

        private void ResetDialog() {
            // Clear the AvaloniaEdit text area and color spans.
            progressTextEditor.Document.Text = string.Empty;
            mProgressTransformer.Clear();
            mLastResults.Clear();
            OutputItems.Clear();
            IsOutputSelectEnabled = false;
            outputTextBox.Text = STR_NO_RESULTS;
        }

        // NOTE: executes on work thread.  DO NOT do any UI work here.
        private void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs e) {
            BackgroundWorker? worker = sender as BackgroundWorker;
            if (worker == null) {
                throw new Exception("BackgroundWorker WTF?");
            }

            TestRunner test = new TestRunner();
            if (e.Argument != null) {
                test.RetainOutput = (bool)e.Argument;
            }
            List<TestRunner.TestResult> results = test.Run(worker, mTestLibName, mTestIfaceName);

            if (worker.CancellationPending) {
                e.Cancel = true;
            } else {
                e.Result = results;
            }
        }

        // Callback that fires when a progress update is made.
        // Thread-safety note (G-01): ProgressChanged fires on the UI thread; document
        // mutations here are safe.
        private void BackgroundWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e) {
            ProgressMessage? msg = e.UserState as ProgressMessage;
            if (msg == null) {
                string? str = e.UserState as string;
                if (!string.IsNullOrEmpty(str)) {
                    Debug.WriteLine("Sub-progress: " + e.UserState);
                }
            } else {
                Color color = msg.HasColor ? msg.Color : sDefaultColor;
                AppendColoredText(msg.Text, color);
            }
        }

        // Callback that fires when execution completes.
        private void BackgroundWorker_RunWorkerCompleted(object? sender,
                RunWorkerCompletedEventArgs e) {
            if (e.Cancelled) {
                Debug.WriteLine("Test halted -- user cancellation");
            } else if (e.Error != null) {
                Debug.WriteLine("Test harness failed: " + e.Error.ToString());
                AppendColoredText("\r\n", sDefaultColor);
                AppendColoredText(e.Error.ToString(), sDefaultColor);
            } else {
                Debug.WriteLine("Tests complete");
                List<TestRunner.TestResult>? results = e.Result as List<TestRunner.TestResult>;
                if (results != null) {
                    mLastResults = results;
                    PopulateOutputSelect();
                }
            }

            RunButtonLabel = STR_RUN_TEST;
            IsNotRunning = true;
        }

        /// <summary>
        /// Appends colored text to the AvaloniaEdit progress editor.
        /// Adds a color span and auto-scrolls to the end.
        /// </summary>
        private void AppendColoredText(string text, Color color) {
            int startOffset = progressTextEditor.Document.TextLength;
            progressTextEditor.Document.Insert(startOffset, text);
            if (text.Length > 0) {
                mProgressTransformer.AddSpan(startOffset, text.Length, color);
            }
            // Auto-scroll: move caret to end and scroll to it.
            progressTextEditor.CaretOffset = progressTextEditor.Document.TextLength;
            progressTextEditor.ScrollTo(
                progressTextEditor.TextArea.Caret.Line,
                progressTextEditor.TextArea.Caret.Column);
        }

        private void PopulateOutputSelect() {
            OutputItems.Clear();
            if (mLastResults.Count == 0) {
                IsOutputSelectEnabled = false;
                outputTextBox.Text = STR_ALL_PASSED;
                return;
            }
            foreach (TestRunner.TestResult result in mLastResults) {
                OutputItems.Add(result);
            }
            IsOutputSelectEnabled = true;
            // Trigger update — fires SelectedIndexChanged which populates the text box.
            outputSelectComboBox.SelectedIndex = 0;
        }

        private void OutputSelectComboBox_SelectedIndexChanged(object? sender,
                Avalonia.Controls.SelectionChangedEventArgs e) {
            int sel = outputSelectComboBox.SelectedIndex;
            if (sel < 0) {
                // selection has been cleared
                outputTextBox.Text = STR_NO_RESULTS;
                return;
            }
            if (mLastResults == null || mLastResults.Count <= sel) {
                Debug.WriteLine("SelIndexChanged to " + sel + ", not available");
                return;
            }

            TestRunner.TestResult results = mLastResults[sel];

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(results.Name);
            Exception? ex = results.Exc;
            bool first = true;
            while (ex != null) {
                sb.AppendLine();
                if (first) {
                    first = false;
                } else {
                    sb.Append("Caused by: ");
                }
                sb.AppendLine(ex.Message);
                sb.AppendLine(ex.GetType().Name + ":");
                sb.AppendLine(ex.StackTrace);
                ex = ex.InnerException;
            }

            outputTextBox.Text = sb.ToString();
        }
    }


    /// <summary>
    /// AvaloniaEdit DocumentColorizingTransformer that applies flat (start, length, color) spans
    /// to the text as colored runs.  Used for the test runner progress output.
    /// </summary>
    internal class ProgressTextTransformer : DocumentColorizingTransformer {
        private record struct ColorSpan(int Start, int Length, Color Color);
        private readonly List<ColorSpan> mSpans = new List<ColorSpan>();

        public void AddSpan(int start, int length, Color color) {
            mSpans.Add(new ColorSpan(start, length, color));
        }

        public void Clear() {
            mSpans.Clear();
        }

        protected override void ColorizeLine(DocumentLine line) {
            int lineStart = line.Offset;
            int lineEnd = line.EndOffset;      // exclusive, excludes line terminator

            foreach (var span in mSpans) {
                int spanEnd = span.Start + span.Length;
                if (spanEnd <= lineStart || span.Start >= lineEnd) {
                    continue;   // no overlap with this line
                }
                int overlapStart = Math.Max(span.Start, lineStart);
                int overlapEnd = Math.Min(spanEnd, lineEnd);
                if (overlapStart < overlapEnd) {
                    Color c = span.Color;
                    ChangeLinePart(overlapStart, overlapEnd, element => {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(c));
                    });
                }
            }
        }
    }
}
