using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public sealed class WatchCliSessionDiscoveryTests
{
    [Fact]
    public async Task WatchCli_Docx_CompanionCommands_CanDiscoverSelectionMarksAndClose()
    {
        using var harness = new CliSubprocessHarness();
        var path = harness.CreateTempFile(".docx");
        CreateWatchDocx(path);

        var selectionPath = await DiscoverWatchPathAsync(harness, path, "data-path=\"([^\"]*/body/p[^\"]*)\"");
        selectionPath.Should().Be("/body/p[1]");

        await AssertWatchLifecycleAsync(harness, path, selectionPath);
    }

    [Fact]
    public async Task WatchCli_Pptx_CompanionCommands_CanDiscoverSelectionMarksAndClose()
    {
        using var harness = new CliSubprocessHarness();
        var path = harness.CreateTempFile(".pptx");
        CreateWatchPptx(path);
        var selectionPath = await DiscoverWatchPathByTextAsync(harness, path, "Watch Target");

        await AssertWatchLifecycleAsync(harness, path, selectionPath);
    }

    [Fact]
    public async Task WatchCli_Pptx_CompanionCommands_CanDiscoverSelectionMarksAndClose_WithLongTmpDir()
    {
        using var harness = new CliSubprocessHarness(useLongTmpLayout: true);
        var path = harness.CreateTempFile(".pptx");
        CreateWatchPptx(path);
        var selectionPath = await DiscoverWatchPathByTextAsync(harness, path, "Watch Target");

        await AssertWatchLifecycleAsync(harness, path, selectionPath);
    }

    private static async Task AssertWatchLifecycleAsync(
        CliSubprocessHarness harness,
        string filePath,
        string selectedPath)
    {
        var port = GetFreePort();
        using var watch = harness.StartBackground("watch", filePath, "--port", port.ToString());

        var started = await watch.WaitForStdoutContainsAsync($"Watch: http://localhost:{port}", TimeSpan.FromSeconds(10));
        started.Should().BeTrue($"watch stdout should announce the listening port. stdout={watch.Stdout}\nstderr={watch.Stderr}");

        using var http = new HttpClient();
        await WaitForWatchPageContainsAsync(http, port, selectedPath, TimeSpan.FromSeconds(10));

        var selectionPayload = new StringContent(
            $$"""{"paths":["{{selectedPath}}"]}""",
            Encoding.UTF8,
            "application/json");
        var selectionResponse = await http.PostAsync($"http://127.0.0.1:{port}/api/selection", selectionPayload);
        selectionResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getSelected = await harness.RunAsync(TimeSpan.FromSeconds(10), "get", filePath, "selected", "--json");
        getSelected.TimedOut.Should().BeFalse();
        getSelected.ExitCode.Should().Be(0, getSelected.Stderr + getSelected.Stdout);
        getSelected.Stdout.Should().Contain("\"matches\": 1");
        getSelected.Stdout.Should().Contain("\"path\":");

        var markSelected = await harness.RunAsync(
            TimeSpan.FromSeconds(10),
            "mark",
            filePath,
            "selected",
            "--prop",
            "note=watch-cli-test",
            "--json");
        markSelected.TimedOut.Should().BeFalse();
        markSelected.ExitCode.Should().Be(0, markSelected.Stderr + markSelected.Stdout);
        markSelected.Stdout.Should().Contain("watch-cli-test");
        markSelected.Stdout.Should().Contain("\"path\":");

        var getMarks = await harness.RunAsync(TimeSpan.FromSeconds(10), "get-marks", filePath, "--json");
        getMarks.TimedOut.Should().BeFalse();
        getMarks.ExitCode.Should().Be(0, getMarks.Stderr + getMarks.Stdout);
        getMarks.Stdout.Should().Contain("watch-cli-test");
        getMarks.Stdout.Should().Contain("\"marks\":[");

        var unmark = await harness.RunAsync(TimeSpan.FromSeconds(10), "unmark", filePath, "--all");
        unmark.TimedOut.Should().BeFalse();
        unmark.ExitCode.Should().Be(0, unmark.Stderr + unmark.Stdout);
        unmark.Stdout.Should().Contain("Removed 1 mark");

        var unwatch = await harness.RunAsync(TimeSpan.FromSeconds(10), "unwatch", filePath);
        unwatch.TimedOut.Should().BeFalse();
        unwatch.ExitCode.Should().Be(0, unwatch.Stderr + unwatch.Stdout);
        unwatch.Stdout.Should().Contain("Watch stopped");

        var exitCode = await watch.WaitForExitAsync(TimeSpan.FromSeconds(10));
        exitCode.Should().Be(0, watch.Stdout + watch.Stderr);
    }

    private static async Task<string> DiscoverWatchPathAsync(CliSubprocessHarness harness, string filePath, string htmlPattern)
    {
        var port = GetFreePort();
        using var watch = harness.StartBackground("watch", filePath, "--port", port.ToString());
        var started = await watch.WaitForStdoutContainsAsync($"Watch: http://localhost:{port}", TimeSpan.FromSeconds(10));
        started.Should().BeTrue($"watch stdout should announce the listening port. stdout={watch.Stdout}\nstderr={watch.Stderr}");

        using var http = new HttpClient();
        var path = await WaitForWatchPathAsync(http, port, htmlPattern, TimeSpan.FromSeconds(10));
        path.Should().NotBeNullOrWhiteSpace("watch page should expose a real data-path for companion commands");

        var unwatch = await harness.RunAsync(TimeSpan.FromSeconds(10), "unwatch", filePath);
        unwatch.TimedOut.Should().BeFalse();
        unwatch.ExitCode.Should().Be(0, unwatch.Stdout + unwatch.Stderr);
        await watch.WaitForExitAsync(TimeSpan.FromSeconds(10));

        return path;
    }

    private static Task<string> DiscoverWatchPathByTextAsync(CliSubprocessHarness harness, string filePath, string text)
    {
        var escapedText = Regex.Escape(text);
        return DiscoverWatchPathAsync(
            harness,
            filePath,
            $@"(?s)data-path=""([^""]*/shape\[@id=\d+\][^""]*)""[^>]*>.*?{escapedText}");
    }

    private static async Task WaitForWatchPageContainsAsync(HttpClient http, int port, string expectedPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var html = await http.GetStringAsync($"http://127.0.0.1:{port}/");
                if (html.Contains($"data-path=\"{expectedPath}\"", StringComparison.Ordinal))
                    return;
            }
            catch
            {
                // watch page not ready yet
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"watch page never exposed expected path: {expectedPath}");
    }

    private static async Task<string> WaitForWatchPathAsync(HttpClient http, int port, string htmlPattern, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var html = await http.GetStringAsync($"http://127.0.0.1:{port}/");
                var match = Regex.Match(html, htmlPattern);
                if (match.Success)
                    return WebUtility.HtmlDecode(match.Groups[1].Value);
            }
            catch
            {
                // watch page not ready yet
            }

            await Task.Delay(100);
        }

        return string.Empty;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void CreateWatchDocx(string path)
    {
        BlankDocCreator.Create(path);
        using var word = new WordHandler(path, editable: true);
        word.Add("/body", "paragraph", null, new()
        {
            ["text"] = "Watch Target",
            ["style"] = "Heading 1"
        });
    }

    private static void CreateWatchPptx(string path)
    {
        BlankDocCreator.Create(path);
        using var ppt = new PowerPointHandler(path, editable: true);
        ppt.Add("/", "slide", null, new() { ["title"] = "Watch Slide" });
        ppt.Add("/slide[1]", "shape", null, new()
        {
            ["text"] = "Watch Target",
            ["x"] = "1cm",
            ["y"] = "1cm",
            ["width"] = "4cm",
            ["height"] = "2cm",
            ["fill"] = "4472C4"
        });
    }
}
