// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

/// <summary>
/// Bug #85: Excel Query("row") silently treats "row" as column name "ROW"
/// instead of querying rows. The CellSelector regex ^[A-Z]+$ matches "row"
/// because it's 3 alpha characters, causing it to be interpreted as column filter.
/// Same issue affects "sheet" element type query (4 chars so not affected by length check).
/// </summary>
public class ExcelQueryRowBugTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");

    [Fact]
    public void Query_Row_ShouldNotBeTreatedAsColumnName()
    {
        BlankDocCreator.Create(_path);
        using var handler = new ExcelHandler(_path, editable: true);

        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A1", ["value"] = "Name" });
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "B1", ["value"] = "Age" });
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "A2", ["value"] = "Alice" });
        handler.Add("/Sheet1", "cell", null, new() { ["ref"] = "B2", ["value"] = "30" });

        // Query("row") should NOT be interpreted as column filter for column "ROW"
        // It should either return rows or throw an informative error
        // Currently it silently returns 0 results because no cells are in column "ROW"
        var results = handler.Query("row");

        // The bug: results will be empty because "row" is treated as column "ROW"
        // Expected: should return row data or at least not silently return empty
        results.Count.Should().BeGreaterThan(0,
            "because Query(\"row\") should query rows, not search for cells in non-existent column 'ROW'. " +
            "ParseCellSelector treats 'row' as a column name because it matches ^[A-Z]+$ regex.");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}
