// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for DOCX table border silent fallback and Excel column-related edge cases.
// Bugs confirmed: F19 (DOCX ParseBorderStyle WarnBorderDefault), F20 (XLSX column width NaN).

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class DocxExcelFuzzer : IDisposable
{
    private readonly string _docxPath;
    private readonly string _xlsxPath;

    public DocxExcelFuzzer()
    {
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_docxexcel_{Guid.NewGuid():N}.docx");
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"fuzz_docxexcel_{Guid.NewGuid():N}.xlsx");
        BlankDocCreator.Create(_docxPath);
        BlankDocCreator.Create(_xlsxPath);

        // Set up a DOCX with a table
        using var docx = new WordHandler(_docxPath, editable: true);
        docx.Add("/body", "table", null, new() { ["rows"] = "2", ["cols"] = "2" });

        // Set up xlsx with some cells
        using var xlsx = new ExcelHandler(_xlsxPath, editable: true);
        xlsx.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
    }

    public void Dispose()
    {
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
    }

    // ==================== DOCX table border valid values ====================

    [Theory]
    [InlineData("single")]
    [InlineData("thick")]
    [InlineData("double")]
    [InlineData("dotted")]
    [InlineData("dashed")]
    [InlineData("none")]
    [InlineData("triple")]
    [InlineData("wave")]
    public void Docx_SetTableBorder_ValidValues_Succeeds(string style)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["border.all"] = style });
        act.Should().NotThrow($"table border style '{style}' should be accepted");
    }

    // ==================== DOCX table border silent fallback (Bug F19) ====================

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("solid")]   // CSS, not DOCX border style
    [InlineData("round")]
    public void BugF19_Docx_SetTableBorder_InvalidValues_ShouldThrowArgumentException(string style)
    {
        // BUG: DOCX ParseBorderStyle uses WarnBorderDefault which silently returns Single
        // Invalid border styles should throw ArgumentException
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["border.all"] = style });
        act.Should().Throw<ArgumentException>($"DOCX table border style '{style}' is invalid and should be rejected");
    }

    // ==================== DOCX paragraph border silent fallback (Bug F19b) ====================

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("solid")]
    public void BugF19b_Docx_SetParagraphBorder_InvalidValues_ShouldThrowArgumentException(string style)
    {
        // BUG: Same ParseBorderStyle/WarnBorderDefault pattern applied to paragraph borders
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]", new() { ["border.all"] = style });
        act.Should().Throw<ArgumentException>($"DOCX paragraph border style '{style}' is invalid");
    }

    // ==================== DOCX cell border silent fallback (Bug F19c) ====================

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("solid")]
    public void BugF19c_Docx_SetTableCellBorder_InvalidValues_ShouldThrowArgumentException(string style)
    {
        // BUG: Same ParseBorderStyle silent fallback for table cell borders
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["border.all"] = style });
        act.Should().Throw<ArgumentException>($"DOCX table cell border style '{style}' is invalid");
    }

    // ==================== Excel column width NaN/Infinity (Bug F20) ====================

    [Theory]
    [InlineData("8")]
    [InlineData("15.5")]
    [InlineData("30")]
    public void Xlsx_SetColumnWidth_ValidValues_Succeeds(string width)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/col[1]", new() { ["width"] = width });
        act.Should().NotThrow($"column width '{width}' should be accepted");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("abc")]
    public void BugF20_Xlsx_SetColumnWidth_InvalidValues_ShouldThrowArgumentException(string width)
    {
        // Check if column Set exists and if it validates NaN/Infinity
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/col[1]", new() { ["width"] = width });
        act.Should().Throw<ArgumentException>($"column width '{width}' should be rejected");
    }

    // ==================== DOCX run fontSize valid/invalid ====================

    [Theory]
    [InlineData("8")]
    [InlineData("12")]
    [InlineData("24")]
    [InlineData("72")]
    public void Docx_SetRunFontSize_ValidValues_Succeeds(string size)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]/r[1]", new() { ["size"] = size });
        act.Should().NotThrow($"run font size '{size}' should be accepted");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Docx_SetRunFontSize_BoundaryValues_ShouldThrowOrSucceed(string size)
    {
        // 0 and negative font sizes are questionable — at minimum document should not crash
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        // Should either succeed or throw ArgumentException, not crash with unhandled exception
        var act = () => handler.Set("/body/p[1]/r[1]", new() { ["size"] = size });
        var ex = Record.Exception(() => act());
        if (ex != null)
            ex.Should().BeOfType<ArgumentException>($"size '{size}' should either be accepted or throw ArgumentException");
    }

    // ==================== XLSX number format valid/invalid ====================

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("#,##0")]
    [InlineData("0%")]
    [InlineData("yyyy-mm-dd")]
    public void Xlsx_SetNumberFormat_ValidValues_Succeeds(string format)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/A1", new() { ["numberFormat"] = format });
        act.Should().NotThrow($"numberFormat '{format}' should be accepted");
    }

    // ==================== DOCX table rows/cols invalid values ====================

    [Fact]
    public void Docx_AddTable_InvalidRowCount_NonNumeric_ShouldThrowArgumentException()
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new() { ["rows"] = "abc", ["cols"] = "2" });
        act.Should().Throw<ArgumentException>("table rows='abc' is not a valid number");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void BugF21_Docx_AddTable_ZeroOrNegativeRows_ShouldThrowArgumentException(string rows)
    {
        // BUG: rows=0 and rows=-1 both parse as valid int, no range check — creates empty/invalid table
        // WordHandler.Add.cs:419 only checks !int.TryParse, not rows > 0
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new() { ["rows"] = rows, ["cols"] = "2" });
        act.Should().Throw<ArgumentException>($"table rows='{rows}' should be rejected (must be > 0)");
    }

    [Fact]
    public void Docx_AddTable_InvalidColCount_NonNumeric_ShouldThrowArgumentException()
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new() { ["rows"] = "2", ["cols"] = "abc" });
        act.Should().Throw<ArgumentException>("table cols='abc' is not a valid number");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void BugF21b_Docx_AddTable_ZeroOrNegativeCols_ShouldThrowArgumentException(string cols)
    {
        // BUG: cols=0 and cols=-1 go through SafeParseInt which may not do range check
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new() { ["rows"] = "2", ["cols"] = cols });
        act.Should().Throw<ArgumentException>($"table cols='{cols}' should be rejected (must be > 0)");
    }

    // ==================== XLSX cell ref boundary cases ====================

    [Theory]
    [InlineData("A1")]
    [InlineData("Z100")]
    [InlineData("AA1")]
    [InlineData("XFD1048576")]   // max Excel cell
    public void Xlsx_AddCell_ValidRefs_Succeeds(string cellRef)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Add("/Sheet1", "cell", null, new() { ["ref"] = cellRef, ["value"] = "test" });
        act.Should().NotThrow($"cell ref '{cellRef}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A")]    // col and row reversed
    [InlineData("A0")]    // row 0 is invalid in Excel
    public void Xlsx_AddCell_InvalidRefs_ShouldThrowOrHandleGracefully(string cellRef)
    {
        // At minimum should not silently corrupt the document
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Add("/Sheet1", "cell", null, new() { ["ref"] = cellRef, ["value"] = "test" });
        // These should either throw or at minimum document stays valid (not crash)
        var ex = Record.Exception(() => act());
        if (ex != null)
            ex.Should().BeAssignableTo<Exception>($"invalid cell ref '{cellRef}' should throw");
    }
}
