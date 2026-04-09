/*
 * Copyright 2023 faddenSoft
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
using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AppCommon;

namespace cp2_avalonia {
    /// <summary>
    /// "About" dialog.
    /// </summary>
    public partial class AboutBox : Window {
        private const string LEGAL_STUFF_FILE_NAME = "LegalStuff.txt";

        /// <summary>
        /// Version string, for display.
        /// </summary>
        public string ProgramVersionString {
            get { return GlobalAppVersion.AppVersion.ToString(); }
        }

        /// <summary>
        /// Operating system version, for display.
        /// </summary>
        public string OsPlatform {
            get { return "OS: " + RuntimeInformation.OSDescription; }
        }

        /// <summary>
        /// Runtime information, for display.
        /// </summary>
        public string RuntimeInfo {
            get {
                return "Runtime: " + RuntimeInformation.FrameworkDescription + " / " +
                    RuntimeInformation.RuntimeIdentifier +
                    (Environment.IsPrivilegedProcess ? " (Admin)" : "");
            }
        }

        /// <summary>
        /// Determines whether a message about assertions is visible.
        /// </summary>
        public bool IsDebugBuild {
            get {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Contents of LegalStuff.txt, for display.
        /// </summary>
        public string LegalStuffText { get; private set; }

        public AboutBox() {
            // Load legal text before DataContext is set so the binding finds the value.
            string pathName = Path.Combine(GetRuntimeDataDir(), LEGAL_STUFF_FILE_NAME);
            try {
                LegalStuffText = File.ReadAllText(pathName);
            } catch (Exception ex) {
                LegalStuffText = ex.ToString();
            }

            InitializeComponent();
            DataContext = this;
        }

        private void WebsiteLink_Tapped(object? sender, TappedEventArgs e) {
            CommonUtil.ShellCommand.OpenUrl("https://ciderpress2.com/");
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) {
            Close();
        }

        private static string GetRuntimeDataDir() {
            string baseDir = AppContext.BaseDirectory;
            // In dev builds the base dir ends with e.g. cp2_avalonia/bin/Debug/net8.0/.
            // Walk up four levels to reach the solution root.
            string marker = Path.Combine(baseDir, LEGAL_STUFF_FILE_NAME);
            if (File.Exists(marker))
            {
                return baseDir;
            }

            for (int i = 0; i < 4; i++) {
                baseDir = Path.GetDirectoryName(baseDir) ?? baseDir;
                marker = Path.Combine(baseDir, LEGAL_STUFF_FILE_NAME);
                if (File.Exists(marker))
                {
                    return baseDir;
                }
            }
            return AppContext.BaseDirectory; // fallback
        }
    }
}
