// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiConversationHistory(LauncherPaths paths)
{
    private readonly AiChatStore _store = new(paths);

    /// <summary>Commits revision-checked conversation changes and returns a lossless public snapshot.</summary>
    public async Task<AiChatRecord> UpdateAsync(AiConversationUpdate request, CancellationToken ct)
    {
        var conversation = _store.GetDocument(request.ConversationId);
        AiChatStore.Writable(conversation);
        if (conversation["revision"]!.GetValue<long>() != request.ExpectedRevision)
            throw new LauncherOperationException("AI_CONFIGURATION_CONFLICT", "The conversation changed. Reload its revision.");
        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
                throw new ArgumentException("Use a nonempty conversation title of at most 200 characters.");
            conversation["title"] = request.Title.Trim();
            conversation["titleSource"] = "manual";
        }

        if (request.ClearInstance && request.InstanceId is not null) throw new ArgumentException("Choose an instance or clear the binding.");
        if (conversation["gameSessionId"] is not null && (request.ClearInstance || request.InstanceId is not null))
            throw new ArgumentException("A temporary game conversation keeps its original game session and instance.");
        if (conversation["repairId"] is not null && (request.ClearInstance || request.InstanceId is not null || request.Mode is not null))
            throw new ArgumentException("A repair conversation keeps its original incident, instance and scoped delegation.");
        if ((request.ClearInstance || request.InstanceId is not null || request.ModelId is not null) && conversation["skinScopeId"] is not null)
            throw new ArgumentException("A skin conversation keeps its original binding.");
        if (request.InstanceId is not null) _ = new LauncherClient(paths).Instances.Get(request.InstanceId);
        if (request.ModelId is not null)
        {
            var config = await new AiServiceStore(paths).GetAsync(ct).ConfigureAwait(false);
            if (!config.Services.Any(service => service.Enabled && service.Models.Any(model => model.Id == request.ModelId)))
                throw new LauncherOperationException("AI_MODEL_NOT_FOUND", "No enabled configured model has this identifier.");
        }

        void ChangeContext(string kind, string? value)
        {
            if (conversation[kind + "Id"]?.ToString() == value) return;
            conversation[kind + "Id"] = value;
            var last = _store.TurnDocuments(request.ConversationId).LastOrDefault();
            if (last is null) return;
            var changes = conversation["contextChanges"] as JsonArray ?? [];
            if (conversation["contextChanges"] is null) conversation["contextChanges"] = changes;
            changes.Add((JsonNode)new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["afterTurnId"] = last["id"]!.DeepClone(), ["kind"] = kind, ["value"] = value });
        }

        if (request.ClearInstance || request.InstanceId is not null) ChangeContext("instance", request.InstanceId);
        if (request.ModelId is not null) ChangeContext("model", request.ModelId);
        if (request.Mode is not null)
        {
            if (request.Mode is not ("read_only" or "safe" or "allow_operations")) throw new ArgumentException("Use read_only, safe or allow_operations mode.");
            conversation["mode"] = request.Mode;
            conversation["operationRevision"] = (conversation["operationRevision"]?.GetValue<long>() ?? 0) + 1;
            conversation.Remove("operationGrantRevision");
            conversation.Remove("operationToolGrants");
        }

        if (request.FileAccessEnabled is { } files) conversation["fileAccessEnabled"] = files;
        if (request.NetworkAccessEnabled is { } network) conversation["networkAccessEnabled"] = network;
        conversation["revision"] = request.ExpectedRevision + 1;
        await _store.SaveConversationAsync(conversation, ct).ConfigureAwait(false);
        return AiChatRecord.Read(conversation);
    }

    /// <summary>Reads the exact deletion binding while distinguishing independent jobs from owned turns.</summary>
    public AiConversationRemovalReview ReviewRemoval(string id)
    {
        var conversation = _store.GetDocument(id);
        var turns = _store.TurnDocuments(id);
        var jobs = turns.SelectMany(turn => turn?["jobIds"] is JsonArray ids ? ids.Select(value => value!.ToString()) : [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var revision = conversation["revision"]!.DeepClone();
        var turnIds = new JsonArray(turns.Select(turn => turn!["id"]!.DeepClone()).ToArray());
        // Turn progress may change while deletion is being confirmed; identity and new job effects may not.
        var fingerprint = new JsonObject
        {
            ["conversationId"] = id,
            ["revision"] = revision.DeepClone(),
            ["turnIds"] = turnIds.DeepClone(),
            ["independentJobIds"] = new JsonArray([.. jobs.Select(job => (JsonNode?)JsonValue.Create(job))])
        };
        return new()
        {
            ConversationId = id,
            Revision = revision,
            TurnIds = turnIds,
            IndependentJobIds = jobs,
            StopsActiveTurn = turns.Any(turn => !AiChatStore.IsTerminal(turn?["status"]?.ToString())),
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToJsonString())))
        };
    }

    /// <summary>Removes exactly reviewed history and reports any retained cleanup evidence.</summary>
    public Task<AiConversationRemovalReceipt> RemoveAsync(AiConversationRemoval request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AiChatStore.Writable(_store.GetDocument(request.ConversationId));
        var review = ReviewRemoval(request.ConversationId);
        if (!request.Confirm || review.Revision.GetValue<long>() != request.ExpectedRevision || review.Fingerprint != request.Fingerprint)
        {
            throw new LauncherOperationException("AI_HISTORY_REVIEW_CHANGED", "Confirm the exact current deletion review. No history was removed.",
                JsonSerializer.SerializeToNode(review, AiJsonContext.Default.AiConversationRemovalReview));
        }

        var source = Path.Combine(paths.Root, "ai", "conversations", AiChatStore.CheckId(request.ConversationId));
        var staging = Path.Combine(paths.Root, "ai", "removed-conversations", request.ConversationId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
        Directory.Move(source, staging);
        var result = new AiConversationRemovalReceipt { ConversationId = request.ConversationId, Removed = true, IndependentJobIds = review.IndependentJobIds };
        try { Directory.Delete(staging, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            result = result with
            {
                RetainedRecoveryPath = staging,
                Warnings = new JsonArray(new JsonObject
                {
                    ["code"] = "AI_HISTORY_CLEANUP_PENDING",
                    ["message"] = "The conversation is removed from history, but some private files remain at the recovery path. They were not silently discarded.",
                    ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
                })
            };
        }

        return Task.FromResult(result);
    }

    /// <summary>Writes a portable export atomically and reports its exact byte length and hash.</summary>
    public async Task<AiConversationExportReceipt> ExportAsync(AiConversationExport request, CancellationToken ct)
    {
        if (request.Format is not ("json" or "text")) throw new ArgumentException("Use json or text export format.");
        var target = Path.GetFullPath(request.Destination);
        var aiRoot = Path.Combine(paths.Root, "ai") + Path.DirectorySeparatorChar;
        if (target.StartsWith(aiRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an export destination outside the assistant's private record and credential directory.");
        if (File.Exists(target) && !request.Overwrite)
            throw new LauncherOperationException("AI_EXPORT_EXISTS", "The destination already exists. Choose another file or explicitly confirm overwrite.");
        var conversation = _store.GetDocument(request.ConversationId);
        var receipts = _store.TurnDocuments(request.ConversationId);
        var turns = new JsonArray();
        var text = new StringBuilder("# ").AppendLine(conversation["title"]?.ToString() ?? request.ConversationId);
        foreach (var receipt in receipts)
        {
            ct.ThrowIfCancellationRequested();
            var id = receipt!["id"]!.ToString();
            try
            {
                var turn = _store.GetTurnDocument(request.ConversationId, id);
                var events = new JsonArray();
                long cursor = 0;
                var partialAnswer = new StringBuilder();
                do
                {
                    var page = await _store.EventsAsync(request.ConversationId, id, cursor, ct: ct).ConfigureAwait(false);
                    foreach (var item in page.Items)
                    {
                        events.Add(JsonSerializer.SerializeToNode(item, AiJsonContext.Default.AiChatEvent));
                        if (item.Kind == "text_delta") partialAnswer.Append(item.Data?["text"]?.ToString());
                    }

                    cursor = page.NextSequence;
                    if (!page.HasMore) break;
                } while (true);
                turn["events"] = events;
                turns.Add((JsonNode)turn);
                text.AppendLine().AppendLine("## User").AppendLine(turn["prompt"]?.ToString())
                    .AppendLine().Append("## Assistant (").Append(turn["status"]).AppendLine(")")
                    .AppendLine(turn["answer"]?.ToString() ?? partialAnswer.ToString());
                if (turn["error"] is { } error) text.AppendLine().AppendLine(error.ToJsonString());
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                turns.Add((JsonNode)new JsonObject { ["id"] = id, ["status"] = "unreadable", ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
                text.AppendLine().Append("## Unreadable turn ").AppendLine(id);
            }
        }

        var document = new JsonObject { ["schemaVersion"] = 1, ["exportedUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["conversation"] = conversation, ["turns"] = turns, ["providerStateIncluded"] = false };
        var bytes = Encoding.UTF8.GetBytes(request.Format == "json" ? document.ToJsonString(new() { WriteIndented = true }) : text.ToString());
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            if (request.Overwrite && File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        return new AiConversationExportReceipt { Path = target, Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Format = request.Format, ProviderStateIncluded = false };
    }
}