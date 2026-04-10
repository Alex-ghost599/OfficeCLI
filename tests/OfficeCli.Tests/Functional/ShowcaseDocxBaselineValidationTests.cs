using FluentAssertions;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ShowcaseDocxBaselineValidationTests
{
    private static string RepoRoot => CliSubprocessHarness.GetRepositoryRoot();

    [Fact]
    public void AnnualReport_ShowcaseSample_ValidatesCleanly()
    {
        var path = Path.Combine(RepoRoot, "assets", "showcase", "annual-report.docx");

        using var handler = new WordHandler(path, editable: false);
        var errors = handler.Validate();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void AcademicPaper_ShowcaseSample_ValidatesCleanly()
    {
        var path = Path.Combine(RepoRoot, "assets", "showcase", "academic-paper.docx");

        using var handler = new WordHandler(path, editable: false);
        var errors = handler.Validate();

        errors.Should().BeEmpty();
    }
}
