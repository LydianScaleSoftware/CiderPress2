/*
 * Copyright 2022 faddenSoft
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
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using CommonUtil;
using static DiskArc.Defs;

namespace cp2_avalonia.LibTest {
    /// <summary>
    /// Bulk compression test runner.
    /// </summary>
    public partial class BulkCompress : Window, INotifyPropertyChanged {
        // String constants replacing WPF FindResource() pattern.
        private const string STR_RUN_TEST = "Run Test";
        private const string STR_CANCEL_TEST = "Cancel";

        private BackgroundWorker mWorker;
        private CompressionFormat mFormat;
        private AppHook mAppHook;

        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// True when we're not running.  Used to enable the "run test" button.
        /// </summary>
        public bool CanStartRunning {
            get { return mCanStartRunning; }
            set { mCanStartRunning = value; OnPropertyChanged(); }
        }
        private bool mCanStartRunning;

        /// <summary>
        /// Pathname of disk image or file archive to test.
        /// </summary>
        public string PathName {
            get { return mPathName; }
            set { mPathName = value; OnPropertyChanged(); PathNameChanged(); }
        }
        private string mPathName;

        /// <summary>
        /// Current label for Run/Cancel button.
        /// </summary>
        public string RunButtonLabel {
            get { return mRunButtonLabel; }
            set { mRunButtonLabel = value; OnPropertyChanged(); }
        }
        private string mRunButtonLabel = string.Empty;

        /// <summary>
        /// Progress message text.
        /// </summary>
        public string ProgressMsg {
            get { return mProgressMsg; }
            set { mProgressMsg = value; OnPropertyChanged(); }
        }
        private string mProgressMsg = string.Empty;


        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML loader (AVLN3001).
        /// </summary>
        public BulkCompress() {
            mPathName = string.Empty;
            mAppHook = new AppHook(new SimpleMessageLog());
            mWorker = new BackgroundWorker();
            InitializeComponent();
        }

        public BulkCompress(AppHook appHook) {
            InitializeComponent();
            DataContext = this;

            mPathName = string.Empty;
            mAppHook = appHook;

            // Create and configure the BackgroundWorker.
            mWorker = new BackgroundWorker();
            mWorker.WorkerReportsProgress = true;
            mWorker.WorkerSupportsCancellation = true;
            mWorker.DoWork += BackgroundWorker_DoWork;
            mWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            mWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;

            CanStartRunning = false;    // need pathname first
            RunButtonLabel = STR_RUN_TEST;
            radioCompressNuLZW2.IsChecked = true;

            ResetLog();
        }

        /// <summary>
        /// Handles a click on the "choose file" button.  Populates the pathname property.
        /// </summary>
        private async void ChooseFileButton_Click(object sender, RoutedEventArgs e) {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Select File",
                AllowMultiple = false,
            });
            if (files.Count == 0) {
                return;
            }
            string? pathName = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(pathName)) {
                PathName = pathName;
            }
        }

        /// <summary>
        /// Handles a click on the "run test" button, which becomes a "cancel test" button once
        /// the test has started.
        /// </summary>
        private void RunCancelButton_Click(object sender, RoutedEventArgs e) {
            if (mWorker.IsBusy) {
                mWorker.CancelAsync();
            } else {
                ResetLog();
                chooseFileButton.IsEnabled = false;
                RunButtonLabel = STR_CANCEL_TEST;
                mWorker.RunWorkerAsync(mPathName);
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

        private void PathNameChanged() {
            CanStartRunning = !string.IsNullOrEmpty(PathName);
        }

        private void ResetLog() {
            logTextBox.Text = string.Empty;
            ProgressMsg = "Ready";
        }

        private void CompGroup_CheckedChanged(object? sender, RoutedEventArgs e) {
            if (radioCompressSqueeze.IsChecked == true) {
                mFormat = CompressionFormat.Squeeze;
            } else if (radioCompressNuLZW1.IsChecked == true) {
                mFormat = CompressionFormat.NuLZW1;
            } else if (radioCompressNuLZW2.IsChecked == true) {
                mFormat = CompressionFormat.NuLZW2;
            } else if (radioCompressDeflate.IsChecked == true) {
                mFormat = CompressionFormat.Deflate;
            } else if (radioCompressLZC12.IsChecked == true) {
                mFormat = CompressionFormat.LZC12;
            } else if (radioCompressLZC16.IsChecked == true) {
                mFormat = CompressionFormat.LZC16;
            } else if (radioCompressLZHuf.IsChecked == true) {
                mFormat = CompressionFormat.LZHuf;
            } else if (radioCompressZX0.IsChecked == true) {
                mFormat = CompressionFormat.ZX0;
            } else {
                mFormat = CompressionFormat.Uncompressed;
            }
        }

        // NOTE: executes on work thread.  DO NOT do any UI work here.
        private void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs e) {
            BackgroundWorker? worker = sender as BackgroundWorker;
            if (worker == null) {
                throw new Exception("BackgroundWorker WTF?");
            }

            if (e.Argument == null) {
                worker.ReportProgress(0, new ProgressMessage("Pathname was null"));
                return;
            }

            string pathName = (string)e.Argument;
            BulkCompressTest.RunTest(worker, pathName, mFormat, mAppHook);

            if (worker.CancellationPending) {
                e.Cancel = true;
            }
        }

        // Callback that fires when a progress update is made.
        private void BackgroundWorker_ProgressChanged(object? sender, ProgressChangedEventArgs e) {
            ProgressMessage? msg = e.UserState as ProgressMessage;
            if (msg == null) {
                string? str = e.UserState as string;
                if (str != null) {
                    ProgressMsg = str;
                }
            } else {
                // Append text; TextBox has no AppendText() in Avalonia.
                logTextBox.Text += msg.Text;
                // Auto-scroll by moving caret to the end.
                logTextBox.CaretIndex = logTextBox.Text?.Length ?? 0;
            }
        }

        // Callback that fires when execution completes.
        private void BackgroundWorker_RunWorkerCompleted(object? sender,
                RunWorkerCompletedEventArgs e) {
            if (e.Cancelled) {
                ProgressMsg = "Halted (user cancellation)";
            } else if (e.Error != null) {
                ProgressMsg = "Failed";
                Debug.WriteLine("Test harness failed: " + e.Error.ToString());
                logTextBox.Text += "\r\n";
                logTextBox.Text += e.Error.ToString();
                logTextBox.CaretIndex = logTextBox.Text?.Length ?? 0;
            } else {
                ProgressMsg = "Test complete";
            }

            RunButtonLabel = STR_RUN_TEST;
            chooseFileButton.IsEnabled = true;
            CanStartRunning = true;
        }
    }
}
