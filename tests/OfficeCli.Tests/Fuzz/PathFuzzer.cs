// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

// Fuzz tests for DOM path parameters: invalid indices, special characters, empty paths.
// Tests that out-of-bounds/malformed paths throw appropriate exceptions.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Fuzz;

public class PathFuzzer : IDisposable
{
    private readonly string _pptxPath;
    private readonly string _xlsxPath;
    private readonly string _docxPath;

    public PathFuzzer()
    {
        _pptxPath = Path.Combine(Path.GetTempPath(), $"fuzz_path_{Guid.NewGuid():N}.pptx");
        _xlsxPath = Path.Combine(Path.GetTempPath(), $"fuzz_path_{Guid.NewGuid():N}.xlsx");
        _docxPath = Path.Combine(Path.GetTempPath(), $"fuzz_path_{Guid.NewGuid():N}.docx");
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

    // ==================== PPTX Get: out-of-bounds slide index ====================

    [Theory]
    [InlineData("/slide[0]")]     // 0-based not valid (1-based)
    [InlineData("/slide[-1]")]    // negative index
    [InlineData("/slide[99999]")] // way out of bounds
    [InlineData("/slide[2]")]     // out of bounds (only 1 slide exists)
    public void Pptx_Get_OutOfBoundSlideIndex_ThrowsOrReturnsNull(string path)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: false);
        // Either throws ArgumentException or returns null — must not crash with unhandled exception
        try
        {
            var result = handler.Get(path);
            result.Should().BeNull($"path '{path}' is out of bounds and should return null");
        }
        catch (ArgumentException)
        {
            // Acceptable: clear error about invalid index
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            false.Should().BeTrue($"path '{path}' caused unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ==================== PPTX Get: out-of-bounds shape index ====================
    // Note: shape[-1] confirmed bug F42 — moved to FuzzBugTests2.cs

    [Theory]
    [InlineData("/slide[1]/shape[0]")]      // 0-based
    [InlineData("/slide[1]/shape[99999]")]  // way out of bounds
    [InlineData("/slide[1]/shape[2]")]      // only 1 shape exists
    public void Pptx_Get_OutOfBoundShapeIndex_ThrowsOrReturnsNull(string path)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: false);
        try
        {
            var result = handler.Get(path);
            result.Should().BeNull($"path '{path}' is out of bounds and should return null");
        }
        catch (ArgumentException)
        {
            // Acceptable
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            false.Should().BeTrue($"path '{path}' caused unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ==================== PPTX Set: out-of-bounds paths ====================

    [Theory]
    [InlineData("/slide[0]/shape[1]")]
    [InlineData("/slide[-1]/shape[1]")]
    [InlineData("/slide[99999]/shape[1]")]
    public void Pptx_Set_OutOfBoundPath_ThrowsException(string path)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Set(path, new() { ["text"] = "Hello" });
        act.Should().Throw<Exception>($"path '{path}' is out of bounds");
    }

    // ==================== DOCX Get: out-of-bounds paragraph index ====================

    [Theory]
    [InlineData("/body/p[0]")]
    [InlineData("/body/p[-1]")]
    [InlineData("/body/p[99999]")]
    public void Docx_Get_OutOfBoundParagraphIndex_ThrowsOrReturnsNull(string path)
    {
        using var handler = new WordHandler(_docxPath, editable: false);
        try
        {
            var result = handler.Get(path);
            result.Should().BeNull($"path '{path}' is out of bounds and should return null");
        }
        catch (ArgumentException)
        {
            // Acceptable
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            false.Should().BeTrue($"path '{path}' caused unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ==================== XLSX Get: invalid cell references ====================
    // Note: A0, ZZZ99999, 00 confirmed bug F43 — moved to FuzzBugTests2.cs

    [Theory]
    [InlineData("/Sheet1/A1")]         // valid cell
    [InlineData("/Sheet1/Z1")]         // valid column Z
    public void Xlsx_Get_ValidCellRef_DoesNotThrow(string path)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: false);
        var act = () => handler.Get(path);
        act.Should().NotThrow<NullReferenceException>($"valid cell path '{path}' should not throw NullReferenceException");
    }

    // ==================== XLSX Get: non-existent sheet ====================

    [Theory]
    [InlineData("/NonExistentSheet")]
    [InlineData("/Sheet999")]
    [InlineData("/")]  // root
    public void Xlsx_Get_NonExistentSheet_ThrowsOrReturnsNull(string path)
    {
        using var handler = new ExcelHandler(_xlsxPath, editable: false);
        try
        {
            var result = handler.Get(path);
            // Root "/" returns workbook info — that's OK
            // Non-existent sheet should return null
            if (path != "/")
                result.Should().BeNull($"sheet path '{path}' does not exist");
        }
        catch (ArgumentException)
        {
            // Acceptable
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            false.Should().BeTrue($"path '{path}' caused unexpected exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ==================== Add with invalid parent paths ====================

    [Theory]
    [InlineData("/slide[0]")]
    [InlineData("/slide[99999]")]
    [InlineData("/slide[-1]")]
    public void Pptx_Add_InvalidParentPath_ThrowsException(string parent)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: true);
        var act = () => handler.Add(parent, "shape", null, new() { ["text"] = "Hello" });
        act.Should().Throw<Exception>($"parent path '{parent}' is invalid");
    }

    // ==================== Path with special characters ====================

    [Theory]
    [InlineData("/slide[1]/shape[<>]")]
    [InlineData("/slide[1]/shape[&]")]
    [InlineData("/slide[1]/shape[\"1\"]")]
    [InlineData("/slide[1]/shape[\0]")]
    public void Pptx_Get_PathWithSpecialChars_DoesNotCrashWithUnhandledException(string path)
    {
        using var handler = new PowerPointHandler(_pptxPath, editable: false);
        var act = () => handler.Get(path);
        act.Should().NotThrow<NullReferenceException>($"path '{path}' must not cause NullReferenceException");
        act.Should().NotThrow<StackOverflowException>($"path '{path}' must not cause StackOverflowException");
    }

    // Note: empty path confirmed bug F44 — moved to FuzzBugTests2.cs
}
