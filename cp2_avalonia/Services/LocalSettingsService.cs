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

using CommonUtil;

namespace cp2_avalonia.Services;

/// <summary>
/// <see cref="ISettingsService"/> over a caller-owned <see cref="SettingsHolder"/>.  Nothing is
/// read from or written to disk.  The Settings dialog gives its sub-dialogs one of these
/// wrapped around the dialog's local copy, so their changes stay in that copy until the user
/// presses Apply or OK, and are discarded on Cancel.
/// </summary>
public sealed class LocalSettingsService(SettingsHolder holder) : ISettingsService
{
    public event Action<string>? SettingChanged;
#pragma warning disable CS0067     // never raised: Save() does nothing
    public event Action<string>? SaveFailed;
#pragma warning restore CS0067

    public bool GetBool(string key, bool defaultValue) => holder.GetBool(key, defaultValue);
    public void SetBool(string key, bool value) {
        holder.SetBool(key, value);
        SettingChanged?.Invoke(key);
    }

    public int GetInt(string key, int defaultValue) => holder.GetInt(key, defaultValue);
    public void SetInt(string key, int value) {
        holder.SetInt(key, value);
        SettingChanged?.Invoke(key);
    }

    public string GetString(string key, string defaultValue) =>
        holder.GetString(key, defaultValue);
    public void SetString(string key, string? value) {
        holder.SetString(key, value);
        SettingChanged?.Invoke(key);
    }

    public T GetEnum<T>(string key, T defaultValue) where T : struct =>
        holder.GetEnum(key, defaultValue);
    public void SetEnum<T>(string key, T value) where T : struct {
        holder.SetEnum(key, value);
        SettingChanged?.Invoke(key);
    }

    public void Load() { }
    public void Save() { }

    public SettingsHolder Snapshot() => new SettingsHolder(holder);

    public void ReplaceSettings(SettingsHolder source) {
        holder.ReplaceSettings(source);
        SettingChanged?.Invoke(string.Empty);
    }

    public void MergeSettings(SettingsHolder delta) {
        holder.MergeSettings(delta);
        SettingChanged?.Invoke(string.Empty);
    }
}
