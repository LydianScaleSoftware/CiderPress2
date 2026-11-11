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

namespace cp2_avalonia.ViewModels;

/// <summary>
/// Pure decisions for "open this file" requests that the operating system delivers (the
/// command line, and the macOS file-activation event).  macOS delivers a file named on the
/// command line twice, so these requests have to be recognised as duplicates; see
/// <see cref="MainViewModel.OpenWorkFileAsync"/>.
/// </summary>
internal static class OpenRequestPolicy
{
    /// <summary>
    /// True if both paths name the same file.  Paths are made absolute first; the comparison
    /// ignores case except on Linux (the Windows and default macOS file systems do not
    /// distinguish case).
    /// </summary>
    public static bool IsSamePath(string? a, string? b)
    {
        if (a == null || b == null)
        {
            return false;
        }
        try
        {
            a = Path.GetFullPath(a);
            b = Path.GetFullPath(b);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException ||
                ex is PathTooLongException)
        {
            // Compare the strings as given.
        }
        return string.Equals(a, b, OperatingSystem.IsLinux() ?
            StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if an OS-delivered request for <paramref name="path"/> needs no work because
    /// that file is already open and the open file is at least as restricted as requested.
    /// A read-only request for a file open read-write is not satisfied: the file has to be
    /// reopened read-only.
    /// </summary>
    /// <param name="openPath">Path of the open work file, or null if none.</param>
    /// <param name="openCanWrite">True if the open work file is writable.</param>
    /// <param name="path">Requested path.</param>
    /// <param name="readOnly">True if the request asks for read-only access.</param>
    public static bool IsSatisfiedByOpenFile(string? openPath, bool openCanWrite, string path,
            bool readOnly)
    {
        return openPath != null && IsSamePath(openPath, path) && !(readOnly && openCanWrite);
    }
}
