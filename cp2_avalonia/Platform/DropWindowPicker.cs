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

namespace cp2_avalonia.Platform;

/// <summary>
/// Decides which of this process's windows a drag was released over.
/// </summary>
internal static class DropWindowPicker
{
    /// <summary>
    /// Finds our own window in the chain of X windows under the pointer.
    /// </summary>
    /// <param name="chain">Windows under the pointer, from the root's child (usually the
    ///   window manager's frame) down to the innermost.</param>
    /// <param name="ownWindows">Our windows, keyed by native handle.</param>
    /// <returns>The innermost of our windows in the chain, or default if the pointer is not
    ///   over one of ours (a window of another program, even one that overlaps ours).</returns>
    public static T? Pick<T>(IReadOnlyList<nint> chain, IReadOnlyDictionary<nint, T> ownWindows)
    {
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (ownWindows.TryGetValue(chain[i], out T? win))
            {
                return win;
            }
        }
        return default;
    }
}
