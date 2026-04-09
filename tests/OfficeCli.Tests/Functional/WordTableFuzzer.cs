// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Bug confirmation tests for Word table/cell Set parameters — Round 4b.
// F49: Word Set table cell width/padding accepts invalid values silently (raw string to OpenXML Width)
// F49b: Word Add table width/cellspacing accepts invalid values silently

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class WordTableFuzzer : IDisposable
{
    private readonly string _docxPath;

    public WordTableFuzzer()
    {
        _docxPath = Path.Combine(Path.GetTempPath(), $"wtfuzz_{Guid.NewGuid():N}.docx");
        BlankDocCreator.Create(_docxPath);

        // Pre-create a table with 2 rows x 2 cols
        using var docx = new WordHandler(_docxPath, editable: true);
        docx.Add("/body", "table", null, new() { ["rows"] = "2", ["cols"] = "2" });
    }

    public void Dispose()
    {
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
    }

    // ==================== F49: Word Set table cell width accepts invalid values ====================
    // Bug: WordHandler.Set.cs:1134 — `tcPr.TableCellWidth = new TableCellWidth { Width = value, ... }`
    // No validation — "abc", "NaN", "-100" stored as raw string in XML Width attribute.
    // Fix: validate with SafeParseUint/SafeParseInt (width is in twips, non-negative integer).

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("-100")]
    public void F49_Word_SetTableCellWidth_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["width"] = value });
        act.Should().Throw<ArgumentException>(
            $"table cell width '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    // ==================== F49: Word Set table cell padding accepts invalid values ====================
    // Bug: WordHandler.Set.cs:1140-1143 — same raw string assignment to Width attributes.

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("-100")]
    public void F49_Word_SetTableCellPadding_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["padding"] = value });
        act.Should().Throw<ArgumentException>(
            $"table cell padding '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    // ==================== F49: Word Set table width accepts invalid values ====================
    // Bug: WordHandler.Set.cs:1334 — `tblPr.TableWidth = new TableWidth { Width = value, ... }`

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("-100")]
    public void F49_Word_SetTableWidth_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["width"] = value });
        act.Should().Throw<ArgumentException>(
            $"table width '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    // ==================== F49: Word Set table cell spacing accepts invalid values ====================
    // Bug: WordHandler.Set.cs:1341

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("-100")]
    public void F49_Word_SetTableCellSpacing_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["cellspacing"] = value });
        act.Should().Throw<ArgumentException>(
            $"table cellspacing '{value}' is invalid — should be rejected, not stored as raw string in XML");
    }

    // ==================== F49b: Word Add table width invalid ====================
    // Bug: WordHandler.Add.cs:471 — `tblProps.TableWidth = new TableWidth { Width = tv, ... }` raw string (non-percent path)

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("-100")]
    public void F49b_Word_AddTable_Width_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new()
        {
            ["rows"] = "1", ["cols"] = "1", ["width"] = value
        });
        act.Should().Throw<ArgumentException>(
            $"Add table with width='{value}' should be rejected — not stored as raw XML string");
    }

    // ==================== F49b: Word Add table cellspacing invalid ====================
    // Bug: WordHandler.Add.cs:478 — same raw string pattern

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("-100")]
    public void F49b_Word_AddTable_CellSpacing_InvalidValue_ThrowsArgumentException(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new()
        {
            ["rows"] = "1", ["cols"] = "1", ["cellspacing"] = value
        });
        act.Should().Throw<ArgumentException>(
            $"Add table with cellspacing='{value}' should be rejected — not stored as raw XML string");
    }

    // ==================== Valid values regression ====================

    [Theory]
    [InlineData("0")]
    [InlineData("1440")]
    [InlineData("2880")]
    public void Word_SetTableCellWidth_ValidValues_Succeeds(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]/tr[1]/tc[1]", new() { ["width"] = value });
        act.Should().NotThrow($"table cell width '{value}' (twips) is valid");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("720")]
    [InlineData("1440")]
    public void Word_SetTableWidth_ValidValues_Succeeds(string value)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Set("/body/tbl[1]", new() { ["width"] = value });
        act.Should().NotThrow($"table width '{value}' (twips) is valid");
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("50%", "1")]
    public void Word_AddTable_Width_ValidValues_Succeeds(string width, string cols)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new()
        {
            ["rows"] = "1", ["cols"] = cols, ["width"] = width
        });
        act.Should().NotThrow($"Add table with width='{width}' should succeed");
    }
}
