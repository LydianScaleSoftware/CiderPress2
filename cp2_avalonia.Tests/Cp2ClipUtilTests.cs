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

public class Cp2ClipUtilTests : IDisposable
{
    private readonly string mTempRoot;
    private readonly string mClipDir;

    public Cp2ClipUtilTests()
    {
        // A private "temp root" so the test doesn't depend on the real one.
        mTempRoot = Path.Combine(Path.GetTempPath(), "cp2test_" + Guid.NewGuid().ToString("N"));
        mClipDir = Path.Combine(mTempRoot, "cp2clip_1_abc");
        Directory.CreateDirectory(Path.Combine(mClipDir, "xfer"));
        File.WriteAllText(Path.Combine(mClipDir, "xfer", "e0000"), "data");
    }

    public void Dispose() => Directory.Delete(mTempRoot, true);

    private bool Allowed(string path) => Cp2ClipUtil.IsAllowedTransferPath(path, mTempRoot);

    [Fact]
    public void AcceptsFileInsideClipOrDragDir()
    {
        Assert.True(Allowed(Path.Combine(mClipDir, "xfer", "e0000")));
        Assert.True(Allowed(Path.Combine(mTempRoot, "cp2drag_9_ff", "xfer", "e0003")));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("relative/path")]
    [InlineData("")]
    public void RejectsPathsOutsideTemp(string path)
    {
        Assert.False(Allowed(path));
    }

    [Fact]
    public void RejectsHomeDirectoryFile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(Allowed(Path.Combine(home, ".ssh", "known_hosts")));
    }

    [Fact]
    public void RejectsDotDotSegments()
    {
        Assert.False(Allowed(mClipDir + "/xfer/../../outside"));
        Assert.False(Allowed(Path.Combine(mTempRoot, "cp2clip_1_abc", "..", "other")));
    }

    [Fact]
    public void RejectsLookAlikePrefixesAndOtherTempDirs()
    {
        Assert.False(Allowed(Path.Combine(mTempRoot, "cp2clipboard", "xfer", "e0000")));
        Assert.False(Allowed(Path.Combine(mTempRoot, "xcp2clip_1", "e0000")));
        Assert.False(Allowed(Path.Combine(mTempRoot, "other", "cp2clip_1", "e0000")));
        // The directory itself is not a file path.
        Assert.False(Allowed(mClipDir));
        // A sibling that merely starts with the temp root's name.
        Assert.False(Allowed(mTempRoot + "_evil/cp2clip_1/xfer/e0000"));
    }

    [Fact]
    public void RejectsSymlinkOutOfTheDirectory()
    {
        if (OperatingSystem.IsWindows()) return;     // symlink creation needs privileges
        string secret = Path.Combine(mTempRoot, "secret.txt");
        File.WriteAllText(secret, "secret");
        string link = Path.Combine(mClipDir, "xfer", "e0001");
        File.CreateSymbolicLink(link, secret);
        Assert.False(Allowed(link));

        string linkDir = Path.Combine(mClipDir, "linkdir");
        Directory.CreateSymbolicLink(linkDir, mTempRoot);
        Assert.False(Allowed(Path.Combine(linkDir, "secret.txt")));
    }

    [Fact]
    public void ExtractFileData_DropsDisallowedPaths()
    {
        string good = Path.Combine(Path.GetTempPath(), "cp2clip_1_abc", "xfer", "e0000");
        string json = "{\"Cp2FileData\":[" + System.Text.Json.JsonSerializer.Serialize(good) +
            ",\"/etc/passwd\",null]}";
        string?[]? paths = Cp2ClipUtil.ExtractFileData(
            "CiderPressII:clip:v1:" + json);
        Assert.NotNull(paths);
        Assert.Equal(new string?[] { good, null, null }, paths);
    }
}
