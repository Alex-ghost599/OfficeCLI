using System.Diagnostics;
using FluentAssertions;

namespace OfficeCli.Tests.Functional;

internal sealed class CliSubprocessHarness : IDisposable
{
    private readonly List<string> _pathsToDelete = [];
    private readonly string _workspaceRoot;
    private readonly string _homeDir;
    private readonly string _tmpDir;

    public CliSubprocessHarness()
    {
        var shortId = Guid.NewGuid().ToString("N")[..8];
        _workspaceRoot = Path.Combine("/tmp", $"ocli-{shortId}");
        _homeDir = Path.Combine(_workspaceRoot, "home");
        _tmpDir = Path.Combine(_workspaceRoot, "tmp");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_homeDir);
        Directory.CreateDirectory(_tmpDir);

        _pathsToDelete.Add(_workspaceRoot);
    }

    public string HomeDir => _homeDir;
    public string TmpDir => _tmpDir;
    public string CliExecutablePath => ResolveCliExecutablePath();

    public string CreateTempFile(string extension)
    {
        var path = Path.Combine(_workspaceRoot, $"{Guid.NewGuid():N}{extension}");
        _pathsToDelete.Add(path);
        return path;
    }

    public async Task<CliCommandResult> RunAsync(TimeSpan timeout, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CliExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        startInfo.Environment["HOME"] = _homeDir;
        startInfo.Environment["TMPDIR"] = _tmpDir;
        startInfo.Environment["OFFICECLI_SKIP_UPDATE"] = "1";

        return await RunProcessAsync(startInfo, timeout);
    }

    public async Task<CliCommandResult> RunShellAsync(TimeSpan timeout, string script)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/zsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(script);
        startInfo.Environment["HOME"] = _homeDir;
        startInfo.Environment["TMPDIR"] = _tmpDir;
        startInfo.Environment["OFFICECLI_SKIP_UPDATE"] = "1";

        return await RunProcessAsync(startInfo, timeout);
    }

    private static async Task<CliCommandResult> RunProcessAsync(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitForExitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitForExitTask, Task.Delay(timeout));
        if (completed != waitForExitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new CliCommandResult(-1, await stdoutTask, await stderrTask, TimedOut: true);
        }

        await waitForExitTask;
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new CliCommandResult(process.ExitCode, stdout, stderr, TimedOut: false);
    }

    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(100);
        }

        throw new Xunit.Sdk.XunitException(message);
    }

    public static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string ResolveCliExecutablePath()
    {
        var repoRoot = GetRepositoryRoot();
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "officecli", "bin", "Release", "net10.0", "osx-arm64", "officecli"),
            Path.Combine(repoRoot, "src", "officecli", "bin", "Release", "net10.0", "officecli")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        path.Should().NotBeNull("CLI subprocess tests require a Release build of officecli before execution");
        return path!;
    }

    public void Dispose()
    {
        foreach (var path in _pathsToDelete.OrderByDescending(p => p.Length))
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best effort cleanup for temp audit assets
            }
        }
    }
}

internal readonly record struct CliCommandResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);
