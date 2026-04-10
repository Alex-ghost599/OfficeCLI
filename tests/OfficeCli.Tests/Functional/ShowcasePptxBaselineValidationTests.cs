using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ShowcasePptxBaselineValidationTests
{
    private static string RepoRoot => CliSubprocessHarness.GetRepositoryRoot();

    [Fact]
    public async Task BudgetReview_Example_CliValidateAndCheck_AreClean()
    {
        using var harness = new CliSubprocessHarness();
        var path = Path.Combine(RepoRoot, "examples", "budget_review_v2.pptx");

        var validate = await harness.RunAsync(TimeSpan.FromSeconds(30), "validate", path, "--json");
        validate.ExitCode.Should().Be(0, validate.Stdout + validate.Stderr);
        ReadValidationCount(validate.Stdout).Should().Be(0);

        var check = await harness.RunAsync(TimeSpan.FromSeconds(30), "check", path, "--json");
        check.ExitCode.Should().Be(0, check.Stdout + check.Stderr);
        ReadIssueCount(check.Stdout).Should().Be(0);
    }

    [Fact]
    public async Task AlienGuide_Example_CliCheck_IsClean()
    {
        using var harness = new CliSubprocessHarness();
        var path = Path.Combine(RepoRoot, "examples", "Alien_Guide.pptx");

        var check = await harness.RunAsync(TimeSpan.FromSeconds(30), "check", path, "--json");
        check.ExitCode.Should().Be(0, check.Stdout + check.Stderr);
        ReadIssueCount(check.Stdout).Should().Be(0);
    }

    private static int ReadValidationCount(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        return doc.RootElement.GetProperty("data").GetProperty("count").GetInt32();
    }

    private static int ReadIssueCount(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        return doc.RootElement.GetProperty("issueCount").GetInt32();
    }
}
