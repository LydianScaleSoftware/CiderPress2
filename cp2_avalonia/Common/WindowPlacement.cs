/*
 * Copyright 2025 faddenSoft
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
using System.Text.Json;
using System.Text.Json.Serialization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using cp2_avalonia.Services;

namespace cp2_avalonia.Common {
    // This class has auto-generated members, and must be at the top level.
    [JsonSerializable(typeof(WindowPlacement.SavedPos))]
    internal partial class WinPosContext : JsonSerializerContext { }

    /// <summary>
    /// Saves and restores main window position and size.  Replaces the WPF Win32
    /// Get/SetWindowPlacement approach with a JSON-based cross-platform implementation.
    /// </summary>
    public static class WindowPlacement {
        // Cache of normal-state (non-maximized) bounds, keyed by window instance.
        private static readonly Dictionary<Window, (PixelPoint Pos, double W, double H)>
            sNormalBounds = new();

        /// <summary>
        /// Call from the MainWindow constructor to begin tracking normal-state bounds.
        /// These are needed when the window is maximized so we can save/restore the
        /// unmaximized size and position.
        /// </summary>
        public static void TrackNormalBounds(Window window) {
            void UpdateCache(Window w) {
                if (w.WindowState == WindowState.Normal) {
                    sNormalBounds[w] = (w.Position, w.Width, w.Height);
                }
            }
            // Seed the cache, so a window that is minimized or maximized before it was ever
            // moved or resized still has normal bounds to save.
            window.Opened += (_, _) => UpdateCache(window);
            window.PositionChanged += (_, _) => UpdateCache(window);
            window.Resized += (_, _) => UpdateCache(window);
            window.Closed += (_, _) => sNormalBounds.Remove(window);
        }

        internal class SavedPos {
            public int X { get; set; }
            public int Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string State { get; set; }
            public SavedPos(int x, int y, double width, double height, string state) {
                X = x;
                Y = y;
                Width = width;
                Height = height;
                State = state;
            }
        }

        /// <summary>
        /// Serializes the window's current position and state to a JSON string.
        /// </summary>
        public static string Save(Window window) {
            (PixelPoint Pos, double W, double H)? normal = null;
            if (sNormalBounds.TryGetValue(window, out var nb)) {
                normal = nb;
            }
            SavedPos data = BuildSavedPos(window.WindowState, window.Position,
                window.Width, window.Height, normal);
            return JsonSerializer.Serialize<SavedPos>(data, WinPosContext.Default.SavedPos);
        }

        /// <summary>
        /// Chooses what to save.  Whenever the window is not in the Normal state its current
        /// position and size are not the "normal" ones (a minimized window may be at
        /// -32000,-32000, a maximized or full-screen one covers the screen), so the cached
        /// normal bounds are saved instead.  Only Normal and Maximized are saved as states:
        /// a window that was minimized or full screen comes back as a normal window.
        /// </summary>
        internal static SavedPos BuildSavedPos(WindowState state, PixelPoint position,
                double width, double height, (PixelPoint Pos, double W, double H)? normalBounds) {
            int x = position.X;
            int y = position.Y;
            double w = width;
            double h = height;
            if (state != WindowState.Normal && normalBounds.HasValue) {
                x = normalBounds.Value.Pos.X;
                y = normalBounds.Value.Pos.Y;
                w = normalBounds.Value.W;
                h = normalBounds.Value.H;
            }
            string savedState = state == WindowState.Maximized ?
                WindowState.Maximized.ToString() : WindowState.Normal.ToString();
            return new SavedPos(x, y, w, h, savedState);
        }

        /// <summary>
        /// Converts a saved state string to a state to restore.  Only Normal and Maximized
        /// are accepted; anything else (including Minimized or FullScreen written by an
        /// older version) is treated as Normal.
        /// </summary>
        internal static WindowState ParseSavedState(string? state) {
            return Enum.TryParse<WindowState>(state, out var parsed) &&
                parsed == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        /// <summary>
        /// Smallest part of a window, in pixels in each direction, that must be inside a
        /// screen's working area for the saved position to be used.
        /// </summary>
        internal const int MIN_VISIBLE_PIXELS = 100;

        /// <summary>
        /// Checks that a saved window rectangle can be reached on the current screens.
        /// </summary>
        /// <param name="x">Saved left edge, in pixels.</param>
        /// <param name="y">Saved top edge, in pixels.</param>
        /// <param name="widthDip">Saved width, in device-independent pixels.</param>
        /// <param name="heightDip">Saved height, in device-independent pixels.</param>
        /// <param name="screens">Working area (pixels) and scaling of each screen.</param>
        /// <param name="clampedWidth">Width to use, no larger than the chosen screen.</param>
        /// <param name="clampedHeight">Height to use, no larger than the chosen screen.</param>
        /// <returns>True if at least <see cref="MIN_VISIBLE_PIXELS"/> pixels of the window
        ///   (in both directions) lie inside some screen's working area.  False if the window
        ///   is off screen, in which case the platform should place it.</returns>
        internal static bool IsPlacementOnScreen(int x, int y, double widthDip, double heightDip,
                IReadOnlyList<(PixelRect WorkingArea, double Scaling)> screens,
                out double clampedWidth, out double clampedHeight) {
            clampedWidth = widthDip;
            clampedHeight = heightDip;
            long bestArea = 0;
            bool found = false;
            foreach (var (area, scaling) in screens) {
                double scale = scaling > 0 ? scaling : 1.0;
                int wPx = (int)Math.Round(widthDip * scale);
                int hPx = (int)Math.Round(heightDip * scale);
                PixelRect inter = new PixelRect(x, y, wPx, hPx).Intersect(area);
                if (inter.Width < MIN_VISIBLE_PIXELS || inter.Height < MIN_VISIBLE_PIXELS) {
                    continue;
                }
                long interArea = (long)inter.Width * inter.Height;
                if (interArea > bestArea) {
                    bestArea = interArea;
                    found = true;
                    clampedWidth = Math.Min(widthDip, area.Width / scale);
                    clampedHeight = Math.Min(heightDip, area.Height / scale);
                }
            }
            return found;
        }

        /// <summary>
        /// Restores a window's position and state from a JSON string previously produced
        /// by <see cref="Save"/>.
        /// </summary>
        public static void Restore(Window window, string json) {
            try {
                SavedPos? data = JsonSerializer.Deserialize<SavedPos>(json,
                    WinPosContext.Default.SavedPos);
                if (data == null) {
                    AppLog.D("Window placement: ignoring invalid saved data '" + json + "'");
                    return;
                }
                if (data.Width <= 0 || data.Height <= 0 ||
                        double.IsNaN(data.Width) || double.IsNaN(data.Height)) {
                    AppLog.D("Window placement: ignoring saved data with invalid size");
                    return;
                }

                double width = data.Width;
                double height = data.Height;
                var screens = new List<(PixelRect WorkingArea, double Scaling)>();
                try {
                    foreach (Screen screen in window.Screens.All) {
                        screens.Add((screen.WorkingArea, screen.Scaling));
                    }
                } catch (Exception ex) {
                    AppLog.D("Window placement: unable to enumerate screens", ex);
                }

                if (screens.Count > 0 && !IsPlacementOnScreen(data.X, data.Y, width, height,
                        screens, out width, out height)) {
                    // The saved position isn't on any connected screen (a monitor was
                    // unplugged or the layout changed).  Keep the default position.
                    AppLog.I("Window placement: saved position is off screen; using default");
                    return;
                }

                window.Width = width;
                window.Height = height;
                window.Position = new PixelPoint(data.X, data.Y);
                window.WindowState = ParseSavedState(data.State);
            } catch (Exception ex) {
                // Ignore invalid or missing placement data; window stays at default position.
                AppLog.D("Window placement: ignoring invalid saved data", ex);
            }
        }
    }
}
