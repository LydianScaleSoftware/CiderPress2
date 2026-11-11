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

public class BitmapUtilTests
{
    private static byte[] Expand(byte[] indices, int[] palette, int width)
    {
        // Same row-by-row loop as BitmapUtil.ConvertToBitmap, into a plain array.
        int height = indices.Length / width;
        int[] pal = BitmapUtil.BuildPalette(palette);
        byte[] result = new byte[indices.Length * 4];
        for (int row = 0; row < height; row++) {
            BitmapUtil.ExpandIndexedRow(indices.AsSpan(row * width, width), pal,
                result.AsSpan(row * width * 4, width * 4));
        }
        return result;
    }

    [Fact]
    public void ShortPalette_OutOfRangeIndices_AreOpaqueBlack_NoException()
    {
        int[] palette = { unchecked((int)0xffff0000), unchecked((int)0xff00ff00) };  // red, green
        byte[] result = Expand(new byte[] { 0, 1, 2, 255 }, palette, 4);

        // BGRA order.
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, result[0..4]);      // red
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, result[4..8]);      // green
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, result[8..12]);       // index 2: black
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, result[12..16]);      // index 255: black
    }

    [Fact]
    public void PaletteAlpha_PassesThroughUnchanged()
    {
        int[] palette = { unchecked((int)0x80102030), 0x00112233 };
        byte[] result = Expand(new byte[] { 0, 1 }, palette, 2);

        Assert.Equal(new byte[] { 0x30, 0x20, 0x10, 0x80 }, result[0..4]);   // alpha 128 kept
        Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 0x00 }, result[4..8]);   // alpha 0 kept
    }

    [Fact]
    public void RowByRow_MatchesWholeImageExpansion()
    {
        int[] palette = new int[16];
        for (int i = 0; i < palette.Length; i++) palette[i] = unchecked((int)(0xff000000 | (uint)(i * 0x111111)));
        var rng = new Random(1234);
        byte[] indices = new byte[7 * 5];
        rng.NextBytes(indices);
        for (int i = 0; i < indices.Length; i++) indices[i] &= 0x0f;

        byte[] viaRows = Expand(indices, palette, 7);

        // Reference: expand the whole array in a single call.
        int[] pal = BitmapUtil.BuildPalette(palette);
        byte[] whole = new byte[indices.Length * 4];
        BitmapUtil.ExpandIndexedRow(indices, pal, whole);
        Assert.Equal(whole, viaRows);
    }
}
