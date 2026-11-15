// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AiEffectsRead(string TurnId);

public sealed record AiEffectAcknowledgement
{
    public required string ConversationId { get; init; }
    public required string TurnId { get; init; }
    public required string InvocationId { get; init; }
    public required string Fingerprint { get; init; }
    public required bool AcceptUnknown { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(AiEffectsRead))]
[JsonSerializable(typeof(AiEffectAcknowledgement))]
[JsonSerializable(typeof(AiTargetObservation))]
[JsonSerializable(typeof(AiEffectObservation))]
[JsonSerializable(typeof(AiToolSourceIdentity))]
internal sealed partial class AiEffectJsonContext : JsonSerializerContext;

internal sealed class AiToolJournal(LauncherPaths paths)
{
    private readonly AiChatStore _store = new(paths);
    public static AIFunctionDeclaration ReadTool() => AiToolDeclarations.Create("conversation_effects_check",
        "Read and verify a stopped earlier turn's durable mutation effects in this conversation. Never replays writes. Desired state observation is not proof of the actor; recovered jobs must be observed to terminal status.",
        AiEffectJsonContext.Default.AiEffectsRead.GetJsonSchemaAsNode(new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true
        }).AsObject());

    public JsonObject? ReuseRecorded(JsonObject turn, string name, JsonObject arguments, AiToolSourceIdentity? sourceIdentity = null, bool priorTurnsOnly = false)
    {
        var goal = turn["goalId"]?.ToString() ?? turn["id"]!.ToString();
        foreach (var prior in _store.TurnDocuments(turn["conversationId"]!.ToString(), includeProtocolState: true).Reverse())
        {
            if (priorTurnsOnly && prior["id"]?.ToString() == turn["id"]?.ToString()) continue;
            if ((prior["goalId"]?.ToString() ?? prior["id"]!.ToString()) != goal || prior["intents"] is not JsonArray records) continue;
            foreach (var intent in records.OfType<JsonObject>().Reverse())
            {
                if (intent["name"]?.ToString() != name || !JsonNode.DeepEquals(intent["arguments"], arguments)
                    || intent["state"]?.ToString() != "result_recorded" || intent["result"] is not JsonObject result)
                {
                    continue;
                }

                if (!JsonNode.DeepEquals(intent["sourceIdentity"], AiToolSourceIdentities.Snapshot(sourceIdentity)))
                    throw new LauncherOperationException("AI_RETRY_SOURCE_CHANGED", "The source registration, tool identity or schema changed since this goal's recorded call. Do not replay the old attempt under a different contract.");
                var reused = result.DeepClone().AsObject();
                reused["reusedInvocationId"] = intent["id"]!.DeepClone();
                reused["mutationReplayed"] = false;
                reused["verification"] = JsonSerializer.SerializeToNode(Verify(intent, turn), AiEffectJsonContext.Default.AiEffectObservation);
                return reused;
            }
        }

        return null;
    }

    public async Task<JsonObject> PrepareAsync(JsonObject turn, string invocation, string callId, string name,
        JsonObject arguments, CancellationToken ct, AiToolSourceIdentity? sourceIdentity = null)
    {
        foreach (var receipt in _store.TurnDocuments(turn["conversationId"]!.ToString(), includeProtocolState: true))
        {
            var previous = receipt["id"]?.ToString() == turn["id"]?.ToString() ? turn : receipt;
            if (previous["intents"] is not JsonArray intents) continue;
            foreach (var pending in intents.OfType<JsonObject>().Where(item => item["state"]?.ToString() == "unknown_effect"))
            {
                if (pending["name"]?.ToString() is "game_edit" or "game_operation_cancel")
                {
                    var observed = Verify(pending, turn);
                    if (observed is AiGameEffectObservation { EffectResolved: true })
                    {
                        pending["verification"] = JsonSerializer.SerializeToNode(observed, AiEffectJsonContext.Default.AiEffectObservation);
                        pending["state"] = observed.DesiredState == true ? "desired_state_observed" : "effect_outcome_observed";
                        RefreshUnknownList(previous);
                        if (!ReferenceEquals(previous, turn)) await _store.SaveTurnAsync(previous, ct).ConfigureAwait(false);
                        continue;
                    }
                }

                if (Overlaps(pending["arguments"]!.AsObject(), pending["name"]!.ToString(), arguments, name)
                    && (pending["acknowledgement"]?["acceptedUnknown"]?.GetValue<bool>() != true
                        || (previous["goalId"]?.ToString() ?? previous["id"]!.ToString()) == (turn["goalId"]?.ToString() ?? turn["id"]!.ToString())))
                {
                    throw new LauncherOperationException("AI_EFFECT_REVIEW_REQUIRED", "A previous call has an unresolved overlapping effect. Inspect its durable intent. An exact user acknowledgement may allow a new separately authorized goal, never replay this uncertain goal.",
                        new JsonObject { ["turnId"] = previous["id"]!.DeepClone(), ["invocationId"] = pending["id"]!.DeepClone() });
                }
            }
        }

        var before = Observe(name, arguments);
        var intent = new JsonObject
        {
            ["id"] = invocation,
            ["callId"] = callId,
            ["name"] = name,
            ["arguments"] = arguments.DeepClone(),
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["state"] = "prepared",
            ["operationRevision"] = turn["operationRevision"]?.DeepClone() ?? JsonValue.Create(0L),
            ["before"] = JsonSerializer.SerializeToNode(before, AiEffectJsonContext.Default.AiTargetObservation),
            ["executionAttribution"] = "not_dispatched"
        };
        if (sourceIdentity is not null) intent["sourceIdentity"] = AiToolSourceIdentities.Snapshot(sourceIdentity);
        var records = turn["intents"] as JsonArray ?? [];
        records.Add((JsonNode)intent);
        if (turn["intents"] is null) turn["intents"] = records;
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        return intent;
    }

    public async Task DispatchingAsync(JsonObject turn, JsonObject intent, CancellationToken ct)
    {
        intent["state"] = "dispatching";
        intent["executionAttribution"] = "awaiting_result";
        intent["dispatchedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
    }

    public async Task CompleteAsync(JsonObject turn, JsonObject intent, JsonObject result, CancellationToken ct)
    {
        intent["state"] = "result_recorded";
        intent["executionAttribution"] = "tool_result";
        intent["completedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        if (intent["name"]?.ToString() == "skin_propose" && result["data"]?["proposalId"] is { } proposalId)
            intent["skinProposalId"] = proposalId.DeepClone();
        try { intent["verification"] = JsonSerializer.SerializeToNode(Verify(intent, turn, withinInvocation: true), AiEffectJsonContext.Default.AiEffectObservation); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException or ArgumentException or JsonException)
        {
            intent["verification"] = new JsonObject { ["status"] = "unavailable", ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() };
        }

        result["verification"] = intent["verification"]!.DeepClone();
        if (intent["name"]?.ToString() is "game_edit" or "game_operation_cancel"
            && result["data"]?["state"]?.ToString() is "queued" or "applying" or "unknown")
        {
            intent["state"] = "unknown_effect";
            intent["executionAttribution"] = "awaiting_game_receipt";
        }

        intent["result"] = result.DeepClone();
        RefreshUnknownList(turn);
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
    }

    public async Task UnknownAsync(JsonObject turn, JsonObject intent, Exception error)
    {
        if (intent["state"]?.ToString() != "dispatching") return;
        intent["state"] = "unknown_effect";
        intent["executionAttribution"] = "unknown";
        intent["transportError"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone();
        RefreshUnknownList(turn);
        await _store.SaveTurnAsync(turn, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task NotDispatchedAsync(JsonObject turn, JsonObject intent)
    {
        intent["state"] = "not_dispatched";
        intent["executionAttribution"] = "not_dispatched";
        RefreshUnknownList(turn);
        await _store.SaveTurnAsync(turn, CancellationToken.None).ConfigureAwait(false);
    }

    public static void Recover(JsonObject turn)
    {
        if (turn["intents"] is not JsonArray records) return;
        foreach (var intent in records.OfType<JsonObject>())
        {
            if (intent["state"]?.ToString() == "prepared")
                intent["state"] = "not_dispatched";
            if (intent["state"]?.ToString() == "dispatching")
            {
                intent["state"] = "unknown_effect";
                intent["executionAttribution"] = "unknown";
            }
        }

        RefreshUnknownList(turn);
    }

    /// <summary>Observes and persists verification evidence for incomplete effects without replaying a mutation.</summary>
    public async Task<AiChatRecord> CheckAsync(string conversation, string turnId, CancellationToken ct)
        => AiChatRecord.Read(await CheckDocumentAsync(conversation, turnId, ct).ConfigureAwait(false));

    internal async Task<JsonObject> CheckDocumentAsync(string conversation, string turnId, CancellationToken ct)
    {
        var turn = _store.GetTurnDocument(conversation, turnId, true);
        if (!AiChatStore.IsTerminal(turn["status"]?.ToString()) && AiGenerationLease.OwnerAlive(turn))
            throw new LauncherOperationException("AI_EFFECT_CHECK_BUSY", "Wait until the owning turn has stopped before checking incomplete effects.");
        Recover(turn);
        if (turn["intents"] is JsonArray records)
        {
            foreach (var intent in records.OfType<JsonObject>().Where(item => item["state"]?.ToString() == "unknown_effect"))
            {
                try
                {
                    if (RecoverJob(turn, intent)) continue;
                    var verification = Verify(intent, turn);
                    intent["verification"] = JsonSerializer.SerializeToNode(verification, AiEffectJsonContext.Default.AiEffectObservation);
                    if (verification.DesiredState == true || verification is AiGameEffectObservation { EffectResolved: true })
                        intent["state"] = verification.DesiredState == true ? "desired_state_observed" : "effect_outcome_observed";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException or ArgumentException)
                {
                    intent["verification"] = new JsonObject { ["status"] = "unavailable", ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() };
                }
            }
        }

        RefreshUnknownList(turn);
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        return _store.GetTurnDocument(conversation, turnId);
    }

    /// <summary>Records explicit acceptance of one exact uncertainty without replaying or inferring its outcome.</summary>
    public async Task<AiChatRecord> AcknowledgeAsync(AiEffectAcknowledgement request, CancellationToken ct)
    {
        var turn = _store.GetTurnDocument(request.ConversationId, request.TurnId, true);
        if (!AiChatStore.IsTerminal(turn["status"]?.ToString()))
            throw new LauncherOperationException("AI_EFFECT_CHECK_BUSY", "Stop the owning turn before acknowledging an unknown outcome.");
        var intent = (turn["intents"] as JsonArray ?? []).OfType<JsonObject>()
            .SingleOrDefault(item => item["id"]?.ToString() == request.InvocationId);
        if (!request.AcceptUnknown || intent is null || intent["state"]?.ToString() != "unknown_effect"
            || request.Fingerprint != EffectFingerprint(intent))
        {
            throw new LauncherOperationException("AI_EFFECT_ACKNOWLEDGEMENT_STALE", "Explicitly accept the exact current unknown-effect fingerprint; no authorization or outcome was changed.");
        }

        intent["acknowledgement"] = new JsonObject
        {
            ["acceptedUnknown"] = true,
            ["actor"] = "user",
            ["fingerprint"] = request.Fingerprint,
            ["observedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["newGoalsOnly"] = true,
            ["note"] = "The user accepts the remaining uncertainty for a new explicitly authorized goal. This is not proof of success/failure, permission for this model to replay the old goal, or a rollback."
        };
        RefreshUnknownList(turn);
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        await _store.AppendEventAsync(turn, "effect_uncertainty_acknowledged", request.InvocationId,
            intent["acknowledgement"]!.DeepClone().AsObject(), ct).ConfigureAwait(false);
        return _store.GetTurn(request.ConversationId, request.TurnId);
    }

    private static string EffectFingerprint(JsonObject intent)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["id"] = intent["id"]!.DeepClone(),
            ["name"] = intent["name"]!.DeepClone(),
            ["arguments"] = intent["arguments"]!.DeepClone(),
            ["sourceIdentity"] = intent["sourceIdentity"]?.DeepClone(),
            ["dispatchedUtc"] = intent["dispatchedUtc"]?.DeepClone()
        }.ToJsonString())));

    private bool RecoverJob(JsonObject turn, JsonObject intent)
    {
        if (intent["arguments"]?["background"]?.GetValue<bool>() != true) return false;
        var id = AiChatStore.CheckId(intent["id"]!.ToString());
        if (!File.Exists(Path.Combine(paths.JobsDirectory, id + ".json"))) return false;
        var job = new LauncherClient(paths).Jobs.Get(id);
        if (job.ClientRequestId != id
            || job.Request?.Parameters?["instanceId"]?.ToString() != intent["arguments"]?["instanceId"]?.ToString())
        {
            return false;
        }

        intent["state"] = "result_recorded";
        intent["executionAttribution"] = "core_job_admission";
        intent["result"] = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["operation"] = intent["name"]!.DeepClone(),
            ["status"] = "succeeded",
            ["data"] = JsonSerializer.SerializeToNode(job, ApplicationJsonContext.Job),
            ["jobReceiptObserved"] = true,
            ["transportResultRecovered"] = false,
            ["note"] = "A correlated durable Core job exists. This is a fresh job observation, not the lost tool response or proof of task completion."
        };
        intent["verification"] = new JsonObject
        {
            ["status"] = "core_job_receipt",
            ["jobId"] = id,
            ["jobStatus"] = job.Status,
            ["desiredStateObserved"] = null
        };
        var jobs = turn["jobIds"] as JsonArray ?? [];
        if (!jobs.Any(item => item?.ToString() == id)) jobs.Add((JsonNode?)JsonValue.Create(id));
        if (turn["jobIds"] is null) turn["jobIds"] = jobs;
        return true;
    }

    private AiTargetObservation Observe(string name, JsonObject arguments)
    {
        if (name is not ("config_set" or "config_patch"))
        {
            return new AiToolTargetReference(arguments["instanceId"]?.DeepClone(), arguments["reviewId"]?.DeepClone(),
                arguments["jobId"]?.DeepClone(), arguments["sessionId"]?.DeepClone());
        }

        var client = new LauncherClient(paths);
        var instance = arguments["instanceId"]?.GetValue<string>() is { } id ? client.Instances.Get(id) : null;
        var all = client.Settings.GetOverrides(instance);
        var keys = (arguments["key"] is { } key ? [key.ToString()]
            : (arguments["changes"] as JsonObject ?? []).Select(pair => pair.Key))
            .Concat(Removed(arguments)).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Any(JavaKey)) keys.AddRange(["javaPath", "javaRuntimeId", "javaRuntimeExecutable"]);
        var owned = new JsonObject();
        foreach (var selected in keys.Distinct(StringComparer.Ordinal))
            if (all.TryGetPropertyValue(selected, out var value)) owned[selected] = value?.DeepClone();
        return new AiSettingsObservation(arguments["instanceId"]?.DeepClone(), owned, DateTimeOffset.UtcNow.ToString("O"));
    }

    private AiEffectObservation Verify(JsonObject intent, JsonObject turn, bool withinInvocation = false)
    {
        if (intent["name"]?.ToString() is "game_edit" or "game_operation_cancel")
        {
            var gameArguments = intent["arguments"]!;
            var session = gameArguments["sessionId"]!.ToString();
            var operation = gameArguments["request"]?["operationId"]?.ToString() ?? gameArguments["operationId"]!.ToString();
            GameBridgeService.ValidateId(session);
            GameBridgeService.ValidateId(operation);
            if (turn["instanceId"]?.GetValue<string>() is { } bound && new LauncherClient(paths).Sessions.Get(session).InstanceId != bound)
                throw new LauncherOperationException("AI_INSTANCE_SCOPE", "The game receipt belongs to another instance.");
            var journalFile = GameBridgeService.JournalPath(paths, session, operation);
            if (!File.Exists(journalFile)) return new AiGameEffectObservation("unavailable", false, false, null);
            var entry = JsonFiles.ReadSnapshot(journalFile, stream => JsonSerializer.Deserialize(stream, BridgeWireJsonContext.Default.BridgeJournalEntry)
                ?? throw new InvalidDataException("Missing game operation intent."));
            if (gameArguments["request"] is { } request && GameBridgeService.RequestFingerprint(request.Deserialize(GameBridgeJsonContext.Default.GameEditRequest)!) != entry.Fingerprint)
                throw new LauncherOperationException("GAME_OPERATION_CONFLICT", "The game receipt does not match this intent.");
            return new AiGameEffectObservation("observed",
                entry.Operation.State == "applied" && entry.Request.Kind is "world.setTime" or "player.setHealth",
                entry.Operation.State is "applied" or "rejected" or "canceled", entry.Operation);
        }

        if (intent["name"]?.ToString() is "repair_apply" or "repair_verify" or "repair_restore")
        {
            var args = intent["arguments"]!;
            if (args["repairId"]?.ToString() != turn["repairId"]?.ToString())
                throw new LauncherOperationException("AI_REPAIR_SCOPE", "The repair receipt is outside this turn's incident.");
            var client = new LauncherClient(paths);
            var record = client.Repairs.ReadDocument(args["repairId"]!.ToString());
            var plan = AiRepairService.Plan(record, args["planId"]!.ToString());
            var state = plan["state"]?.ToString();
            var known = intent["name"]!.ToString() switch
            {
                "repair_apply" => plan["afterSettings"] is not null && state is "applied" or "verifying" or "observed" or "restored",
                "repair_restore" => state == "restored",
                _ => plan["outcome"] is not null || client.Sessions.List().Any(s => s.InstanceId == record["instanceId"]?.ToString()
                    && s.AdditionalData.TryGetValue("repairPlanId", out var tag) && tag.GetString() == args["planId"]!.ToString())
            };
            return new AiRepairEffectObservation(known, args["repairId"]!.DeepClone(), AiRepairPlanReport.Read(plan.DeepClone().AsObject()));
        }

        if (intent["fileTransaction"] is JsonObject file)
        {
            var root = file["rootPath"]!.ToString();
            var state = new AiExtensionStore(paths).EffectiveDocument();
            if (!(file["pathAccess"] is { } approvedPath && (withinInvocation || new AiPathAccess(paths).Allows(turn["conversationId"]!.ToString(), approvedPath.ToString(), false)))
                && !state["fileRoots"]!.AsArray().Any(item => item?["id"]?.ToString() == file["rootId"]!.ToString()
                && string.Equals(item["path"]?.ToString(), root, StringComparison.OrdinalIgnoreCase)))
            {
                throw new LauncherOperationException("AI_FILE_ROOT_REVOKED", "Reauthorize this directory before inspecting the incomplete file transaction.");
            }

            using var opened = AiScopedFiles.Open(root, file["path"]!.ToString());
            var observedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(opened.Stream));
            return new AiFileEffectObservation(observedHash == file["desiredSha256"]!.ToString(), observedHash,
                file["backupPath"]?.DeepClone(), intent["executionAttribution"]!.DeepClone());
        }

        if (intent["name"]?.ToString()?.StartsWith("skin_", StringComparison.Ordinal) == true)
        {
            var proposalId = intent["skinProposalId"]?.GetValue<string>() ?? intent["arguments"]?["proposalId"]?.GetValue<string>()
                ?? intent["result"]?["data"]?["proposalId"]?.GetValue<string>();
            if (proposalId is not null)
            {
                return new AiSkinEffectObservation(new Skins.SkinService(paths).Proposals.Describe(proposalId));
            }
        }

        if (intent["name"]?.ToString() is not ("config_set" or "config_patch"))
        {
            return new AiUnverifiedEffectObservation(intent["executionAttribution"]!.DeepClone());
        }

        var arguments = intent["arguments"]!.AsObject();
        var observed = (AiSettingsObservation)Observe(intent["name"]!.ToString(), arguments);
        var current = observed.Overrides;
        var changes = Changes(intent["name"]!.ToString(), arguments);
        var removed = Removed(arguments).Where(key => !changes.ContainsKey(key)).ToArray();
        var matches = changes.All(pair => current.ContainsKey(pair.Key) && JsonNode.DeepEquals(current[pair.Key], pair.Value))
            && removed.All(key => !current.ContainsKey(key));
        return new AiSettingsEffectObservation(matches, intent["executionAttribution"]!.DeepClone(), observed);
    }

    private static bool Overlaps(JsonObject earlier, string earlierName, JsonObject proposed, string proposedName)
    {
        var a = earlier["instanceId"]?.ToString();
        var b = proposed["instanceId"]?.ToString();
        if (a is not null && b is not null && a != b) return false;
        if (earlierName is not ("config_set" or "config_patch") || proposedName is not ("config_set" or "config_patch"))
            return true;
        var first = Changes(earlierName, earlier).Select(pair => pair.Key).Concat(Removed(earlier)).ToHashSet(StringComparer.Ordinal);
        var second = Changes(proposedName, proposed).Select(pair => pair.Key).Concat(Removed(proposed));
        return second.Any(key => first.Contains(key) || (first.Any(JavaKey) && JavaKey(key)));
    }

    private static bool JavaKey(string key) => key is "javaPath" or "javaRuntimeId" or "javaRuntimeExecutable";
    private static JsonObject Changes(string name, JsonObject arguments) => name == "config_set"
        ? new JsonObject { [arguments["key"]!.ToString()] = arguments["value"]?.DeepClone() }
        : arguments["changes"]?.DeepClone().AsObject() ?? [];
    private static string[] Removed(JsonObject arguments)
        => arguments["removeKeys"] is JsonArray keys ? [.. keys.Select(key => key!.GetValue<string>())] : [];

    private static void RefreshUnknownList(JsonObject turn)
        => turn["unknownEffects"] = new JsonArray((turn["intents"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(item => item["state"]?.ToString() == "unknown_effect")
            .Select(item => (JsonNode)new JsonObject
            {
                ["invocationId"] = item["id"]!.DeepClone(),
                ["name"] = item["name"]!.DeepClone(),
                ["arguments"] = item["arguments"]!.DeepClone(),
                ["executionAttribution"] = "unknown",
                ["fingerprint"] = EffectFingerprint(item),
                ["acknowledgement"] = item["acknowledgement"]?.DeepClone()
            }).ToArray());
}