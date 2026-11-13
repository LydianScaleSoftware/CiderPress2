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
using System.Collections.Generic;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Input;

namespace cp2_avalonia.Views;

/// <summary>
/// Gives in-window menu items the shortcut labels of the macOS menu bar.
/// </summary>
/// <remarks>
/// The in-window menus and context menus are written with the Windows/Linux gestures
/// (<c>Ctrl+C</c>), while the macOS menu bar (<see cref="NativeMenu"/>) declares the Mac ones
/// (<c>Meta+C</c>, shown as ⌘C).  The main window hides its in-window menu on macOS, but
/// context menus still show, and would read ⌃C.  Copying the gesture of the menu bar item with
/// the same command keeps the labels matching the menu bar.  Items whose command has no
/// menu-bar gesture keep their own label.
/// </remarks>
public static class MenuGestureLabels
{
    /// <summary>
    /// Sets the <see cref="MenuItem.InputGesture"/> of each item in <paramref name="items"/>
    /// whose command has a gesture somewhere in <paramref name="nativeMenu"/>.
    /// </summary>
    public static void CopyFromNativeMenu(NativeMenu nativeMenu, IEnumerable<object?> items)
    {
        var gestures = new Dictionary<ICommand, KeyGesture>(ReferenceEqualityComparer.Instance);
        Collect(nativeMenu, gestures);
        foreach (object? obj in items) {
            if (obj is MenuItem mi && mi.Command != null &&
                    gestures.TryGetValue(mi.Command, out KeyGesture? gesture)) {
                mi.InputGesture = gesture;
            }
        }
    }

    private static void Collect(NativeMenu menu, Dictionary<ICommand, KeyGesture> gestures)
    {
        foreach (NativeMenuItemBase baseItem in menu.Items) {
            if (baseItem is not NativeMenuItem item) {
                continue;
            }
            if (item.Command != null && item.Gesture != null) {
                gestures.TryAdd(item.Command, item.Gesture);
            }
            if (item.Menu != null) {
                Collect(item.Menu, gestures);
            }
        }
    }
}
