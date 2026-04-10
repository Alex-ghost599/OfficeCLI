using FluentAssertions;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public sealed class ChartAddPartPlaceholderValidationTests
{
    private const string ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void Word_AddPartChart_CreatesSchemaValidPlaceholder_AndRawSetCanEditIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chart-add-part-{Guid.NewGuid():N}.docx");
        try
        {
            BlankDocCreator.Create(path);
            using var handler = new WordHandler(path, editable: true);

            var (_, partPath) = handler.AddPart("/", "chart");
            ReplaceChartLayout(handler, partPath);

            handler.Validate().Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Excel_AddPartChart_CreatesSchemaValidPlaceholder_AndRawSetCanEditIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chart-add-part-{Guid.NewGuid():N}.xlsx");
        try
        {
            BlankDocCreator.Create(path);
            using var handler = new ExcelHandler(path, editable: true);

            var (_, partPath) = handler.AddPart("/Sheet1", "chart");
            ReplaceChartLayout(handler, partPath);

            handler.Validate().Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Pptx_AddPartChart_CreatesSchemaValidPlaceholder_AndRawSetCanEditIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chart-add-part-{Guid.NewGuid():N}.pptx");
        try
        {
            BlankDocCreator.Create(path);
            using var handler = new PowerPointHandler(path, editable: true);
            handler.Add("/", "slide", null, new() { ["title"] = "Placeholder slide" });

            var (_, partPath) = handler.AddPart("/slide[1]", "chart");
            ReplaceChartLayout(handler, partPath);

            handler.Validate().Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void ReplaceChartLayout(WordHandler handler, string partPath)
    {
        handler.RawSet(
            partPath,
            "/*[local-name()='chartSpace']/*[local-name()='chart']/*[local-name()='plotArea']/*[local-name()='layout']",
            "replace",
            $"""<c:layout xmlns:c="{ChartNs}"/>""");
    }

    private static void ReplaceChartLayout(ExcelHandler handler, string partPath)
    {
        handler.RawSet(
            partPath,
            "/*[local-name()='chartSpace']/*[local-name()='chart']/*[local-name()='plotArea']/*[local-name()='layout']",
            "replace",
            $"""<c:layout xmlns:c="{ChartNs}"/>""");
    }

    private static void ReplaceChartLayout(PowerPointHandler handler, string partPath)
    {
        handler.RawSet(
            partPath,
            "/*[local-name()='chartSpace']/*[local-name()='chart']/*[local-name()='plotArea']/*[local-name()='layout']",
            "replace",
            $"""<c:layout xmlns:c="{ChartNs}"/>""");
    }
}
