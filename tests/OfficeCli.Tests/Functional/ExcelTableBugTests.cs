// Bug test: ExcelHandler.Set uses TotalsRowCount for "totalrow"
// but Get/readback reads TotalsRowShown. This mismatch means
// Set totalrow=true won't be reflected in Get.

using FluentAssertions;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ExcelTableBugTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Set_TotalRow_True_Should_Be_Reflected_In_Get()
    {
        // Arrange: create xlsx with a table that has totalRow=false
        var path = CreateTempFile();
        BlankDocCreator.Create(path);

        using (var handler = new ExcelHandler(path, editable: true))
        {
            // Add header row data
            handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "Name" });
            handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "B1", ["value"] = "Score" });
            handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A2", ["value"] = "Alice" });
            handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "B2", ["value"] = "90" });
            handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A3", ["value"] = "Bob" });
            handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "B3", ["value"] = "85" });

            // Add table without totalRow
            handler.Add("/Sheet1", "table", null, new()
            {
                ["ref"] = "A1:B3",
                ["name"] = "TestTable",
                ["totalRow"] = "false"
            });

            // Verify totalRow is false initially
            var node = handler.Get("/Sheet1/table[1]");
            node.Format["totalRow"].Should().Be(false);

            // Act: Set totalRow to true
            handler.Set("/Sheet1/table[1]", new() { ["totalrow"] = "true" });

            // Assert: Get should reflect totalRow=true
            var updatedNode = handler.Get("/Sheet1/table[1]");
            updatedNode.Format["totalRow"].Should().Be(true,
                "because Set(totalrow=true) should update the same property that Get reads (TotalsRowShown), " +
                "but Set actually modifies TotalsRowCount instead");
        }
    }
}
