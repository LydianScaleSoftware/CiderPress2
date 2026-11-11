/*
 * Copyright 2026 faddenSoft
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
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using AppCommon;
using cp2_avalonia.Services;

namespace cp2_avalonia.ViewModels;

/// <summary>
/// ViewModel for the cancellable WorkProgress dialog.
/// </summary>
public class WorkProgressViewModel : ObservableObject
{
    // ── Nested types (moved from WorkProgress code-behind) ────────────────────

    /// <summary>Task-specific callbacks.</summary>
    public interface IWorker
    {
        /// <summary>Does the work; runs on a background thread.</summary>
        object DoWork(BackgroundWorker worker);
        /// <summary>Called on the UI thread after successful completion.</summary>
        bool RunWorkerCompleted(object? results);
    }

    /// <summary>
    /// Message box query sent via ReportProgress from a worker thread.
    /// Uses Monitor.Wait/Pulse for cross-thread synchronisation.
    /// </summary>
    public class MessageBoxQuery(string text, string caption, MBButton button, MBIcon image)
    {
        private const MBResult RESULT_UNSET = (MBResult)(-1000);

        public string Text { get; } = text;
        public string Caption { get; } = caption;
        public MBButton Button { get; } = button;
        public MBIcon Image { get; } = image;

        private MBResult mResult = RESULT_UNSET;
        private readonly object mLockObj = new object();

        /// <summary>True once <see cref="SetResult"/> has been called.</summary>
        public bool HasResult
        {
            get { lock (mLockObj) { return mResult != RESULT_UNSET; } }
        }

        public MBResult WaitForResult()
        {
            lock (mLockObj) {
                while (mResult == RESULT_UNSET) { Monitor.Wait(mLockObj); }
                return mResult;
            }
        }

        public void SetResult(MBResult value)
        {
            lock (mLockObj) {
                mResult = value;
                Monitor.PulseAll(mLockObj);
            }
        }
    }

    /// <summary>
    /// File overwrite query sent via ReportProgress from a worker thread.
    /// </summary>
    public class OverwriteQuery(CallbackFacts what)
    {
        private CallbackFacts.Results mResult = CallbackFacts.Results.Unknown;
        private bool mUseForAll;
        private readonly object mLockObj = new object();

        public CallbackFacts Facts { get; } = what;

        /// <summary>True once <see cref="SetResult"/> has been called.</summary>
        public bool HasResult
        {
            get { lock (mLockObj) { return mResult != CallbackFacts.Results.Unknown; } }
        }

        public CallbackFacts.Results WaitForResult(out bool useForAll)
        {
            lock (mLockObj) {
                while (mResult == CallbackFacts.Results.Unknown) { Monitor.Wait(mLockObj); }
                useForAll = mUseForAll;
                return mResult;
            }
        }

        public void SetResult(CallbackFacts.Results value, bool useForAll)
        {
            lock (mLockObj) {
                mResult = value;
                mUseForAll = useForAll;
                Monitor.PulseAll(mLockObj);
            }
        }
    }

    // ── BackgroundWorker and callback ─────────────────────────────────────────
    private readonly IWorker mCallbacks;
    private readonly IDialogService mDialogService;
    private BackgroundWorker? mWorker;
    private readonly bool mIsIndeterminate;

    public event Action<bool>? CloseRequested;

    public IRelayCommand CancelCommand { get; }

    // ── Bindable properties ───────────────────────────────────────────────────
    private string mMessageText = string.Empty;
    public string MessageText {
        get => mMessageText;
        set => SetProperty(ref mMessageText, value);
    }

    private double mProgressValue;
    public double ProgressValue {
        get => mProgressValue;
        set => SetProperty(ref mProgressValue, value);
    }

    public bool IsIndeterminate => mIsIndeterminate;

    private bool mIsCancelEnabled = true;
    public bool IsCancelEnabled {
        get => mIsCancelEnabled;
        set => SetProperty(ref mIsCancelEnabled, value);
    }

    /// <summary>Result read by caller after ShowDialogAsync returns.</summary>
    public bool DialogResult { get; private set; }

    /// <summary>
    /// The exception that stopped the worker, or null.  When set, an error message has
    /// already been shown to the user, so callers should say "Failed", not "Cancelled".
    /// </summary>
    public Exception? WorkerError { get; private set; }

    /// <summary>True while the background worker is running.</summary>
    public bool IsBusy => mWorker?.IsBusy ?? false;

    /// <param name="callbacks">Work to do.</param>
    /// <param name="isIndeterminate">True to show an indeterminate progress bar.</param>
    /// <param name="dialogService">Used for the overwrite and message-box prompts the worker
    ///   can raise.  When this view model is shown through the same service, the prompts are
    ///   owned by the progress window.</param>
    public WorkProgressViewModel(IWorker callbacks, bool isIndeterminate,
            IDialogService dialogService)
    {
        mCallbacks = callbacks;
        mIsIndeterminate = isIndeterminate;
        mDialogService = dialogService;

        CancelCommand = new RelayCommand(() => {
            mWorker?.CancelAsync();
            IsCancelEnabled = false;
        });
    }

    /// <summary>
    /// Called from code-behind's Window_Loaded handler once the window is ready.
    /// Starts the background worker.
    /// </summary>
    public void StartWorker()
    {
        mWorker = new BackgroundWorker {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };
        mWorker.DoWork += DoWork;
        mWorker.ProgressChanged += ProgressChanged;
        mWorker.RunWorkerCompleted += RunWorkerCompleted;
        mWorker.RunWorkerAsync();
    }

    /// <summary>
    /// Cancels the operation. Called from the View's OnClosing override when the
    /// user tries to close the window while work is still in progress.
    /// </summary>
    public void RequestCancel()
    {
        mWorker?.CancelAsync();
        IsCancelEnabled = false;
    }

    // ── BackgroundWorker handlers ─────────────────────────────────────────────

    // Executes on the work thread.  Do NOT access GUI objects here.
    private void DoWork(object? sender, DoWorkEventArgs e)
    {
        Debug.Assert(sender == mWorker);
        object results = mCallbacks.DoWork(mWorker!);
        if (mWorker!.CancellationPending) {
            e.Cancel = true;
        } else {
            e.Result = results;
        }
    }

    // Executes on the UI thread each time the worker calls ReportProgress().
    private void ProgressChanged(object? sender, ProgressChangedEventArgs e)
    {
        // These arrive on the UI thread (BackgroundWorker marshals ProgressChanged).  The
        // worker thread is blocked in WaitForResult until the query is answered.
        if (e.UserState is OverwriteQuery oq) {
            _ = HandleOverwriteQueryAsync(oq);
            return;
        }

        if (e.UserState is MessageBoxQuery qq) {
            _ = HandleMessageBoxQueryAsync(qq);
            return;
        }

        if (e.UserState is string msg && !string.IsNullOrEmpty(msg)) {
            MessageText = msg;
        }
        int percent = e.ProgressPercentage;
        if (percent >= 0 && percent <= 100) {
            ProgressValue = percent;
        }
    }

    /// <summary>
    /// Asks the user how to handle an existing file and hands the answer to the waiting
    /// worker.  The worker must always get an answer, or it blocks forever, so any failure
    /// to show the dialog answers Cancel.
    /// </summary>
    internal async Task HandleOverwriteQueryAsync(OverwriteQuery oq)
    {
        try {
            var oqVm = new OverwriteQueryViewModel(oq.Facts);
            bool? result = await mDialogService.ShowDialogAsync(oqVm);
            if (result == true) {
                oq.SetResult(oqVm.Result, oqVm.UseForAll);
            } else {
                oq.SetResult(CallbackFacts.Results.Cancel, false);
            }
        } catch (Exception ex) {
            AppLog.E("Overwrite query failed", ex);
        } finally {
            if (!oq.HasResult) {
                oq.SetResult(CallbackFacts.Results.Cancel, false);
            }
        }
    }

    /// <summary>
    /// Shows a message box for the worker, with the requested buttons and icon, and hands
    /// the button pressed to the waiting worker.  As with overwrite queries, the worker
    /// always gets an answer.
    /// </summary>
    internal async Task HandleMessageBoxQueryAsync(MessageBoxQuery mq)
    {
        try {
            MBResult result = await mDialogService.ShowMessageAsync(mq.Text, mq.Caption,
                mq.Button, mq.Image);
            mq.SetResult(result);
        } catch (Exception ex) {
            AppLog.E("Message box query failed", ex);
        } finally {
            if (!mq.HasResult) {
                mq.SetResult(MBResult.Cancel);
            }
        }
    }

    // Executes on the UI thread after the worker finishes.
    private async void RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
    {
        try {
            if (e.Cancelled) {
                DialogResult = false;
            } else if (e.Error != null) {
                AppLog.E("Background operation failed", e.Error);
                WorkerError = e.Error;
                DialogResult = false;
                // Tell the user why; the callers only know "not completed".
                await ShowWorkerErrorAsync(e.Error);
            } else {
                DialogResult = mCallbacks.RunWorkerCompleted(e.Result);
            }
        } catch (Exception ex) {
            AppLog.E("Completing background operation failed", ex);
            DialogResult = false;
        }
        CloseRequested?.Invoke(DialogResult);
    }

    private async Task ShowWorkerErrorAsync(Exception error)
    {
        try {
            await mDialogService.ShowMessageAsync("The operation failed: " + error.Message,
                "Failed", MBButton.OK, MBIcon.Error);
        } catch (Exception ex) {
            AppLog.E("Unable to show worker error", ex);
        }
    }
}
