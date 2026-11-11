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
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using cp2_avalonia.Common;

namespace cp2_avalonia.Tests;

/// <summary>
/// D3: finding a column divider from the pointer position.
/// </summary>
public class DataGridHeaderGripTests
{
    [Theory]
    [InlineData(0, DataGridHeaderGrip.Edge.Left)]
    [InlineData(4.9, DataGridHeaderGrip.Edge.Left)]
    [InlineData(5, DataGridHeaderGrip.Edge.Left)]
    [InlineData(5.5, DataGridHeaderGrip.Edge.None)]
    [InlineData(50, DataGridHeaderGrip.Edge.None)]
    [InlineData(94.5, DataGridHeaderGrip.Edge.None)]
    [InlineData(95, DataGridHeaderGrip.Edge.Right)]
    [InlineData(100, DataGridHeaderGrip.Edge.Right)]
    public void Classify_UsesTheEdgeBands(double x, DataGridHeaderGrip.Edge expected)
    {
        Assert.Equal(expected, DataGridHeaderGrip.Classify(x, 100));
    }

    [Fact]
    public void Classify_NarrowHeader_NearerEdgeWins()
    {
        Assert.Equal(DataGridHeaderGrip.Edge.Left, DataGridHeaderGrip.Classify(2, 8));
        Assert.Equal(DataGridHeaderGrip.Edge.Right, DataGridHeaderGrip.Classify(6, 8));
        Assert.Equal(DataGridHeaderGrip.Edge.None, DataGridHeaderGrip.Classify(1, 0));
    }

    private static List<DataGridColumn> Columns(params string[] headers)
    {
        var list = new List<DataGridColumn>();
        foreach (string h in headers) {
            list.Add(new DataGridTextColumn { Header = h });
        }
        return list;
    }

    [AvaloniaFact]
    public void ResolveTarget_RightEdge_IsTheHeadersOwnColumn()
    {
        var cols = Columns("A", "B", "C");
        Assert.Same(cols[1], DataGridHeaderGrip.ResolveTarget(cols, 1, DataGridHeaderGrip.Edge.Right));
    }

    [AvaloniaFact]
    public void ResolveTarget_LeftEdge_IsTheNearestVisibleColumnToTheLeft()
    {
        var cols = Columns("A", "B", "C", "D");
        Assert.Same(cols[1], DataGridHeaderGrip.ResolveTarget(cols, 2, DataGridHeaderGrip.Edge.Left));

        cols[1].IsVisible = false;      // hidden columns have no divider; skip to A
        Assert.Same(cols[0], DataGridHeaderGrip.ResolveTarget(cols, 2, DataGridHeaderGrip.Edge.Left));

        cols[0].IsVisible = false;      // nothing visible on the left
        Assert.Null(DataGridHeaderGrip.ResolveTarget(cols, 2, DataGridHeaderGrip.Edge.Left));
        Assert.Null(DataGridHeaderGrip.ResolveTarget(cols, 0, DataGridHeaderGrip.Edge.Left));
    }

    [AvaloniaFact]
    public void ResolveTarget_BadInput_IsNull()
    {
        var cols = Columns("A");
        Assert.Null(DataGridHeaderGrip.ResolveTarget(cols, -1, DataGridHeaderGrip.Edge.Right));
        Assert.Null(DataGridHeaderGrip.ResolveTarget(cols, 1, DataGridHeaderGrip.Edge.Right));
        Assert.Null(DataGridHeaderGrip.ResolveTarget(cols, 0, DataGridHeaderGrip.Edge.None));
    }

    [AvaloniaFact]
    public void FindColumnIndex_MatchesOnHeaderContent()
    {
        var cols = Columns("Filename", "Pathname", "Type");
        Assert.Equal(1, DataGridHeaderGrip.FindColumnIndex(cols, "Pathname"));
        Assert.Equal(-1, DataGridHeaderGrip.FindColumnIndex(cols, "Nope"));
        Assert.Equal(-1, DataGridHeaderGrip.FindColumnIndex(cols, null));
    }
}
