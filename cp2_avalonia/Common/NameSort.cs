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
using System.Globalization;

using DiskArc;

namespace cp2_avalonia.Common;

/// <summary>
/// Orders file names the way Finder and Explorer do: ignoring case, numbers in numeric order
/// (<c>FILE2</c> before <c>FILE10</c>), accented letters next to their base letter.  Used by the
/// file list's Filename and Pathname columns; kept separate so other lists can share it.
/// </summary>
/// <remarks>
/// <para>The comparison is <see cref="CompareOptions.IgnoreCase"/> |
/// <see cref="CompareOptions.NumericOrdering"/> on the current culture.  Numeric ordering needs
/// ICU: in invariant-globalization mode (and anywhere else ICU is missing) the option is
/// silently ignored, so we probe for it once and fall back to a small hand-written natural
/// comparer.  The fallback has no accent folding (<c>Café</c> sorts after <c>Cafz</c>), which
/// is the known limit of that mode.</para>
/// <para>Names that compare equal (<c>PRODOS</c> and <c>ProDOS</c>, or <c>7</c> and
/// <c>007</c>, which numeric ordering reads as the same number) are ordered shorter first and
/// then by <see cref="string.CompareOrdinal(string, string)"/>, so the order is total and
/// stable.</para>
/// </remarks>
internal static class NameSort
{
    private const CompareOptions NATURAL_OPTIONS =
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering;

    /// <summary>
    /// True if the platform's collation honours <see cref="CompareOptions.NumericOrdering"/>.
    /// </summary>
    internal static bool IcuNumericSupported { get; } = ProbeNumericOrdering();

    private static bool ProbeNumericOrdering()
    {
        try {
            return CultureInfo.CurrentCulture.CompareInfo.Compare("2", "10", NATURAL_OPTIONS) < 0;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// Compares two file names (single path components).
    /// </summary>
    public static int CompareNames(string? a, string? b) =>
        CompareNames(a, b, !IcuNumericSupported);

    /// <summary>
    /// Compares two file names, choosing the algorithm explicitly (for tests).
    /// </summary>
    internal static int CompareNames(string? a, string? b, bool useFallback)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        int cmp = CompareSegment(a.AsSpan(), b.AsSpan(), useFallback);
        if (cmp == 0) {
            cmp = TieBreak(a, b);
        }
        return cmp;
    }

    /// <summary>
    /// Orders two names that the collation considers equal: shorter first (so <c>7</c> comes
    /// before <c>007</c>), then by code point (so <c>PRODOS</c> comes before <c>ProDOS</c>).
    /// </summary>
    private static int TieBreak(string a, string b)
    {
        if (a.Length != b.Length) {
            return a.Length.CompareTo(b.Length);
        }
        return string.CompareOrdinal(a, b);
    }

    /// <summary>
    /// Compares two full path names one segment at a time, so that a folder's contents stay
    /// together (<c>DIR</c>, <c>DIR/FILE</c>, then <c>DIR.BAK</c>).
    /// </summary>
    /// <param name="separator">Directory separator of the file system, or
    ///   <see cref="IFileEntry.NO_DIR_SEP"/> if it has none.</param>
    public static int ComparePathNames(string? a, string? b, char separator) =>
        ComparePathNames(a, b, separator, !IcuNumericSupported);

    /// <summary>
    /// Compares two full path names, choosing the algorithm explicitly (for tests).
    /// </summary>
    internal static int ComparePathNames(string? a, string? b, char separator, bool useFallback)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        ReadOnlySpan<char> restA = a.AsSpan();
        ReadOnlySpan<char> restB = b.AsSpan();
        bool split = separator != IFileEntry.NO_DIR_SEP;
        while (true) {
            ReadOnlySpan<char> segA = Take(ref restA, separator, split, out bool moreA);
            ReadOnlySpan<char> segB = Take(ref restB, separator, split, out bool moreB);
            int cmp = CompareSegment(segA, segB, useFallback);
            if (cmp != 0) {
                return cmp;
            }
            if (!moreA || !moreB) {
                // One path ran out of segments; the shorter one comes first.
                if (moreA != moreB) {
                    return moreA ? 1 : -1;
                }
                break;
            }
        }
        return TieBreak(a, b);
    }

    /// <summary>
    /// Removes the first segment from <paramref name="rest"/> and returns it.
    /// <paramref name="more"/> is true if a separator followed it (so more segments remain).
    /// </summary>
    private static ReadOnlySpan<char> Take(ref ReadOnlySpan<char> rest, char separator,
        bool split, out bool more)
    {
        int idx = split ? rest.IndexOf(separator) : -1;
        if (idx < 0) {
            ReadOnlySpan<char> all = rest;
            rest = ReadOnlySpan<char>.Empty;
            more = false;
            return all;
        }
        ReadOnlySpan<char> seg = rest.Slice(0, idx);
        rest = rest.Slice(idx + 1);
        more = true;
        return seg;
    }

    private static int CompareSegment(ReadOnlySpan<char> a, ReadOnlySpan<char> b,
        bool useFallback)
    {
        if (useFallback) {
            return CompareNaturalFallback(a, b);
        }
        return CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, NATURAL_OPTIONS);
    }

    /// <summary>
    /// Compares two code-like strings (type, aux type, format, access) ignoring case only.
    /// Natural order is wrong for these: an aux type is hex, so <c>$2A00</c> would sort before
    /// <c>$1000</c> if the digits were read as a number.
    /// </summary>
    public static int CompareCodes(string? a, string? b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- Fallback natural comparer ----

    /// <summary>
    /// Natural comparison without ICU: runs of digits compare by value, other characters
    /// ignoring case, with punctuation before digits before letters (as ICU does for ASCII).
    /// </summary>
    internal static int CompareNaturalFallback(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length) {
            char ca = a[i];
            char cb = b[j];
            if (IsDigit(ca) && IsDigit(cb)) {
                int endA = i;
                while (endA < a.Length && IsDigit(a[endA])) {
                    endA++;
                }
                int endB = j;
                while (endB < b.Length && IsDigit(b[endB])) {
                    endB++;
                }
                ReadOnlySpan<char> runA = a.Slice(i, endA - i);
                ReadOnlySpan<char> runB = b.Slice(j, endB - j);
                int cmp = CompareDigitRuns(runA, runB);
                if (cmp != 0) {
                    return cmp;
                }
                i = endA;
                j = endB;
                continue;
            }
            int wa = Weight(ca);
            int wb = Weight(cb);
            if (wa != wb) {
                return wa.CompareTo(wb);
            }
            i++;
            j++;
        }
        if (i < a.Length) {
            return 1;
        }
        if (j < b.Length) {
            return -1;
        }
        return 0;       // equal as far as natural order goes; the caller breaks the tie
    }

    private static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

    /// <summary>
    /// Compares two runs of ASCII digits by numeric value, without parsing (any length).
    /// </summary>
    private static int CompareDigitRuns(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a.Length != b.Length) {
            return a.Length.CompareTo(b.Length);
        }
        return a.SequenceCompareTo(b);
    }

    /// <summary>
    /// Sort weight of a character that is not part of a digit pair: class in the high bits
    /// (punctuation, digit, letter, other), upper-cased code in the low bits.
    /// </summary>
    private static int Weight(char ch)
    {
        if (IsDigit(ch)) {
            return (1 << 16) | ch;
        }
        char up = char.ToUpperInvariant(ch);
        if (up >= 'A' && up <= 'Z') {
            return (2 << 16) | up;
        }
        if (ch < 0x80) {
            return ch;                  // ASCII punctuation, space and controls
        }
        return (3 << 16) | up;
    }
}
