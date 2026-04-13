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
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace cp2_avalonia {
    public partial class App : Application {
        /// <summary>
        /// Theme mode choices persisted via AppSettings.
        /// </summary>
        public enum ThemeMode { Light, Dark, System }

        // Icon brush colors and disabled opacity, per theme.
        private static readonly Color LightIconColor = Color.Parse("#212121");
        private static readonly Color DarkIconColor = Color.Parse("#E0E0E0");
        private const double LightIconDisabledOpacity = 0.4;
        private const double DarkIconDisabledOpacity = 0.5;

        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);
            ApplyTheme();
        }

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }

        private MainWindow? GetMainWindow() =>
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow as MainWindow;

        private void OnNativeAboutClick(object? sender, EventArgs e) =>
            GetMainWindow()?.AboutCommand?.Execute(null);

        private void OnNativeSettingsClick(object? sender, EventArgs e) =>
            GetMainWindow()?.EditAppSettingsCommand?.Execute(null);

        private void OnNativeQuitClick(object? sender, EventArgs e) =>
            GetMainWindow()?.ExitCommand?.Execute(null);

        /// <summary>
        /// Applies the theme based on the current setting.
        /// </summary>
        public void ApplyTheme() {
            ThemeMode mode = AppSettings.Global.GetEnum(AppSettings.THEME_MODE, ThemeMode.Light);
            RequestedThemeVariant = mode switch {
                ThemeMode.Dark => ThemeVariant.Dark,
                ThemeMode.System => ThemeVariant.Default,
                _ => ThemeVariant.Light,
            };

            // Update icon brushes and disabled opacity imperatively.
            // We mutate the existing brush Color rather than replacing with a new
            // instance, because DrawingImage resources in Icons.axaml hold direct
            // references to the original brush objects.
            bool isDark = ActualThemeVariant == ThemeVariant.Dark;
            Color iconColor = isDark ? DarkIconColor : LightIconColor;
            if (Resources["IconForegroundBrush"] is SolidColorBrush fgBrush) {
                fgBrush.Color = iconColor;
            }
            if (Resources["IconForegroundFillBrush"] is SolidColorBrush fillBrush) {
                fillBrush.Color = iconColor;
            }
            Resources["IconDisabledOpacity"] = isDark ? DarkIconDisabledOpacity : LightIconDisabledOpacity;
        }
    }
}
