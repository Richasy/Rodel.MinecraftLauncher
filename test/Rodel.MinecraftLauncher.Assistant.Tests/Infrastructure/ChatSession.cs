using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Owns the real Core coordinator and runner; stopping it awaits background work before storage is removed.</summary>
internal sealed class ChatSession(AiSandbox sandbox) : IAsyncDisposable
{
    private readonly SemaphoreSlim _generation = new(1, 1);
    private readonly AiLocalInferenceRuntime _local = new();
    private AiChatCoordinator? _coordinator;

    /// <summary>Executes the same typed dispatch used by Core, without a CLI process or static runtime cache.</summary>
    public async Task<JsonObject> InvokeAsync(string method, JsonObject data, CancellationToken ct)
    {
        var operation = AiHostOperations.Read(method, data) as AiChatOperation
            ?? throw new ArgumentException("The chat fixture accepts conversation operations only.", nameof(method));
        var result = await (_coordinator ??= new AiChatCoordinator(sandbox.Paths, _generation, _local)).ExecuteAsync(operation, ct);
        return result is JsonObject document ? document
            : JsonSerializer.SerializeToNode(result, AiJsonContext.Default.GetTypeInfo(result.GetType())!)!.AsObject();
    }

    /// <summary>Commits and starts an actual background turn.</summary>
    public Task<JsonObject> SendAsync(AiChatSend request, CancellationToken ct)
        => InvokeAsync("chat.send", JsonSerializer.SerializeToNode(request, AiChatJsonContext.Default.AiChatSend)!.AsObject(), ct);

    /// <summary>Observes durable terminal state and owner completion, rather than assuming a model response means persistence finished.</summary>
    public async Task<JsonObject> FinishAsync(JsonObject receipt, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (true)
        {
            var turn = await InvokeAsync("chat.turn", Reference(receipt), ct);
            if (AiChatStates.IsTerminal(turn["status"]!.ToString()) && !_coordinator!.IsActive) return turn;
            await timer.WaitForNextTickAsync(ct);
        }
    }

    /// <summary>Waits for owned auxiliary metadata work to release its request and credential leases without extending turn completion.</summary>
    public async Task FinishMetadataAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (_coordinator?.HasBackgroundWork == true)
            await timer.WaitForNextTickAsync(ct);
    }

    /// <summary>Builds an observation reference from a real send receipt.</summary>
    public static JsonObject Reference(JsonObject receipt) => new()
    {
        ["conversationId"] = receipt["conversationId"]!.DeepClone(),
        ["turnId"] = receipt["id"]!.DeepClone()
    };

    /// <summary>Waits for a real durable event checkpoint, not merely bytes written by the synthetic server.</summary>
    public async Task<JsonObject> EventAsync(JsonObject receipt, string kind, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (true)
        {
            var events = await InvokeAsync("chat.events", Reference(receipt), ct);
            if (events["items"]!.AsArray().FirstOrDefault(item => item!["kind"]!.ToString() == kind) is JsonObject found)
                return found;
            await timer.WaitForNextTickAsync(ct);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_coordinator is not null) await _coordinator.StopAsync();
        }
        finally
        {
            await _local.DisposeAsync();
            _generation.Dispose();
        }
    }
}