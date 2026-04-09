// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using OfficeCli.Core;
using Xunit;

namespace OfficeCli.Tests.Core;

public class ExcelStyleManagerTests : IDisposable
{
    private readonly MemoryStream _ms;
    private readonly SpreadsheetDocument _doc;
    private readonly WorkbookPart _workbookPart;
    private readonly ExcelStyleManager _manager;

    public ExcelStyleManagerTests()
    {
        _ms = new MemoryStream();
        _doc = SpreadsheetDocument.Create(_ms, SpreadsheetDocumentType.Workbook);
        _workbookPart = _doc.AddWorkbookPart();
        _workbookPart.Workbook = new Workbook();
        _manager = new ExcelStyleManager(_workbookPart);
    }

    public void Dispose()
    {
        _doc.Dispose();
        _ms.Dispose();
    }

    // ==================== EnsureStylesPart ====================

    [Fact]
    public void EnsureStylesPart_CreatesPart_WhenNoneExists()
    {
        var part = _manager.EnsureStylesPart();
        part.Should().NotBeNull();
        part.Stylesheet.Should().NotBeNull();
    }

    [Fact]
    public void EnsureStylesPart_ReturnsSamePart_WhenCalledTwice()
    {
        var part1 = _manager.EnsureStylesPart();
        var part2 = _manager.EnsureStylesPart();
        part1.Should().BeSameAs(part2);
    }

    // ==================== DefaultStylesheet structure ====================

    [Fact]
    public void DefaultStylesheet_HasFonts()
    {
        var ss = _manager.EnsureStylesPart().Stylesheet;
        ss!.Fonts.Should().NotBeNull();
        ss.Fonts!.Elements<Font>().Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void DefaultStylesheet_HasFills_WithNoneAndGray125()
    {
        var ss = _manager.EnsureStylesPart().Stylesheet;
        var fills = ss!.Fills!.Elements<Fill>().ToList();
        fills.Should().HaveCountGreaterThanOrEqualTo(2);
        fills[0].PatternFill?.PatternType?.Value.Should().Be(PatternValues.None);
        fills[1].PatternFill?.PatternType?.Value.Should().Be(PatternValues.Gray125);
    }

    [Fact]
    public void DefaultStylesheet_HasCellFormats()
    {
        var ss = _manager.EnsureStylesPart().Stylesheet;
        ss!.CellFormats.Should().NotBeNull();
        ss.CellFormats!.Elements<CellFormat>().Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ==================== NumFmt ====================

    [Theory]
    [InlineData("general", 0u)]
    [InlineData("0", 1u)]
    [InlineData("0.00", 2u)]
    [InlineData("#,##0", 3u)]
    [InlineData("#,##0.00", 4u)]
    [InlineData("0%", 9u)]
    [InlineData("0.00%", 10u)]
    public void ApplyStyle_BuiltinNumFmt_UsesCorrectId(string format, uint expectedId)
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["numFmt"] = format });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var lastXf = ss!.CellFormats!.Elements<CellFormat>().Last();
        lastXf.NumberFormatId?.Value.Should().Be(expectedId);
    }

    [Fact]
    public void ApplyStyle_CustomNumFmt_IdStartsAt164()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["numFmt"] = "#,##0.00\"元\"" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var customFmts = ss!.NumberingFormats?.Elements<NumberingFormat>().ToList();
        customFmts.Should().NotBeNullOrEmpty();
        customFmts!.First().NumberFormatId?.Value.Should().BeGreaterThanOrEqualTo(164u);
    }

    [Fact]
    public void ApplyStyle_CustomNumFmt_Deduplicated()
    {
        var cell1 = new Cell();
        var cell2 = new Cell();
        const string fmt = "YYYY-MM-DD";

        uint idx1 = _manager.ApplyStyle(cell1, new Dictionary<string, string> { ["numFmt"] = fmt });
        uint idx2 = _manager.ApplyStyle(cell2, new Dictionary<string, string> { ["numFmt"] = fmt });

        idx1.Should().Be(idx2);

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var matching = ss!.NumberingFormats?.Elements<NumberingFormat>()
            .Where(nf => nf.FormatCode?.Value == fmt).ToList();
        matching.Should().HaveCount(1);
    }

    // ==================== Font ====================

    [Fact]
    public void ApplyStyle_Bold_CreatesNewBoldFont()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.bold"] = "true" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasBold = ss!.Fonts!.Elements<Font>().Any(f => f.Bold != null);
        hasBold.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_BoldAndItalic_CreatesCorrectFont()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string>
        {
            ["font.bold"] = "true",
            ["font.italic"] = "true"
        });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasBoldItalic = ss!.Fonts!.Elements<Font>().Any(f => f.Bold != null && f.Italic != null);
        hasBoldItalic.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_SameFont_Deduplicated()
    {
        var cell1 = new Cell();
        var cell2 = new Cell();
        var props = new Dictionary<string, string> { ["font.bold"] = "true", ["font.size"] = "14" };

        uint idx1 = _manager.ApplyStyle(cell1, props);
        uint idx2 = _manager.ApplyStyle(cell2, props);

        idx1.Should().Be(idx2);
    }

    [Fact]
    public void ApplyStyle_FontSize_Applied()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.size"] = "14" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasSize14 = ss!.Fonts!.Elements<Font>().Any(f => Math.Abs((f.FontSize?.Val?.Value ?? 0) - 14.0) < 0.01);
        hasSize14.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_FontColor_Normalized()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.color"] = "ff0000" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasColor = ss!.Fonts!.Elements<Font>().Any(f => f.Color?.Rgb?.Value == "FFFF0000");
        hasColor.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_FontColor_WithHash_Stripped()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.color"] = "#FF0000" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasColor = ss!.Fonts!.Elements<Font>().Any(f => f.Color?.Rgb?.Value == "FFFF0000");
        hasColor.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_FontUnderline_Single()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.underline"] = "true" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasUnderline = ss!.Fonts!.Elements<Font>().Any(f => f.Underline != null);
        hasUnderline.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_FontUnderline_Double()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.underline"] = "double" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasDouble = ss!.Fonts!.Elements<Font>().Any(f => f.Underline?.Val?.Value == UnderlineValues.Double);
        hasDouble.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_FontStrike()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["font.strike"] = "true" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasStrike = ss!.Fonts!.Elements<Font>().Any(f => f.Strike != null);
        hasStrike.Should().BeTrue();
    }

    // ==================== Fill ====================

    [Fact]
    public void ApplyStyle_Fill_CreatesNewSolidFill()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["fill"] = "4472C4" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasFill = ss!.Fills!.Elements<Fill>().Any(f =>
            f.PatternFill != null &&
            f.PatternFill.PatternType?.Value == PatternValues.Solid &&
            f.PatternFill.ForegroundColor?.Rgb?.Value == "FF4472C4");
        hasFill.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_Fill_Deduplicated()
    {
        var cell1 = new Cell();
        var cell2 = new Cell();

        uint idx1 = _manager.ApplyStyle(cell1, new Dictionary<string, string> { ["fill"] = "FF0000" });
        uint idx2 = _manager.ApplyStyle(cell2, new Dictionary<string, string> { ["fill"] = "FF0000" });

        idx1.Should().Be(idx2);
    }

    [Fact]
    public void ApplyStyle_Fill_DifferentColors_DifferentIndices()
    {
        var cell1 = new Cell();
        var cell2 = new Cell();

        uint idx1 = _manager.ApplyStyle(cell1, new Dictionary<string, string> { ["fill"] = "FF0000" });
        uint idx2 = _manager.ApplyStyle(cell2, new Dictionary<string, string> { ["fill"] = "00FF00" });

        idx1.Should().NotBe(idx2);
    }

    // ==================== Alignment ====================

    [Fact]
    public void ApplyStyle_AlignmentHorizontalCenter()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["alignment.horizontal"] = "center" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasCenter = ss!.CellFormats!.Elements<CellFormat>().Any(xf =>
            xf.Alignment != null &&
            xf.Alignment.Horizontal?.Value == HorizontalAlignmentValues.Center);
        hasCenter.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_AlignmentVerticalTop()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["alignment.vertical"] = "top" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasTop = ss!.CellFormats!.Elements<CellFormat>().Any(xf =>
            xf.Alignment != null &&
            xf.Alignment.Vertical?.Value == VerticalAlignmentValues.Top);
        hasTop.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_AlignmentWrapText()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string> { ["alignment.wrapText"] = "true" });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var hasWrap = ss!.CellFormats!.Elements<CellFormat>().Any(xf =>
            xf.Alignment != null &&
            xf.Alignment.WrapText?.Value == true);
        hasWrap.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_SameAlignment_Deduplicated()
    {
        var cell1 = new Cell();
        var cell2 = new Cell();
        var props = new Dictionary<string, string> { ["alignment.horizontal"] = "right" };

        uint idx1 = _manager.ApplyStyle(cell1, props);
        uint idx2 = _manager.ApplyStyle(cell2, props);

        idx1.Should().Be(idx2);
    }

    // ==================== IsStyleKey ====================

    [Theory]
    [InlineData("numFmt", true)]
    [InlineData("fill", true)]
    [InlineData("font.bold", true)]
    [InlineData("font.color", true)]
    [InlineData("alignment.horizontal", true)]
    [InlineData("border.left", true)]
    [InlineData("text", false)]
    [InlineData("value", false)]
    [InlineData("formula", false)]
    public void IsStyleKey_CorrectlyClassifies(string key, bool expected)
    {
        ExcelStyleManager.IsStyleKey(key).Should().Be(expected);
    }

    // ==================== Combined properties ====================

    [Fact]
    public void ApplyStyle_MultipleProps_AllApplied()
    {
        var cell = new Cell();
        _manager.ApplyStyle(cell, new Dictionary<string, string>
        {
            ["font.bold"] = "true",
            ["font.size"] = "16",
            ["fill"] = "FFFF00",
            ["alignment.horizontal"] = "center"
        });

        var ss = _manager.EnsureStylesPart().Stylesheet;
        var xf = ss!.CellFormats!.Elements<CellFormat>().Last();
        xf.ApplyFont?.Value.Should().BeTrue();
        xf.ApplyFill?.Value.Should().BeTrue();
        xf.ApplyAlignment?.Value.Should().BeTrue();
    }

    [Fact]
    public void ApplyStyle_CaseInsensitiveKeys()
    {
        var cell = new Cell();
        var act = () => _manager.ApplyStyle(cell, new Dictionary<string, string>
        {
            ["FONT.BOLD"] = "true",
            ["Alignment.Horizontal"] = "center"
        });
        act.Should().NotThrow();
    }
}
