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
using System.Threading.Tasks;
using Avalonia;

using cp2_avalonia.Common;
using cp2_avalonia.Services;

namespace cp2_avalonia {
    internal class Program {
        /// <summary>
        /// Options parsed from the command line, made available to the UI (e.g. to open a
        /// file at startup).  Null until <see cref="Main"/> runs.
        /// </summary>
        public static CommandLineOptions? StartupOptions { get; private set; }

        // Avalonia configuration; don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        [STAThread]
        public static void Main(string[] args) {
            CommandLineOptions opts = CommandLineOptions.Parse(args);

            // Handle the exit-early cases before spinning up the GUI, so the messages land on
            // stdout/stderr for anyone who launched us from a terminal.
            if (opts.HasError) {
                Console.Error.WriteLine(opts.ErrorMessage);
                Console.Error.WriteLine();
                Console.Error.WriteLine(CommandLineOptions.GetUsage());
                Environment.Exit(2);
                return;
            }
            if (opts.ShowHelp) {
                // Everything goes to stderr: on Windows the GUI subsystem has no console, but a
                // debugger's output window still captures stderr.
                Console.Error.WriteLine(CommandLineOptions.GetUsage());
                return;
            }
            if (opts.IsPhysicalDrive) {
                Console.Error.WriteLine("Opening a physical drive (" + opts.FilePath +
                    ") is not yet implemented on this platform.");
                Environment.Exit(1);
                return;
            }

            StartupOptions = opts;

            InstallGlobalExceptionHandlers();
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// Routes otherwise-unhandled exceptions to the application log so they appear
        /// in the debug log dialog (DEBUG &gt; Show Debug Log) instead of only going to
        /// stderr / the debugger.
        /// </summary>
        private static void InstallGlobalExceptionHandlers() {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
                if (e.ExceptionObject is Exception ex) {
                    AppLog.E("Unhandled exception" + (e.IsTerminating ? " (terminating)" : ""), ex);
                } else {
                    AppLog.E("Unhandled non-exception error: " + e.ExceptionObject);
                }
            };
            TaskScheduler.UnobservedTaskException += (sender, e) => {
                AppLog.E("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
#if !DEBUG
            CommonUtil.Crash.AppTag = "CiderPress2";
            CommonUtil.Crash.AppIdent = "CiderPress II v" +
                AppCommon.GlobalAppVersion.AppVersion.ToString();
            AppDomain.CurrentDomain.UnhandledException +=
                new UnhandledExceptionEventHandler(CommonUtil.Crash.CrashReporter);
#endif
        }
    }
}
