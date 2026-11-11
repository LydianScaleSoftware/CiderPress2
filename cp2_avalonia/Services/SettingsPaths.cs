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

namespace cp2_avalonia.Services;

/// <summary>
/// Decides where the settings file lives, and writes it safely.  Kept free of Avalonia
/// and of global state so it can be unit tested.
/// </summary>
internal static class SettingsPaths
{
    public enum HostOS { Windows, MacOS, Linux }

    /// <summary>Folder name inside the per-user application-data folder.</summary>
    private const string WINDOWS_MAC_DIR_NAME = "CiderPress2";
    /// <summary>Linux convention is a lower-case name inside ~/.config.</summary>
    private const string LINUX_DIR_NAME = "ciderpress2";

    public static HostOS CurrentOS =>
        OperatingSystem.IsWindows() ? HostOS.Windows :
        OperatingSystem.IsMacOS() ? HostOS.MacOS : HostOS.Linux;

    /// <summary>
    /// Returns the per-user settings folder for the OS:
    /// <c>%APPDATA%\CiderPress2</c>, <c>~/Library/Application Support/CiderPress2</c>, or
    /// <c>~/.config/ciderpress2</c>.  Returns null if the system has no application-data
    /// folder (e.g. no home directory).
    /// </summary>
    /// <param name="os">Operating system.</param>
    /// <param name="appDataDir">Result of
    ///   <c>Environment.GetFolderPath(SpecialFolder.ApplicationData)</c>.</param>
    public static string? GetUserSettingsDir(HostOS os, string appDataDir)
    {
        if (string.IsNullOrEmpty(appDataDir)) {
            return null;
        }
        return Path.Combine(appDataDir,
            os == HostOS.Linux ? LINUX_DIR_NAME : WINDOWS_MAC_DIR_NAME);
    }

    /// <summary>
    /// Chooses the settings file.  A file beside the executable (checked only in the
    /// executable's own directory) is a portable install and wins; otherwise the file is
    /// in the per-user folder.  If there is no per-user folder, the executable's directory
    /// is used.
    /// </summary>
    /// <param name="fileName">Settings file name.</param>
    /// <param name="os">Operating system.</param>
    /// <param name="appDataDir">Per-user application-data folder (see
    ///   <see cref="GetUserSettingsDir"/>).</param>
    /// <param name="baseDirectory">Directory containing the executable.</param>
    /// <param name="fileExists">Tests whether a file exists.</param>
    public static string GetSettingsFilePath(string fileName, HostOS os, string appDataDir,
            string baseDirectory, Func<string, bool> fileExists)
    {
        string portable = Path.Combine(baseDirectory, fileName);
        if (fileExists(portable)) {
            return portable;
        }
        string? userDir = GetUserSettingsDir(os, appDataDir);
        return userDir == null ? portable : Path.Combine(userDir, fileName);
    }

    /// <summary>
    /// Finds the directory holding read-only files shipped with the app (LegalStuff.txt).
    /// A packaged build has them beside the executable.  In a development build the
    /// executable is somewhere under the repository, so walk up to the directory that has
    /// the solution file.  Returns the executable's directory if neither is found.
    /// </summary>
    public static string FindAppResourceDir(string baseDirectory, string resourceFileName,
            string solutionFileName, Func<string, bool> fileExists)
    {
        if (fileExists(Path.Combine(baseDirectory, resourceFileName))) {
            return baseDirectory;
        }
        DirectoryInfo? dir = new DirectoryInfo(baseDirectory);
        while (dir != null) {
            if (fileExists(Path.Combine(dir.FullName, solutionFileName)) ||
                    fileExists(Path.Combine(dir.FullName, resourceFileName))) {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return baseDirectory;
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="path"/> so that a failure leaves
    /// the old file unchanged: writes a temporary file in the same folder, then renames it
    /// over the target.  Creates the folder if needed.
    /// </summary>
    /// <param name="writeTemp">Writes the temporary file (path, text); defaults to
    ///   <see cref="File.WriteAllText(string, string?)"/>.  Replaceable for testing.</param>
    public static void WriteFileAtomic(string path, string text,
            Action<string, string>? writeTemp = null)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) {
            Directory.CreateDirectory(dir);
        }
        string tempPath = path + ".tmp";
        try {
            (writeTemp ?? File.WriteAllText)(tempPath, text);
            File.Move(tempPath, path, overwrite: true);
        } catch {
            try { File.Delete(tempPath); } catch (Exception) { /* best effort */ }
            throw;
        }
    }
}
