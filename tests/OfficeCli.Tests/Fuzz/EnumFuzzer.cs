// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for enum-valued properties: underline, alignment, border style, etc.
// Tests all legal values, case variants, and invalid strings.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Fuzz;

public class EnumFuzzer : IDisposable
{
    private readonly string _pptxPath;
    private readonly string _xlsxPath;
    private readonly string _docxPath;

    public EnumFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_enum_{Guid.NewGuid():N}.pptx");
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"fuzz_enum_{Guid.NewGuid():N}.xlsx");
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_enum_{Guid.NewGuid():N}.docx");
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

    // ==================== PPTX underline — all legal values ====================

    [Theory]
    [InlineData("true")]
    [InlineData("single")]
    [InlineData("sng")]
    [InlineData("double")]
    [InlineData("dbl")]
    [InlineData("heavy")]
    [InlineData("dotted")]
    [InlineData("dash")]
    [InlineData("wavy")]
    [InlineData("false")]
    [InlineData("none")]
    [InlineData("TRUE")]    // uppercase variant
    [InlineData("SINGLE")]
    [InlineData("Double")]  // mixed case
    public void Pptx_SetUnderline_AllLegalValues_Succeeds(string underline)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["underline"] = underline });
        act.Should().NotThrow($"underline '{underline}' is a legal value");
    }

    // NOTE: The spec says unknown underline values silently default to Single (not ArgumentException).
    // This is a design choice — we verify here the current behavior is consistent (no crash).
    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("XXXXXXX")]
    public void Pptx_SetUnderline_UnknownValues_DoesNotCrash(string underline)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        // These may silently default to Single — we only check no unhandled exception
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["underline"] = underline });
        act.Should().NotThrow<NullReferenceException>($"underline '{underline}' must not cause NullReferenceException");
        act.Should().NotThrow<InvalidOperationException>($"underline '{underline}' must not cause InvalidOperationException");
    }

    // ==================== PPTX alignment — all legal values ====================

    [Theory]
    [InlineData("left")]
    [InlineData("l")]
    [InlineData("center")]
    [InlineData("c")]
    [InlineData("right")]
    [InlineData("r")]
    [InlineData("justify")]
    [InlineData("j")]
    [InlineData("LEFT")]     // uppercase
    [InlineData("CENTER")]
    [InlineData("Right")]    // mixed case
    public void Pptx_SetAlign_AllLegalValues_Succeeds(string align)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["align"] = align });
        act.Should().NotThrow($"align '{align}' is a legal value");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("middle")]   // not a valid PPTX align value
    public void Pptx_SetAlign_InvalidValues_ThrowsArgumentException(string align)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["align"] = align });
        act.Should().Throw<ArgumentException>($"align '{align}' is invalid");
    }

    // ==================== PPTX vertical alignment ====================

    [Theory]
    [InlineData("top")]
    [InlineData("t")]
    [InlineData("center")]
    [InlineData("middle")]
    [InlineData("c")]
    [InlineData("m")]
    [InlineData("bottom")]
    [InlineData("b")]
    [InlineData("TOP")]
    public void Pptx_SetValign_AllLegalValues_Succeeds(string valign)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["valign"] = valign });
        act.Should().NotThrow($"valign '{valign}' is a legal value");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("left")]  // horizontal alignment, not vertical
    public void Pptx_SetValign_InvalidValues_ThrowsArgumentException(string valign)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["valign"] = valign });
        act.Should().Throw<ArgumentException>($"valign '{valign}' is invalid");
    }

    // ==================== PPTX transition types ====================

    [Theory]
    [InlineData("fade")]
    [InlineData("push")]
    [InlineData("wipe")]
    [InlineData("split")]
    [InlineData("reveal")]
    [InlineData("random")]
    [InlineData("cover")]
    [InlineData("uncover")]
    [InlineData("zoom")]
    [InlineData("morph")]
    [InlineData("none")]
    [InlineData("fade-fast")]
    [InlineData("fade-slow")]
    [InlineData("FADE")]    // uppercase should work
    public void Pptx_SetTransition_AllLegalValues_Succeeds(string transition)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]", new() { ["transition"] = transition });
        act.Should().NotThrow($"transition '{transition}' is a legal value");
    }

    // ==================== PPTX autoFit ====================

    [Theory]
    [InlineData("true")]
    [InlineData("normal")]
    [InlineData("shape")]
    [InlineData("false")]
    [InlineData("none")]
    public void Pptx_SetAutoFit_LegalValues_Succeeds(string autoFit)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["autoFit"] = autoFit });
        act.Should().NotThrow($"autoFit '{autoFit}' is a legal value");
    }

    // ==================== XLSX border style ====================

    [Theory]
    [InlineData("thin")]
    [InlineData("medium")]
    [InlineData("thick")]
    [InlineData("double")]
    [InlineData("dashed")]
    [InlineData("dotted")]
    [InlineData("none")]
    [InlineData("THIN")]    // uppercase
    public void Xlsx_SetBorderAll_LegalValues_Succeeds(string borderStyle)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
        var act = () => handler.Set("/Sheet1/A1", new() { ["border.all"] = borderStyle });
        act.Should().NotThrow($"border style '{borderStyle}' is legal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("solid")]  // 'solid' is not a valid Excel border style
    public void Xlsx_SetBorderAll_InvalidValues_ThrowsArgumentException(string borderStyle)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "test" });
        var act = () => handler.Set("/Sheet1/A1", new() { ["border.all"] = borderStyle });
        act.Should().Throw<ArgumentException>($"border style '{borderStyle}' is invalid");
    }

    // ==================== DOCX paragraph alignment ====================

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("justify")]
    [InlineData("LEFT")]
    [InlineData("CENTER")]
    public void Docx_SetParagraphAlignment_LegalValues_Succeeds(string alignment)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]", new() { ["alignment"] = alignment });
        act.Should().NotThrow($"alignment '{alignment}' is legal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("middle")]
    public void Docx_SetParagraphAlignment_InvalidValues_ThrowsArgumentException(string alignment)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Set("/body/p[1]", new() { ["alignment"] = alignment });
        act.Should().Throw<ArgumentException>($"alignment '{alignment}' is invalid");
    }

    // ==================== PPTX preset shapes ====================

    [Theory]
    [InlineData("rect")]
    [InlineData("roundRect")]
    [InlineData("ellipse")]
    [InlineData("triangle")]
    [InlineData("diamond")]
    [InlineData("rightArrow")]
    [InlineData("star5")]
    public void Pptx_SetPreset_LegalValues_Succeeds(string preset)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["preset"] = preset });
        act.Should().NotThrow($"preset '{preset}' is legal");
    }

    // ==================== Bool properties (true/false/yes/no/1/0/on/off) ====================

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("TRUE")]
    [InlineData("False")]
    public void Pptx_SetBold_AllBoolVariants_DoesNotThrow(string boolVal)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["bold"] = boolVal });
        act.Should().NotThrow($"bold='{boolVal}' should not crash (IsTruthy handles it)");
    }
}
