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
using cp2_avalonia.Common;

namespace cp2_avalonia.Tests;

public class AnimatedGifEncoderTests
{
    [Theory]
    [InlineData(0, 2)]          // 0 would mean "unspecified" to viewers
    [InlineData(5, 2)]          // rounds to 1 (or 0), below the minimum
    [InlineData(20, 2)]
    [InlineData(100, 10)]
    [InlineData(1000000, 65535)]
    public void ToGifDelay_ClampsToViewerFriendlyRange(int msec, int expectedHundredths)
    {
        Assert.Equal(expectedHundredths, AnimatedGifEncoder.ToGifDelay(msec));
    }
}
