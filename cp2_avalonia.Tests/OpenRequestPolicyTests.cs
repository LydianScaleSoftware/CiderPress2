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
using cp2_avalonia.ViewModels;

namespace cp2_avalonia.Tests;

/// <summary>
/// G1: recognising the duplicate "open this file" requests that macOS delivers.
/// </summary>
public class OpenRequestPolicyTests
{
    private static readonly string Disk = Path.Combine(Path.GetTempPath(), "disk.po");

    [Fact]
    public void IsSamePath_NullNeverMatches()
    {
        Assert.False(OpenRequestPolicy.IsSamePath(null, Disk));
        Assert.False(OpenRequestPolicy.IsSamePath(Disk, null));
        Assert.False(OpenRequestPolicy.IsSamePath(null, null));
    }

    [Fact]
    public void IsSamePath_NormalizesBeforeComparing()
    {
        string dir = Path.GetDirectoryName(Disk)!;
        string roundabout = Path.Combine(dir, "sub", "..", "disk.po");
        Assert.True(OpenRequestPolicy.IsSamePath(Disk, roundabout));
        Assert.False(OpenRequestPolicy.IsSamePath(Disk, Path.Combine(dir, "other.po")));
    }

    [Fact]
    public void IsSamePath_CaseFollowsPlatform()
    {
        string upper = Path.Combine(Path.GetDirectoryName(Disk)!, "DISK.PO");
        Assert.Equal(!OperatingSystem.IsLinux(), OpenRequestPolicy.IsSamePath(Disk, upper));
    }

    [Fact]
    public void IsSatisfied_NothingOpen()
    {
        Assert.False(OpenRequestPolicy.IsSatisfiedByOpenFile(null, false, Disk, false));
    }

    [Fact]
    public void IsSatisfied_DifferentFile()
    {
        string other = Path.Combine(Path.GetDirectoryName(Disk)!, "other.po");
        Assert.False(OpenRequestPolicy.IsSatisfiedByOpenFile(other, true, Disk, false));
    }

    [Fact]
    public void IsSatisfied_SameFileReadWrite()
    {
        // A plain activation for the file that is already open is a duplicate.
        Assert.True(OpenRequestPolicy.IsSatisfiedByOpenFile(Disk, true, Disk, false));
    }

    [Fact]
    public void IsSatisfied_SameFileAlreadyReadOnly()
    {
        // Open read-only: neither a read-write nor a read-only request needs a reload.
        Assert.True(OpenRequestPolicy.IsSatisfiedByOpenFile(Disk, false, Disk, false));
        Assert.True(OpenRequestPolicy.IsSatisfiedByOpenFile(Disk, false, Disk, true));
    }

    [Fact]
    public void IsSatisfied_ReadOnlyRequestForReadWriteFile()
    {
        // The "-r" request arriving after the activation opened the file read-write.
        Assert.False(OpenRequestPolicy.IsSatisfiedByOpenFile(Disk, true, Disk, true));
    }
}
