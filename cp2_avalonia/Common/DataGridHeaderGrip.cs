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
using System;
using System.Collections.Generic;

using Avalonia.Controls;

namespace cp2_avalonia.Common;

/// <summary>
/// Works out whether a press on a DataGrid column header is on a column divider (the resize
/// "grip"), and which column that divider resizes.
/// </summary>
/// <remarks>
/// The DataGrid's header has no grip control to hit-test (no <c>Thumb</c>, no named gripper
/// element): it resizes when the pointer is within a few pixels of a header edge, so we do
/// the same test on the pointer position.
/// </remarks>
public static class DataGridHeaderGrip
{
    /// <summary>
    /// Width of the resize band at each edge of a header, in device-independent pixels.
    /// The same value the DataGrid itself uses to show its resize cursor.
    /// </summary>
    public const double RESIZE_BAND = 5;

    /// <summary>Which edge of a header, if any, a position is on.</summary>
    public enum Edge { None, Left, Right }

    /// <summary>
    /// Classifies a pointer position within a header.
    /// </summary>
    /// <param name="x">Pointer X, relative to the header's left edge.</param>
    /// <param name="headerWidth">Width of the header.</param>
    public static Edge Classify(double x, double headerWidth)
    {
        if (headerWidth <= 0) {
            return Edge.None;
        }
        // A header narrower than two bands: the nearer edge wins.
        double fromLeft = x;
        double fromRight = headerWidth - x;
        if (fromRight <= RESIZE_BAND && fromRight <= fromLeft) {
            return Edge.Right;
        }
        if (fromLeft <= RESIZE_BAND) {
            return Edge.Left;
        }
        return Edge.None;
    }

    /// <summary>
    /// Picks the column a divider resizes: the right edge of a header resizes that header's
    /// column; the left edge resizes the nearest visible column to its left (null if there is
    /// none, i.e. the left edge of the first column).
    /// </summary>
    /// <param name="columns">The grid's columns, in display order.</param>
    /// <param name="headerIndex">Index in <paramref name="columns"/> of the pressed header.</param>
    /// <param name="edge">Edge that was hit.</param>
    public static DataGridColumn? ResolveTarget(IReadOnlyList<DataGridColumn> columns,
        int headerIndex, Edge edge)
    {
        if (headerIndex < 0 || headerIndex >= columns.Count) {
            return null;
        }
        switch (edge) {
            case Edge.Right:
                return columns[headerIndex];
            case Edge.Left:
                for (int i = headerIndex - 1; i >= 0; i--) {
                    if (columns[i].IsVisible) {
                        return columns[i];
                    }
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Finds the index of the column that owns a header control.  The header's content is
    /// bound to the column's <c>Header</c>, which is unique within each grid we use this on.
    /// Returns -1 if there is no such column.
    /// </summary>
    public static int FindColumnIndex(IReadOnlyList<DataGridColumn> columns, object? headerContent)
    {
        for (int i = 0; i < columns.Count; i++) {
            if (Equals(columns[i].Header, headerContent)) {
                return i;
            }
        }
        return -1;
    }
}
