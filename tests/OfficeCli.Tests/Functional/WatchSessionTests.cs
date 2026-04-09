using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using OfficeCli.Core;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public sealed class WatchSessionTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CreateTempDocx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"watch_{Guid.NewGuid():N}.docx");
        _tempFiles.Add(path);
        BlankDocCreator.Create(path);
        using var handler = new WordHandler(path, editable: true);
        handler.Add("/body", "paragraph", null, new() { ["text"] = "Watch Target" });
        return path;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(50);
        }

        throw new Xunit.Sdk.XunitException(message);
    }

    [Fact]
    public void Watch_CompanionCommands_ReturnEmptyState_WhenNoWatchRunning()
    {
        var filePath = CreateTempDocx();

        WatchServer.GetExistingWatchPort(filePath).Should().BeNull();
        WatchNotifier.QuerySelection(filePath).Should().BeNull();
        WatchNotifier.QueryMarksFull(filePath).Should().BeNull();
        WatchNotifier.RemoveMarks(filePath, new UnmarkRequest { All = true }).Should().BeNull();
        WatchNotifier.SendClose(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task Watch_CompanionCommands_CanDiscoverSelectionMarksAndCloseLifecycle()
    {
        var filePath = CreateTempDocx();
        string initialHtml;
        using (var word = new WordHandler(filePath, editable: false))
            initialHtml = word.ViewAsHtml();

        var port = GetFreePort();
        using var watch = new WatchServer(filePath, port, idleTimeout: TimeSpan.FromMinutes(2), initialHtml: initialHtml);
        using var cts = new CancellationTokenSource();
        var runTask = watch.RunAsync(cts.Token);

        try
        {
            WaitUntil(
                () => WatchServer.GetExistingWatchPort(filePath) == port,
                TimeSpan.FromSeconds(5),
                "watch server should publish its pipe/port before companion commands connect");

            using var http = new HttpClient();
            var body = new StringContent("{\"paths\":[\"/body/p[1]\"]}", Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"http://127.0.0.1:{port}/api/selection", body);
            response.EnsureSuccessStatusCode();

            WaitUntil(
                () => WatchNotifier.QuerySelection(filePath)?.Length == 1,
                TimeSpan.FromSeconds(5),
                "watch selection should be discoverable through WatchNotifier");

            var selection = WatchNotifier.QuerySelection(filePath);
            selection.Should().Equal("/body/p[1]");

            var id = WatchNotifier.AddMark(filePath, new MarkRequest
            {
                Path = selection![0],
                Color = "yellow",
                Note = "watch-session-test",
            });

            id.Should().NotBeNullOrEmpty();

            WaitUntil(
                () => (WatchNotifier.QueryMarksFull(filePath)?.Marks.Length ?? -1) == 1,
                TimeSpan.FromSeconds(5),
                "mark added through companion command should be readable back from watch");

            var marks = WatchNotifier.QueryMarksFull(filePath);
            marks.Should().NotBeNull();
            marks!.Version.Should().BeGreaterThanOrEqualTo(1);
            marks.Marks.Should().ContainSingle();
            marks.Marks[0].Id.Should().Be(id);
            marks.Marks[0].Path.Should().Be("/body/p[1]");

            var removed = WatchNotifier.RemoveMarks(filePath, new UnmarkRequest { All = true });
            removed.Should().Be(1);

            WaitUntil(
                () => (WatchNotifier.QueryMarksFull(filePath)?.Marks.Length ?? -1) == 0,
                TimeSpan.FromSeconds(5),
                "unmark should clear watch marks for subsequent companion reads");

            WatchNotifier.SendClose(filePath).Should().BeTrue();
            await runTask.WaitAsync(TimeSpan.FromSeconds(5));

            WaitUntil(
                () => WatchServer.GetExistingWatchPort(filePath) == null,
                TimeSpan.FromSeconds(5),
                "watch pipe should disappear after close");

            WatchNotifier.QuerySelection(filePath).Should().BeNull();
            WatchNotifier.QueryMarksFull(filePath).Should().BeNull();
        }
        finally
        {
            if (!runTask.IsCompleted)
            {
                cts.Cancel();
                try { await runTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
        }
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
