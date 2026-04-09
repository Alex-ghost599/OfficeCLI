// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Extended fuzz tests for properties not covered in round 1.
// Focus: lineSpacing, charSpacing, lineDash silent fallback, lineOpacity NaN/Infinity,
//        XLSX font.size NaN, DOCX indent/twips invalid values.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ExtendedFuzzer : IDisposable
{
    private readonly string _pptxPath;
    private readonly string _xlsxPath;
    private readonly string _docxPath;

    public ExtendedFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_ext_{Guid.NewGuid():N}.pptx");
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"fuzz_ext_{Guid.NewGuid():N}.xlsx");
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_ext_{Guid.NewGuid():N}.docx");
        BlankDocCreator.Create(_pptxPath);
        BlankDocCreator.Create(_xlsxPath);
        BlankDocCreator.Create(_docxPath);

        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() { ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm", ["width"] = "10cm", ["height"] = "3cm", ["fill"] = "4472C4" });
    }

    public void Dispose()
    {
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
    }

    // ==================== Bug F2 extension: lineOpacity NaN/Infinity ====================

    [Fact]
    public void BugF2_Pptx_SetLineOpacity_NaN_ShouldThrowArgumentException()
    {
        // lineOpacity uses double.TryParse but no NaN/Infinity guard — same pattern as opacity
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        handler.Set("/slide[1]/shape[1]", new() { ["line"] = "FF0000" });
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["lineopacity"] = "NaN" });
        act.Should().Throw<ArgumentException>("NaN lineOpacity should be rejected");
    }

    [Fact]
    public void BugF2_Pptx_SetLineOpacity_Infinity_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        handler.Set("/slide[1]/shape[1]", new() { ["line"] = "FF0000" });
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["lineopacity"] = "Infinity" });
        act.Should().Throw<ArgumentException>("Infinity lineOpacity should be rejected");
    }

    // ==================== PPTX lineSpacing valid values ====================

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("2.0")]
    [InlineData("0.8")]
    public void Pptx_SetLineSpacing_ValidValues_Succeeds(string spacing)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["lineSpacing"] = spacing });
        act.Should().NotThrow($"lineSpacing '{spacing}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Pptx_SetLineSpacing_InvalidValues_ThrowsException(string spacing)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["lineSpacing"] = spacing });
        act.Should().Throw<Exception>($"lineSpacing '{spacing}' is invalid");
    }

    // ==================== PPTX spaceBefore/spaceAfter valid/invalid ====================

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("12")]
    [InlineData("24")]
    public void Pptx_SetSpaceBefore_ValidValues_Succeeds(string space)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["spaceBefore"] = space });
        act.Should().NotThrow($"spaceBefore '{space}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    public void Pptx_SetSpaceBefore_InvalidValues_ThrowsException(string space)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["spaceBefore"] = space });
        act.Should().Throw<Exception>($"spaceBefore '{space}' is invalid");
    }

    // ==================== PPTX lineDash silent fallback ====================

    [Theory]
    [InlineData("solid")]
    [InlineData("dot")]
    [InlineData("dash")]
    [InlineData("dashdot")]
    [InlineData("longdash")]
    public void Pptx_SetLineDash_ValidValues_Succeeds(string dash)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["linedash"] = dash });
        act.Should().NotThrow($"lineDash '{dash}' is valid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("dotted")]  // 'dotted' is CSS, not PPTX lineDash
    public void Pptx_SetLineDash_InvalidValues_ShouldThrowArgumentException(string dash)
    {
        // BUG candidate: lineDash switch has `_ => Solid` fallback — silent failure
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["linedash"] = dash });
        act.Should().Throw<ArgumentException>($"lineDash '{dash}' is invalid and should be rejected");
    }

    // ==================== XLSX font.size invalid values ====================

    [Theory]
    [InlineData("8")]
    [InlineData("11")]
    [InlineData("24")]
    [InlineData("10.5")]
    public void Xlsx_SetFontSize_ValidValues_Succeeds(string size)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
        var act = () => handler.Set("/Sheet1/A1", new() { ["font.size"] = size });
        act.Should().NotThrow($"font.size '{size}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Xlsx_SetFontSize_InvalidValues_ThrowsException(string size)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
        var act = () => handler.Set("/Sheet1/A1", new() { ["font.size"] = size });
        act.Should().Throw<Exception>($"font.size '{size}' is invalid");
    }

    // ==================== DOCX indent twips — invalid values ====================

    [Theory]
    [InlineData("0")]
    [InlineData("720")]
    [InlineData("1440")]
    [InlineData("-720")]  // hanging indent (negative first-line)
    public void Docx_SetFirstLineIndent_ValidValues_Succeeds(string indent)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]", new() { ["firstLineIndent"] = indent });
        act.Should().NotThrow($"firstLineIndent '{indent}' should be accepted");
    }

    // ==================== XLSX alignment.horizontal invalid values ====================

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("justify")]
    public void Xlsx_SetAlignmentHorizontal_ValidValues_Succeeds(string align)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
        var act = () => handler.Set("/Sheet1/A1", new() { ["alignment.horizontal"] = align });
        act.Should().NotThrow($"alignment.horizontal '{align}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("middle")]
    public void Xlsx_SetAlignmentHorizontal_InvalidValues_ShouldThrowArgumentException(string align)
    {
        // BUG candidate: alignment switch may have silent fallback
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
        var act = () => handler.Set("/Sheet1/A1", new() { ["alignment.horizontal"] = align });
        act.Should().Throw<ArgumentException>($"alignment.horizontal '{align}' is invalid");
    }

    // ==================== XLSX zoom — boundary values ====================

    [Theory]
    [InlineData("10")]
    [InlineData("100")]
    [InlineData("400")]
    public void Xlsx_SetSheetZoom_ValidValues_Succeeds(string zoom)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1", new() { ["zoom"] = zoom });
        act.Should().NotThrow($"zoom '{zoom}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    public void Xlsx_SetSheetZoom_InvalidValues_ThrowsException(string zoom)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1", new() { ["zoom"] = zoom });
        act.Should().Throw<Exception>($"zoom '{zoom}' is invalid");
    }
}
