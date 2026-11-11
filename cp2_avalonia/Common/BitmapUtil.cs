/*
 * Copyright 2023 faddenSoft
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
using System;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace cp2_avalonia.Common {
    /// <summary>
    /// Bitmap conversion utilities for Avalonia.
    /// </summary>
    public static class BitmapUtil {
        /// <summary>
        /// Converts a FileConv IBitmap to an Avalonia WriteableBitmap.
        /// </summary>
        /// <param name="src">Source bitmap (indexed-8 or 32-bit direct color).</param>
        /// <returns>Newly-allocated WriteableBitmap in Bgra8888 format.</returns>
        public static WriteableBitmap ConvertToBitmap(FileConv.IBitmap src) {
            int width = src.Width;
            int height = src.Height;
            byte[] srcPixels = src.GetPixels();

            var wb = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using var fb = wb.Lock();
            int rowBytes = fb.RowBytes;
            IntPtr dest = fb.Address;

            if (src.IsIndexed8) {
                // Expand one row at a time straight into the framebuffer, instead of
                // building a second full-size BGRA array (64 MB for a large zoomed bitmap).
                int[] palette = BuildPalette(src.GetColors());
                byte[] rowBgra = new byte[width * 4];
                for (int row = 0; row < height; row++) {
                    ExpandIndexedRow(srcPixels.AsSpan(row * width, width), palette, rowBgra);
                    Marshal.Copy(rowBgra, 0, dest + row * rowBytes, rowBgra.Length);
                }
            } else {
                // Direct color: GetPixels() returns bytes in BGRA order (little-endian ARGB).
                CopyRowByRow(srcPixels, width, height, dest, rowBytes);
            }

            return wb;
        }

        /// <summary>
        /// Opaque black, ARGB.  Used for palette indices that the palette doesn't define.
        /// </summary>
        internal const int UNDEFINED_COLOR = unchecked((int)0xff000000);

        /// <summary>
        /// Copies the palette into a 256-entry array, so any byte value is a valid index.
        /// Entries the source palette doesn't have are opaque black.
        /// </summary>
        internal static int[] BuildPalette(int[]? colors) {
            int[] palette = new int[256];
            Array.Fill(palette, UNDEFINED_COLOR);
            if (colors != null) {
                Array.Copy(colors, palette, Math.Min(colors.Length, palette.Length));
            }
            return palette;
        }

        /// <summary>
        /// Expands one row of indexed-8 pixels to 32-bit BGRA using the palette.  Alpha is
        /// passed through unchanged, as it is for direct-color bitmaps.
        /// </summary>
        /// <param name="indices">Palette indices for the row.</param>
        /// <param name="palette">256-entry ARGB palette (see <see cref="BuildPalette"/>).</param>
        /// <param name="dstRow">Output, at least <c>indices.Length * 4</c> bytes.</param>
        internal static void ExpandIndexedRow(ReadOnlySpan<byte> indices, int[] palette,
                Span<byte> dstRow) {
            int dst = 0;
            foreach (byte index in indices) {
                int argb = palette[index];
                // BGRA order for PixelFormat.Bgra8888
                dstRow[dst++] = (byte)argb;
                dstRow[dst++] = (byte)(argb >> 8);
                dstRow[dst++] = (byte)(argb >> 16);
                dstRow[dst++] = (byte)(argb >> 24);
            }
        }

        /// <summary>
        /// Copies 32-bit BGRA pixel data row by row into an ILockedFramebuffer, respecting
        /// the framebuffer's RowBytes stride.
        /// </summary>
        private static void CopyRowByRow(byte[] src, int width, int height,
                IntPtr dest, int rowBytes) {
            int srcStride = width * 4;
            for (int row = 0; row < height; row++) {
                int srcOffset = row * srcStride;
                IntPtr destRow = dest + row * rowBytes;
                Marshal.Copy(src, srcOffset, destRow, srcStride);
            }
        }
    }
}
