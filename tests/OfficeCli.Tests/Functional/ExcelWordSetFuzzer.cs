// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Bug confirmation tests for Excel and Word Set/Add parameters — Round 4.
// F47: Word Set paragraph spacebefore/spaceafter/linespacing accepts invalid values silently
// Also includes regression tests verifying Excel column/row width/height NaN validation works.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ExcelWordSetFuzzer : IDisposable
{
    private readonly string _xlsxPath;
    private readonly string _docxPath;
    private readonly string _pptxPath;

    public ExcelWordSetFuzzer()
    {
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"ewfuzz_{Guid.NewGuid():N}.xlsx");
        _docxPath = Path.Combine(Path.GetTempPath(), $"ewfuzz_{Guid.NewGuid():N}.docx");
        _pptxPath = Path.Combine(Path.GetTempPath(), $"ewfuzz_{Guid.NewGuid():N}.pptx");
        BlankDocCreator.Create(_xlsxPath);
        BlankDocCreator.Create(_docxPath);
        BlankDocCreator.Create(_pptxPath);

        // Pre-create Word paragraph for Set tests
        using var docx = new WordHandler(_docxPath, editable: true);
        docx.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
    }

    public void Dispose()
    {
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
    }

    // ==================== Regression: Excel column/row numeric validation works ====================
    // These verify that Excel Set properly rejects invalid numeric inputs.

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    public void Excel_SetColumnWidth_InvalidValue_ThrowsArgumentException(string width)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/col[1]", new() { ["width"] = width });
        act.Should().Throw<ArgumentException>(
            $"column width '{width}' is invalid — should throw ArgumentException");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    public void Excel_SetRowHeight_InvalidValue_ThrowsArgumentException(string height)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        var act = () => handler.Set("/Sheet1/row[1]", new() { ["height"] = height });
        act.Should().Throw<ArgumentException>(
            $"row height '{height}' is invalid — should throw ArgumentException");
    }

    // ==================== F47: Word Set paragraph spacebefore/spaceafter/linespacing accepts invalid values ====================
    // Bug: WordHandler.Set.cs:1581,1585,1589 — raw string assigned to OpenXML UInt32Value/StringValue.
    // No validation — "abc", "NaN", "-100" are stored without error, creating invalid DOCX XML.
    // Fix: validate with SafeParseUint/SafeParseInt before assigning.

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("-100")]
    public void F47_Word_SetSpaceBefore_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/p[1]", new() { ["spacebefore"] = value });
        act.Should().Throw<ArgumentException>(
            $"spacebefore '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("-100")]
    public void F47_Word_SetSpaceAfter_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/p[1]", new() { ["spaceafter"] = value });
        act.Should().Throw<ArgumentException>(
            $"spaceafter '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("-100")]
    public void F47_Word_SetLineSpacing_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/p[1]", new() { ["linespacing"] = value });
        act.Should().Throw<ArgumentException>(
            $"linespacing '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    // ==================== F47b: Word Add paragraph spacebefore/spaceafter/linespacing same bug ====================
    // Bug: WordHandler.Add.cs:70,75,80 — same pattern as F47 in Set path.

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("-100")]
    [InlineData("")]
    public void F47b_Word_AddParagraph_SpaceBefore_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "paragraph", null, new()
        {
            ["text"] = "Hello", ["spacebefore"] = value
        });
        act.Should().Throw<ArgumentException>(
            $"Add paragraph with spacebefore='{value}' should be rejected — not stored as raw XML string");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("-100")]
    [InlineData("")]
    public void F47b_Word_AddParagraph_SpaceAfter_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "paragraph", null, new()
        {
            ["text"] = "Hello", ["spaceafter"] = value
        });
        act.Should().Throw<ArgumentException>(
            $"Add paragraph with spaceafter='{value}' should be rejected — not stored as raw XML string");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("-100")]
    [InlineData("")]
    public void F47b_Word_AddParagraph_LineSpacing_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "paragraph", null, new()
        {
            ["text"] = "Hello", ["linespacing"] = value
        });
        act.Should().Throw<ArgumentException>(
            $"Add paragraph with linespacing='{value}' should be rejected — not stored as raw XML string");
    }

    // ==================== Valid values — regression: Word spacing valid values succeed ====================

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("240")]
    [InlineData("480")]
    public void Word_SetSpaceBefore_ValidValues_Succeeds(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/p[1]", new() { ["spacebefore"] = value });
        act.Should().NotThrow($"spacebefore '{value}' is a valid value in twips");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("240")]
    [InlineData("480")]
    public void Word_SetSpaceAfter_ValidValues_Succeeds(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/p[1]", new() { ["spaceafter"] = value });
        act.Should().NotThrow($"spaceafter '{value}' is a valid value in twips");
    }

    [Theory]
    [InlineData("240")]
    [InlineData("360")]
    [InlineData("480")]
    public void Word_SetLineSpacing_ValidValues_Succeeds(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/p[1]", new() { ["linespacing"] = value });
        act.Should().NotThrow($"linespacing '{value}' is a valid value");
    }
}
