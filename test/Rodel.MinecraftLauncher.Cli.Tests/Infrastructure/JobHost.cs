using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Owns a dispatched test job's verified process identity and observes persisted terminal state before cleanup.
/// </summary>
internal sealed class JobHost(CliSandbox cli, string id, Process? owner) : IAsyncDisposable
{
    internal string Id => id;

    internal static async Task<JobHost> StartAsync(CliSandbox cli, params string[] arguments)
    {
        var response = (await cli.RunAsync(["--background", .. arguments])).ReadEnvelope(0);
        Assert.AreEqual("queued", response["data"]?["status"]?.GetValue<string>(), "Queue acceptance must not claim task completion.");
        var id = response["data"]!["id"]!.GetValue<string>();
        var identity = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cli.Root, "jobs", id + ".json.owner")))!;
        Process? owner = null;
        try
        {
            owner = Process.GetProcessById(identity["processId"]!.GetValue<int>());
            var expectedStart = DateTimeOffset.Parse(identity["processStartUtc"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            if (owner.StartTime.ToUniversalTime() != expectedStart.UtcDateTime)
            {
                owner.Dispose();
                owner = null;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            owner?.Dispose();
            owner = null;
        }

        return new(cli, id, owner);
    }

    internal async Task<JsonObject> WaitForTerminalAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var response = (await cli.RunAsync("job", "show", id)).ReadEnvelope(0);
            var state = response["data"]!.AsObject();
            if (state["status"]?.GetValue<string>() is "succeeded" or "failed" or "canceled" or "interrupted")
            {
                if (owner is not null)
                    await owner.WaitForExitAsync(deadline.Token);
                return state;
            }

            // Poll an actual persisted condition; the delay is not evidence that the job has completed.
            await Task.Delay(50, deadline.Token);
        }
    }

    /// <summary>Terminates any remaining owned job-host process tree and waits for exit before releasing the process handle.</summary>
    /// <returns>A task completing after the owned host exits, bounded by the cleanup timeout.</returns>
    public async ValueTask DisposeAsync()
    {
        if (owner is null)
            return;
        try
        {
            if (!owner.HasExited)
                owner.Kill(entireProcessTree: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await owner.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            owner.Dispose();
        }
    }
}