// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for PPTX Add path — same NaN bugs as Set but in the Add handler.
// Bugs: F22 (lineSpacing NaN in Add), F23 (spaceBefore/After NaN in Add),
//       F24 (rotation NaN in Add), F25 (audio volume NaN).

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class PptxAddFuzzer : IDisposable
{
    private readonly string _pptxPath;

    public PptxAddFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_pptxadd_{Guid.NewGuid():N}.pptx");
        BlankDocCreator.Create(_pptxPath);
        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
    }

    public void Dispose()
    {
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
    }

    // ==================== lineSpacing NaN in Add (Bug F22) ====================

    [Fact]
    public void BugF22_Pptx_AddShape_LineSpacing_NaN_ShouldThrowArgumentException()
    {
        // BUG: PowerPointHandler.Add.cs:266 — double.TryParse without NaN check
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["lineSpacing"] = "NaN"
        });
        act.Should().Throw<ArgumentException>("lineSpacing=NaN during Add should be rejected");
    }

    [Fact]
    public void BugF22_Pptx_AddShape_LineSpacing_Infinity_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["lineSpacing"] = "Infinity"
        });
        act.Should().Throw<ArgumentException>("lineSpacing=Infinity during Add should be rejected");
    }

    // ==================== spaceBefore NaN in Add (Bug F23) ====================

    [Fact]
    public void BugF23_Pptx_AddShape_SpaceBefore_NaN_ShouldThrowArgumentException()
    {
        // BUG: PowerPointHandler.Add.cs:280 — double.TryParse without NaN check
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["spaceBefore"] = "NaN"
        });
        act.Should().Throw<ArgumentException>("spaceBefore=NaN during Add should be rejected");
    }

    [Fact]
    public void BugF23_Pptx_AddShape_SpaceAfter_NaN_ShouldThrowArgumentException()
    {
        // BUG: PowerPointHandler.Add.cs:291 — same pattern for spaceAfter
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["spaceAfter"] = "NaN"
        });
        act.Should().Throw<ArgumentException>("spaceAfter=NaN during Add should be rejected");
    }

    // ==================== rotation NaN in Add (Bug F24) ====================

    [Fact]
    public void BugF24_Pptx_AddShape_Rotation_NaN_ShouldThrowArgumentException()
    {
        // BUG: PowerPointHandler.Add.cs:328 — double.TryParse without NaN check
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["rotation"] = "NaN"
        });
        act.Should().Throw<ArgumentException>("rotation=NaN during Add should be rejected");
    }

    [Fact]
    public void BugF24_Pptx_AddShape_Rotation_Infinity_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["rotation"] = "Infinity"
        });
        act.Should().Throw<ArgumentException>("rotation=Infinity during Add should be rejected");
    }

    // ==================== Valid Add values to confirm base behavior ====================

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("2.0")]
    public void Pptx_AddShape_LineSpacing_ValidValues_Succeeds(string spacing)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["lineSpacing"] = spacing
        });
        act.Should().NotThrow($"lineSpacing='{spacing}' during Add should be accepted");
    }

    [Theory]
    [InlineData("45")]
    [InlineData("-90")]
    [InlineData("0")]
    [InlineData("180")]
    public void Pptx_AddShape_Rotation_ValidValues_Succeeds(string rotation)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["rotation"] = rotation
        });
        act.Should().NotThrow($"rotation='{rotation}' during Add should be accepted");
    }

    // ==================== PPTX Add slide with invalid transition ====================

    [Theory]
    [InlineData("")]
    [InlineData("marbled")]
    [InlineData("twist")]
    public void BugF25_Pptx_AddSlide_InvalidTransition_ShouldThrowArgumentException(string transition)
    {
        // BUG: ApplyTransition switch uses `_ => null` — unknown transitions create empty <p:transition/>
        // Empty transition element confuses PowerPoint rendering
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/", "slide", null, new() { ["transition"] = transition });
        act.Should().Throw<ArgumentException>($"transition='{transition}' is invalid and should be rejected");
    }

    [Theory]
    [InlineData("fade")]
    [InlineData("push")]
    [InlineData("split")]
    [InlineData("cut")]
    [InlineData("zoom")]
    [InlineData("dissolve")]
    [InlineData("wipe")]
    public void Pptx_AddSlide_ValidTransition_Succeeds(string transition)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/", "slide", null, new() { ["transition"] = transition });
        act.Should().NotThrow($"transition='{transition}' should be accepted");
    }

    // ==================== PPTX Add shape with invalid position unit ====================

    [Theory]
    [InlineData("5cm")]
    [InlineData("2in")]
    [InlineData("72pt")]
    [InlineData("914400")]
    public void Pptx_AddShape_ValidPositions_Succeeds(string pos)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = pos, ["y"] = pos,
            ["width"] = "10cm", ["height"] = "3cm"
        });
        act.Should().NotThrow($"position '{pos}' should be accepted");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("5abc")]
    public void Pptx_AddShape_InvalidPositions_ThrowsException(string pos)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = pos, ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        act.Should().Throw<Exception>($"position '{pos}' should throw");
    }

    [Fact]
    public void BugF26_Pptx_AddShape_Position_Infinity_ShouldThrowFormatException()
    {
        // BUG: EmuConverter.ParseWithUnit uses double.TryParse without Infinity check
        // "Infinitycm" passes TryParse, then (long)(Infinity * 360000) = long.MinValue (negative)
        // The negative check fires but throws FormatException not ArgumentException, and
        // some callers may absorb this silently
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "Infinitycm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm"
        });
        // Should throw (FormatException from EmuConverter) — confirmed not throwing means NaN passthrough bug
        act.Should().Throw<Exception>("Infinity position value should be rejected");
    }

    // ==================== PPTX shape text properties during Add ====================

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void BugF22b_Pptx_AddShape_FontSize_NaN_ShouldThrowArgumentException(string size)
    {
        // BUG: Same ParseFontSize NaN issue applies when size is set during Add
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm",
            ["size"] = size
        });
        act.Should().Throw<ArgumentException>($"font size='{size}' during Add should be rejected");
    }
}
