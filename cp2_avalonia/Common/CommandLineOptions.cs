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
using System;
using System.IO;

using AppCommon;
using CommonUtil;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Parses the process command line.  Mirrors the minimal launch-time options supported by
    /// the WPF predecessor (an optional read-only flag plus a single file/drive to open), and
    /// adds a help flag and basic validation of unrecognized or excess arguments.
    /// </summary>
    /// <remarks>
    /// Usage: <c>CiderPress2 [options] [file]</c>
    /// </remarks>
    public sealed class CommandLineOptions {
        /// <summary>Program name used in usage text (matches the assembly / executable name).</summary>
        private const string ProgramName = "CiderPress2";

        /// <summary>True if the user requested the help/usage text.</summary>
        public bool ShowHelp { get; private set; }

        /// <summary>True if the file should be opened read-only ("-r" / "--read-only").</summary>
        public bool ReadOnly { get; private set; }

        /// <summary>
        /// Full path of the file to open on startup, or null if none was supplied.  Only set
        /// when the argument is a regular file path (see <see cref="IsPhysicalDrive"/>).
        /// </summary>
        public string? FilePath { get; private set; }

        /// <summary>
        /// True if the supplied path names a physical drive (e.g. <c>\\.\PhysicalDrive0</c>).
        /// This is not yet implemented on any platform; the caller should report that and exit.
        /// </summary>
        public bool IsPhysicalDrive { get; private set; }

        /// <summary>Error message describing why parsing failed, or null on success.</summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>True if the command line could not be parsed.</summary>
        public bool HasError => ErrorMessage != null;

        private CommandLineOptions() { }

        /// <summary>
        /// Parses the arguments passed to Main().  Never throws; failures are reported via
        /// <see cref="ErrorMessage"/>.
        /// </summary>
        /// <param name="args">Argument array from Main() (does NOT include the program name).</param>
        public static CommandLineOptions Parse(string[] args) {
            CommandLineOptions opts = new CommandLineOptions();
            string? rawPath = null;

            foreach (string arg in args) {
                if (string.IsNullOrEmpty(arg)) {
                    continue;
                }
                // macOS may append a process-serial-number argument (e.g. "-psn_0_12345")
                // when launching a GUI app; ignore it rather than flagging it as invalid.
                if (arg.StartsWith("-psn_", StringComparison.Ordinal)) {
                    continue;
                }

                if (arg.Length > 1 && arg[0] == '-') {
                    switch (arg) {
                        case "-h":
                        case "-?":
                        case "--help":
                            opts.ShowHelp = true;
                            break;
                        case "-r":
                        case "--read-only":
                            opts.ReadOnly = true;
                            break;
                        default:
                            opts.ErrorMessage = "Unknown option: " + arg;
                            return opts;
                    }
                } else {
                    // Positional argument: the file (or drive) to open.
                    if (rawPath != null) {
                        opts.ErrorMessage = "Too many arguments; only one file may be specified.";
                        return opts;
                    }
                    rawPath = arg;
                }
            }

            // Help wins over everything else; skip path validation.
            if (opts.ShowHelp || rawPath == null) {
                return opts;
            }

            // A physical-drive path is recognized but not supported yet.
            if (rawPath.StartsWith(PhysicalDriveAccess.Win.PHYSICAL_DRIVE_PREFIX,
                    StringComparison.OrdinalIgnoreCase)) {
                opts.IsPhysicalDrive = true;
                opts.FilePath = rawPath;
                return opts;
            }

            // Resolve to a full path (matches the WPF behavior).  Path.GetFullPath throws on
            // paths containing invalid characters, so trap that as a parameter error.
            try {
                opts.FilePath = Path.GetFullPath(rawPath);
            } catch (Exception ex) {
                opts.ErrorMessage = "Invalid file path '" + rawPath + "': " + ex.Message;
            }
            return opts;
        }

        /// <summary>
        /// Returns the multi-line usage message.
        /// </summary>
        public static string GetUsage() {
            return
                "CiderPress II GUI v" + GlobalAppVersion.AppVersion + Environment.NewLine +
                Environment.NewLine +
                "Usage: " + ProgramName + " [options] [file]" + Environment.NewLine +
                Environment.NewLine +
                "Options:" + Environment.NewLine +
                "  -r, --read-only   Open the file in read-only mode." + Environment.NewLine +
                "  -h, --help        Show this help message and exit." + Environment.NewLine +
                Environment.NewLine +
                "Arguments:" + Environment.NewLine +
                "  file              Path to a disk image or file archive to open on startup.";
        }
    }
}
