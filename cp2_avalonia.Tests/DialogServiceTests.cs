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
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using cp2_avalonia.Services;

namespace cp2_avalonia.Tests;

public class DialogServiceTests
{
    private class VmOne { }
    private class VmTwo { }
    private class ViewOne : Window { public static ViewOne? Last; public ViewOne() { Last = this; } }
    private class ViewTwo : Window { public static ViewTwo? Last; public ViewTwo() { Last = this; } }

    private class Host(Window owner) : IDialogHost
    {
        public Window GetOwnerWindow() => owner;
    }

    [AvaloniaFact]
    public async Task IsModalActive_TracksOpenAndNestedModals()
    {
        var owner = new Window();
        owner.Show();
        var svc = new DialogService(new Host(owner));
        svc.Register<VmOne, ViewOne>();
        svc.Register<VmTwo, ViewTwo>();

        Assert.False(svc.IsModalActive);
        int changes = 0;
        svc.ModalStateChanged += () => changes++;

        Task<bool?> first = svc.ShowDialogAsync(new VmOne());
        Dispatcher.UIThread.RunJobs();
        Assert.True(svc.IsModalActive);

        Task<bool?> second = svc.ShowDialogAsync(new VmTwo());
        Dispatcher.UIThread.RunJobs();
        Assert.True(svc.IsModalActive);

        ViewTwo.Last!.Close(true);
        await second;
        Assert.True(svc.IsModalActive);         // the outer modal is still open

        ViewOne.Last!.Close(true);
        await first;
        Assert.False(svc.IsModalActive);
        Assert.Equal(2, changes);       // inactive -> active once, active -> inactive once
    }
}
