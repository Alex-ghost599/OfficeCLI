using FluentAssertions;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class SkillInstallerFunctionalTests
{
    [Fact]
    public async Task HelpAndLoadSkill_ExposeWordFormAndStripInstallerNoise()
    {
        using var cli = new CliSubprocessHarness();

        var help = await cli.RunAsync(TimeSpan.FromSeconds(20), "help", "skills");
        help.ExitCode.Should().Be(0, help.Stdout + help.Stderr);
        help.Stdout.Should().Contain("word-form");
        help.Stdout.Should().Contain("morph-ppt-3d");
        help.Stdout.Should().Contain("codex-desktop");

        var skill = await cli.RunAsync(TimeSpan.FromSeconds(20), "load_skill", "word-form");
        skill.ExitCode.Should().Be(0, skill.Stdout + skill.Stderr);
        skill.Stdout.Should().Contain("## Local Check");
        skill.Stdout.Should().Contain("OfficeCLI 1.0.109");
        skill.Stdout.Should().Contain("FormField help under-lists add-time props");
        skill.Stdout.Should().NotContain("curl -fsSL");
        skill.Stdout.Should().NotContain("install.ps1");
        skill.Stdout.Should().NotContain("v1.0.63");
    }

    [Fact]
    public async Task InstallToCodexDesktop_WritesLocalSkillsWithoutInstallerNoise()
    {
        using var cli = new CliSubprocessHarness();

        var rootInstall = await cli.RunAsync(TimeSpan.FromSeconds(20), "skills", "codex-desktop");
        rootInstall.ExitCode.Should().Be(0, rootInstall.Stdout + rootInstall.Stderr);
        var rootSkill = File.ReadAllText(Path.Combine(cli.HomeDir, ".codex", "skills", "officecli", "SKILL.md"));
        rootSkill.Should().Contain("## Local Check");
        rootSkill.Should().Contain("officecli help all --jsonl");
        rootSkill.Should().Contain("word-form");
        rootSkill.Should().NotContain("curl -fsSL");
        rootSkill.Should().NotContain("install.ps1");

        var wordFormInstall = await cli.RunAsync(TimeSpan.FromSeconds(20), "skills", "install", "word-form", "codex-desktop");
        wordFormInstall.ExitCode.Should().Be(0, wordFormInstall.Stdout + wordFormInstall.Stderr);
        var wordFormSkill = File.ReadAllText(Path.Combine(cli.HomeDir, ".codex", "skills", "officecli-word-form", "SKILL.md"));
        wordFormSkill.Should().Contain("## Local Check");
        wordFormSkill.Should().Contain("FormField help under-lists add-time props");
        wordFormSkill.Should().NotContain("## BEFORE YOU START");
        wordFormSkill.Should().NotContain("curl -fsSL");
        wordFormSkill.Should().NotContain("install.ps1");

        var morphInstall = await cli.RunAsync(TimeSpan.FromSeconds(20), "skills", "install", "morph-ppt-3d", "codex-desktop");
        morphInstall.ExitCode.Should().Be(0, morphInstall.Stdout + morphInstall.Stderr);
        var morphSkill = File.ReadAllText(Path.Combine(cli.HomeDir, ".codex", "skills", "morph-ppt-3d", "SKILL.md"));
        morphSkill.Should().NotContain("## Setup");
        morphSkill.Should().NotContain("curl -fsSL");
        morphSkill.Should().NotContain("install.ps1");
    }
}
