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
using CommonUtil;

using cp2_avalonia.Services;

namespace cp2_avalonia.Tests;

/// <summary>
/// In-memory <see cref="ISettingsService"/> for tests.
/// </summary>
internal class FakeSettingsService : ISettingsService
{
#pragma warning disable CS0067     // event never used
    public event Action<string>? SettingChanged;
    public event Action<string>? SaveFailed;
#pragma warning restore CS0067

    private readonly Dictionary<string, object> mValues = new();

    public bool GetBool(string key, bool defaultValue) => Get(key, defaultValue);
    public void SetBool(string key, bool value) => mValues[key] = value;
    public int GetInt(string key, int defaultValue) => Get(key, defaultValue);
    public void SetInt(string key, int value) => mValues[key] = value;
    public string GetString(string key, string defaultValue) => Get(key, defaultValue);
    public void SetString(string key, string? value) => mValues[key] = value ?? string.Empty;
    public T GetEnum<T>(string key, T defaultValue) where T : struct => Get(key, defaultValue);
    public void SetEnum<T>(string key, T value) where T : struct => mValues[key] = value;

    public void Load() { }
    public void Save() { }
    public SettingsHolder Snapshot() => new SettingsHolder();
    public void ReplaceSettings(SettingsHolder source) { }
    public void MergeSettings(SettingsHolder delta) { }

    private T Get<T>(string key, T defaultValue) =>
        mValues.TryGetValue(key, out object? val) && val is T t ? t : defaultValue;
}
