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
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Tests;

public class FindMatchTests
{
    // "ab" occurs at 0, 4 and 8.
    private const string TEXT = "ab..ab..ab";
    private const string SEARCH = "ab";

    /// <summary>Applies one Find step, the way DoFind tracks the current match.</summary>
    private static (int start, int end) Step((int start, int end) cur, bool forward)
    {
        int idx = FileViewerViewModel.FindMatch(TEXT, SEARCH, cur.start, cur.end, forward);
        Assert.True(idx >= 0);
        return (idx, idx + SEARCH.Length);
    }

    [Fact]
    public void Next_VisitsEachHitAndWraps()
    {
        var cur = (start: 0, end: 0);
        cur = Step(cur, true); Assert.Equal(0, cur.start);
        cur = Step(cur, true); Assert.Equal(4, cur.start);
        cur = Step(cur, true); Assert.Equal(8, cur.start);
        cur = Step(cur, true); Assert.Equal(0, cur.start);      // wrap
    }

    [Fact]
    public void Previous_MovesBackAndWraps()
    {
        // Next x2 puts us on the second hit; Previous must move off it.
        var cur = (start: 0, end: 0);
        cur = Step(cur, true);
        cur = Step(cur, true);
        Assert.Equal(4, cur.start);
        cur = Step(cur, false); Assert.Equal(0, cur.start);
        cur = Step(cur, false); Assert.Equal(8, cur.start);     // wrap to the last hit
        cur = Step(cur, false); Assert.Equal(4, cur.start);
    }

    [Fact]
    public void SingleHit_ReturnsItself_AndNoHitReturnsMinusOne()
    {
        Assert.Equal(1, FileViewerViewModel.FindMatch("xabx", "ab", 1, 3, true));
        Assert.Equal(1, FileViewerViewModel.FindMatch("xabx", "ab", 1, 3, false));
        Assert.Equal(-1, FileViewerViewModel.FindMatch("xyz", "ab", 0, 0, true));
        Assert.Equal(-1, FileViewerViewModel.FindMatch("", "ab", 0, 0, false));
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        Assert.Equal(2, FileViewerViewModel.FindMatch("..AB..", "ab", 0, 0, true));
    }
}
