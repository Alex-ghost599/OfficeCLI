// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

namespace OfficeCli.Core;

/// <summary>
/// Installs the officecli binary, and offers an explicit opt-in setup flow for
/// PATH changes, skills, MCP registration, macOS compatibility tweaks, and
/// update settings.
/// </summary>
public static class Installer
{
    private static readonly string HomeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string BinDir = Path.Combine(HomeDir, ".local", "bin");
    private static readonly string TargetPath = Path.Combine(BinDir, "officecli");

    /// <summary>
    /// MCP targets and the skill aliases that overlap with them.
    /// If any of the skill aliases were installed, skip MCP for that target.
    /// </summary>
    private static readonly (string McpTarget, string DetectDir, string[] SkillAliases)[] McpTargets =
    [
        ("claude", ".claude", ["claude", "claude-code"]),
        ("cursor", ".cursor", ["cursor"]),
        ("vscode", ".vscode", []),
        ("lms", ".cache/lm-studio", []),
    ];

    internal sealed record InstallBinaryResult(string SourcePath, string InstallDir, string InstalledPath, bool Copied);

    internal sealed class InstallActionHooks
    {
        public Func<InstallBinaryResult>? InstallBinary { get; init; }
        public Func<bool>? IsInteractive { get; init; }
        public Func<string, bool>? ConfirmRunSetup { get; init; }
        public Func<string, int>? RunSetup { get; init; }
        public Action<string>? WriteLine { get; init; }
    }

    internal sealed class SetupSelections
    {
        public bool AddToPath { get; set; }
        public bool InstallSkills { get; set; }
        public bool RegisterMcp { get; set; }
        public bool ApplyMacCompatibility { get; set; }
        public bool EnableAutoUpdate { get; set; }
    }

    internal sealed class SetupActionHooks
    {
        public Action<string>? WriteLine { get; init; }
        public Action? AddToPath { get; init; }
        public Func<string, HashSet<string>>? InstallSkills { get; init; }
        public Action<HashSet<string>, string>? InstallMcpFallback { get; init; }
        public Action<string>? ApplyMacCompatibility { get; init; }
        public Action<bool>? SetAutoUpdate { get; init; }
    }

    public static int RunInstall(string[] args)
    {
        var target = args.Length >= 1 ? args[0] : "all";
        try { return RunInstallCore(target); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static int RunSetup(string[] args)
    {
        var target = args.Length >= 1 ? args[0] : "all";
        try { return RunSetupCore(target); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static int RunInstallCore(string target, InstallActionHooks? hooks = null)
    {
        hooks ??= new InstallActionHooks();
        var writeLine = hooks.WriteLine ?? Console.WriteLine;
        var installBinary = hooks.InstallBinary ?? (() => InstallBinary());
        var isInteractive = hooks.IsInteractive ?? IsInteractiveConsole;
        var confirmRunSetup = hooks.ConfirmRunSetup ?? PromptYesNo;
        var runSetup = hooks.RunSetup ?? (nextTarget => RunSetupCore(nextTarget));

        var result = installBinary();

        if (!result.Copied)
        {
            writeLine($"Binary already installed at {result.InstalledPath}");
        }

        writeLine("OfficeCli installed successfully.");
        writeLine($"Binary path: {result.InstalledPath}");
        writeLine("No environment or agent configuration has been changed.");
        writeLine($"Optional next step: {result.InstalledPath} setup {target}");

        if (!isInteractive())
            return 0;

        if (!confirmRunSetup("Run optional setup now?"))
        {
            PrintSetupHints(writeLine, result.InstalledPath, target);
            return 0;
        }

        return runSetup(target);
    }

    internal static int RunSetupCore(string target)
    {
        if (!IsInteractiveConsole())
        {
            PrintSetupHints(Console.WriteLine, TargetPath, target);
            return 0;
        }

        var installedPath = Environment.ProcessPath ?? TargetPath;
        Console.WriteLine("Optional setup");
        Console.WriteLine("Each step is opt-in. Default is No.");
        Console.WriteLine();

        var selections = new SetupSelections
        {
            AddToPath = PromptYesNo($"Add {BinDir} to PATH?"),
            InstallSkills = PromptYesNo($"Install OfficeCli skills for detected agents (target: {target})?"),
            RegisterMcp = PromptYesNo($"Register MCP for detected tools not covered by skills (target: {target})?"),
            ApplyMacCompatibility = OperatingSystem.IsMacOS() &&
                                    PromptYesNo("Apply macOS compatibility tweaks (remove quarantine and ad-hoc codesign)?"),
            EnableAutoUpdate = PromptYesNo("Enable automatic update checks?")
        };

        ExecuteSetupSelections(target, selections, installedPath);
        return 0;
    }

    internal static void ExecuteSetupSelections(
        string target,
        SetupSelections selections,
        string? installedPath = null,
        SetupActionHooks? hooks = null)
    {
        hooks ??= new SetupActionHooks();

        var writeLine = hooks.WriteLine ?? Console.WriteLine;
        var addToPath = hooks.AddToPath ?? AddBinDirToPath;
        var installSkills = hooks.InstallSkills ?? SkillInstaller.Install;
        var installMcpFallback = hooks.InstallMcpFallback ?? InstallMcpFallback;
        var applyMacCompatibility = hooks.ApplyMacCompatibility ?? ApplyMacCompatibility;
        var setAutoUpdate = hooks.SetAutoUpdate ?? UpdateChecker.SetAutoUpdate;

        HashSet<string> skilledTools = [];

        if (selections.AddToPath)
        {
            addToPath();
        }
        else
        {
            writeLine($"Skipped PATH update. You can do this later with: officecli setup {target}");
            writeLine($"Manual PATH line: export PATH=\"{BinDir}:$PATH\"");
        }

        if (selections.InstallSkills)
        {
            skilledTools = installSkills(target);
        }
        else
        {
            writeLine($"Skipped skill installation. You can do this later with: officecli skills install");
        }

        if (selections.RegisterMcp)
        {
            installMcpFallback(skilledTools, target);
        }
        else
        {
            writeLine($"Skipped MCP registration. You can do this later with: officecli mcp <target> or officecli mcp list");
        }

        if (OperatingSystem.IsMacOS())
        {
            if (selections.ApplyMacCompatibility)
            {
                applyMacCompatibility(installedPath ?? (Environment.ProcessPath ?? TargetPath));
            }
            else
            {
                writeLine("Skipped macOS compatibility tweaks. You can rerun setup later if Gatekeeper blocks execution.");
            }
        }

        setAutoUpdate(selections.EnableAutoUpdate);
        writeLine($"autoUpdate = {selections.EnableAutoUpdate.ToString().ToLowerInvariant()}");
    }

    internal static InstallBinaryResult InstallBinary(string? sourcePath = null, string? installDir = null)
    {
        var src = sourcePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(src))
            throw new InvalidOperationException("Unable to determine officecli executable path.");

        var destinationDir = installDir ?? BinDir;
        var destinationPath = Path.Combine(destinationDir, "officecli");

        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(destinationPath), StringComparison.Ordinal))
            return new InstallBinaryResult(src, destinationDir, destinationPath, Copied: false);

        var srcInfo = new FileInfo(src);
        if (srcInfo.Length < 5 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Skipping binary install: not a published self-contained binary. Run: dotnet publish -c Release -r <rid> --self-contained -p:PublishSingleFile=true");
        }

        Directory.CreateDirectory(destinationDir);
        File.Copy(src, destinationPath, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(destinationPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { }
        }

        return new InstallBinaryResult(src, destinationDir, destinationPath, Copied: true);
    }

    internal static void InstallMcpFallback(HashSet<string> skilledTools, string target)
    {
        var isAll = target.Equals("all", StringComparison.OrdinalIgnoreCase);

        foreach (var (mcpTarget, detectDir, skillAliases) in McpTargets)
        {
            if (!isAll && !mcpTarget.Equals(target, StringComparison.OrdinalIgnoreCase))
                continue;

            if (skillAliases.Any(a => skilledTools.Contains(a)))
                continue;

            if (Directory.Exists(Path.Combine(HomeDir, detectDir)))
                McpInstaller.Install(mcpTarget);
        }
    }

    private static bool IsInteractiveConsole()
    {
        try
        {
            return !Console.IsInputRedirected && !Console.IsOutputRedirected;
        }
        catch
        {
            return false;
        }
    }

    private static bool PromptYesNo(string prompt)
    {
        Console.Write($"{prompt} [y/N]: ");
        var input = Console.ReadLine()?.Trim();
        return input is not null &&
               (input.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintSetupHints(Action<string> writeLine, string installedPath, string target)
    {
        writeLine("Optional configuration steps were skipped.");
        writeLine($"Run later: {installedPath} setup {target}");
        writeLine("Manual one-off commands:");
        writeLine($"  {installedPath} config autoUpdate true");
        writeLine($"  {installedPath} skills install");
        writeLine($"  {installedPath} mcp list");
    }

    private static void AddBinDirToPath()
    {
        if (IsInPath())
            return;

        if (OperatingSystem.IsWindows())
        {
            AddBinDirToWindowsPath();
            return;
        }

        var exportLine = $"export PATH=\"{BinDir}:$PATH\"";
        string profilePath;
        var shell = Environment.GetEnvironmentVariable("SHELL") ?? "";

        if (shell.EndsWith("/zsh", StringComparison.Ordinal))
            profilePath = Path.Combine(HomeDir, ".zshrc");
        else if (shell.EndsWith("/bash", StringComparison.Ordinal))
            profilePath = Path.Combine(HomeDir, ".bashrc");
        else if (shell.EndsWith("/fish", StringComparison.Ordinal))
        {
            var fishConfig = Path.Combine(HomeDir, ".config", "fish", "config.fish");
            AppendIfMissing(fishConfig, $"fish_add_path {BinDir}", BinDir);
            return;
        }
        else
            profilePath = Path.Combine(HomeDir, ".profile");

        AppendIfMissing(profilePath, exportLine, BinDir);
    }

    private static void AddBinDirToWindowsPath()
    {
        var currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var parts = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(p => PathsEqual(p, BinDir)))
            return;

        var updated = string.IsNullOrWhiteSpace(currentPath) ? BinDir : $"{currentPath};{BinDir}";
        Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        Console.WriteLine($"Added {BinDir} to PATH (restart your terminal to take effect).");
    }

    private static bool IsInPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        return pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => PathsEqual(p, BinDir));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AppendIfMissing(string profilePath, string line, string marker)
    {
        if (File.Exists(profilePath))
        {
            var content = File.ReadAllText(profilePath);
            if (content.Contains(marker, StringComparison.Ordinal))
                return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.AppendAllText(profilePath, $"\n# Added by officecli setup\n{line}\n");
        Console.WriteLine($"Added {marker} to PATH in {profilePath}");
        Console.WriteLine($"Run: source {profilePath}  (or open a new terminal)");
    }

    private static void ApplyMacCompatibility(string installedPath)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            using var xattr = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xattr",
                Arguments = $"-d com.apple.quarantine \"{installedPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            xattr?.WaitForExit(3000);
        }
        catch { }

        try
        {
            using var codesign = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "codesign",
                Arguments = $"-s - -f \"{installedPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            codesign?.WaitForExit(5000);
        }
        catch { }
    }
}
