// Copyright 2025 OfficeCli (officecli.ai)
// SPDX-License-Identifier: Apache-2.0

using System.IO.Pipes;
using System.Text;

namespace OfficeCli.Core;

public enum ResidentProbeState
{
    NotRunning,
    Starting,
    Ready,
    Failed
}

public class ResidentProbeResult
{
    public string PipeName { get; set; } = "";
    public ResidentProbeState State { get; set; }
    public string Error { get; set; } = "";
}

public static class ResidentClient
{
    public static ResidentProbeResult Probe(string filePath)
    {
        var pipeName = ResidentServer.GetPipeName(filePath);
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName + "-ping", PipeDirection.InOut);
            client.Connect(100);

            var pingRequest = new ResidentRequest { Command = "__ping__" };
            var json = System.Text.Json.JsonSerializer.Serialize(pingRequest, ResidentJsonContext.Default.ResidentRequest);
            PipeWriteLine(client, json);

            var responseLine = PipeReadLine(client);
            if (responseLine == null)
                return new ResidentProbeResult { PipeName = pipeName, State = ResidentProbeState.NotRunning };

            var response = System.Text.Json.JsonSerializer.Deserialize<ResidentResponse>(responseLine, ResidentJsonContext.Default.ResidentResponse);
            if (response == null || string.IsNullOrEmpty(response.Stdout))
                return new ResidentProbeResult { PipeName = pipeName, State = ResidentProbeState.NotRunning };

            var residentFilePath = Path.GetFullPath(response.Stdout);
            var requestedFilePath = Path.GetFullPath(filePath);
            if (!string.Equals(residentFilePath, requestedFilePath, StringComparison.OrdinalIgnoreCase))
                return new ResidentProbeResult { PipeName = pipeName, State = ResidentProbeState.NotRunning };

            return new ResidentProbeResult
            {
                PipeName = pipeName,
                State = ParseProbeState(response.State),
                Error = response.Stderr
            };
        }
        catch
        {
            return new ResidentProbeResult { PipeName = pipeName, State = ResidentProbeState.NotRunning };
        }
    }

    /// <summary>
    /// Check if a resident is running for this file (without consuming a connection).
    /// Just tries to connect briefly.
    /// </summary>
    public static bool TryConnect(string filePath, out string pipeName)
    {
        var probe = Probe(filePath);
        pipeName = probe.PipeName;
        return probe.State == ResidentProbeState.Ready;
    }

    /// <summary>
    /// Send a command to the resident server in a single connection.
    /// Returns null if no resident is running or the file doesn't match.
    /// </summary>
    public static ResidentResponse? TrySend(string filePath, ResidentRequest request, int maxRetries = 2)
    {
        var pipeName = ResidentServer.GetPipeName(filePath);
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                client.Connect(1000); // 1s timeout (was 200ms — too short under load)

                var json = System.Text.Json.JsonSerializer.Serialize(request, ResidentJsonContext.Default.ResidentRequest);
                PipeWriteLine(client, json);

                var responseLine = PipeReadLine(client);
                if (responseLine == null) continue;

                var response = System.Text.Json.JsonSerializer.Deserialize<ResidentResponse>(responseLine, ResidentJsonContext.Default.ResidentResponse);
                if (response != null) return response;
            }
            catch
            {
                if (attempt == maxRetries) return null;
                Thread.Sleep(50 * (attempt + 1)); // brief backoff before retry
            }
        }
        return null;
    }

    /// <summary>
    /// Send a close command to the resident server.
    /// </summary>
    public static bool SendClose(string filePath, int maxRetries = 2)
    {
        // Send close via the dedicated ping pipe (always responsive)
        var pipeName = ResidentServer.GetPipeName(filePath) + "-ping";
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                client.Connect(1000);

                var request = new ResidentRequest { Command = "__close__" };
                var json = System.Text.Json.JsonSerializer.Serialize(request, ResidentJsonContext.Default.ResidentRequest);
                PipeWriteLine(client, json);

                var responseLine = PipeReadLine(client);
                if (responseLine == null)
                    continue;

                var response = System.Text.Json.JsonSerializer.Deserialize<ResidentResponse>(responseLine, ResidentJsonContext.Default.ResidentResponse);
                if (response != null && response.ExitCode == 0)
                    return true;
            }
            catch
            {
                if (attempt == maxRetries)
                    return false;
            }

            Thread.Sleep(50 * (attempt + 1));
        }

        return false;
    }

    private static ResidentProbeState ParseProbeState(string? state)
    {
        return state?.ToLowerInvariant() switch
        {
            "starting" => ResidentProbeState.Starting,
            "failed" => ResidentProbeState.Failed,
            "ready" or "" or null => ResidentProbeState.Ready,
            _ => ResidentProbeState.Ready
        };
    }

    // ==================== Pipe I/O helpers ====================
    //
    // On Windows, StreamReader/StreamWriter deadlock on named pipes under .NET 11
    // preview — the managed stream wrapper's internal buffering stalls reads even
    // when bytes are available on the wire.  Raw byte I/O avoids the issue.
    //
    // On Linux/macOS, StreamReader/StreamWriter work fine and are faster (buffered
    // reads), so we keep using them.

    private const int MaxLineLength = 1_048_576; // 1 MB safety limit

    private static void PipeWriteLine(Stream pipe, string line)
    {
        if (!OperatingSystem.IsWindows())
        {
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(line);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        pipe.Write(bytes, 0, bytes.Length);
        pipe.Flush();
    }

    private static string? PipeReadLine(Stream pipe)
    {
        if (!OperatingSystem.IsWindows())
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            return reader.ReadLine();
        }
        var buffer = new byte[1];
        var lineBytes = new List<byte>(256);
        while (true)
        {
            var bytesRead = pipe.Read(buffer, 0, 1);
            if (bytesRead == 0) return lineBytes.Count > 0 ? Encoding.UTF8.GetString(lineBytes.ToArray()) : null;
            if (buffer[0] == (byte)'\n')
            {
                if (lineBytes.Count > 0 && lineBytes[^1] == (byte)'\r')
                    lineBytes.RemoveAt(lineBytes.Count - 1);
                return Encoding.UTF8.GetString(lineBytes.ToArray());
            }
            if (lineBytes.Count >= MaxLineLength)
                return null;
            lineBytes.Add(buffer[0]);
        }
    }
}
