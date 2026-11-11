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
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using cp2_avalonia.Views;

namespace cp2_avalonia.Tests;

/// <summary>
/// F1: window key bindings must not take editing keys from a focused text box.
/// </summary>
public class TextInputFocusTests
{
    private class TestCommand(Func<bool> canExecute) : ICommand
    {
        public int Executed { get; private set; }
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) => Executed++;
    }

    private static (Window window, TextBox box, Button other) Make(ICommand deleteCommand)
    {
        // The text box needs its template to edit text.
        var styles = Avalonia.Application.Current!.Styles;
        if (!styles.OfType<FluentTheme>().Any()) {
            styles.Add(new FluentTheme());
        }
        var box = new TextBox { Text = "abc", CaretIndex = 0 };
        var other = new Button { Content = "x" };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(other);
        var window = new Window { Content = panel, Width = 300, Height = 200 };
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Delete),
            Command = deleteCommand });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, box, other);
    }

    private static void PressDelete(Window window)
    {
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void IsTextInputFocused_FollowsFocus()
    {
        var (window, box, other) = Make(new TestCommand(() => true));
        Assert.False(TextInputFocus.IsTextInputFocused(null));

        box.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(TextInputFocus.IsTextInputFocused(window));

        other.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(TextInputFocus.IsTextInputFocused(window));
        window.Close();
    }

    [AvaloniaFact]
    public void AlwaysEnabledWindowBinding_TakesDeleteFromTheTextBox()
    {
        // The problem (F1): an enabled command runs and the key never reaches the text box.
        var cmd = new TestCommand(() => true);
        var (window, box, _) = Make(cmd);
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        PressDelete(window);

        Assert.Equal(1, cmd.Executed);
        Assert.Equal("abc", box.Text);
    }

    [AvaloniaFact]
    public void CommandThatDeclinesWhileTyping_LetsTheTextBoxHaveTheKey()
    {
        // The fix: the command reports CanExecute false while a text box has focus.
        Window? window = null;
        var cmd = new TestCommand(() => !TextInputFocus.IsTextInputFocused(window));
        var made = Make(cmd);
        window = made.window;
        made.box.Focus();
        Dispatcher.UIThread.RunJobs();

        PressDelete(window);

        Assert.Equal(0, cmd.Executed);
        Assert.Equal("bc", made.box.Text);

        // With focus elsewhere the binding works again.
        made.other.Focus();
        Dispatcher.UIThread.RunJobs();
        PressDelete(window);
        Assert.Equal(1, cmd.Executed);
        window.Close();
    }
}
