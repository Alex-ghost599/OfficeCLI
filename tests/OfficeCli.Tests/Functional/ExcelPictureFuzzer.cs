// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Bug confirmation tests for Excel picture Add positioning parameters — Round 4d.
// F54: Excel Add picture x/y/width/height invalid values silently default to 0/5 instead of throwing.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ExcelPictureFuzzer : IDisposable
{
    private readonly string _xlsxPath;
    // Use a real image file for picture tests
    private readonly string _pngPath;

    public ExcelPictureFuzzer()
    {
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"excelpic_{Guid.NewGuid():N}.xlsx");
        _pngPath = Path.Combine(Path.GetTempPath(), $"excelpic_{Guid.NewGuid():N}.png");
        BlankDocCreator.Create(_xlsxPath);

        // Create a minimal valid PNG (1x1 pixel)
        // PNG magic bytes + IHDR chunk for 1x1 pixel
        byte[] minimalPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, // IHDR length
            0x49, 0x48, 0x44, 0x52, // "IHDR"
            0x00, 0x00, 0x00, 0x01, // width = 1
            0x00, 0x00, 0x00, 0x01, // height = 1
            0x08, 0x02,             // bit depth 8, color type RGB
            0x00, 0x00, 0x00,       // compression, filter, interlace
            0x90, 0x77, 0x53, 0xDE, // CRC
            0x00, 0x00, 0x00, 0x0C, // IDAT length
            0x49, 0x44, 0x41, 0x54, // "IDAT"
            0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, // compressed data
            0xE2, 0x21, 0xBC, 0x33, // CRC
            0x00, 0x00, 0x00, 0x00, // IEND length
            0x49, 0x45, 0x4E, 0x44, // "IEND"
            0xAE, 0x42, 0x60, 0x82  // CRC
        ];
        File.WriteAllBytes(_pngPath, minimalPng);
    }

    public void Dispose()
    {
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
        if (File.Exists(_pngPath)) File.Delete(_pngPath);
    }

    // ==================== F54: Excel Add picture x/y/width/height invalid values silently default ====================
    // Bug: ExcelHandler.Add.cs:641-644 — `int.TryParse(value) ? value : default` pattern.
    // Invalid values (e.g., "abc", "NaN") silently use default (0 or 5) instead of throwing.
    // Fix: throw ArgumentException for invalid values instead of using default.

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("1.5")]     // float not accepted for column index
    [InlineData("Infinity")]
    public void F54_Excel_AddPicture_InvalidX_ThrowsArgumentException(string x)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Add("/Sheet1", "picture", null, new()
        {
            ["path"] = _pngPath, ["x"] = x, ["y"] = "0", ["width"] = "3", ["height"] = "3"
        });
        act.Should().Throw<ArgumentException>(
            $"picture x='{x}' is not a valid column index — should throw ArgumentException, not silently use 0");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void F54_Excel_AddPicture_InvalidWidth_ThrowsArgumentException(string width)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Add("/Sheet1", "picture", null, new()
        {
            ["path"] = _pngPath, ["x"] = "0", ["y"] = "0", ["width"] = width, ["height"] = "3"
        });
        act.Should().Throw<ArgumentException>(
            $"picture width='{width}' is invalid — should throw ArgumentException, not silently use 5");
    }

    // ==================== Valid values — regression ====================

    [Fact]
    public void Excel_AddPicture_ValidDimensions_Succeeds()
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Add("/Sheet1", "picture", null, new()
        {
            ["path"] = _pngPath, ["x"] = "0", ["y"] = "0", ["width"] = "3", ["height"] = "3"
        });
        act.Should().NotThrow("valid picture dimensions should succeed");
    }
}
