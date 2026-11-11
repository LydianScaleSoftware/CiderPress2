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

public class SettingsPathsTests
{
    private const string FILE = "CiderPress2-settings.json";

    [Theory]
    [InlineData("Windows", "C:/Users/me/AppData/Roaming", "CiderPress2")]
    [InlineData("MacOS", "/Users/me/Library/Application Support", "CiderPress2")]
    [InlineData("Linux", "/home/me/.config", "ciderpress2")]
    public void UserDir_UsesPerOsFolderName(string osName, string appData, string name)
    {
        var os = Enum.Parse<SettingsPaths.HostOS>(osName);
        string? dir = SettingsPaths.GetUserSettingsDir(os, appData);
        Assert.Equal(Path.Combine(appData, name), dir);
    }

    [Fact]
    public void UserDir_IsNull_WhenNoAppDataFolder()
    {
        Assert.Null(SettingsPaths.GetUserSettingsDir(SettingsPaths.HostOS.Linux, ""));
    }

    [Fact]
    public void FilePath_DefaultsToPerUserFolder()
    {
        string path = SettingsPaths.GetSettingsFilePath(FILE, SettingsPaths.HostOS.MacOS,
            "/Users/me/Library/Application Support", "/Applications/CiderPress II", _ => false);
        Assert.Equal(Path.Combine("/Users/me/Library/Application Support", "CiderPress2", FILE),
            path);
    }

    [Fact]
    public void FilePath_PortableFileBesideExecutableWins()
    {
        string baseDir = "/opt/cp2";
        string portable = Path.Combine(baseDir, FILE);
        string path = SettingsPaths.GetSettingsFilePath(FILE, SettingsPaths.HostOS.Linux,
            "/home/me/.config", baseDir, p => p == portable);
        Assert.Equal(portable, path);
    }

    [Fact]
    public void FilePath_FallsBackToExecutableDir_WhenNoUserFolder()
    {
        string path = SettingsPaths.GetSettingsFilePath(FILE, SettingsPaths.HostOS.Linux,
            "", "/opt/cp2", _ => false);
        Assert.Equal(Path.Combine("/opt/cp2", FILE), path);
    }

    [Fact]
    public void ResourceDir_PrefersExecutableDir_ThenWalksUpToSolution()
    {
        string root = Path.Combine(Path.GetTempPath(), "cp2test_" + Guid.NewGuid().ToString("N"));
        string deep = Path.Combine(root, "a", "b", "c", "d", "e", "f", "g", "h");
        Directory.CreateDirectory(deep);
        try {
            File.WriteAllText(Path.Combine(root, "CiderPress2.sln"), "");
            // Eight levels down: the old six-level limit would have missed it.
            Assert.Equal(root, SettingsPaths.FindAppResourceDir(deep, "LegalStuff.txt",
                "CiderPress2.sln", File.Exists));

            File.WriteAllText(Path.Combine(deep, "LegalStuff.txt"), "");
            Assert.Equal(deep, SettingsPaths.FindAppResourceDir(deep, "LegalStuff.txt",
                "CiderPress2.sln", File.Exists));
        } finally {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WriteFileAtomic_FailingWriter_LeavesOldFileIntact()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cp2test_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "sub", FILE);
        try {
            SettingsPaths.WriteFileAtomic(path, "old");        // creates the folders
            Assert.Equal("old", File.ReadAllText(path));

            Assert.Throws<IOException>(() => SettingsPaths.WriteFileAtomic(path, "new",
                (tmp, text) => {
                    File.WriteAllText(tmp, "partial");
                    throw new IOException("disk full");
                }));

            Assert.Equal("old", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));

            SettingsPaths.WriteFileAtomic(path, "new");
            Assert.Equal("new", File.ReadAllText(path));
        } finally {
            Directory.Delete(dir, true);
        }
    }
}
