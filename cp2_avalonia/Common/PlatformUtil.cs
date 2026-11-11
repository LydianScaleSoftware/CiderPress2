/*
 * Copyright 2025 faddenSoft
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
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;

using cp2_avalonia.Services;
using cp2_avalonia.ViewModels;
using cp2_avalonia.Views;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Cross-platform utility functions that replace WPF-/Windows-specific helpers
    /// from cp2_wpf/WinUtil.cs.
    /// </summary>
    public static class PlatformUtil {
        /// <summary>
        /// Finds the directory holding read-only files shipped with the app, such as
        /// LegalStuff.txt.  In a distribution these sit beside the executable.  During
        /// development, this is the repository root (where CiderPress2.sln lives).
        /// </summary>
        public static string GetAppResourceDir() {
            return SettingsPaths.FindAppResourceDir(AppContext.BaseDirectory,
                "LegalStuff.txt", "CiderPress2.sln", File.Exists);
        }

        /// <summary>
        /// Gets the per-user folder for settings: <c>%APPDATA%\CiderPress2</c> on Windows,
        /// <c>~/Library/Application Support/CiderPress2</c> on macOS, and
        /// <c>~/.config/ciderpress2</c> on Linux.  The folder may not exist yet.  Returns null
        /// if the system has no application-data folder.
        /// </summary>
        public static string? GetUserSettingsDir() {
            return SettingsPaths.GetUserSettingsDir(SettingsPaths.CurrentOS,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }

        /// <summary>
        /// Shows a modal message box owned by a specific window, using the same
        /// <see cref="MessageBoxView"/> as <see cref="IDialogService.ShowMessageAsync"/>.
        /// </summary>
        /// <remarks>
        /// For code-behind of windows that need to own the box themselves (the log viewer,
        /// the drop-target tool, file viewers).  View models must not call this; they use
        /// <see cref="IDialogService"/>.  A box shown here does not count as a modal dialog
        /// for <see cref="IDialogService"/>, which is fine because these windows don't touch
        /// the open archive.
        /// </remarks>
        /// <param name="owner">Parent window.</param>
        /// <param name="message">Text to display.</param>
        /// <param name="title">Window title.</param>
        /// <param name="buttons">Buttons to show.</param>
        /// <param name="icon">Icon to show.</param>
        /// <returns>The button that was pressed.</returns>
        public static async Task<MBResult> ShowMessageAsync(Window owner, string message,
                string title, MBButton buttons = MBButton.OK, MBIcon icon = MBIcon.None) {
            var vm = new MessageBoxViewModel(message, title, buttons, icon);
            var view = new MessageBoxView { DataContext = vm };
            await view.ShowDialog<bool?>(owner);
            return vm.Result;
        }    }
}
