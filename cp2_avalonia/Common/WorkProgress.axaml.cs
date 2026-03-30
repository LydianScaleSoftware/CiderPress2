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
using System.Diagnostics;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AppCommon;
using cp2_avalonia.Actions;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Cancellable progress dialog.  The dialog returns true if the operation ran to
    /// completion, false if it was cancelled or halted early with an error.
    /// </summary>
    public partial class WorkProgress : Window {
        /// <summary>
        /// Task-specific callbacks.
        /// </summary>
        public interface IWorker {
            /// <summary>
            /// Does the work, executing on a work thread.
            /// </summary>
            object DoWork(BackgroundWorker worker);

            /// <summary>
            /// Called on successful completion of the work.  Executes on the main thread.
            /// </summary>
            /// <returns>Value to return from dialog (usually true on success).</returns>
            bool RunWorkerCompleted(object? results);
        }

        /// <summary>
        /// Message box query, sent from a non-GUI thread via the progress update mechanism.
        /// Uses Monitor.Wait/Pulse for cross-thread synchronization.
        /// </summary>
        public class MessageBoxQuery {
            private const MBResult RESULT_UNSET = (MBResult)(-1000);

            public string Text { get; private set; }
            public string Caption { get; private set; }
            public MBButton Button { get; private set; }
            public MBIcon Image { get; private set; }

            private MBResult mResult = RESULT_UNSET;
            private readonly object mLockObj = new object();

            public MessageBoxQuery(string text, string caption, MBButton button, MBIcon image) {
                Text = text;
                Caption = caption;
                Button = button;
                Image = image;
            }

            /// <summary>
            /// Waits for a result from the GUI thread.  Call this from the worker thread
            /// after ReportProgress().
            /// </summary>
            public MBResult WaitForResult() {
                lock (mLockObj) {
                    while (mResult == RESULT_UNSET) {
                        Monitor.Wait(mLockObj);
                    }
                    return mResult;
                }
            }

            /// <summary>
            /// Sets the result and signals the waiting worker thread.  Call from the GUI thread.
            /// </summary>
            public void SetResult(MBResult value) {
                lock (mLockObj) {
                    mResult = value;
                    Monitor.Pulse(mLockObj);
                }
            }
        }

        /// <summary>
        /// File overwrite query, sent from a non-GUI thread via the progress update mechanism.
        /// </summary>
        public class OverwriteQuery {
            private CallbackFacts.Results mResult = CallbackFacts.Results.Unknown;
            private bool mUseForAll = false;
            private readonly object mLockObj = new object();

            public CallbackFacts Facts { get; private set; }

            public OverwriteQuery(CallbackFacts what) {
                Facts = what;
            }

            /// <summary>
            /// Waits for a result from the GUI thread.
            /// </summary>
            public CallbackFacts.Results WaitForResult(out bool useForAll) {
                lock (mLockObj) {
                    while (mResult == CallbackFacts.Results.Unknown) {
                        Monitor.Wait(mLockObj);
                    }
                    useForAll = mUseForAll;
                    return mResult;
                }
            }

            /// <summary>
            /// Sets the result and signals the waiting worker thread.
            /// </summary>
            public void SetResult(CallbackFacts.Results value, bool useForAll) {
                lock (mLockObj) {
                    mResult = value;
                    mUseForAll = useForAll;
                    Monitor.Pulse(mLockObj);
                }
            }
        }

        private IWorker? mCallbacks;
        private BackgroundWorker? mWorker;
        private bool mDialogResult = false;

        /// <summary>
        /// Result of the operation: true if completed successfully, false if cancelled or failed.
        /// Read this after ShowDialog() returns.
        /// </summary>
        public bool DialogResult => mDialogResult;

        public WorkProgress() {
            InitializeComponent();
        }

        public WorkProgress(Window owner, IWorker callbacks, bool isIndeterminate) {
            InitializeComponent();
            Owner = owner;

            progressBar.IsIndeterminate = isIndeterminate;

            mCallbacks = callbacks;

            mWorker = new BackgroundWorker();
            mWorker.WorkerReportsProgress = true;
            mWorker.WorkerSupportsCancellation = true;
            mWorker.DoWork += DoWork;
            mWorker.ProgressChanged += ProgressChanged;
            mWorker.RunWorkerCompleted += RunWorkerCompleted;
        }

        private void Window_Loaded(object? sender, RoutedEventArgs e) {
            mWorker!.RunWorkerAsync();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            mWorker!.CancelAsync();
            cancelButton.IsEnabled = false;
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e) {
            if (mWorker!.IsBusy) {
                Debug.WriteLine("Close requested, issuing cancel");
                mWorker.CancelAsync();
                e.Cancel = true;
            }
        }

        // NOTE: executes on the work thread.  Do not access GUI objects here.
        private void DoWork(object? sender, DoWorkEventArgs e) {
            Debug.Assert(sender == mWorker);
            object results = mCallbacks!.DoWork(mWorker!);
            if (mWorker!.CancellationPending) {
                e.Cancel = true;
            } else {
                e.Result = results;
            }
        }

        /// <summary>
        /// Executes when a progress update is sent from the work thread.  The update can be
        /// a MessageBoxQuery, OverwriteQuery, or a plain string/percentage update.
        /// </summary>
        private async void ProgressChanged(object? sender, ProgressChangedEventArgs e) {
            // Handle OverwriteQuery — show the overwrite dialog and wait for user choice.
            if (e.UserState is OverwriteQuery oq) {
                var dialog = new OverwriteQueryDialog(oq.Facts);
                bool? result = await dialog.ShowDialog<bool?>(this);
                if (result == true) {
                    oq.SetResult(dialog.Result, dialog.UseForAll);
                } else {
                    oq.SetResult(CallbackFacts.Results.Cancel, false);
                }
                return;
            }

            // Handle MessageBoxQuery — show a simple modal message box.
            if (e.UserState is MessageBoxQuery qq) {
                var msgWin = new Window {
                    Title = qq.Caption,
                    Width = 400,
                    SizeToContent = SizeToContent.Height,
                    CanResize = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new Avalonia.Controls.StackPanel {
                        Margin = new Avalonia.Thickness(16),
                        Spacing = 12,
                        Children = {
                            new Avalonia.Controls.TextBlock {
                                Text = qq.Text,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            new Avalonia.Controls.Button {
                                Content = "OK",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                Width = 80
                            }
                        }
                    }
                };
                // Wire the OK button to close the window.
                var sp = (Avalonia.Controls.StackPanel)msgWin.Content!;
                var okBtn = (Avalonia.Controls.Button)sp.Children[1];
                okBtn.Click += (_, _) => msgWin.Close();
                await msgWin.ShowDialog(this);
                qq.SetResult(MBResult.OK);
                return;
            }

            // Handle progress message (string update).
            if (e.UserState is string msg && !string.IsNullOrEmpty(msg)) {
                messageText.Text = msg;
            }

            int percent = e.ProgressPercentage;
            if (percent >= 0 && percent <= 100) {
                progressBar.Value = percent;
            }
        }

        // Callback that fires when work thread completes.
        private void RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e) {
            if (e.Cancelled) {
                Debug.WriteLine("RunWorkerCompleted: CANCELLED");
                mDialogResult = false;
                Close();
            } else if (e.Error != null) {
                Debug.WriteLine("RunWorkerCompleted: ERROR " + e.Error);
                mDialogResult = false;
                Close();
            } else {
                mDialogResult = mCallbacks!.RunWorkerCompleted(e.Result);
                Close();
            }
        }
    }
}
