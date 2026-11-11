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
using cp2_avalonia.Services;

namespace cp2_avalonia.Tests;

public class ClipTransferFilesTests
{
    private const string TEMP = "/tmp/cp2clip_1_abc";

    [Theory]
    [InlineData("x")]
    [InlineData("X")]
    [InlineData("f")]
    [InlineData("F")]
    [InlineData("xfer")]
    [InlineData("files")]
    [InlineData("e0000")]
    public void ForeignPaths_NeverOverlapTransferFiles(string foreignName)
    {
        string foreign = ClipTransferFiles.GetForeignPath(TEMP, foreignName);
        // Foreign entries live under .../files/, transfer files under .../xfer/.  Compare
        // case-insensitively too, because macOS and Windows file systems are.
        for (int i = 0; i < 5; i++) {
            string xfer = ClipTransferFiles.GetXferPath(TEMP, i);
            Assert.False(foreign.Equals(xfer, StringComparison.OrdinalIgnoreCase));
            Assert.False(foreign.StartsWith(xfer + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
            Assert.False(xfer.StartsWith(foreign + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TopLevelForeignPath_IsFirstComponent()
    {
        string expected = Path.Combine(TEMP, ClipTransferFiles.FOREIGN_SUBDIR, "DIR");
        Assert.Equal(expected, ClipTransferFiles.GetTopLevelForeignPath(TEMP, "DIR/SUB/FILE1"));
        Assert.Equal(expected, ClipTransferFiles.GetTopLevelForeignPath(TEMP, "DIR"));
        Assert.Equal(expected, ClipTransferFiles.GetTopLevelForeignPath(TEMP,
            "DIR" + Path.DirectorySeparatorChar + "FILE"));
    }
}
