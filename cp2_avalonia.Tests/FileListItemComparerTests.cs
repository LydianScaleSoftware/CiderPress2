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
using System.Text.RegularExpressions;

using cp2_avalonia.Models;

namespace cp2_avalonia.Tests;

/// <summary>
/// D1: the file list's column comparer, on synthetic items.
/// </summary>
public class FileListItemComparerTests
{
    private static FileListItem Item(string path, long dataLen = 0, long rawLen = 0,
        long rsrcLen = 0, long totalSize = 0, string type = "", string auxType = "",
        string dataFmt = "", string rsrcFmt = "", string access = "", DateTime when = default,
        char sep = '/')
    {
        string name = path.Contains(sep) ? path.Substring(path.LastIndexOf(sep) + 1) : path;
        return new FileListItem(name, path, sep, type, auxType, when, dataLen, rawLen, dataFmt,
            rsrcLen, rsrcFmt, totalSize, access);
    }

    private static int Cmp(string column, FileListItem a, FileListItem b, bool asc = true) =>
        new FileListItem.ItemComparer(column, asc).Compare(a, b);

    private static List<string> Order(string column, bool asc, params FileListItem[] items) =>
        items.OrderBy(i => i, new FileListItem.ItemComparer(column, asc))
            .Select(i => i.PathName).ToList();

    [Theory]
    [InlineData("DataLength")]
    [InlineData("RawDataLength")]
    [InlineData("RsrcLength")]
    [InlineData("TotalSize")]
    public void SizeColumns_DoNotOverflowOnLargeDifferences(string column)
    {
        FileListItem Make(string path, long len) => column switch {
            "DataLength" => Item(path, dataLen: len),
            "RawDataLength" => Item(path, rawLen: len),
            "RsrcLength" => Item(path, rsrcLen: len),
            _ => Item(path, totalSize: len),
        };
        FileListItem small = Make("small", 100);
        FileListItem big = Make("big", 3_000_000_000);
        FileListItem huge = Make("huge", 4L * 1024 * 1024 * 1024);
        FileListItem zero = Make("zero", 0);

        Assert.True(Cmp(column, big, small) > 0);       // was negative: (int)(3e9 - 100)
        Assert.True(Cmp(column, small, big) < 0);
        Assert.True(Cmp(column, huge, zero) > 0);       // was 0: (int)(4 GB)
        Assert.Equal(new[] { "zero", "small", "big", "huge" },
            Order(column, true, huge, small, zero, big));
        Assert.Equal(new[] { "huge", "big", "small", "zero" },
            Order(column, false, huge, small, zero, big));
    }

    [Fact]
    public void RsrcLength_SortsOnTheDisplayedValue()
    {
        // A MacZip entry reports no resource length of its own (FileEntry says 0); the item
        // carries the one from the AppleDouble header, and that is what must sort.
        FileListItem a = Item("a", rsrcLen: 5000);
        FileListItem b = Item("b", rsrcLen: 40);
        FileListItem c = Item("c", rsrcLen: 0);
        Assert.Equal(new[] { "c", "b", "a" }, Order("RsrcLength", true, a, b, c));
    }

    [Fact]
    public void FileName_IsNaturalAndCaseInsensitive()
    {
        FileListItem[] items = { Item("ZEBRA"), Item("file10"), Item("apple"), Item("FILE2"),
            Item("Banana") };
        Assert.Equal(new[] { "apple", "Banana", "FILE2", "file10", "ZEBRA" },
            Order("FileName", true, items));
        Assert.Equal(new[] { "ZEBRA", "file10", "FILE2", "Banana", "apple" },
            Order("FileName", false, items));
    }

    [Fact]
    public void PathName_KeepsAFoldersContentsTogether()
    {
        FileListItem[] items = { Item("DIR.BAK"), Item("DIR/FILE"), Item("DIR"), Item("A") };
        Assert.Equal(new[] { "A", "DIR", "DIR/FILE", "DIR.BAK" }, Order("PathName", true, items));

        // HFS uses ':'.
        FileListItem[] hfs = { Item("DIR.BAK", sep: ':'), Item("DIR:FILE", sep: ':'),
            Item("DIR", sep: ':') };
        Assert.Equal(new[] { "DIR", "DIR:FILE", "DIR.BAK" }, Order("PathName", true, hfs));
    }

    [Fact]
    public void AuxType_SortsInHexOrder()
    {
        FileListItem[] items = { Item("c", auxType: "$FF00"), Item("a", auxType: "$1000"),
            Item("b", auxType: "$2A00") };
        Assert.Equal(new[] { "a", "b", "c" }, Order("AuxType", true, items));
    }

    [Fact]
    public void CodeColumns_IgnoreCase()
    {
        FileListItem a = Item("a", type: "txt");
        FileListItem b = Item("b", type: "BIN");
        // Case-sensitive ordinal would put "BIN" first and "txt" last either way; what matters
        // is that 'txt' and 'TXT' tie on the primary key and fall through to the pathname.
        Assert.True(Cmp("Type", a, b) > 0);
        FileListItem upper = Item("z", type: "TXT");
        Assert.True(Cmp("Type", a, upper) < 0);     // equal types: pathname "a" < "z"
    }

    [Fact]
    public void EqualKeys_FallBackToPathName_InTheSameDirection()
    {
        FileListItem x = Item("alpha", dataLen: 10);
        FileListItem y = Item("beta", dataLen: 10);
        Assert.True(Cmp("DataLength", x, y, asc: true) < 0);
        Assert.True(Cmp("DataLength", x, y, asc: false) > 0);
    }

    [Fact]
    public void ModDate_SortsChronologically()
    {
        FileListItem old = Item("old", when: new DateTime(1985, 1, 1));
        FileListItem recent = Item("recent", when: new DateTime(2024, 6, 1));
        Assert.True(Cmp("ModDate", old, recent) < 0);
    }

    [Fact]
    public void UnknownColumnPath_IsReported()
    {
        Assert.False(FileListItem.TryGetColumnId("Bogus", out _));
        Assert.False(FileListItem.TryGetColumnId(null, out _));
        Assert.False(FileListItem.TryGetColumnId("Filename", out _));   // header text, not a path
        Assert.True(FileListItem.TryGetColumnId("RsrcLength", out var id));
        Assert.Equal(FileListItem.ColumnId.RsrcLength, id);
    }

    [Fact]
    public void EveryColumnInTheGrid_HasAKnownSortMemberPath()
    {
        string? dir = AppContext.BaseDirectory;
        string xamlPath = string.Empty;
        while (dir != null) {
            string candidate = Path.Combine(dir, "cp2_avalonia", "Views", "MainWindow.axaml");
            if (File.Exists(candidate)) {
                xamlPath = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotEqual(string.Empty, xamlPath);

        string xaml = File.ReadAllText(xamlPath);
        int start = xaml.IndexOf("Name=\"fileListDataGrid\"", StringComparison.Ordinal);
        int colsStart = xaml.IndexOf("<DataGrid.Columns>", start, StringComparison.Ordinal);
        int colsEnd = xaml.IndexOf("</DataGrid.Columns>", colsStart, StringComparison.Ordinal);
        Assert.True(start > 0 && colsStart > start && colsEnd > colsStart);
        string columns = xaml.Substring(colsStart, colsEnd - colsStart);

        MatchCollection cols = Regex.Matches(columns, @"<DataGrid(?:Template|Text)Column(?=[\s>])[^>]*>");
        Assert.Equal(13, cols.Count);
        var seen = new HashSet<FileListItem.ColumnId>();
        foreach (Match m in cols) {
            Match path = Regex.Match(m.Value, @"SortMemberPath=""([^""]*)""");
            Assert.True(path.Success, "Column without SortMemberPath: " + m.Value);
            Assert.True(FileListItem.TryGetColumnId(path.Groups[1].Value, out var id),
                "Unknown SortMemberPath: " + path.Groups[1].Value);
            Assert.True(seen.Add(id), "Duplicate SortMemberPath: " + path.Groups[1].Value);
        }
    }
}
