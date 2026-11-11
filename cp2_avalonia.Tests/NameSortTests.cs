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
using System.Globalization;

using DiskArc;

using cp2_avalonia.Common;

namespace cp2_avalonia.Tests;

/// <summary>
/// D1: the natural, case-insensitive name order (decision X3).
/// </summary>
public class NameSortTests
{
    // The X3 example list, in the order Finder / Explorer would show it.
    private static readonly string[] X3Order = {
        "7", "007", "A-1", "A1", "apple", "Banana", "Cafe", "Café", "Cafz", "DIR", "DIR.BAK",
        "file1", "FILE2", "FILE10", "PRODOS", "ProDOS", "ZEBRA",
    };

    // The part of that list the fallback comparer can order the same way (it has no accent
    // folding).
    private static readonly string[] AsciiOrder = {
        "7", "007", "A-1", "A1", "apple", "Banana", "DIR", "DIR.BAK",
        "file1", "FILE2", "FILE10", "PRODOS", "ProDOS", "ZEBRA",
    };

    /// <summary>Runs a test body under en-US, so the expected order is the documented one.</summary>
    private static void UnderEnUs(Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try {
            body();
        } finally {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static string[] Shuffled(string[] list, int seed)
    {
        Random rnd = new Random(seed);
        return list.OrderBy(_ => rnd.Next()).ToArray();
    }

    [Fact]
    public void X3List_SortsInDocumentedOrder_Icu()
    {
        if (!NameSort.IcuNumericSupported) {
            return;     // invariant-globalization mode: the fallback tests cover it
        }
        UnderEnUs(() => {
            string[] sorted = Shuffled(X3Order, 1)
                .OrderBy(s => s, Comparer<string>.Create((a, b) => NameSort.CompareNames(a, b, false)))
                .ToArray();
            Assert.Equal(X3Order, sorted);
        });
    }

    [Fact]
    public void AsciiList_SortsTheSameWay_Fallback()
    {
        for (int seed = 0; seed < 5; seed++) {
            string[] sorted = Shuffled(AsciiOrder, seed)
                .OrderBy(s => s, Comparer<string>.Create((a, b) => NameSort.CompareNames(a, b, true)))
                .ToArray();
            Assert.Equal(AsciiOrder, sorted);
        }
    }

    [Fact]
    public void Fallback_MatchesIcu_ForAsciiNames()
    {
        if (!NameSort.IcuNumericSupported) {
            return;
        }
        UnderEnUs(() => {
            for (int i = 0; i < AsciiOrder.Length; i++) {
                for (int j = 0; j < AsciiOrder.Length; j++) {
                    int icu = Math.Sign(NameSort.CompareNames(AsciiOrder[i], AsciiOrder[j], false));
                    int fb = Math.Sign(NameSort.CompareNames(AsciiOrder[i], AsciiOrder[j], true));
                    Assert.True(icu == fb,
                        $"'{AsciiOrder[i]}' vs '{AsciiOrder[j]}': ICU {icu}, fallback {fb}");
                }
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Numbers_CompareByValue(bool fallback)
    {
        if (!fallback && !NameSort.IcuNumericSupported) {
            return;
        }
        Assert.True(NameSort.CompareNames("FILE2", "FILE10", fallback) < 0);
        Assert.True(NameSort.CompareNames("file9", "FILE10", fallback) < 0);
        Assert.True(NameSort.CompareNames("7", "007", fallback) < 0);      // value ties by length
        Assert.True(NameSort.CompareNames("007x", "7y", fallback) < 0);    // later parts decide first
    }

    [Fact]
    public void Fallback_HandlesDigitRunsLongerThanAnyInteger()
    {
        Assert.True(NameSort.CompareNames("a99999999999999999999", "a100000000000000000000",
            true) < 0);
        Assert.True(NameSort.CompareNames("a100000000000000000000", "a99999999999999999999",
            true) > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamesEqualIgnoringCase_FallBackToOrdinal(bool fallback)
    {
        if (!fallback && !NameSort.IcuNumericSupported) {
            return;
        }
        Assert.True(NameSort.CompareNames("PRODOS", "ProDOS", fallback) < 0);
        Assert.True(NameSort.CompareNames("ProDOS", "PRODOS", fallback) > 0);
        Assert.Equal(0, NameSort.CompareNames("ProDOS", "ProDOS", fallback));
    }

    [Theory]
    [InlineData('/', false)]
    [InlineData('/', true)]
    [InlineData(':', false)]
    [InlineData(':', true)]
    public void PathNames_CompareOneSegmentAtATime(char sep, bool fallback)
    {
        if (!fallback && !NameSort.IcuNumericSupported) {
            return;
        }
        string[] expected = { "DIR", $"DIR{sep}FILE", $"DIR{sep}FILE2", $"DIR{sep}FILE10",
            "DIR.BAK" };
        string[] sorted = Shuffled(expected, 3)
            .OrderBy(s => s, Comparer<string>.Create(
                (a, b) => NameSort.ComparePathNames(a, b, sep, fallback)))
            .ToArray();
        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void PathNames_ShorterPathFirstWhenPrefixEqual()
    {
        Assert.True(NameSort.ComparePathNames("A/B", "A/B/C", '/', true) < 0);
        Assert.True(NameSort.ComparePathNames("A/B/C", "A/B", '/', true) > 0);
        Assert.Equal(0, NameSort.ComparePathNames("A/B", "A/B", '/', true));
    }

    [Fact]
    public void PathNames_NoSeparator_TreatsSlashAsPartOfTheName()
    {
        // DOS 3.3 / Pascal style: no directories, so no splitting.
        char none = IFileEntry.NO_DIR_SEP;
        Assert.True(NameSort.ComparePathNames("A/Z", "A.B", none, true) < 0 ==
            NameSort.CompareNames("A/Z", "A.B", true) < 0);
    }

    [Fact]
    public void Codes_IgnoreCase_AndKeepHexOrder()
    {
        string[] hex = { "$1000", "$2A00", "$FF00" };
        string[] sorted = Shuffled(hex, 2).OrderBy(s => s,
            Comparer<string>.Create(NameSort.CompareCodes)).ToArray();
        Assert.Equal(hex, sorted);
        Assert.Equal(0, NameSort.CompareCodes("abc", "ABC"));
        Assert.True(NameSort.CompareCodes("$2a00", "$2B00") < 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomNames_AreATotalOrder(bool fallback)
    {
        if (!fallback && !NameSort.IcuNumericSupported) {
            return;
        }
        UnderEnUs(() => {
            const string alphabet = "aAbB09 -.éÉ12";
            Random rnd = new Random(1234);
            string Make() {
                int len = rnd.Next(0, 6);
                return new string(Enumerable.Range(0, len)
                    .Select(_ => alphabet[rnd.Next(alphabet.Length)]).ToArray());
            }
            string[] names = Enumerable.Range(0, 120).Select(_ => Make()).ToArray();

            int Cmp(string a, string b) => Math.Sign(NameSort.CompareNames(a, b, fallback));
            foreach (string a in names) {
                Assert.Equal(0, Cmp(a, a));
                foreach (string b in names) {
                    Assert.Equal(-Cmp(a, b), Cmp(b, a));       // antisymmetric
                }
            }
            for (int n = 0; n < 4000; n++) {
                string a = names[rnd.Next(names.Length)];
                string b = names[rnd.Next(names.Length)];
                string c = names[rnd.Next(names.Length)];
                if (Cmp(a, b) <= 0 && Cmp(b, c) <= 0) {
                    Assert.True(Cmp(a, c) <= 0, $"'{a}' <= '{b}' <= '{c}' but '{a}' > '{c}'");
                }
            }
        });
    }
}
