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
using Avalonia.Input;

using CommunityToolkit.Mvvm.Input;

using cp2_avalonia.Views;

namespace cp2_avalonia.Tests;

/// <summary>
/// AH7: context menu shortcut labels follow the macOS menu bar.
/// </summary>
public class MenuGestureLabelsTests
{
    [AvaloniaFact]
    public void ItemsTakeTheMenuBarGesture_ForTheSameCommand_AndKeepTheirOwnOtherwise()
    {
        var copy = new RelayCommand(() => { });
        var view = new RelayCommand(() => { });
        var test = new RelayCommand(() => { });

        // Menu bar: Edit > Copy (Meta+C), Actions > View Files (no gesture).
        var bar = new NativeMenu();
        var edit = new NativeMenuItem("Edit") { Menu = new NativeMenu() };
        edit.Menu.Add(new NativeMenuItem("Copy") {
            Command = copy, Gesture = KeyGesture.Parse("Meta+C") });
        var actions = new NativeMenuItem("Actions") { Menu = new NativeMenu() };
        actions.Menu.Add(new NativeMenuItem("View Files") { Command = view });
        bar.Add(edit);
        bar.Add(new NativeMenuItemSeparator());
        bar.Add(actions);

        var copyItem = new MenuItem { Command = copy, InputGesture = KeyGesture.Parse("Ctrl+C") };
        var viewItem = new MenuItem { Command = view, InputGesture = KeyGesture.Parse("Enter") };
        var testItem = new MenuItem { Command = test };
        var noCommand = new MenuItem { InputGesture = KeyGesture.Parse("Ctrl+X") };

        MenuGestureLabels.CopyFromNativeMenu(bar,
            new object?[] { copyItem, new Separator(), viewItem, testItem, noCommand });

        Assert.Equal(KeyGesture.Parse("Meta+C"), copyItem.InputGesture);
        Assert.Equal(KeyGesture.Parse("Enter"), viewItem.InputGesture);
        Assert.Null(testItem.InputGesture);
        Assert.Equal(KeyGesture.Parse("Ctrl+X"), noCommand.InputGesture);
    }
}
