using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class ShowcasePptxBaselineValidationTests
{
    private static string RepoRoot => CliSubprocessHarness.GetRepositoryRoot();

    [Fact]
    public async Task BudgetReview_Example_CliValidateAndIssues_AreClean()
    {
        using var harness = new CliSubprocessHarness();
        var sourcePath = Path.Combine(RepoRoot, "examples", "budget_review_v2.pptx");
        var path = harness.CopyFixture(sourcePath);

        var validate = await harness.RunAsync(TimeSpan.FromSeconds(30), "validate", path, "--json");
        validate.ExitCode.Should().Be(0, validate.Stdout + validate.Stderr);
        ReadValidationCount(validate.Stdout).Should().Be(0);

        var issues = await harness.RunAsync(TimeSpan.FromSeconds(30), "view", path, "issues", "--json");
        issues.ExitCode.Should().Be(0, issues.Stdout + issues.Stderr);
        ReadIssueCount(issues.Stdout).Should().Be(0);
    }

    [Fact]
    public async Task AlienGuide_Example_CliIssues_AreClean()
    {
        using var harness = new CliSubprocessHarness();
        var sourcePath = Path.Combine(RepoRoot, "examples", "Alien_Guide.pptx");
        var path = harness.CopyFixture(sourcePath);

        var issues = await harness.RunAsync(TimeSpan.FromSeconds(30), "view", path, "issues", "--json");
        issues.ExitCode.Should().Be(0, issues.Stdout + issues.Stderr);
        ReadIssueCount(issues.Stdout).Should().Be(0);
    }

    private static int ReadValidationCount(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = doc.RootElement.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("count", out var count))
            return count.GetInt32();

        return 0;
    }

    private static int ReadIssueCount(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        return doc.RootElement.GetProperty("data").GetProperty("count").GetInt32();
    }
}
