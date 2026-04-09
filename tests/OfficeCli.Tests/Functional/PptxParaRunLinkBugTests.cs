// Bug #87 — PPTX Set: paragraph/run path doesn't handle "link" property
// File: PowerPointHandler.Set.cs, lines 186-210
// The /slide[N]/shape[M]/paragraph[P]/run[K] path passes ALL properties
// (including "link") to SetRunOrShapeProperties, which doesn't know how
// to handle "link" and reports it as unsupported.
// The /slide[N]/shape[M]/run[K] path correctly extracts "link" and calls
// ApplyRunHyperlink separately.
//
// Bug #88 — PPTX Set: paragraph-level path also doesn't handle "link"
// File: PowerPointHandler.Set.cs, lines 280-284
// The /slide[N]/shape[M]/paragraph[P] path passes unknown properties
// (including "link") to SetRunOrShapeProperties via the default case,
// which doesn't handle "link" and reports it as unsupported.

using FluentAssertions;
using OfficeCli;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class PptxParaRunLinkBugTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pptx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Bug87_PptxSet_ParagraphRunPath_LinkPropertySilentlyFails()
    {
        BlankDocCreator.Create(_path);
        using var pptx = new PowerPointHandler(_path, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() { ["text"] = "Click me" });

        // Set link via /slide[N]/shape[M]/paragraph[P]/run[K] path
        var unsupported = pptx.Set("/slide[1]/shape[1]/paragraph[1]/run[1]", new()
        {
            ["link"] = "https://example.com"
        });

        // Bug: "link" is reported as unsupported because the paragraph/run
        // path doesn't extract and handle it like the shape/run path does
        unsupported.Should().NotContain("link",
            "link property should be handled for paragraph/run path, " +
            "but it's passed to SetRunOrShapeProperties which doesn't handle it");
    }

    [Fact]
    public void Bug87_Control_ShapeRunPath_LinkPropertyWorks()
    {
        // Control test: verify the shape/run path handles link correctly
        BlankDocCreator.Create(_path);
        using var pptx = new PowerPointHandler(_path, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() { ["text"] = "Click me" });

        // Set link via /slide[N]/shape[M]/run[K] path (should work)
        var unsupported = pptx.Set("/slide[1]/shape[1]/run[1]", new()
        {
            ["link"] = "https://example.com"
        });

        unsupported.Should().NotContain("link",
            "link property should be handled for shape/run path");
    }

    [Fact]
    public void Bug88_PptxSet_ParagraphPath_LinkPropertySilentlyFails()
    {
        BlankDocCreator.Create(_path);
        using var pptx = new PowerPointHandler(_path, editable: true);
        pptx.Add("/", "slide", null, new());
        pptx.Add("/slide[1]", "shape", null, new() { ["text"] = "Click me" });

        // Set link via /slide[N]/shape[M]/paragraph[P] path
        // This should apply the link to all runs in the paragraph
        var unsupported = pptx.Set("/slide[1]/shape[1]/paragraph[1]", new()
        {
            ["link"] = "https://example.com"
        });

        // Bug: "link" falls to the default case which passes it to
        // SetRunOrShapeProperties, which doesn't handle "link"
        unsupported.Should().NotContain("link",
            "link property should be handled for paragraph path, " +
            "but the default case passes it to SetRunOrShapeProperties which doesn't handle it");
    }
}
