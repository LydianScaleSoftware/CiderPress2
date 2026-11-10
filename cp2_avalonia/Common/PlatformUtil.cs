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

namespace cp2_avalonia.Common {
    /// <summary>
    /// Cross-platform utility functions that replace WPF-/Windows-specific helpers
    /// from cp2_wpf/WinUtil.cs.
    /// </summary>
    public static class PlatformUtil {
        /// <summary>
        /// Finds the runtime data directory.  During development, this is the repository root
        /// (where CiderPress2.sln lives).  In production, the executable and settings live in
        /// the same directory.
        /// </summary>
        public static string GetRuntimeDataDir() {
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            int maxDepth = 6;
            while (dir != null && maxDepth-- > 0) {
                if (File.Exists(Path.Combine(dir.FullName, "CiderPress2-settings.json")) ||
                    File.Exists(Path.Combine(dir.FullName, "CiderPress2.sln"))) {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return baseDir;
        }

        /// <summary>
        /// Shows a simple modal message box with an OK button.
        /// </summary>
        /// <param name="owner">Parent window.</param>
        /// <param name="message">Text to display.</param>
        /// <param name="title">Window title.</param>
        public static async Task ShowMessageAsync(Window owner, string message, string title) {
            var dlg = new Window {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel {
                    Margin = new Avalonia.Thickness(16),
                    Spacing = 12,
                    Children = {
                        new TextBlock {
                            Text = message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new Button {
                            Content = "OK",
                            Width = 80,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                        }
                    }
                }
            };
            var sp = (StackPanel)dlg.Content!;
            var btn = (Button)sp.Children[1];
            btn.Click += (_, _) => dlg.Close();
            await dlg.ShowDialog(owner);
        }
    }
}
