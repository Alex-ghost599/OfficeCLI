// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for numeric-valued properties (font size, position, rotation, etc.).
// Tests boundary values: 0, negative, very large, non-numeric strings.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Fuzz;

public class NumberFuzzer : IDisposable
{
    private readonly string _pptxPath;
    private readonly string _xlsxPath;
    private readonly string _docxPath;

    public NumberFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_num_{Guid.NewGuid():N}.pptx");
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"fuzz_num_{Guid.NewGuid():N}.xlsx");
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_num_{Guid.NewGuid():N}.docx");
        BlankDocCreator.Create(_pptxPath);
        BlankDocCreator.Create(_xlsxPath);
        BlankDocCreator.Create(_docxPath);

        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() { ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm", ["width"] = "10cm", ["height"] = "3cm" });
    }

    public void Dispose()
    {
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
    }

    // ==================== Font size — valid values ====================

    public static IEnumerable<object[]> ValidFontSizes => new[]
    {
        new object[] { "8" },
        new object[] { "12" },
        new object[] { "24" },
        new object[] { "72" },
        new object[] { "10.5" },
        new object[] { "14pt" },
        new object[] { "36pt" },
        new object[] { "0.5" },   // very small but parseable
    };

    [Theory]
    [MemberData(nameof(ValidFontSizes))]
    public void Pptx_SetShapeSize_ValidFontSize_Succeeds(string size)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["size"] = size });
        act.Should().NotThrow($"'{size}' is a valid font size");
    }

    [Theory]
    [MemberData(nameof(ValidFontSizes))]
    public void Docx_SetRunSize_ValidFontSize_Succeeds(string size)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]/r[1]", new() { ["size"] = size });
        act.Should().NotThrow($"'{size}' is a valid font size");
    }

    // ==================== Font size — invalid values must throw ====================

    public static IEnumerable<object[]> InvalidFontSizes => new[]
    {
        new object[] { "" },
        new object[] { "abc" },
        new object[] { "NaN" },
        new object[] { "Infinity" },
        new object[] { "-Infinity" },
        // "1,000" (comma thousands separator) silently parsed as 1.0 — confirmed bug F41 in FuzzBugTests2.cs
        new object[] { "12px" },     // px suffix not supported for font size
        new object[] { "12em" },     // em not supported
    };

    [Theory]
    [MemberData(nameof(InvalidFontSizes))]
    public void Pptx_SetShapeSize_InvalidFontSize_ThrowsArgumentException(string size)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["size"] = size });
        act.Should().Throw<ArgumentException>($"'{size}' is not a valid font size");
    }

    [Theory]
    [MemberData(nameof(InvalidFontSizes))]
    public void Docx_SetRunSize_InvalidFontSize_ThrowsArgumentException(string size)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]/r[1]", new() { ["size"] = size });
        act.Should().Throw<ArgumentException>($"'{size}' is not a valid font size");
    }

    // ==================== ParseHelpers.ParseFontSize unit tests ====================

    [Theory]
    [InlineData("12", 12.0)]
    [InlineData("10.5", 10.5)]
    [InlineData("14pt", 14.0)]
    [InlineData("36pt", 36.0)]
    [InlineData("  24  ", 24.0)]  // whitespace trimmed
    public void ParseHelpers_ParseFontSize_ValidInput_ReturnsExpected(string input, double expected)
    {
        var result = ParseHelpers.ParseFontSize(input);
        result.Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ParseHelpers_ParseFontSize_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => ParseHelpers.ParseFontSize(input);
        act.Should().Throw<ArgumentException>();
    }

    // ==================== Rotation — boundary values ====================

    [Theory]
    [InlineData("0")]
    [InlineData("45")]
    [InlineData("90")]
    [InlineData("180")]
    [InlineData("270")]
    [InlineData("359")]
    [InlineData("-45")]   // negative rotation
    [InlineData("360")]   // full rotation
    [InlineData("720")]   // over-rotation
    public void Pptx_SetShapeRotation_ValidValues_Succeeds(string rotation)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rotation"] = rotation });
        act.Should().NotThrow($"rotation '{rotation}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Pptx_SetShapeRotation_InvalidValues_ThrowsArgumentException(string rotation)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rotation"] = rotation });
        act.Should().Throw<ArgumentException>($"rotation '{rotation}' is invalid");
    }

    // ==================== Opacity — boundary values ====================

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0.5")]
    [InlineData("1")]
    [InlineData("1.0")]
    public void Pptx_SetShapeOpacity_ValidValues_Succeeds(string opacity)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        // Set fill first, then opacity
        handler.Set("/slide[1]/shape[1]", new() { ["fill"] = "FF0000" });
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["opacity"] = opacity });
        act.Should().NotThrow($"opacity '{opacity}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Pptx_SetShapeOpacity_InvalidValues_ThrowsArgumentException(string opacity)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["opacity"] = opacity });
        act.Should().Throw<ArgumentException>($"opacity '{opacity}' is invalid");
    }

    // ==================== Position/size (EMU/cm) — boundary values ====================

    public static IEnumerable<object[]> ValidPositions => new[]
    {
        new object[] { "0" },
        new object[] { "0cm" },
        new object[] { "2cm" },
        new object[] { "10cm" },
        new object[] { "1in" },
        new object[] { "72pt" },
        new object[] { "914400" }, // raw EMU = 1 inch
    };

    [Theory]
    [MemberData(nameof(ValidPositions))]
    public void Pptx_SetShapeX_ValidPositions_Succeeds(string x)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["x"] = x });
        act.Should().NotThrow($"position '{x}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("2xx")]
    public void Pptx_SetShapeX_InvalidPositions_ThrowsArgumentException(string x)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["x"] = x });
        act.Should().Throw<ArgumentException>($"invalid position '{x}' should throw ArgumentException");
    }

    // ==================== SafeParseInt boundary values ====================

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("2147483647")]   // int.MaxValue
    [InlineData("-2147483648")]  // int.MinValue
    public void ParseHelpers_SafeParseInt_ValidInput_Succeeds(string input)
    {
        var act = () => ParseHelpers.SafeParseInt(input, "test");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("NaN")]
    [InlineData("2147483648")]   // int.MaxValue + 1
    public void ParseHelpers_SafeParseInt_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => ParseHelpers.SafeParseInt(input, "test");
        act.Should().Throw<ArgumentException>();
    }

    // ==================== Xlsx row height — numeric ====================

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    [InlineData("100")]
    [InlineData("409")]   // Excel max row height
    public void Xlsx_SetRowHeight_ValidValues_Succeeds(string height)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/row[1]", new() { ["height"] = height });
        act.Should().NotThrow($"row height '{height}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    public void Xlsx_SetRowHeight_InvalidValues_ThrowsArgumentException(string height)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/row[1]", new() { ["height"] = height });
        act.Should().Throw<ArgumentException>($"row height '{height}' is invalid");
    }
}
