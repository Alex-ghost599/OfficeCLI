using System.Diagnostics;
using FluentAssertions;

namespace OfficeCli.Tests.Functional;

internal sealed class CliSubprocessHarness : IDisposable
{
    private readonly List<string> _pathsToDelete = [];
    private readonly List<CliBackgroundProcess> _backgroundProcesses = [];
    private readonly string _workspaceRoot;
    private readonly string _homeDir;
    private readonly string _tmpDir;

    public CliSubprocessHarness(bool useLongTmpLayout = false)
    {
        var shortId = Guid.NewGuid().ToString("N")[..8];
        _workspaceRoot = useLongTmpLayout
            ? Path.Combine("/private/tmp", $"officecli-watch-longtmp-regression-{shortId}", "workspace-root")
            : Path.Combine("/tmp", $"ocli-{shortId}");
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

    public CliBackgroundProcess StartBackground(params string[] args)
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

        var background = CliBackgroundProcess.Start(startInfo);
        _backgroundProcesses.Add(background);
        return background;
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
        foreach (var process in _backgroundProcesses)
        {
            try { process.Dispose(); } catch { }
        }

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

internal sealed class CliBackgroundProcess : IDisposable
{
    private readonly Process _process;
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private readonly object _stdoutLock = new();
    private readonly object _stderrLock = new();
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    private CliBackgroundProcess(Process process)
    {
        _process = process;
        _stdoutPump = PumpAsync(process.StandardOutput, _stdout, _stdoutLock);
        _stderrPump = PumpAsync(process.StandardError, _stderr, _stderrLock);
    }

    public static CliBackgroundProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo };
        process.Start();
        return new CliBackgroundProcess(process);
    }

    public int ProcessId => _process.Id;

    public string Stdout
    {
        get { lock (_stdoutLock) return _stdout.ToString(); }
    }

    public string Stderr
    {
        get { lock (_stderrLock) return _stderr.ToString(); }
    }

    public bool HasExited => _process.HasExited;

    public async Task<bool> WaitForStdoutContainsAsync(string text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Stdout.Contains(text, StringComparison.Ordinal))
                return true;

            if (_process.HasExited)
                return Stdout.Contains(text, StringComparison.Ordinal);

            await Task.Delay(50);
        }

        return Stdout.Contains(text, StringComparison.Ordinal);
    }

    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        var waitForExitTask = _process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitForExitTask, Task.Delay(timeout));
        if (completed != waitForExitTask)
            return -1;

        await waitForExitTask;
        await Task.WhenAll(_stdoutPump, _stderrPump);
        return _process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, StringWriter sink, object gate)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            lock (gate)
            {
                sink.WriteLine(line);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort cleanup
        }

        try { _process.WaitForExit(1000); } catch { }
        try { Task.WhenAll(_stdoutPump, _stderrPump).Wait(1000); } catch { }
        _process.Dispose();
        _stdout.Dispose();
        _stderr.Dispose();
    }
}
