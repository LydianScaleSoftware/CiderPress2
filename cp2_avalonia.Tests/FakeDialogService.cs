/*
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
using Avalonia.Controls;

using cp2_avalonia.Services;

namespace cp2_avalonia.Tests;

/// <summary>
/// Scriptable <see cref="IDialogService"/> for tests.
/// </summary>
internal class FakeDialogService : IDialogService
{
    public bool IsModalActive { get; set; }
#pragma warning disable CS0067     // never raised by the fake
    public event Action? ModalStateChanged;
#pragma warning restore CS0067

    /// <summary>If set, every dialog or message box call throws this.</summary>
    public Exception? ThrowOnShow { get; set; }

    /// <summary>Button returned by <see cref="ShowMessageAsync"/>.</summary>
    public MBResult MessageResult { get; set; } = MBResult.OK;

    /// <summary>Value returned by <see cref="ShowDialogAsync{TViewModel}"/>.</summary>
    public bool? DialogResult { get; set; } = true;

    /// <summary>Records of message boxes shown: (text, caption, buttons, icon).</summary>
    public List<(string Text, string Caption, MBButton Buttons, MBIcon Icon)> Messages { get; } = new();

    /// <summary>View models passed to <see cref="ShowDialogAsync{TViewModel}"/>.</summary>
    public List<object?> Dialogs { get; } = new();

    public void Register<TViewModel, TView>() where TView : Window, new() { }

    public Task<bool?> ShowDialogAsync<TViewModel>(TViewModel viewModel)
    {
        if (ThrowOnShow != null) throw ThrowOnShow;
        Dialogs.Add(viewModel);
        return Task.FromResult(DialogResult);
    }

    public Task<MBResult> ShowMessageAsync(string text, string caption,
        MBButton buttons = MBButton.OK, MBIcon icon = MBIcon.None)
    {
        if (ThrowOnShow != null) throw ThrowOnShow;
        Messages.Add((text, caption, buttons, icon));
        return Task.FromResult(MessageResult);
    }

    public Task<(MBResult result, bool suppress)> ShowSuppressibleMessageAsync(string text,
        string caption, string suppressLabel, MBButton buttons = MBButton.OK,
        MBIcon icon = MBIcon.None) =>
        Task.FromResult((MessageResult, false));

    public Task<bool> ShowConfirmAsync(string text, string caption) => Task.FromResult(true);
    public void ShowModeless<TViewModel>(TViewModel viewModel) { }
    public bool TryActivate<TViewModel>() => false;
    public Task ShowLoadingAsync(string message, Func<Task> work) => work();
}
