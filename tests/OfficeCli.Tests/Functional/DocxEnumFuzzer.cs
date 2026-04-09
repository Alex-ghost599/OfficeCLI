// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for DOCX enum properties with silent fallback bugs.
// Bugs: F27 (table cell valign), F28 (section break type), F30 (text direction), F31 (table alignment).
// Also covers PPTX table cell valign (F27b).

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class DocxEnumFuzzer : IDisposable
{
    private readonly string _docxPath;
    private readonly string _pptxPath;

    public DocxEnumFuzzer()
    {
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_docxenum_{Guid.NewGuid():N}.docx");
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_docxenum_{Guid.NewGuid():N}.pptx");
        BlankDocCreator.Create(_docxPath);
        BlankDocCreator.Create(_pptxPath);

        // Set up DOCX with table and paragraph
        using var docx = new WordHandler(_docxPath, editable: true);
        docx.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        docx.Add("/body", "table", null, new() { ["rows"] = "2", ["cols"] = "2" });

        // Set up PPTX with table
        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "table", null, new() { ["rows"] = "2", ["cols"] = "2" });
    }

    public void Dispose()
    {
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
    }

    // ==================== DOCX table cell valign valid/invalid (Bug F27) ====================

    [Theory]
    [InlineData("top")]
    [InlineData("center")]
    [InlineData("bottom")]
    public void Docx_SetTableCellValign_ValidValues_Succeeds(string valign)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["valign"] = valign });
        act.Should().NotThrow($"table cell valign '{valign}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("middle")]
    [InlineData("invalid")]
    public void BugF27_Docx_SetTableCellValign_InvalidValues_ShouldThrowArgumentException(string valign)
    {
        // BUG: WordHandler.Set.cs:1129 uses `_ => TableVerticalAlignmentValues.Top` — silent fallback
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["valign"] = valign });
        act.Should().Throw<ArgumentException>($"table cell valign '{valign}' is invalid and should be rejected");
    }

    // ==================== PPTX table cell valign valid/invalid (Bug F27b) ====================

    [Theory]
    [InlineData("top")]
    [InlineData("middle")]
    [InlineData("center")]
    [InlineData("bottom")]
    public void Pptx_SetTableCellValign_ValidValues_Succeeds(string valign)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/table[1]/tr[1]/tc[1]", new() { ["valign"] = valign });
        act.Should().NotThrow($"PPTX table cell valign '{valign}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("left")]
    public void BugF27b_Pptx_SetTableCellValign_InvalidValues_ShouldThrowArgumentException(string valign)
    {
        // BUG: PowerPointHandler.ShapeProperties.cs:846 uses `_ => TextAnchoringTypeValues.Top` — silent fallback
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/table[1]/tr[1]/tc[1]", new() { ["valign"] = valign });
        act.Should().Throw<ArgumentException>($"PPTX table cell valign '{valign}' is invalid and should be rejected");
    }

    // ==================== DOCX section break type (Bug F28) ====================

    [Theory]
    [InlineData("nextPage")]
    [InlineData("continuous")]
    [InlineData("evenPage")]
    [InlineData("oddPage")]
    public void Docx_SetSectionBreakType_ValidValues_Succeeds(string breakType)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/section[1]", new() { ["type"] = breakType });
        act.Should().NotThrow($"section break type '{breakType}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("page")]   // generic "page" not a valid OOXML section mark value
    public void BugF28_Docx_SetSectionBreakType_InvalidValues_ShouldThrowArgumentException(string breakType)
    {
        // BUG: WordHandler.Set.cs:365 uses `_ => SectionMarkValues.NextPage` — silent fallback
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/section[1]", new() { ["type"] = breakType });
        act.Should().Throw<ArgumentException>($"section break type '{breakType}' is invalid and should be rejected");
    }

    // ==================== DOCX text direction (Bug F30) ====================

    [Theory]
    [InlineData("lrtb")]
    [InlineData("btlr")]
    [InlineData("tbrl")]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    public void Docx_SetTableCellTextDirection_ValidValues_Succeeds(string dir)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["textDirection"] = dir });
        act.Should().NotThrow($"textDirection '{dir}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("rtl")]
    [InlineData("invalid")]
    public void BugF30_Docx_SetTableCellTextDirection_InvalidValues_ShouldThrowArgumentException(string dir)
    {
        // BUG: WordHandler.Set.cs:1181 uses `_ => TextDirectionValues.LefToRightTopToBottom` — silent fallback
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["textDirection"] = dir });
        act.Should().Throw<ArgumentException>($"textDirection '{dir}' is invalid and should be rejected");
    }

    // ==================== DOCX table alignment (Bug F31) ====================

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    public void Docx_SetTableAlignment_ValidValues_Succeeds(string align)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["alignment"] = align });
        act.Should().NotThrow($"table alignment '{align}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("justify")]
    [InlineData("invalid")]
    public void BugF31_Docx_SetTableAlignment_InvalidValues_ShouldThrowArgumentException(string align)
    {
        // BUG: WordHandler.Set.cs:1322 uses `_ => TableRowAlignmentValues.Left` — silent fallback
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["alignment"] = align });
        act.Should().Throw<ArgumentException>($"table alignment '{align}' is invalid and should be rejected");
    }

    // ==================== DOCX underline in PPTX shapes — Silent fallback (Bug F32) ====================

    [Theory]
    [InlineData("single")]
    [InlineData("double")]
    [InlineData("heavy")]
    [InlineData("dotted")]
    [InlineData("none")]
    public void Pptx_SetUnderline_ValidValues_Succeeds(string underline)
    {
        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        var act = () => pptx.Set("/slide[1]/shape[1]", new() { ["underline"] = underline });
        act.Should().NotThrow($"underline '{underline}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("solid")]   // CSS value, not OOXML underline
    [InlineData("overline")]
    public void BugF32_Pptx_SetUnderline_InvalidValues_ShouldThrowArgumentException(string underline)
    {
        // BUG: PowerPointHandler.ShapeProperties.cs:160 uses `_ => TextUnderlineValues.Single` — silent fallback
        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        var act = () => pptx.Set("/slide[1]/shape[1]", new() { ["underline"] = underline });
        act.Should().Throw<ArgumentException>($"underline '{underline}' is invalid and should be rejected");
    }

    // ==================== DOCX strikethrough valid/invalid (Bug F33) ====================

    [Theory]
    [InlineData("single")]
    [InlineData("double")]
    [InlineData("none")]
    [InlineData("true")]
    [InlineData("false")]
    public void Pptx_SetStrikethrough_ValidValues_Succeeds(string strike)
    {
        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        var act = () => pptx.Set("/slide[1]/shape[1]", new() { ["strikethrough"] = strike });
        act.Should().NotThrow($"strikethrough '{strike}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    public void BugF33_Pptx_SetStrikethrough_InvalidValues_ShouldThrowArgumentException(string strike)
    {
        // BUG: PowerPointHandler.ShapeProperties.cs:174 uses `_ => TextStrikeValues.SingleStrike` — silent fallback
        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        var act = () => pptx.Set("/slide[1]/shape[1]", new() { ["strikethrough"] = strike });
        act.Should().Throw<ArgumentException>($"strikethrough '{strike}' is invalid and should be rejected");
    }
}
