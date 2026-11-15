// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiExternalMcpTools(LauncherPaths paths, JsonObject turn,
    Func<string, string?, JsonObject, Task> emit) : IAsyncDisposable
{
    private readonly Dictionary<string, (AiMcpSession Session, JsonObject Registration, AiExternalToolIdentity Identity)> _tools = new(StringComparer.Ordinal);
    private readonly List<AiMcpSession> _sessions = [];
    private readonly AiToolPolicy _policy = new(paths, turn);
    private readonly AiToolJournal _journal = new(paths);

    public async Task<IReadOnlyList<AIFunctionDeclaration>> ConnectAsync(CancellationToken ct)
    {
        if (turn["mode"]?.ToString() != "allow_operations") return [];
        var state = await new AiExtensionStore(paths).ReadDocumentAsync(ct).ConfigureAwait(false);
        var offered = new List<AIFunctionDeclaration>();
        foreach (var registration in state["mcp"]!.AsArray().OfType<JsonObject>().Where(item => item["enabled"]?.GetValue<bool>() == true))
        {
            ct.ThrowIfCancellationRequested();
            var id = registration["id"]!.ToString();
            await emit("mcp_started", "mcp:" + id, new JsonObject { ["id"] = id, ["source"] = "third_party" }).ConfigureAwait(false);
            try
            {
                _policy.CheckExternal(ct);
                var session = await AiMcpSession.ConnectAsync(registration.Deserialize(AssistantJsonContext.Default.AiMcpRegistration)
                    ?? throw new JsonException("The external MCP registration is empty."), ct).ConfigureAwait(false);
                _sessions.Add(session);
                foreach (var tool in session.Tools)
                {
                    var identity = new AiExternalToolIdentity(id, Hash(registration), session.ActualName(tool.Name), Hash(AiToolDeclarations.Schema(tool)));
                    _tools.Add(tool.Name, (session, registration.DeepClone().AsObject(), identity));
                    offered.Add(AIFunctionFactory.CreateDeclaration(tool.Name,
                        tool.Description + " Third-party effects are not trusted as read-only; this independent program has user-process permissions, not an OS sandbox.",
                        tool.JsonSchema));
                }

                await emit("mcp_ready", "mcp:" + id,
                    JsonSerializer.SerializeToNode(session.Describe(), AiJsonContext.Default.AiMcpDescription)!.AsObject()).ConfigureAwait(false);
                foreach (var warning in session.Warnings)
                {
                    await emit("mcp_catalog_warning", "mcp:" + id, new JsonObject
                    {
                        ["id"] = id,
                        ["warning"] = JsonSerializer.SerializeToNode(warning, AssistantJsonContext.Default.AiMcpCatalogWarning)
                    }).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                await emit("mcp_finished", "mcp:" + id, new JsonObject
                {
                    ["id"] = id,
                    ["status"] = "unavailable",
                    ["ordinaryConversationAvailable"] = true,
                    ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
                }).ConfigureAwait(false);
            }
        }

        return offered;
    }

    public bool Contains(string name) => _tools.ContainsKey(name);

    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, string invocation, string callId, CancellationToken ct)
    {
        if (!_tools.TryGetValue(name, out var tool))
            throw new LauncherOperationException("AI_MCP_TOOL_STALE", "This third-party tool is not in the frozen turn catalog.");
        await CheckAsync(tool.Registration, ct).ConfigureAwait(false);
        if (turn["retryOf"] is not null && _journal.ReuseRecorded(turn, name, arguments, tool.Identity, priorTurnsOnly: true) is { } reused)
        {
            await emit("intent_result_reused", invocation, new JsonObject
            {
                ["name"] = name,
                ["toolCallId"] = callId,
                ["reusedInvocationId"] = reused["reusedInvocationId"]!.DeepClone(),
                ["mutationReplayed"] = false,
                ["sourceId"] = tool.Registration["id"]!.DeepClone()
            }).ConfigureAwait(false);
            return reused;
        }

        var intent = await _journal.PrepareAsync(turn, invocation, callId, name, arguments, ct, tool.Identity).ConfigureAwait(false);
        await emit("intent_prepared", invocation, new JsonObject { ["name"] = name, ["toolCallId"] = callId, ["sourceIdentity"] = AiToolSourceIdentities.Snapshot(tool.Identity) }).ConfigureAwait(false);
        try
        {
            await CheckAsync(tool.Registration, ct).ConfigureAwait(false);
            await _journal.DispatchingAsync(turn, intent, ct).ConfigureAwait(false);
        }
        catch
        {
            await _journal.NotDispatchedAsync(turn, intent).ConfigureAwait(false);
            throw;
        }

        JsonObject result;
        var dispatched = false;
        var grant = new AiCallGrant(token => CheckAsync(tool.Registration, token), ct);
        await using var grantLifetime = grant.ConfigureAwait(false);
        try
        {
            await CheckAsync(tool.Registration, ct).ConfigureAwait(false);
            dispatched = true;
            var response = await tool.Session.CallAsync(name, arguments, grant.Token).ConfigureAwait(false);
            result = JsonSerializer.SerializeToNode(response, AssistantJsonContext.Default.AiMcpCallResult)!.AsObject();
            var images = response.Data.Content.Where(block => block.Type == "image" && block.Data is not null).ToArray();
            if (images.Length > 0)
                result["imageContent"] = JsonSerializer.SerializeToNode(images, AssistantJsonContext.Default.AiMcpContentArray);
        }
        catch (Exception error)
        {
            await grant.StopAsync().ConfigureAwait(false);
            if (!dispatched)
            {
                await _journal.NotDispatchedAsync(turn, intent).ConfigureAwait(false);
                throw;
            }

            await _journal.UnknownAsync(turn, intent, error).ConfigureAwait(false);
            if (error is OperationCanceledException && ct.IsCancellationRequested) throw;
            if (grant.Revocation is { } revoked)
            {
                throw new LauncherOperationException("AI_MCP_EFFECT_UNKNOWN", "Permission changed during the dispatched third-party call. Its effect is unknown; returned data is withheld.",
                    new JsonObject { ["invocationId"] = invocation, ["revocation"] = LauncherOperationException.ToResult(revoked)["error"]!.DeepClone() });
            }

            throw new LauncherOperationException("AI_MCP_EFFECT_UNKNOWN",
                "The third-party call has no recorded response. Its effect is unknown; do not replay it.",
                new JsonObject { ["invocationId"] = invocation, ["sourceIdentity"] = AiToolSourceIdentities.Snapshot(tool.Identity), ["cause"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
        }

        await grant.StopAsync().ConfigureAwait(false);
        await _journal.CompleteAsync(turn, intent, result, ct).ConfigureAwait(false);
        await emit("intent_result_recorded", invocation, new JsonObject
        {
            ["name"] = name,
            ["toolCallId"] = callId,
            ["status"] = result["status"]!.DeepClone(),
            ["sourceIdentity"] = AiToolSourceIdentities.Snapshot(tool.Identity)
        }).ConfigureAwait(false);
        try { await CheckAsync(tool.Registration, ct).ConfigureAwait(false); }
        catch (LauncherOperationException error)
        {
            return new JsonObject
            {
                ["status"] = "failed",
                ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone(),
                ["data"] = new JsonObject { ["invocationId"] = invocation, ["resultRetained"] = true, ["effectsMayHaveOccurred"] = true, ["returnedDataWithheld"] = true }
            };
        }

        return result;
    }

    private async Task CheckAsync(JsonObject registration, CancellationToken ct)
    {
        _policy.CheckExternal(ct);
        var latest = await new AiExtensionStore(paths).ReadDocumentAsync(ct).ConfigureAwait(false);
        var current = latest["mcp"]!.AsArray().FirstOrDefault(item => item?["id"]?.ToString() == registration["id"]!.ToString());
        if (current?["enabled"]?.GetValue<bool>() != true || !JsonNode.DeepEquals(current, registration))
            throw new LauncherOperationException("AI_MCP_REGISTRATION_REVOKED", "This MCP was disabled, removed or changed after discovery. Its pending call was not authorized.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
                await emit("mcp_finished", "mcp:" + session.Registration.Id, new JsonObject
                {
                    ["id"] = session.Registration.Id,
                    ["status"] = "closed"
                }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                await emit("mcp_cleanup_error", "mcp:" + session.Registration.Id, new JsonObject
                {
                    ["id"] = session.Registration.Id,
                    ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
                }).ConfigureAwait(false);
            }
        }
    }

    private static string Hash(JsonNode value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonString())));
}