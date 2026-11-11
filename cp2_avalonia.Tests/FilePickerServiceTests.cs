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
using Avalonia.Platform.Storage;

using cp2_avalonia.Services;

namespace cp2_avalonia.Tests;

/// <summary>
/// Save-panel name handling: the macOS panel adds the file type's extension itself, so a
/// suggested name with that extension came out doubled ("NewDisk.po.po").
/// </summary>
public class FilePickerServiceTests
{
    private static readonly FilePickerFileType[] PO_TYPES = {
        new FilePickerFileType("ProDOS-order disk image") { Patterns = new[] { "*.po" } },
        new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
    };

    [Fact]
    public void MacOS_DropsTheFirstTypesExtension()
    {
        string? name = FilePickerService.PrepareSuggestedName("NewDisk.po", PO_TYPES, true,
            out string? stripped);
        Assert.Equal("NewDisk", name);
        Assert.Equal(".po", stripped);

        // Case-insensitive match; the name's own case is kept.
        Assert.Equal("Disk", FilePickerService.PrepareSuggestedName("Disk.PO", PO_TYPES, true,
            out stripped));
        Assert.Equal(".PO", stripped);
    }

    [Theory]
    [InlineData("NewDisk.po", false)]       // not macOS: unchanged
    [InlineData("NewDisk.dsk", true)]       // extension isn't the first type's
    [InlineData("NewDisk", true)]           // no extension
    [InlineData(".po", true)]               // only an extension
    public void OtherCases_AreUnchanged(string suggested, bool isMac)
    {
        Assert.Equal(suggested, FilePickerService.PrepareSuggestedName(suggested, PO_TYPES,
            isMac, out string? stripped));
        Assert.Null(stripped);
    }

    [Fact]
    public void NoFileTypes_IsUnchanged()
    {
        Assert.Equal("a.po", FilePickerService.PrepareSuggestedName("a.po", null, true,
            out string? stripped));
        Assert.Null(stripped);
        Assert.Null(FilePickerService.PrepareSuggestedName(null, PO_TYPES, true, out stripped));
    }

    [Theory]
    [InlineData("/x/NewDisk.po", ".po", "/x/NewDisk.po")]     // panel added it back
    [InlineData("/x/NewDisk", ".po", "/x/NewDisk.po")]        // it didn't: put it back
    [InlineData("/x/Other.dsk", ".po", "/x/Other.dsk")]       // user chose another extension
    [InlineData("/x/NewDisk", null, "/x/NewDisk")]            // nothing was stripped
    public void FinishSavePath_RestoresAMissingExtensionOnly(string path, string? stripped,
        string expected)
    {
        Assert.Equal(expected, FilePickerService.FinishSavePath(path, stripped));
    }
}
