using FluentAssertions;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ShowcaseXlsxBaselineValidationTests
{
    private static string RepoRoot => CliSubprocessHarness.GetRepositoryRoot();

    [Fact]
    public void Gradebook_ShowcaseSample_ValidatesCleanly()
    {
        var path = Path.Combine(RepoRoot, "assets", "showcase", "gradebook.xlsx");

        using var handler = new ExcelHandler(path, editable: false);
        var errors = handler.Validate();

        errors.Should().BeEmpty();
    }
}
