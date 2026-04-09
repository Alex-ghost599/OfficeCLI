// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Round 7 fuzz tests: silent fallback bugs in PPTX/DOCX Add paths.
// Bugs: F34 (DOCX table alignment Add), F35 (DOCX section break Add),
//       F36 (DOCX header/footer type), F37 (DOCX break type),
//       F39 (PPTX connector preset), F40 (PPTX animation trigger).
// Also: underline/strikethrough Add path silent fallbacks (F32b, F33b).

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class PptxDocxAddEnumFuzzer : IDisposable
{
    private readonly string _docxPath;
    private readonly string _pptxPath;

    public PptxDocxAddEnumFuzzer()
    {
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_r7_{Guid.NewGuid():N}.docx");
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_r7_{Guid.NewGuid():N}.pptx");
        BlankDocCreator.Create(_docxPath);
        BlankDocCreator.Create(_pptxPath);

        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
    }

    public void Dispose()
    {
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
    }

    // ==================== DOCX Add table alignment (Bug F34) ====================

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    public void Docx_AddTable_ValidAlignment_Succeeds(string align)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new() {
            ["rows"] = "2", ["cols"] = "2", ["alignment"] = align
        });
        act.Should().NotThrow($"table alignment '{align}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("justify")]
    [InlineData("invalid")]
    public void BugF34_Docx_AddTable_InvalidAlignment_ShouldThrowArgumentException(string align)
    {
        // BUG: WordHandler.Add.cs:453 uses `_ => TableRowAlignmentValues.Left` — silent fallback
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "table", null, new() {
            ["rows"] = "2", ["cols"] = "2", ["alignment"] = align
        });
        act.Should().Throw<ArgumentException>($"table alignment '{align}' during Add should be rejected");
    }

    // ==================== DOCX Add section break type (Bug F35) ====================

    [Theory]
    [InlineData("nextPage")]
    [InlineData("continuous")]
    [InlineData("evenPage")]
    [InlineData("oddPage")]
    public void Docx_AddSection_ValidBreakType_Succeeds(string breakType)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "section", null, new() { ["type"] = breakType });
        act.Should().NotThrow($"section break type '{breakType}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("page")]
    [InlineData("invalid")]
    public void BugF35_Docx_AddSection_InvalidBreakType_ShouldThrowArgumentException(string breakType)
    {
        // BUG: WordHandler.Add.cs:932 uses `_ => SectionMarkValues.NextPage` — silent fallback
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "section", null, new() { ["type"] = breakType });
        act.Should().Throw<ArgumentException>($"section break type '{breakType}' during Add should be rejected");
    }

    // ==================== DOCX Add header/footer type (Bug F36) ====================

    [Theory]
    [InlineData("default")]
    [InlineData("first")]
    [InlineData("even")]
    public void Docx_AddHeader_ValidType_Succeeds(string headerType)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "header", null, new() { ["type"] = headerType });
        act.Should().NotThrow($"header type '{headerType}' should be accepted");
    }

    [Theory]
    [InlineData("odd")]
    [InlineData("invalid")]
    [InlineData("primary")]
    public void BugF36_Docx_AddHeader_InvalidType_ShouldThrowArgumentException(string headerType)
    {
        // BUG: WordHandler.Add.cs:1270 uses `_ => HeaderFooterValues.Default` — silently creates default header
        using var handler = new WordHandler(_docxPath, editable: true);
        var act = () => handler.Add("/body", "header", null, new() { ["type"] = headerType });
        act.Should().Throw<ArgumentException>($"header type '{headerType}' is invalid and should be rejected");
    }

    // ==================== DOCX Add break type (Bug F37) ====================

    [Theory]
    [InlineData("page")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("textwrapping")]
    public void Docx_AddBreak_ValidType_Succeeds(string breakType)
    {
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Add("/body/p[1]", "break", null, new() { ["type"] = breakType });
        act.Should().NotThrow($"break type '{breakType}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("softreturn")]
    public void BugF37_Docx_AddBreak_InvalidType_ShouldThrowArgumentException(string breakType)
    {
        // BUG: WordHandler.Add.cs:1463 uses `_ => BreakValues.Page` — invalid break types silently create page break
        using var handler = new WordHandler(_docxPath, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Hello" });
        var act = () => handler.Add("/body/p[1]", "break", null, new() { ["type"] = breakType });
        act.Should().Throw<ArgumentException>($"break type '{breakType}' is invalid and should be rejected");
    }

    // ==================== PPTX Add connector preset (Bug F39) ====================

    [Theory]
    [InlineData("straight")]
    [InlineData("elbow")]
    [InlineData("curve")]
    public void Pptx_AddConnector_ValidPreset_Succeeds(string preset)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "connector", null, new() {
            ["x1"] = "1cm", ["y1"] = "1cm", ["x2"] = "5cm", ["y2"] = "5cm",
            ["preset"] = preset
        });
        act.Should().NotThrow($"connector preset '{preset}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("round")]
    [InlineData("zigzag")]
    [InlineData("wavy")]
    public void BugF39_Pptx_AddConnector_InvalidPreset_ShouldThrowArgumentException(string preset)
    {
        // BUG: PowerPointHandler.Add.cs:1019 uses `_ => StraightConnector1` — silent fallback
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "connector", null, new() {
            ["x1"] = "1cm", ["y1"] = "1cm", ["x2"] = "5cm", ["y2"] = "5cm",
            ["preset"] = preset
        });
        act.Should().Throw<ArgumentException>($"connector preset '{preset}' is invalid and should be rejected");
    }

    // ==================== PPTX Add animation trigger (Bug F40) ====================

    [Theory]
    [InlineData("click")]
    [InlineData("afterPrevious")]
    [InlineData("withPrevious")]
    public void Pptx_AddAnimation_ValidTrigger_Succeeds(string trigger)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]/shape[1]", "animation", null, new() {
            ["effect"] = "appear", ["trigger"] = trigger
        });
        act.Should().NotThrow($"animation trigger '{trigger}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("onload")]
    [InlineData("hover")]
    [InlineData("invalid")]
    public void BugF40_Pptx_AddAnimation_InvalidTrigger_ShouldThrowArgumentException(string trigger)
    {
        // BUG: PowerPointHandler.Add.cs:1254 uses `_ => "click"` — invalid triggers silently use click
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]/shape[1]", "animation", null, new() {
            ["effect"] = "appear", ["trigger"] = trigger
        });
        act.Should().Throw<ArgumentException>($"animation trigger '{trigger}' is invalid and should be rejected");
    }

    // ==================== PPTX Add shape underline (Bug F32b — Add path) ====================

    [Theory]
    [InlineData("single")]
    [InlineData("double")]
    [InlineData("heavy")]
    [InlineData("none")]
    public void Pptx_AddShape_ValidUnderline_Succeeds(string underline)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Test", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "5cm", ["height"] = "2cm", ["underline"] = underline
        });
        act.Should().NotThrow($"underline '{underline}' during Add should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("solid")]
    [InlineData("overline")]
    public void BugF32b_Pptx_AddShape_InvalidUnderline_ShouldThrowArgumentException(string underline)
    {
        // BUG: PowerPointHandler.Add.cs:238 uses `_ => TextUnderlineValues.Single` — silent fallback
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Test", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "5cm", ["height"] = "2cm", ["underline"] = underline
        });
        act.Should().Throw<ArgumentException>($"underline '{underline}' during Add should be rejected");
    }

    // ==================== PPTX Add shape strikethrough (Bug F33b — Add path) ====================

    [Theory]
    [InlineData("single")]
    [InlineData("double")]
    [InlineData("none")]
    public void Pptx_AddShape_ValidStrikethrough_Succeeds(string strike)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Test", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "5cm", ["height"] = "2cm", ["strikethrough"] = strike
        });
        act.Should().NotThrow($"strikethrough '{strike}' during Add should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    public void BugF33b_Pptx_AddShape_InvalidStrikethrough_ShouldThrowArgumentException(string strike)
    {
        // BUG: PowerPointHandler.Add.cs:254 uses `_ => TextStrikeValues.SingleStrike` — silent fallback
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Test", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "5cm", ["height"] = "2cm", ["strikethrough"] = strike
        });
        act.Should().Throw<ArgumentException>($"strikethrough '{strike}' during Add should be rejected");
    }
}
