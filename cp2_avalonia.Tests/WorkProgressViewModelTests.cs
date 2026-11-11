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
using AppCommon;

using cp2_avalonia.Services;
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Tests;

public class WorkProgressViewModelTests
{
    private class NullWorker : WorkProgressViewModel.IWorker
    {
        public object DoWork(System.ComponentModel.BackgroundWorker worker) => new object();
        public bool RunWorkerCompleted(object? results) => true;
    }

    private static WorkProgressViewModel MakeVm(FakeDialogService dialogs) =>
        new WorkProgressViewModel(new NullWorker(), false, dialogs);

    private static CallbackFacts MakeFacts() =>
        new CallbackFacts(CallbackFacts.Reasons.FileNameExists) {
            NewPathName = "new/FILE", NewDirSep = '/',
            OrigPathName = "old/FILE", OrigDirSep = '/',
        };

    // Waits for a query result with a timeout, so a regression fails instead of hanging.
    private static T WithTimeout<T>(Func<T> getResult)
    {
        Task<T> task = Task.Run(getResult);
        Assert.True(task.Wait(TimeSpan.FromSeconds(5)), "worker would block forever");
        return task.Result;
    }

    [Fact]
    public async Task MessageBox_ThrowingDialogService_AnswersCancel_NoHang()
    {
        var dialogs = new FakeDialogService { ThrowOnShow = new InvalidOperationException("boom") };
        var vm = MakeVm(dialogs);
        var query = new WorkProgressViewModel.MessageBoxQuery("text", "caption",
            MBButton.OKCancel, MBIcon.Error);

        await vm.HandleMessageBoxQueryAsync(query);

        Assert.Equal(MBResult.Cancel, WithTimeout(query.WaitForResult));
    }

    [Fact]
    public async Task MessageBox_PassesButtonsAndIcon_AndReturnsButtonPressed()
    {
        var dialogs = new FakeDialogService { MessageResult = MBResult.Cancel };
        var vm = MakeVm(dialogs);
        var query = new WorkProgressViewModel.MessageBoxQuery("hello", "Title",
            MBButton.OKCancel, MBIcon.Warning);

        await vm.HandleMessageBoxQueryAsync(query);

        Assert.Equal(("hello", "Title", MBButton.OKCancel, MBIcon.Warning),
            Assert.Single(dialogs.Messages));
        Assert.Equal(MBResult.Cancel, WithTimeout(query.WaitForResult));
    }

    [Fact]
    public async Task Overwrite_ThrowingDialogService_AnswersCancel_NoHang()
    {
        var dialogs = new FakeDialogService { ThrowOnShow = new InvalidOperationException("boom") };
        var vm = MakeVm(dialogs);
        var query = new WorkProgressViewModel.OverwriteQuery(MakeFacts());

        await vm.HandleOverwriteQueryAsync(query);

        var result = WithTimeout(() => query.WaitForResult(out bool _));
        Assert.Equal(CallbackFacts.Results.Cancel, result);
    }

    [Fact]
    public async Task Overwrite_DialogCancelled_AnswersCancel()
    {
        var dialogs = new FakeDialogService { DialogResult = false };
        var vm = MakeVm(dialogs);
        var query = new WorkProgressViewModel.OverwriteQuery(MakeFacts());

        await vm.HandleOverwriteQueryAsync(query);

        Assert.IsType<OverwriteQueryViewModel>(Assert.Single(dialogs.Dialogs));
        Assert.Equal(CallbackFacts.Results.Cancel, WithTimeout(() => query.WaitForResult(out bool _)));
    }
}
