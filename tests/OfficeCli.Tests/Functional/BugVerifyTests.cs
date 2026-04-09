// Verify suspected remaining bug patterns
using System.Globalization;
using FluentAssertions;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class BugVerifyTests : IDisposable
{
    private readonly string _docxPath = Path.Combine(Path.GetTempPath(), $"verify_{Guid.NewGuid():N}.docx");
    private readonly string _xlsxPath = Path.Combine(Path.GetTempPath(), $"verify_{Guid.NewGuid():N}.xlsx");
    private readonly string _pptxPath = Path.Combine(Path.GetTempPath(), $"verify_{Guid.NewGuid():N}.pptx");
    private readonly CultureInfo _origCulture;

    public BugVerifyTests()
    {
        BlankDocCreator.Create(_docxPath);
        BlankDocCreator.Create(_xlsxPath);
        BlankDocCreator.Create(_pptxPath);
        _origCulture = Thread.CurrentThread.CurrentCulture;
    }

    public void Dispose()
    {
        Thread.CurrentThread.CurrentCulture = _origCulture;
        foreach (var p in new[] { _docxPath, _xlsxPath, _pptxPath })
            try { File.Delete(p); } catch { }
    }

    // ==================== Locale: double.Parse without InvariantCulture ====================

    [Fact]
    public void Locale_ParseFontSize_GermanLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        var act = () => ParseHelpers.ParseFontSize("10.5");
        act.Should().NotThrow("ParseFontSize should handle '10.5' regardless of locale");
        act().Should().Be(10.5, "ParseFontSize returns double to preserve fractional sizes");
    }

    [Fact]
    public void Locale_EmuConverter_GermanLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        var act = () => EmuConverter.ParseEmu("2.54cm");
        act.Should().NotThrow("EmuConverter should handle '2.54cm' regardless of locale");
        // 2.54cm = 1 inch = 914400 EMU
        act().Should().Be(914400);
    }

    [Fact]
    public void Locale_ExcelChartData_GermanLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        using var handler = new ExcelHandler(_xlsxPath, true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "X" });
        var act = () => handler.Add("/Sheet1", "chart", null, new()
        {
            ["chartType"] = "column",
            ["data"] = "Sales:10.5,20.3,30.7"
        });
        act.Should().NotThrow("chart data should parse decimals regardless of locale");
    }

    [Fact]
    public void Locale_PptxRotation_GermanLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        using var handler = new PowerPointHandler(_pptxPath, true);
        handler.Add("/", "slide", null, new());
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "test" });
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rotation"] = "45.5" });
        act.Should().NotThrow("rotation should parse '45.5' regardless of locale");
    }

    [Fact]
    public void Locale_PptxOpacity_FrenchLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
        using var handler = new PowerPointHandler(_pptxPath, true);
        handler.Add("/", "slide", null, new());
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "test" });
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["opacity"] = "0.5" });
        act.Should().NotThrow("opacity should parse '0.5' regardless of locale");
    }

    [Fact]
    public void Locale_WordFirstLineIndent_GermanLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        using var handler = new WordHandler(_docxPath, true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "test" });
        var act = () => handler.Set("/body/p[1]", new() { ["firstlineindent"] = "720.5" });
        act.Should().NotThrow("indent should parse '720.5' regardless of locale");
    }

    [Fact]
    public void Locale_ExcelColumnWidth_GermanLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        using var handler = new ExcelHandler(_xlsxPath, true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "X" });
        var act = () => handler.Set("/Sheet1/col[A]", new() { ["width"] = "15.5" });
        act.Should().NotThrow("column width should parse '15.5' regardless of locale");
    }

    // ==================== Input validation: int/uint.Parse on user input ====================

    [Fact]
    public void Validation_TableRowsNonNumeric()
    {
        using var handler = new WordHandler(_docxPath, true);
        var act = () => handler.Add("/body", "table", null, new() { ["rows"] = "three", ["cols"] = "2" });
        // Should throw, but with a helpful message, not raw FormatException
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Validation_SectionPageWidthNonNumeric()
    {
        using var handler = new WordHandler(_docxPath, true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "test" });
        var act = () => handler.Add("/body", "section", null, new() { ["pagewidth"] = "wide" });
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Validation_PptxTableRowsNonNumeric()
    {
        using var handler = new PowerPointHandler(_pptxPath, true);
        handler.Add("/", "slide", null, new());
        var act = () => handler.Add("/slide[1]", "table", null, new() { ["rows"] = "abc", ["cols"] = "3" });
        act.Should().Throw<Exception>();
    }
}
