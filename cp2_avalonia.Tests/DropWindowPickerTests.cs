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
using cp2_avalonia.Platform;

namespace cp2_avalonia.Tests;

/// <summary>
/// C2: deciding which of our windows (if any) a Linux drag was released over.
/// </summary>
public class DropWindowPickerTests
{
    private const nint MAIN = 0x100;
    private const nint VIEWER = 0x200;
    private static readonly Dictionary<nint, string> Own = new()
    {
        [MAIN] = "main",
        [VIEWER] = "viewer",
    };

    [Fact]
    public void OwnWindowInChain()
    {
        // WM frame, then the Avalonia window.
        Assert.Equal("main", DropWindowPicker.Pick(new nint[] { 0x1, MAIN }, Own));
    }

    [Fact]
    public void ForeignWindowOverOurs_IsNotOurs()
    {
        // A terminal (frame + client) under the pointer, overlapping the main window: the
        // chain follows the stacking order and never reaches the main window.
        Assert.Null(DropWindowPicker.Pick(new nint[] { 0x5, 0x6 }, Own));
    }

    [Fact]
    public void ViewerOverMain_PicksViewer()
    {
        // The chain is of the topmost window at the pointer, so only the viewer is in it.
        Assert.Equal("viewer", DropWindowPicker.Pick(new nint[] { 0x3, VIEWER, 0x201 }, Own));
    }

    [Fact]
    public void InnermostWins()
    {
        // Not expected in practice, but the innermost match must win.
        Assert.Equal("viewer", DropWindowPicker.Pick(new nint[] { MAIN, VIEWER }, Own));
    }

    [Fact]
    public void EmptyChain()
    {
        Assert.Null(DropWindowPicker.Pick(Array.Empty<nint>(), Own));
    }
}
