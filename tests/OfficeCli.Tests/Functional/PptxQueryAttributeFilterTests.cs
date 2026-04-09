// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class PptxQueryAttributeFilterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pptx");

    /// <summary>
    /// BT-5 / Bug #84: PPTX Query [attr=value] fails for camelCase attribute keys.
    /// ParseShapeSelector lowercases the key, but Format dictionary uses camelCase keys
    /// with case-sensitive comparison, so TryGetValue("autofit") misses Format["autoFit"].
    /// </summary>
    [Fact]
    public void Query_CamelCaseAttribute_ShouldMatchCaseInsensitively()
    {
        BlankDocCreator.Create(_path);
        using var handler = new PowerPointHandler(_path, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Hello", ["fill"] = "FF0000" });

        // Set autoFit on the shape
        handler.Set("/slide[1]/shape[2]", new() { ["autoFit"] = "normal" });

        // Verify autoFit is set
        var node = handler.Get("/slide[1]/shape[2]");
        node.Format["autoFit"].Should().Be("normal");

        // Query with camelCase key — this should match but fails because
        // ParseShapeSelector lowercases "autoFit" to "autofit", but
        // Format dictionary key is "autoFit" (case-sensitive lookup)
        var results = handler.Query("shape[autoFit=normal]");
        results.Count.Should().Be(1,
            "because shape[autoFit=normal] should find the shape with autoFit=normal, " +
            "but ParseShapeSelector lowercases to 'autofit' which doesn't match Format key 'autoFit'");
    }

    [Fact]
    public void Query_LineWidthAttribute_ShouldMatchCaseInsensitively()
    {
        BlankDocCreator.Create(_path);
        using var handler = new PowerPointHandler(_path, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Bordered", ["line"] = "000000", ["lineWidth"] = "2pt" });

        // Verify lineWidth is set
        var node = handler.Get("/slide[1]/shape[2]");
        node.Format.Should().ContainKey("lineWidth");

        // Query with camelCase key
        var results = handler.Query("shape[lineWidth=2pt]");
        results.Count.Should().BeGreaterOrEqualTo(1,
            "because shape[lineWidth=2pt] should match, but 'linewidth' != 'lineWidth' in case-sensitive lookup");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}
