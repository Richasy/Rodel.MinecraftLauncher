using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Live observations over the existing process harness. No model, tool result, persistence or transport is substituted.</summary>
internal sealed class LiveCli(AssistantCliSession session, string? credentialEnvironment = null)
{
    private readonly string? _secret = credentialEnvironment is null ? null : Environment.GetEnvironmentVariable(credentialEnvironment);

    /// <summary>Gets the isolated real data directory created by the existing CLI session.</summary>
    public string Root => session.Cli.Root;

    /// <summary>Runs a bounded real CLI operation, withholding raw timeout diagnostics when an online credential is in scope.</summary>
    public async Task<JsonObject> RunAsync(string[] arguments, CancellationToken ct, int expectedExit = 0, int seconds = 120)
    {
        CliResult result;
        try
        {
            result = await CliSandbox.RunAsync(session.Cli.CreateStartInfo(arguments),
                timeout: TimeSpan.FromSeconds(seconds), cancellationToken: ct);
        }
        catch (TimeoutException) when (_secret is not null)
        {
            throw new AssertFailedException("Live CLI request timed out; raw child output was withheld to protect the configured credential.");
        }

        if (!string.IsNullOrEmpty(_secret))
        {
            Assert.IsFalse(result.Output.Contains(_secret, StringComparison.Ordinal) || result.Error.Contains(_secret, StringComparison.Ordinal),
                "The CLI exposed the configured credential; raw output was withheld.");
        }

        return result.ReadEnvelope(expectedExit)["data"]?.AsObject()
            ?? throw new AssertFailedException("Live CLI operation did not return an object result.");
    }

    /// <summary>Persists a real Embedded binding without endpoint, credential, discovery or daemon setup.</summary>
    public async Task<string> ConfigureLocalAsync(string model, CancellationToken ct, int? contextLength = null)
    {
        var configuration = await RunAsync(["ai", "service", "save", "--name", "Live Embedded",
            "--protocol", "rodel-inference", "--model", model], ct);
        return await FixSamplingAsync(configuration, ct, contextLength);
    }

    /// <summary>Persists only an environment reference for the explicitly selected real online provider.</summary>
    public async Task<string> ConfigureOnlineAsync(LiveOnlineSettings settings, CancellationToken ct)
    {
        var configuration = await RunAsync(["ai", "service", "save", "--name", "Live online",
            "--protocol", settings.Protocol, "--base-url", settings.Endpoint, "--model", settings.Model,
            "--credential-env", settings.CredentialEnvironment], ct);
        return await FixSamplingAsync(configuration, ct);
    }

    private async Task<string> FixSamplingAsync(JsonObject configuration, CancellationToken ct, int? contextLength = null)
    {
        var service = configuration["services"]!.AsArray().Last()!;
        var binding = service["models"]![0]!["id"]!.ToString();
        string[] arguments = ["ai", "model", "save", "--service", service["id"]!.ToString(), "--id", binding,
            "--expected-revision", service["revision"]!.ToString(), "--temperature", "0"];
        if (contextLength is { } length)
            arguments = [.. arguments, "--context-length", length.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        await RunAsync(arguments, ct);
        return binding;
    }

    /// <summary>Creates an actual persisted conversation with the selected binding.</summary>
    public async Task<string> CreateAsync(string binding, CancellationToken ct)
        => (await RunAsync(["ai", "chat", "create", "--model", binding], ct))["id"]!.ToString();

    /// <summary>Submits one real turn; callers retain the key for idempotency checks, and tools/operation grants are explicit.</summary>
    public async Task<JsonObject> SendAsync(string conversation, string binding, string prompt, string key, bool tools,
        bool operations, CancellationToken ct, int outputTokens = 1024, int modelCalls = 8, int timeoutSeconds = 180)
    {
        var state = await RunAsync(["ai", "chat", "show", conversation], ct);
        string[] arguments =
        [
            "ai", "chat", "send", conversation, "--expected-revision", state["revision"]!.ToString(),
            "--model", binding, "--client-key", key, "--prompt", prompt,
            "--max-output-tokens", outputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--max-turns", modelCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--timeout-seconds", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ];
        if (!tools) arguments = [.. arguments, "--no-tools"];
        if (operations) arguments = [.. arguments, "--allow-operations"];
        return await RunAsync(arguments, ct);
    }

    /// <summary>Waits for a real terminal record without confusing a submitted receipt with completed inference.</summary>
    public Task<JsonObject> FinishAsync(string conversation, string turn, CancellationToken ct, int expectedExit = 0, int timeoutSeconds = 180)
        => RunAsync(["ai", "chat", "turn", "wait", conversation, turn, "--timeout-seconds",
            timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)], ct, expectedExit, timeoutSeconds + 15);

    /// <summary>Reads every durable event page so required real effects cannot be missed behind a page limit.</summary>
    public async Task<JsonArray> EventsAsync(string conversation, string turn, CancellationToken ct)
    {
        var result = new JsonArray();
        long cursor = 0;
        do
        {
            var page = await RunAsync(["ai", "chat", "turn", "events", conversation, turn, "--after",
                cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)], ct);
            foreach (var item in page["items"]!.AsArray()) result.Add(item!.DeepClone());
            var next = page["nextSequence"]!.GetValue<long>();
            if (page["hasMore"]?.GetValue<bool>() != true) break;
            Assert.IsGreaterThan(cursor, next, "Durable event pagination did not advance.");
            cursor = next;
        } while (true);
        return result;
    }

    /// <summary>Observes a persisted text delta while the real model remains active; no sleep is taken as overlap evidence.</summary>
    public async Task<JsonObject> ActiveTextAsync(string conversation, string turn, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (true)
        {
            var events = await EventsAsync(conversation, turn, ct);
            var state = await RunAsync(["ai", "chat", "turn", "show", conversation, turn], ct);
            Assert.IsFalse(Core.Application.AI.AiChatStates.IsTerminal(state["status"]!.ToString()),
                "The real model finished before an in-flight cancellation checkpoint was observed: " + state.ToJsonString());
            if (events.Any(item => item!["kind"]!.ToString() == "text_delta")) return state;
            await timer.WaitForNextTickAsync(ct);
        }
    }
}