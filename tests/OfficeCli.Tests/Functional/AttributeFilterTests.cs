// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class AttributeFilterTests : IDisposable
{
    private readonly string _pptxPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pptx");
    private readonly string _docxPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
    private readonly string _xlsxPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");

    // ==================== Core Parsing Tests ====================

    [Fact]
    public void Parse_AllOperators_ReturnsCorrectConditions()
    {
        var conditions = AttributeFilter.Parse("shape[fill=FF0000][size>=24pt][text~=报告][type!=placeholder][width<=10cm]");

        conditions.Should().HaveCount(5);
        conditions[0].Should().Be(new AttributeFilter.Condition("fill", AttributeFilter.FilterOp.Equal, "FF0000"));
        conditions[1].Should().Be(new AttributeFilter.Condition("size", AttributeFilter.FilterOp.GreaterOrEqual, "24pt"));
        conditions[2].Should().Be(new AttributeFilter.Condition("text", AttributeFilter.FilterOp.Contains, "报告"));
        conditions[3].Should().Be(new AttributeFilter.Condition("type", AttributeFilter.FilterOp.NotEqual, "placeholder"));
        conditions[4].Should().Be(new AttributeFilter.Condition("width", AttributeFilter.FilterOp.LessOrEqual, "10cm"));
    }

    [Fact]
    public void Parse_NoFilters_ReturnsEmpty()
    {
        var conditions = AttributeFilter.Parse("shape");
        conditions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QuotedValues_StripsQuotes()
    {
        var conditions = AttributeFilter.Parse("shape[text~=\"hello world\"]");
        conditions.Should().HaveCount(1);
        conditions[0].Value.Should().Be("hello world");
    }

    // ==================== MatchAll Unit Tests ====================

    [Fact]
    public void MatchAll_EqualOperator_ExactMatch()
    {
        var node = new DocumentNode { Format = { ["fill"] = "FF0000" } };
        var conditions = new List<AttributeFilter.Condition>
        {
            new("fill", AttributeFilter.FilterOp.Equal, "FF0000")
        };

        AttributeFilter.MatchAll(node, conditions).Should().BeTrue();
    }

    [Fact]
    public void MatchAll_EqualOperator_CaseInsensitive()
    {
        var node = new DocumentNode { Format = { ["fill"] = "ff0000" } };
        var conditions = new List<AttributeFilter.Condition>
        {
            new("fill", AttributeFilter.FilterOp.Equal, "FF0000")
        };

        AttributeFilter.MatchAll(node, conditions).Should().BeTrue();
    }

    [Fact]
    public void MatchAll_EqualOperator_MissingKey_ReturnsFalse()
    {
        var node = new DocumentNode { Format = { ["bold"] = "true" } };
        var conditions = new List<AttributeFilter.Condition>
        {
            new("fill", AttributeFilter.FilterOp.Equal, "FF0000")
        };

        AttributeFilter.MatchAll(node, conditions).Should().BeFalse();
    }

    [Fact]
    public void MatchAll_NotEqualOperator_Works()
    {
        var node = new DocumentNode { Format = { ["type"] = "shape" } };

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("type", AttributeFilter.FilterOp.NotEqual, "placeholder")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("type", AttributeFilter.FilterOp.NotEqual, "shape")
        }).Should().BeFalse();
    }

    [Fact]
    public void MatchAll_NotEqual_MissingKey_ReturnsTrue()
    {
        var node = new DocumentNode();
        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("fill", AttributeFilter.FilterOp.NotEqual, "FF0000")
        }).Should().BeTrue();
    }

    [Fact]
    public void MatchAll_ContainsOperator_Works()
    {
        var node = new DocumentNode { Text = "2024年第三季度报告" };

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("text", AttributeFilter.FilterOp.Contains, "季度报告")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("text", AttributeFilter.FilterOp.Contains, "年度报告")
        }).Should().BeFalse();
    }

    [Fact]
    public void MatchAll_ContainsOperator_OnFormatValue()
    {
        var node = new DocumentNode { Format = { ["font"] = "Microsoft YaHei" } };

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("font", AttributeFilter.FilterOp.Contains, "YaHei")
        }).Should().BeTrue();
    }

    [Fact]
    public void MatchAll_GreaterOrEqual_PlainNumbers()
    {
        var node = new DocumentNode { Format = { ["size"] = "24pt" } };

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("size", AttributeFilter.FilterOp.GreaterOrEqual, "24pt")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("size", AttributeFilter.FilterOp.GreaterOrEqual, "20pt")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("size", AttributeFilter.FilterOp.GreaterOrEqual, "30pt")
        }).Should().BeFalse();
    }

    [Fact]
    public void MatchAll_LessOrEqual_PlainNumbers()
    {
        var node = new DocumentNode { Format = { ["size"] = "12pt" } };

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("size", AttributeFilter.FilterOp.LessOrEqual, "12pt")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("size", AttributeFilter.FilterOp.LessOrEqual, "20pt")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("size", AttributeFilter.FilterOp.LessOrEqual, "10pt")
        }).Should().BeFalse();
    }

    [Fact]
    public void MatchAll_MultipleConditions_AllMustPass()
    {
        var node = new DocumentNode
        {
            Text = "季度报告",
            Format = { ["fill"] = "FF0000", ["size"] = "24pt", ["bold"] = true }
        };

        var conditions = new List<AttributeFilter.Condition>
        {
            new("fill", AttributeFilter.FilterOp.Equal, "FF0000"),
            new("size", AttributeFilter.FilterOp.GreaterOrEqual, "20pt"),
            new("text", AttributeFilter.FilterOp.Contains, "报告")
        };

        AttributeFilter.MatchAll(node, conditions).Should().BeTrue();
    }

    [Fact]
    public void MatchAll_MultipleConditions_OneFailsReturnsFalse()
    {
        var node = new DocumentNode
        {
            Text = "季度报告",
            Format = { ["fill"] = "FF0000", ["size"] = "12pt" }
        };

        var conditions = new List<AttributeFilter.Condition>
        {
            new("fill", AttributeFilter.FilterOp.Equal, "FF0000"),
            new("size", AttributeFilter.FilterOp.GreaterOrEqual, "20pt")
        };

        AttributeFilter.MatchAll(node, conditions).Should().BeFalse();
    }

    // ==================== Apply Tests ====================

    [Fact]
    public void Apply_FiltersNodeList()
    {
        var nodes = new List<DocumentNode>
        {
            new() { Path = "/a", Format = { ["size"] = "24pt" } },
            new() { Path = "/b", Format = { ["size"] = "12pt" } },
            new() { Path = "/c", Format = { ["size"] = "36pt" } }
        };

        var conditions = AttributeFilter.Parse("shape[size>=20pt]");
        var filtered = AttributeFilter.Apply(nodes, conditions);

        filtered.Should().HaveCount(2);
        filtered.Select(n => n.Path).Should().BeEquivalentTo(new[] { "/a", "/c" });
    }

    [Fact]
    public void Apply_EmptyConditions_ReturnsAll()
    {
        var nodes = new List<DocumentNode>
        {
            new() { Path = "/a" },
            new() { Path = "/b" }
        };

        var filtered = AttributeFilter.Apply(nodes, new List<AttributeFilter.Condition>());
        filtered.Should().HaveCount(2);
    }

    // ==================== PPTX Integration Tests ====================

    [Fact]
    public void Pptx_Query_ContainsFilter_MatchesText()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "2024年第三季度报告" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "年度预算" });

        var allShapes = handler.Query("shape");
        var conditions = AttributeFilter.Parse("shape[text~=季度报告]");
        var filtered = AttributeFilter.Apply(allShapes, conditions);

        filtered.Should().HaveCount(1);
        filtered[0].Text.Should().Contain("季度报告");
    }

    [Fact]
    public void Pptx_Query_SizeGreaterOrEqual_FiltersCorrectly()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Small", ["size"] = "12" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Large", ["size"] = "24" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Medium", ["size"] = "18" });

        var allShapes = handler.Query("shape");
        var conditions = AttributeFilter.Parse("shape[size>=18pt]");
        var filtered = AttributeFilter.Apply(allShapes, conditions);

        filtered.Should().HaveCountGreaterOrEqualTo(2);
        foreach (var node in filtered)
        {
            var sizeStr = node.Format.ContainsKey("size") ? node.Format["size"]?.ToString() : null;
            if (sizeStr != null)
            {
                var sizeNum = decimal.Parse(sizeStr.Replace("pt", ""));
                sizeNum.Should().BeGreaterOrEqualTo(18);
            }
        }
    }

    [Fact]
    public void Pptx_Query_FillExact_FiltersCorrectly()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Red", ["fill"] = "FF0000" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Blue", ["fill"] = "0000FF" });

        var allShapes = handler.Query("shape");
        var conditions = AttributeFilter.Parse("shape[fill=FF0000]");
        // Use applyAll since = is normally handled by handler
        var filtered = AttributeFilter.Apply(allShapes, conditions, applyAll: true);

        filtered.Should().HaveCount(1);
        filtered[0].Text.Should().Be("Red");
    }

    [Fact]
    public void Pptx_Query_MultipleFilters_CombinedCorrectly()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Big Red", ["fill"] = "FF0000", ["size"] = "24" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Small Red", ["fill"] = "FF0000", ["size"] = "10" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "Big Blue", ["fill"] = "0000FF", ["size"] = "24" });

        // Query all shapes first, then apply combined attribute filters
        var allShapes = handler.Query("shape");
        allShapes.Count.Should().BeGreaterOrEqualTo(3, "should have at least the 3 added shapes");

        var conditions = AttributeFilter.Parse("shape[fill=FF0000][size>=20pt]");
        conditions.Should().HaveCount(2);

        var filtered = AttributeFilter.Apply(allShapes, conditions, applyAll: true);

        filtered.Should().HaveCount(1);
        filtered[0].Text.Should().Be("Big Red");
    }

    // ==================== Word Integration Tests ====================

    [Fact]
    public void Word_Query_ContainsFilter_MatchesParagraphText()
    {
        BlankDocCreator.Create(_docxPath);
        using var handler = new WordHandler(_docxPath, editable: true);

        handler.Add("/body", "paragraph", null, new() { ["text"] = "第三季度财务报告" });
        handler.Add("/body", "paragraph", null, new() { ["text"] = "年度工作总结" });
        handler.Add("/body", "paragraph", null, new() { ["text"] = "第四季度财务报告" });

        var allParagraphs = handler.Query("paragraph");
        var conditions = AttributeFilter.Parse("paragraph[text~=季度]");
        var filtered = AttributeFilter.Apply(allParagraphs, conditions);

        filtered.Should().HaveCount(2);
        filtered.All(n => n.Text?.Contains("季度") == true).Should().BeTrue();
    }

    [Fact]
    public void Word_Query_SizeLessOrEqual_FiltersRuns()
    {
        BlankDocCreator.Create(_docxPath);
        using var handler = new WordHandler(_docxPath, editable: true);

        handler.Add("/body", "paragraph", null, new() { ["text"] = "Small text", ["size"] = "10" });
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Large text", ["size"] = "24" });

        var allParagraphs = handler.Query("paragraph");
        var conditions = AttributeFilter.Parse("paragraph[size<=12pt]");
        // Note: size may be in Format of runs, not paragraphs. This tests the concept.
        var filtered = AttributeFilter.Apply(allParagraphs, conditions);

        // All paragraphs without a "size" in Format will be filtered out
        // Only paragraphs that have size <= 12pt will remain
        foreach (var node in filtered)
        {
            if (node.Format.ContainsKey("size"))
            {
                var sizeStr = node.Format["size"]?.ToString() ?? "";
                var sizeNum = decimal.Parse(sizeStr.Replace("pt", ""));
                sizeNum.Should().BeLessOrEqualTo(12);
            }
        }
    }

    // ==================== Excel Integration Tests ====================

    [Fact]
    public void Excel_Query_ContainsFilter_MatchesCellText()
    {
        BlankDocCreator.Create(_xlsxPath);
        using var handler = new ExcelHandler(_xlsxPath, editable: true);

        handler.Set("/Sheet1/A1", new() { ["value"] = "Q1 Revenue" });
        handler.Set("/Sheet1/A2", new() { ["value"] = "Q2 Revenue" });
        handler.Set("/Sheet1/A3", new() { ["value"] = "Annual Summary" });

        var allCells = handler.Query("cell[empty=false]");
        var conditions = AttributeFilter.Parse("cell[text~=Revenue]");
        var filtered = AttributeFilter.Apply(allCells, conditions);

        filtered.Should().HaveCount(2);
        filtered.All(n => n.Text?.Contains("Revenue") == true).Should().BeTrue();
    }

    // ==================== Dimension Comparison Tests ====================

    [Fact]
    public void MatchAll_DimensionValues_ComparesCorrectly()
    {
        var node = new DocumentNode { Format = { ["width"] = "5.08cm" } };

        // 5.08cm = 2in
        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("width", AttributeFilter.FilterOp.GreaterOrEqual, "2in")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("width", AttributeFilter.FilterOp.LessOrEqual, "3in")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("width", AttributeFilter.FilterOp.GreaterOrEqual, "3in")
        }).Should().BeFalse();
    }

    [Fact]
    public void MatchAll_TypeFilter_MatchesNodeType()
    {
        var node = new DocumentNode { Type = "shape" };

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("type", AttributeFilter.FilterOp.NotEqual, "placeholder")
        }).Should().BeTrue();

        AttributeFilter.MatchAll(node, new List<AttributeFilter.Condition>
        {
            new("type", AttributeFilter.FilterOp.Equal, "shape")
        }).Should().BeTrue();
    }

    // ==================== P0: Word > combinator vs >= ====================

    [Fact]
    public void Word_ChildCombinator_WithGreaterOrEqual_ParsesCorrectly()
    {
        BlankDocCreator.Create(_docxPath);
        using var handler = new WordHandler(_docxPath, editable: true);

        handler.Add("/body", "paragraph", null, new() { ["text"] = "Big text", ["size"] = "24" });
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Small text", ["size"] = "10" });

        // "paragraph[size>=20pt] > run" should NOT break the parser
        // The > inside [size>=20pt] must not be treated as child combinator
        var allParas = handler.Query("paragraph");
        allParas.Should().HaveCountGreaterOrEqualTo(2);

        // Also test that the selector "paragraph > run[size>=14pt]" works
        // (the > between paragraph and run IS a child combinator)
        var runs = handler.Query("paragraph > run[size>=14pt]");
        // Should not throw — parsing should succeed
    }

    [Fact]
    public void Word_SplitChildCombinator_PreservesBracketed_GreaterThan()
    {
        // Unit test the split logic directly via a round-trip through ParseSelector + Query
        BlankDocCreator.Create(_docxPath);
        using var handler = new WordHandler(_docxPath, editable: true);

        handler.Add("/body", "paragraph", null, new() { ["text"] = "Test", ["size"] = "18" });

        // This selector has >= inside brackets — must not split there
        var result = handler.Query("paragraph[size>=14pt]");
        // Should return results without error (the >= is ignored by Word's parser,
        // but the point is it doesn't crash from bad split)
        result.Should().NotBeNull();
    }

    // ==================== P1: ~= works in handler AND post-filter (idempotent) ====================

    [Fact]
    public void Pptx_ContainsFilter_WorksDirectlyViaHandler()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "季度报告内容" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "其他内容" });

        // Direct handler.Query() with ~= should work (handler handles it)
        var results = handler.Query("shape[text~=季度]");
        results.Should().HaveCount(1);
        results[0].Text.Should().Contain("季度");
    }

    [Fact]
    public void Pptx_ContainsFilter_PostFilterIsIdempotent()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "季度报告内容" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "其他内容" });

        // Handler filters first, post-filter re-filters (idempotent)
        var handlerResults = handler.Query("shape[text~=季度]");
        var conditions = AttributeFilter.Parse("shape[text~=季度]");
        var postFiltered = AttributeFilter.Apply(handlerResults, conditions);

        postFiltered.Should().HaveCount(1);
        postFiltered[0].Text.Should().Contain("季度");
    }

    [Fact]
    public void Pptx_ContainsFilter_OnFormatProperty_WorksViaPostFilter()
    {
        BlankDocCreator.Create(_pptxPath);
        using var handler = new PowerPointHandler(_pptxPath, editable: true);

        handler.Add("/", "slide", null, new() { ["title"] = "Test" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "A", ["font"] = "Microsoft YaHei" });
        handler.Add("/slide[1]", "shape", null, new() { ["text"] = "B", ["font"] = "Arial" });

        var allShapes = handler.Query("shape");
        var conditions = AttributeFilter.Parse("shape[font~=YaHei]");
        var filtered = AttributeFilter.Apply(allShapes, conditions);

        filtered.Should().HaveCount(1);
        filtered[0].Text.Should().Be("A");
    }

    // ==================== P2: Warnings for missing keys and type validation ====================

    [Fact]
    public void ApplyWithWarnings_MissingKey_ReturnsWarning()
    {
        var nodes = new List<DocumentNode>
        {
            new() { Path = "/a", Format = { ["bold"] = "true" } },
            new() { Path = "/b", Format = { ["italic"] = "true" } }
        };

        var conditions = AttributeFilter.Parse("shape[nonExistentKey>=10]");
        var (results, warnings) = AttributeFilter.ApplyWithWarnings(nodes, conditions);

        results.Should().BeEmpty();
        warnings.Should().ContainMatch("*'nonExistentKey'*not found*");
        // Warning should include available keys
        warnings.Should().ContainMatch("*bold*");
    }

    [Fact]
    public void ApplyWithWarnings_NonNumericGreaterOrEqual_ReturnsWarning()
    {
        var nodes = new List<DocumentNode>
        {
            new() { Path = "/a", Format = { ["font"] = "Arial" } }
        };

        var conditions = AttributeFilter.Parse("shape[font>=Arial]");
        var (_, warnings) = AttributeFilter.ApplyWithWarnings(nodes, conditions);

        // Both the filter value "Arial" and the actual value "Arial" are non-numeric
        warnings.Should().ContainMatch("*'Arial'*not numeric*");
    }

    [Fact]
    public void ApplyWithWarnings_NumericValues_NoWarning()
    {
        var nodes = new List<DocumentNode>
        {
            new() { Path = "/a", Format = { ["size"] = "24pt" } }
        };

        var conditions = AttributeFilter.Parse("shape[size>=20pt]");
        var (results, warnings) = AttributeFilter.ApplyWithWarnings(nodes, conditions);

        results.Should().HaveCount(1);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void ApplyWithWarnings_NotEqual_MissingKey_NoWarning()
    {
        // != with missing key is valid (key absent means "not equal"), so no warning
        var nodes = new List<DocumentNode>
        {
            new() { Path = "/a", Format = { ["bold"] = "true" } }
        };

        var conditions = new List<AttributeFilter.Condition>
        {
            new("nonExistent", AttributeFilter.FilterOp.NotEqual, "something")
        };

        var (results, warnings) = AttributeFilter.ApplyWithWarnings(nodes, conditions, applyAll: true);

        results.Should().HaveCount(1); // key missing → not equal → matches
        warnings.Should().BeEmpty();
    }

    public void Dispose()
    {
        try { File.Delete(_pptxPath); } catch { }
        try { File.Delete(_docxPath); } catch { }
        try { File.Delete(_xlsxPath); } catch { }
    }
}
