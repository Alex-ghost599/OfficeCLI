// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for PPTX 3D effects, shadow, glow, material, lighting, and bevel properties.
// Bugs confirmed: F12 (material silent fallback), F13 (lighting silent fallback),
//                 F14 (bevel preset silent fallback), F15 (shadow/glow NaN passthrough),
//                 F16 (row height NaN passthrough in Excel).

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class EffectsFuzzer : IDisposable
{
    private readonly string _pptxPath;
    private readonly string _xlsxPath;

    public EffectsFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_eff_{Guid.NewGuid():N}.pptx");
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"fuzz_eff_{Guid.NewGuid():N}.xlsx");
        BlankDocCreator.Create(_pptxPath);
        BlankDocCreator.Create(_xlsxPath);

        using var pptx = new PowerPointHandler(_pptxPath, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() {
            ["text"] = "Hello", ["x"] = "2cm", ["y"] = "2cm",
            ["width"] = "10cm", ["height"] = "3cm", ["fill"] = "4472C4"
        });
    }

    public void Dispose()
    {
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
    }

    // ==================== Shadow NaN/Infinity (Bug F15) ====================

    [Fact]
    public void BugF15_Pptx_SetShadow_NaN_Blur_ShouldThrowArgumentException()
    {
        // shadow format: "COLOR-BLUR-ANGLE-DIST-OPACITY" — BLUR goes through double.TryParse with no NaN guard
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["shadow"] = "000000-NaN-45-3-40" });
        act.Should().Throw<ArgumentException>("NaN shadow blur should be rejected");
    }

    [Fact]
    public void BugF15_Pptx_SetShadow_Infinity_Blur_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["shadow"] = "000000-Infinity-45-3-40" });
        act.Should().Throw<ArgumentException>("Infinity shadow blur should be rejected");
    }

    [Fact]
    public void BugF15_Pptx_SetShadow_NaN_Opacity_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["shadow"] = "000000-4-45-3-NaN" });
        act.Should().Throw<ArgumentException>("NaN shadow opacity should be rejected");
    }

    [Fact]
    public void BugF15_Pptx_SetGlow_NaN_Radius_ShouldThrowArgumentException()
    {
        // glow format: "COLOR-RADIUS-OPACITY" — RADIUS through double.TryParse with no NaN guard
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["glow"] = "4472C4-NaN" });
        act.Should().Throw<ArgumentException>("NaN glow radius should be rejected");
    }

    [Fact]
    public void BugF15_Pptx_SetGlow_Infinity_Opacity_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["glow"] = "4472C4-8-Infinity" });
        act.Should().Throw<ArgumentException>("Infinity glow opacity should be rejected");
    }

    // ==================== Shadow/glow valid values ====================

    [Theory]
    [InlineData("000000")]
    [InlineData("000000-4-45-3-40")]
    [InlineData("FF0000-6-315-4-50")]
    public void Pptx_SetShadow_ValidValues_Succeeds(string shadow)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["shadow"] = shadow });
        act.Should().NotThrow($"shadow '{shadow}' should be accepted");
    }

    [Theory]
    [InlineData("4472C4")]
    [InlineData("4472C4-6")]
    [InlineData("FF0000-10-60")]
    public void Pptx_SetGlow_ValidValues_Succeeds(string glow)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["glow"] = glow });
        act.Should().NotThrow($"glow '{glow}' should be accepted");
    }

    // ==================== Material silent fallback (Bug F12) ====================

    [Theory]
    [InlineData("plastic")]
    [InlineData("metal")]
    [InlineData("warmMatte")]
    [InlineData("flat")]
    [InlineData("clear")]
    [InlineData("matte")]
    [InlineData("powder")]
    [InlineData("softMetal")]
    [InlineData("translucentPowder")]
    [InlineData("darkEdge")]
    public void Pptx_SetMaterial_ValidValues_Succeeds(string material)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["material"] = material });
        act.Should().NotThrow($"material '{material}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("rubber")]
    [InlineData("glass")]
    [InlineData("wood")]
    public void BugF12_Pptx_SetMaterial_InvalidValues_ShouldThrowArgumentException(string material)
    {
        // BUG: ParseMaterial uses `_ => Drawing.PresetMaterialTypeValues.Plastic` — silent fallback
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["material"] = material });
        act.Should().Throw<ArgumentException>($"material '{material}' is invalid and should be rejected");
    }

    // ==================== Lighting silent fallback (Bug F13) ====================

    [Theory]
    [InlineData("threePt")]
    [InlineData("balanced")]
    [InlineData("soft")]
    [InlineData("harsh")]
    [InlineData("flood")]
    [InlineData("brightRoom")]
    [InlineData("glow")]
    [InlineData("flat")]
    [InlineData("contrasting")]
    public void Pptx_SetLighting_ValidValues_Succeeds(string rig)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["lighting"] = rig });
        act.Should().NotThrow($"lighting '{rig}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("overhead")]
    [InlineData("ambient")]
    [InlineData("directional")]
    public void BugF13_Pptx_SetLighting_InvalidValues_ShouldThrowArgumentException(string rig)
    {
        // BUG: ParseLightRig uses `_ => Drawing.LightRigValues.ThreePoints` — silent fallback
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["lighting"] = rig });
        act.Should().Throw<ArgumentException>($"lighting '{rig}' is invalid and should be rejected");
    }

    // ==================== Bevel preset silent fallback (Bug F14) ====================

    [Theory]
    [InlineData("circle")]
    [InlineData("relaxedInset")]
    [InlineData("cross")]
    [InlineData("angle")]
    [InlineData("convex")]
    [InlineData("slope")]
    [InlineData("hardEdge")]
    [InlineData("artDeco")]
    public void Pptx_SetBevel_ValidValues_Succeeds(string bevel)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["bevel"] = bevel });
        act.Should().NotThrow($"bevel '{bevel}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("rounded")]
    [InlineData("flat")]
    [InlineData("emboss")]
    public void BugF14_Pptx_SetBevel_InvalidPreset_ShouldThrowArgumentException(string bevel)
    {
        // BUG: ParseBevelPreset uses WarnAndDefault (warns but silently returns Circle) — should throw
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["bevel"] = bevel });
        act.Should().Throw<ArgumentException>($"bevel preset '{bevel}' is invalid and should be rejected");
    }

    // ==================== Bevel width/height NaN/Infinity (Bug F15b) ====================

    [Fact]
    public void BugF15b_Pptx_SetBevel_NaN_Width_ShouldThrowArgumentException()
    {
        // bevel format: "PRESET-WIDTH-HEIGHT" — width goes through double.TryParse with no NaN guard
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["bevel"] = "circle-NaN-6" });
        act.Should().Throw<ArgumentException>("NaN bevel width should be rejected");
    }

    [Fact]
    public void BugF15b_Pptx_SetBevel_Infinity_Height_ShouldThrowArgumentException()
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["bevel"] = "circle-6-Infinity" });
        act.Should().Throw<ArgumentException>("Infinity bevel height should be rejected");
    }

    // ==================== Excel row height NaN/Infinity (Bug F16) ====================

    [Theory]
    [InlineData("15")]
    [InlineData("20.5")]
    [InlineData("30")]
    public void Xlsx_SetRowHeight_ValidValues_Succeeds(string height)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "row", null, new() { ["ref"] = "1" });
        var act = () => handler.Set("/Sheet1/row[1]", new() { ["height"] = height });
        act.Should().NotThrow($"row height '{height}' should be accepted");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("abc")]
    public void BugF16_Xlsx_SetRowHeight_InvalidValues_ShouldThrowArgumentException(string height)
    {
        // BUG: row height uses double.TryParse without IsNaN/IsInfinity guard
        using var handler = new ExcelHandler(_xlsxPath, editable: true);
        handler.Add("/Sheet1", "row", null, new() { ["ref"] = "1" });
        var act = () => handler.Set("/Sheet1/row[1]", new() { ["height"] = height });
        act.Should().Throw<ArgumentException>($"row height '{height}' should be rejected");
    }

    // ==================== 3D rotation valid/invalid ====================

    [Theory]
    [InlineData("45,30,0")]
    [InlineData("0,0,0")]
    [InlineData("90,45,180")]
    public void Pptx_SetRot3D_ValidValues_Succeeds(string rot)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rot3d"] = rot });
        act.Should().NotThrow($"rot3d '{rot}' should be accepted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Pptx_SetRot3D_InvalidValues_ShouldThrowException(string rot)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rot3d"] = rot });
        act.Should().Throw<Exception>($"rot3d '{rot}' is invalid");
    }

    [Fact]
    public void BugF17_Pptx_SetRot3D_NaN_ShouldThrowArgumentException()
    {
        // BUG: Apply3DRotation uses double.TryParse without NaN/Infinity check
        // "NaN,30,0" passes — NaN is stored as 60000*NaN which overflows to undefined int value
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rot3d"] = "NaN,30,0" });
        act.Should().Throw<ArgumentException>("NaN rot3d should be rejected");
    }

    [Fact]
    public void BugF17b_Pptx_SetRot3D_SingleComponent_ShouldThrowArgumentException()
    {
        // BUG: "45" (single number) matches rotX parse, rotY/rotZ silently default to 0
        // Documented format is "rotX,rotY,rotZ" (triplet) — single component should be rejected
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["rot3d"] = "45" });
        act.Should().Throw<ArgumentException>("rot3d single component '45' should require full triplet format");
    }

    // ==================== 3D depth NaN/Infinity (Bug F15c) ====================

    [Theory]
    [InlineData("10")]
    [InlineData("5.5")]
    [InlineData("0")]
    public void Pptx_Set3DDepth_ValidValues_Succeeds(string depth)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["depth"] = depth });
        act.Should().NotThrow($"depth '{depth}' should be accepted");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("abc")]
    public void BugF15c_Pptx_Set3DDepth_InvalidValues_ShouldThrowArgumentException(string depth)
    {
        // BUG: Apply3DExtrusion uses double.TryParse without NaN/Infinity check
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["depth"] = depth });
        act.Should().Throw<ArgumentException>($"depth '{depth}' should be rejected");
    }

    // ==================== SoftEdge NaN/Infinity ====================

    [Theory]
    [InlineData("3")]
    [InlineData("5.5")]
    [InlineData("0")]
    public void Pptx_SetSoftEdge_ValidValues_Succeeds(string radius)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["softEdge"] = radius });
        act.Should().NotThrow($"softEdge '{radius}' should be accepted");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    [InlineData("abc")]
    public void BugF18_Pptx_SetSoftEdge_InvalidValues_ShouldThrowArgumentException(string radius)
    {
        // BUG: ApplySoftEdge uses double.TryParse without NaN/Infinity check
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set("/slide[1]/shape[1]", new() { ["softEdge"] = radius });
        act.Should().Throw<ArgumentException>($"softEdge '{radius}' should be rejected");
    }
}
