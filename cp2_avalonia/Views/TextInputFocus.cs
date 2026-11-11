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

namespace cp2_avalonia.Views;

/// <summary>
/// Tells whether keyboard focus is in a text box, where editing keys (Delete, Ctrl/Cmd+C,
/// Ctrl/Cmd+V, Ctrl/Cmd+A) belong to the text rather than to the file list.
/// </summary>
/// <remarks>
/// Avalonia tries a window's key bindings, and the macOS native menu its key equivalents,
/// before the focused control sees the key, and a command that reports CanExecute is run and
/// the key swallowed.  The file commands therefore have to decline while this is true.
/// Editable combo boxes and numeric spinners hold a TextBox, which is what has the focus.
/// </remarks>
public static class TextInputFocus
{
    /// <summary>
    /// True if the focused element in the top level is a <see cref="TextBox"/>.
    /// </summary>
    public static bool IsTextInputFocused(TopLevel? topLevel)
    {
        return topLevel?.FocusManager?.GetFocusedElement() is TextBox;
    }
}
