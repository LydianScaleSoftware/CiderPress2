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


using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AppCommon;
using cp2_avalonia.Services;
using cp2_avalonia.Views;
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Cancellable progress dialog.  The dialog returns true if the operation ran to
    /// completion, false if it was canceled or halted early with an error.
    /// </summary>
    public partial class WorkProgress : Window {
        private WorkProgressViewModel? VM => DataContext as WorkProgressViewModel;

        public WorkProgress() {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e) {
            // Unsubscribe from the previous view model, so a DataContext change doesn't
            // leave duplicate handlers.  (The overwrite and message-box prompts are handled
            // by WorkProgressViewModel itself, through IDialogService.)
            if (mHookedVm != null) {
                mHookedVm.CloseRequested -= OnCloseRequested;
                mHookedVm = null;
            }
            if (DataContext is WorkProgressViewModel vm) {
                vm.CloseRequested += OnCloseRequested;
                mHookedVm = vm;
            }
        }

        private WorkProgressViewModel? mHookedVm;

        private void OnCloseRequested(bool result) => Close(result);

        private void Window_Loaded(object? sender, RoutedEventArgs e) {
            VM?.StartWorker();
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e) {
            WorkProgressViewModel? vm = VM;
            if (vm != null && vm.IsBusy) {
                // Closing from the title bar acts like the Cancel button: ask the worker
                // to stop, disable the button, and stay open until it has.
                vm.RequestCancel();
                e.Cancel = true;
            }
        }
    }
}
