using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Owns one isolated CLI data root and only the Core background process started for that root.</summary>
internal sealed class AssistantCliSession(CliSandbox cli) : IAsyncDisposable
{
    /// <summary>Gets the existing process harness; no business response is substituted.</summary>
    public CliSandbox Cli { get; } = cli;

    /// <summary>Establishes a real Core connection in a fresh root; the idle host may legitimately exit between later commands.</summary>
    public static async Task<AssistantCliSession> StartAsync()
    {
        var sandbox = new CliSandbox();
        try
        {
            (await sandbox.RunAsync("ai", "host")).ReadEnvelope(0);
            return new AssistantCliSession(sandbox);
        }
        catch
        {
            sandbox.Dispose();
            throw;
        }
    }

    /// <summary>Configures an offline-capable profile pointing only at the case's controlled HTTP endpoint.</summary>
    public async Task ConfigureAsync(Uri endpoint)
        => (await Cli.RunAsync("ai", "service", "save", "--protocol", "openai-chat",
            "--base-url", endpoint.AbsoluteUri, "--model", "fixture")).ReadEnvelope(0);

    /// <summary>Observes a persisted event before acting on an asynchronous turn.</summary>
    public async Task<JsonObject> EventAsync(string conversation, string turn, string kind, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (true)
        {
            var page = (await Cli.RunAsync("ai", "chat", "turn", "events", conversation, turn)).ReadEnvelope(0)["data"]!;
            if (page["items"]!.AsArray().FirstOrDefault(item => item!["kind"]!.ToString() == kind) is JsonObject found) return found;
            await timer.WaitForNextTickAsync(ct);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (File.Exists(Path.Combine(Cli.Root, "ai", "host.lock")))
        {
            // A five-second idle exit can replace the initial PID. Resolve only this case's root,
            // not a cached startup process or other worktrees' hosts.
            var current = (await Cli.RunAsync("ai", "host")).ReadEnvelope(0)["data"]!;
            Process? host;
            try { host = Process.GetProcessById(current["processId"]!.GetValue<int>()); }
            catch (ArgumentException) { host = null; } // The idle owner already exited after its reply.

            using (host)
            {
                if (host is not null && !host.HasExited)
                {
                    host.Kill(entireProcessTree: true);
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await host.WaitForExitAsync(deadline.Token);
                }
            }
        }

        Cli.Dispose();
    }
}