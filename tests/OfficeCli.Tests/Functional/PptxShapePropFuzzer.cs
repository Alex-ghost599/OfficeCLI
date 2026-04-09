// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Bug confirmation tests for PPTX shape property Set parameters — Round 4c.
// F50: PPTX Set table border lineDash unknown value silently defaults to Solid
// F51: PPTX Set baseline "NaN" passes TryParse, silently stored as 0 (missing IsNaN guard)

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class PptxShapePropFuzzer : IDisposable
{
    private readonly string _pptxPath;

    public PptxShapePropFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"pspfuzz_{Guid.NewGuid():N}.pptx");
        BlankDocCreator.Create(_pptxPath);

        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new()
        {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        // Add a table for border tests
        pptx.Add("/slide[1]", "table", null, new() { ["rows"] = "2", ["cols"] = "2" });
    }

    public void Dispose()
    {
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
    }

    // ==================== F50: PPTX table border lineDash silent fallback ====================
    // Bug: PowerPointHandler.ShapeProperties.cs:967 — `_ => Drawing.PresetLineDashValues.Solid`
    // Unknown lineDash values silently become "solid" instead of rejecting with clear error.
    // Fix: `_ => throw new ArgumentException(...)` listing valid values.

    [Theory]
    [InlineData("invalid")]
    [InlineData("xyz")]
    // "SOLID" is valid (case-insensitive match to "solid"), so not listed here
    [InlineData("")]
    public void F50_Pptx_SetTableBorderLineDash_UnknownValue_ThrowsArgumentException(string lineDash)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        // border with lineDash component: "solid;2;000000;lineDash:dash"
        // The lineDash is specified in the border style compound string or as a sub-property
        // Based on source: it's set via the border value parsing of the "dash" component
        var act = () => handler.Set("/slide[1]/table[1]", new()
        {
            ["border"] = $"solid;2;000000;{lineDash}"
        });
        act.Should().Throw<ArgumentException>(
            $"lineDash='{lineDash}' is unknown — should throw ArgumentException listing valid values (solid, dot, dash, etc.)");
    }

    // ==================== F51: PPTX Set baseline "NaN" passthrough ====================
    // Bug: PowerPointHandler.ShapeProperties.cs:197 — `double.TryParse("NaN", NumberStyles.Float, ...)` returns true.
    // No IsNaN check — `(int)(NaN * 1000) = 0` silently stored as baseline=0 (no super/sub).
    // Fix: add `|| double.IsNaN(blVal) || double.IsInfinity(blVal)` in the TryParse expression.

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void F51_Pptx_SetBaseline_NaNInfinity_ThrowsArgumentException(string value)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["baseline"] = value });
        act.Should().Throw<ArgumentException>(
            $"baseline='{value}' is not a valid percentage — should throw ArgumentException");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("xyz%")]
    public void F51_Pptx_SetBaseline_InvalidString_ThrowsArgumentException(string value)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["baseline"] = value });
        act.Should().Throw<ArgumentException>(
            $"baseline='{value}' is not a valid value — should throw ArgumentException");
    }

    // ==================== F51b: PPTX Add shape baseline NaN — same bug in Add path ====================
    // Bug: PowerPointHandler.Add.cs:1330 — same pattern, no IsNaN guard on TryParse result.

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void F51b_Pptx_AddShape_Baseline_NaNInfinity_ThrowsArgumentException(string value)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new()
        {
            ["text"] = "Hello", ["baseline"] = value,
            ["x"] = "2cm", ["y"] = "5cm", ["width"] = "5cm", ["height"] = "2cm"
        });
        act.Should().Throw<ArgumentException>(
            $"Add shape with baseline='{value}' should throw ArgumentException");
    }

    // ==================== F52: PPTX Set volume NaN passthrough ====================
    // Bug: PowerPointHandler.Set.cs:562 — `double.TryParse(value, out var volVal)` no NaN guard.
    // `(int)(NaN * 1000) = 0` silently sets volume to 0.
    // Fix: add `|| double.IsNaN(volVal) || double.IsInfinity(volVal)` check.
    // Note: volume is set on media shapes only; we skip this if no media shape exists.

    // ==================== F53: PPTX Set crop NaN passthrough ====================
    // Bug: PowerPointHandler.Set.cs:726,734 — no NaN guard on crop percentage TryParse.
    // `(int)(NaN * 1000) = 0` silently sets crop to 0%.
    // Fix: add NaN/Infinity check after TryParse for crop values.
    // Note: crop is on picture shapes; we skip full test here as it needs an image.

    // ==================== Valid values — regression ====================

    [Theory]
    [InlineData("super")]
    [InlineData("sub")]
    [InlineData("none")]
    [InlineData("30")]    // 30% superscript
    [InlineData("-20")]   // -20% subscript
    public void Pptx_SetBaseline_ValidValues_Succeeds(string value)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["baseline"] = value });
        act.Should().NotThrow($"baseline='{value}' is a valid value");
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("dot")]
    [InlineData("dash")]
    [InlineData("dashDot")]
    [InlineData("lgDash")]
    public void Pptx_SetLineDash_ValidValues_Succeeds(string lineDash)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        // lineDash is part of the border compound value
        var act = () => handler.Set("/slide[1]/table[1]", new()
        {
            ["border"] = $"solid;2;000000;{lineDash}"
        });
        act.Should().NotThrow($"lineDash='{lineDash}' is a valid value");
    }
}
