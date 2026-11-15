using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Owns an independent temporary data root; no user model store, configuration or account is opened.</summary>
internal sealed class AiSandbox : IDisposable
{
    /// <summary>Creates this case's writable data root.</summary>
    public AiSandbox()
    {
        Root = Path.Combine(Path.GetTempPath(), "Rodel.MinecraftLauncher.Assistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Paths = new LauncherPaths(Root);
        Store = new AiChatStore(Paths);
    }

    /// <summary>Gets the exact directory created and later removed by this case.</summary>
    public string Root { get; }
    /// <summary>Gets real Core paths scoped to the temporary root.</summary>
    public LauncherPaths Paths { get; }
    /// <summary>Gets the actual conversation store, not a persistence mock.</summary>
    public AiChatStore Store { get; }

    /// <summary>Creates a small explicit conversation fixture with independently defined permission state.</summary>
    public async Task<JsonObject> ConversationAsync(CancellationToken ct)
    {
        var conversation = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = Guid.NewGuid().ToString("N"),
            ["revision"] = 1L,
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["mode"] = "read_only",
            ["operationRevision"] = 0L,
            ["fileAccessEnabled"] = true,
            ["networkAccessEnabled"] = true
        };
        await Store.SaveConversationAsync(conversation, ct);
        return conversation;
    }

    /// <summary>Creates a stored turn, optionally carrying a single explicit operation grant.</summary>
    public async Task<JsonObject> TurnAsync(JsonObject conversation, bool operations, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var turn = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["goalId"] = id,
            ["conversationId"] = conversation["id"]!.DeepClone(),
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = "running",
            ["mode"] = operations ? "allow_operations" : "read_only",
            ["operationRevision"] = conversation["operationRevision"]?.DeepClone() ?? JsonValue.Create(0L),
            ["turnAuthorization"] = operations ? "explicit_turn" : "conversation",
            ["fileAccessEnabled"] = true,
            ["networkAccessEnabled"] = true,
            ["modelId"] = "fixture-model"
        };
        await Store.SaveTurnAsync(turn, ct);
        return turn;
    }

    /// <summary>Creates an offline provider profile for the selected wire protocol, never an HTTP connection.</summary>
    public async Task<AiServiceSnapshot> SnapshotAsync(CancellationToken ct, string protocol = AiProtocols.OpenAiChat)
    {
        var services = new AiServiceStore(Paths);
        var state = await services.SaveServiceAsync(new AiServiceUpdate
        {
            DisplayName = "Context fixture",
            Protocol = protocol,
            BaseUrl = "https://model.invalid/v1/",
            Models = [new AiModelUpdate { Model = "fixture" }]
        }, ct: ct);
        return await services.AcquireAsync(state.Services[0].Id, state.Services[0].Models[0].Id, true, ct);
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(Root, recursive: true);
}