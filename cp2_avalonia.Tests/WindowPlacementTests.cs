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
using Avalonia;
using Avalonia.Controls;

using cp2_avalonia.Common;

namespace cp2_avalonia.Tests;

public class WindowPlacementTests
{
    private static readonly (PixelRect, double)[] sOneScreen =
        { (new PixelRect(0, 0, 1920, 1080), 1.0) };

    [Fact]
    public void BuildSavedPos_NormalState_SavesCurrentBounds()
    {
        var pos = WindowPlacement.BuildSavedPos(WindowState.Normal, new PixelPoint(50, 60),
            800, 600, (new PixelPoint(1, 1), 10, 10));
        Assert.Equal((50, 60, 800.0, 600.0, "Normal"), (pos.X, pos.Y, pos.Width, pos.Height, pos.State));
    }

    [Theory]
    [InlineData(WindowState.Maximized, "Maximized")]
    [InlineData(WindowState.Minimized, "Normal")]
    [InlineData(WindowState.FullScreen, "Normal")]
    public void BuildSavedPos_NonNormalState_SavesNormalBounds(WindowState state, string saved)
    {
        var pos = WindowPlacement.BuildSavedPos(state, new PixelPoint(-32000, -32000),
            1920, 1080, (new PixelPoint(100, 120), 900, 700));
        Assert.Equal((100, 120, 900.0, 700.0, saved), (pos.X, pos.Y, pos.Width, pos.Height, pos.State));
    }

    [Theory]
    [InlineData("Normal", WindowState.Normal)]
    [InlineData("Maximized", WindowState.Maximized)]
    [InlineData("Minimized", WindowState.Normal)]
    [InlineData("FullScreen", WindowState.Normal)]
    [InlineData("junk", WindowState.Normal)]
    [InlineData(null, WindowState.Normal)]
    public void ParseSavedState_AcceptsOnlyNormalAndMaximized(string? text, WindowState expected)
    {
        Assert.Equal(expected, WindowPlacement.ParseSavedState(text));
    }

    [Fact]
    public void OnScreen_WindowFullyInside_IsAccepted()
    {
        Assert.True(WindowPlacement.IsPlacementOnScreen(100, 100, 800, 600, sOneScreen,
            out double w, out double h));
        Assert.Equal((800.0, 600.0), (w, h));
    }

    [Theory]
    [InlineData(10000, 100)]        // second monitor unplugged
    [InlineData(-32000, -32000)]    // minimized on Windows
    [InlineData(1900, 100)]         // only 20 px visible
    [InlineData(100, 1070)]
    public void OnScreen_MostlyOffScreen_IsRejected(int x, int y)
    {
        Assert.False(WindowPlacement.IsPlacementOnScreen(x, y, 800, 600, sOneScreen,
            out _, out _));
    }

    [Fact]
    public void OnScreen_PartlyVisible_IsAccepted()
    {
        // 300 px of the window is on screen.
        Assert.True(WindowPlacement.IsPlacementOnScreen(1620, 100, 800, 600, sOneScreen,
            out _, out _));
    }

    [Fact]
    public void OnScreen_SizeIsClampedToTheScreen()
    {
        Assert.True(WindowPlacement.IsPlacementOnScreen(0, 0, 4000, 3000, sOneScreen,
            out double w, out double h));
        Assert.Equal((1920.0, 1080.0), (w, h));
    }

    [Fact]
    public void OnScreen_SecondScreenAndScaling()
    {
        var screens = new (PixelRect, double)[] {
            (new PixelRect(0, 0, 1920, 1080), 1.0),
            (new PixelRect(1920, 0, 3840, 2160), 2.0),
        };
        // 800x600 DIPs at 2x is 1600x1200 pixels, entirely on the second screen.
        Assert.True(WindowPlacement.IsPlacementOnScreen(2000, 100, 800, 600, screens,
            out double w, out double h));
        Assert.Equal((800.0, 600.0), (w, h));
        // Larger than the second screen in DIPs (3840 / 2 = 1920 wide, 1080 high).
        Assert.True(WindowPlacement.IsPlacementOnScreen(2000, 100, 3000, 2000, screens,
            out w, out h));
        Assert.Equal((1920.0, 1080.0), (w, h));
    }
}
