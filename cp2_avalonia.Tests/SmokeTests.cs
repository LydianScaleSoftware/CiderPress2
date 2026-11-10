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
using cp2_avalonia.Models;

namespace cp2_avalonia.Tests;

/// <summary>
/// Proves that the test project can reach types in cp2_avalonia, including internals
/// (via InternalsVisibleTo).
/// </summary>
public class SmokeTests
{
    [Fact]
    public void ClipInfo_RecognizesClipPrefix()
    {
        // CLIP_PREFIX is internal, so this also checks InternalsVisibleTo.
        Assert.True(ClipInfo.IsClipTextFromCP2(ClipInfo.CLIP_PREFIX + "{}"));
        Assert.False(ClipInfo.IsClipTextFromCP2("not a clip"));
        Assert.False(ClipInfo.IsClipTextFromCP2(null));
    }
}
