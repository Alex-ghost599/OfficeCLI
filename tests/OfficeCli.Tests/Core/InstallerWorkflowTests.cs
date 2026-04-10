using FluentAssertions;
using OfficeCli.Core;
using Xunit;

namespace OfficeCli.Tests.Core;

public class InstallerWorkflowTests
{
    [Fact]
    public void AppConfig_Defaults_AutoUpdate_To_False()
    {
        var config = new AppConfig();

        config.AutoUpdate.Should().BeFalse();
        UpdateChecker.ShouldCheckInBackground(config, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void RunInstallCore_NonInteractive_DoesNotInvoke_OptionalSetup()
    {
        var installCalled = false;
        var setupCalled = false;

        var exitCode = Installer.RunInstallCore("all", new Installer.InstallActionHooks
        {
            InstallBinary = () =>
            {
                installCalled = true;
                return new Installer.InstallBinaryResult("/tmp/officecli", "/tmp/bin", "/tmp/bin/officecli", true);
            },
            IsInteractive = () => false,
            ConfirmRunSetup = _ => true,
            RunSetup = _ =>
            {
                setupCalled = true;
                return 0;
            },
            WriteLine = _ => { }
        });

        exitCode.Should().Be(0);
        installCalled.Should().BeTrue();
        setupCalled.Should().BeFalse();
    }

    [Fact]
    public void ExecuteSetupSelections_NoChoices_PerformsNoOptionalActions()
    {
        var pathCalls = 0;
        var skillCalls = 0;
        var mcpCalls = 0;
        var macCalls = 0;
        bool? autoUpdate = null;

        Installer.ExecuteSetupSelections(
            "all",
            new Installer.SetupSelections(),
            "/tmp/bin/officecli",
            new Installer.SetupActionHooks
            {
                WriteLine = _ => { },
                AddToPath = () => pathCalls++,
                InstallSkills = _ =>
                {
                    skillCalls++;
                    return [];
                },
                InstallMcpFallback = (_, _) => mcpCalls++,
                ApplyMacCompatibility = _ => macCalls++,
                SetAutoUpdate = enabled => autoUpdate = enabled
            });

        pathCalls.Should().Be(0);
        skillCalls.Should().Be(0);
        mcpCalls.Should().Be(0);
        macCalls.Should().Be(0);
        autoUpdate.Should().BeFalse();
    }

    [Fact]
    public void ExecuteSetupSelections_YesChoices_InvokeExpectedActions()
    {
        var pathCalls = 0;
        var skillCalls = 0;
        var mcpCalls = 0;
        var macCalls = 0;
        bool? autoUpdate = null;

        Installer.ExecuteSetupSelections(
            "all",
            new Installer.SetupSelections
            {
                AddToPath = true,
                InstallSkills = true,
                RegisterMcp = true,
                ApplyMacCompatibility = true,
                EnableAutoUpdate = true
            },
            "/tmp/bin/officecli",
            new Installer.SetupActionHooks
            {
                WriteLine = _ => { },
                AddToPath = () => pathCalls++,
                InstallSkills = _ =>
                {
                    skillCalls++;
                    return ["claude"];
                },
                InstallMcpFallback = (skills, target) =>
                {
                    skills.Should().Contain("claude");
                    target.Should().Be("all");
                    mcpCalls++;
                },
                ApplyMacCompatibility = path =>
                {
                    path.Should().Be("/tmp/bin/officecli");
                    macCalls++;
                },
                SetAutoUpdate = enabled => autoUpdate = enabled
            });

        pathCalls.Should().Be(1);
        skillCalls.Should().Be(1);
        mcpCalls.Should().Be(1);
        macCalls.Should().Be(OperatingSystem.IsMacOS() ? 1 : 0);
        autoUpdate.Should().BeTrue();
    }
}
