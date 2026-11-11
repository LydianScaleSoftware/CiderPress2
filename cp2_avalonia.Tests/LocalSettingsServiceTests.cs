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

public class LocalSettingsServiceTests
{
    [Fact]
    public void MergeIntoLocalCopy_DoesNotTouchOriginal_AndSurvivesReplace()
    {
        // Mimics the Settings dialog: global settings, a local snapshot, and a
        // sub-dialog that merges its changes into the local copy only.
        var global = new LocalSettingsService(new SettingsHolder());
        global.SetString("export-test", "old");
        SettingsHolder local = global.Snapshot();

        var delta = new SettingsHolder();
        delta.SetString("export-test", "new");
        new LocalSettingsService(local).MergeSettings(delta);

        // Cancel path: the global settings never saw the change.
        Assert.Equal("old", global.GetString("export-test", ""));

        // OK path: replacing the global settings with the local copy keeps the change.
        global.ReplaceSettings(local);
        Assert.Equal("new", global.GetString("export-test", ""));
    }
}
