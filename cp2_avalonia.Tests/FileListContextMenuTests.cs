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
using System.Text.RegularExpressions;

namespace cp2_avalonia.Tests;

/// <summary>
/// AH6/AH7/AH8: the two file list context menus (the rows' menu and the empty area's menu)
/// stay in step: shortcut labels, and "Change to Directory" in place of the View Files items.
/// </summary>
public class FileListContextMenuTests
{
    private static string ReadMainWindowXaml()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null) {
            string candidate = Path.Combine(dir, "cp2_avalonia", "Views", "MainWindow.axaml");
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("MainWindow.axaml not found");
        return string.Empty;
    }

    // The <ContextMenu> blocks of the file list: the empty-area menu and the DataGrid's menu.
    private static string[] FileListMenus()
    {
        string xaml = ReadMainWindowXaml();
        int panel = xaml.IndexOf("<ContextMenu Name=\"fileListPanelContextMenu\">",
            StringComparison.Ordinal);
        int grid = xaml.IndexOf("<DataGrid.ContextMenu>",
            xaml.IndexOf("Name=\"fileListDataGrid\"", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.True(panel >= 0 && grid >= 0);
        return new[] { Block(xaml, panel), Block(xaml, grid) };
    }

    private static string Block(string xaml, int start) =>
        xaml.Substring(start,
            xaml.IndexOf("</ContextMenu>", start, StringComparison.Ordinal) - start);

    private static Match Item(string menu, string command)
    {
        Match m = Regex.Match(menu,
            "<MenuItem [^>]*Command=\"\\{Binding " + command + "\\}\"[^>]*/>");
        Assert.True(m.Success, command + " missing from a file list menu");
        return m;
    }

    [Theory]
    [InlineData("ChangeToDirectoryCommand", "IsChangeToDirectorySelected")]
    [InlineData("ViewFilesCommand", "ShowViewFilesMenuItems")]
    [InlineData("ViewFilesInNewViewerCommand", "ShowViewFilesMenuItems")]
    public void BothMenus_SwapViewFilesForChangeToDirectory(string command, string visibleWhen)
    {
        foreach (string menu in FileListMenus()) {
            Assert.Contains("IsVisible=\"{Binding " + visibleWhen + "}\"",
                Item(menu, command).Value);
        }
    }

    [Theory]
    [InlineData("ChangeToDirectoryCommand", "Enter")]
    [InlineData("ViewFilesCommand", "Enter")]
    [InlineData("ViewFilesInNewViewerCommand", "Ctrl+Enter")]
    [InlineData("EditAttributesCommand", "Alt+Enter")]
    [InlineData("ExtractFilesCommand", "Ctrl+E")]
    [InlineData("CopyCommand", "Ctrl+C")]
    [InlineData("PasteCommand", "Ctrl+V")]
    [InlineData("DeleteFilesCommand", "Delete")]
    [InlineData("AddFilesCommand", "Ctrl+Shift+A")]
    [InlineData("CreateDirectoryCommand", "Ctrl+Shift+N")]
    public void BothMenus_ShowTheShortcut(string command, string gesture)
    {
        foreach (string menu in FileListMenus()) {
            Assert.Contains("InputGesture=\"" + gesture + "\"", Item(menu, command).Value);
        }
    }
}
