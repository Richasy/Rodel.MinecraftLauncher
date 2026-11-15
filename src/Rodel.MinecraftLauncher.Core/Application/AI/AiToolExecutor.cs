// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiToolExecutor(LauncherPaths paths, JsonObject turn, AiCoreTools launcher,
    Func<string, JsonObject, CancellationToken, string?, Task<JsonObject>>? invoke = null)
{
    private readonly AiToolPolicy _policy = new(paths, turn);
    private readonly AiToolJournal _journal = new(paths);
    private readonly AiChatStore _store = new(paths);
    private readonly Func<string, JsonObject, CancellationToken, string?, Task<JsonObject>> _invoke = invoke ?? launcher.InvokeAsync;

    /// <summary>Checks live grants, records mutation intent and dispatches one tool without replaying uncertain effects.</summary>
    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, string invocation, string callId,
        Func<string, string?, JsonObject, Task> emit, CancellationToken ct)
    {
        var original = arguments.DeepClone();
        _policy.Normalize(name, arguments);
        if (name is "content_apply_review" or "launch" || (name == "runtime_change" && arguments["plan"]?.GetValue<bool>() != true))
            arguments["background"] = true;
        if (name == "content_apply_review") arguments["confirm"] = true;
        var mutation = AiToolPolicy.IsMutation(name, arguments);
        var reused = mutation ? _journal.ReuseRecorded(turn, name, arguments) : null;
        var review = reused is null && NeedsReview(name, arguments)
            ? await CaptureReviewAsync(name, arguments, ct).ConfigureAwait(false) : null;
        if (mutation)
        {
            await _policy.AuthorizeOperationAsync(name, arguments, invocation, emit, ct, review,
                review is null ? null : token => CaptureReviewAsync(name, arguments, token)).ConfigureAwait(false);
        }

        _policy.Check(name, arguments, ct);
        if (reused is not null)
        {
            await emit("intent_result_reused", invocation, new JsonObject
            {
                ["name"] = name,
                ["toolCallId"] = callId,
                ["reusedInvocationId"] = reused["reusedInvocationId"]!.DeepClone(),
                ["mutationReplayed"] = false
            }).ConfigureAwait(false);
            await ObserveJobAsync(reused, invocation, emit, ct).ConfigureAwait(false);
            return reused;
        }

        var requiresReviewNetwork = review is AiContentToolReview { Review.Mode: not "local" };
        if (review is not null)
        {
            var currentReview = await CaptureReviewAsync(name, arguments, ct).ConfigureAwait(false);
            if (!JsonNode.DeepEquals(AiToolReviews.Write(review), AiToolReviews.Write(currentReview)))
                throw new LauncherOperationException("AI_CONFIRMATION_REVIEW_CHANGED", "The reviewed target changed before dispatch; prepare a fresh operation.");
        }

        if (!JsonNode.DeepEquals(original, arguments))
        {
            await emit("tool_arguments_resolved", invocation, new JsonObject
            {
                ["name"] = name,
                ["toolCallId"] = callId,
                ["arguments"] = arguments.DeepClone(),
                ["note"] = "The host resolved scope/background execution. Background work is observed rather than treated as completed."
            }).ConfigureAwait(false);
        }

        using var appFileScope = name == "app_control" ? await AcquireAppScopeAsync(arguments, ct).ConfigureAwait(false) : null;
        JsonObject? intent = null;
        if (AiToolPolicy.IsMutation(name, arguments))
        {
            intent = await _journal.PrepareAsync(turn, invocation, callId, name, arguments, ct).ConfigureAwait(false);
            await emit("intent_prepared", invocation, new JsonObject { ["name"] = name, ["toolCallId"] = callId }).ConfigureAwait(false);
            try
            {
                _policy.Check(name, arguments, ct);
                await _journal.DispatchingAsync(turn, intent, ct).ConfigureAwait(false);
            }
            catch
            {
                await _journal.NotDispatchedAsync(turn, intent).ConfigureAwait(false);
                throw;
            }
        }

        var dispatched = false;
        JsonObject result;
        var grant = new AiCallGrant(token =>
        {
            _policy.Check(name, arguments, token);
            if (requiresReviewNetwork) _policy.RequireNetwork();
            return RecheckAppScopeAsync(name, arguments, token);
        }, ct);
        await using var grantLifetime = grant.ConfigureAwait(false);
        try
        {
            _policy.Check(name, arguments, ct);
            dispatched = true;
            result = await _invoke(name, arguments, grant.Token, intent is null ? null : invocation).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await grant.StopAsync().ConfigureAwait(false);
            if (intent is not null)
            {
                if (!dispatched)
                {
                    await _journal.NotDispatchedAsync(turn, intent).ConfigureAwait(false);
                }
                else
                {
                    await _journal.UnknownAsync(turn, intent, error).ConfigureAwait(false);
                    if (error is not OperationCanceledException)
                    {
                        throw new LauncherOperationException("AI_TOOL_EFFECT_UNKNOWN",
                            "The dispatched mutation did not return a recorded result. Check the durable intent and actual state instead of replaying it.",
                            new JsonObject { ["invocationId"] = intent["id"]!.DeepClone(), ["cause"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
                    }
                }
            }

            if (!ct.IsCancellationRequested && grant.Revocation is { } revoked)
            {
                throw new LauncherOperationException("AI_TOOL_PERMISSION_REVOKED", "Permission changed while this call was in flight. Reads were canceled; dispatched writes must be checked for actual effects.",
                    LauncherOperationException.ToResult(revoked));
            }

            throw;
        }

        await grant.StopAsync().ConfigureAwait(false);
        if (name == "game_snapshot" && _policy.ObserveGameSnapshot(arguments, result))
            await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        if (turn["repairId"]?.ToString() is { } repairId)
            await new LauncherClient(paths).Repairs.RecordResultAsync(repairId, result, CancellationToken.None).ConfigureAwait(false);
        if (intent is not null)
        {
            await _journal.CompleteAsync(turn, intent, result, ct).ConfigureAwait(false);
            await emit("intent_result_recorded", invocation, new JsonObject
            {
                ["name"] = name,
                ["toolCallId"] = callId,
                ["status"] = result["status"]!.DeepClone(),
                ["verification"] = result["verification"]!.DeepClone()
            }).ConfigureAwait(false);
        }

        try { _policy.Check(name, arguments, ct); }
        catch (LauncherOperationException error)
        {
            return JsonSerializer.SerializeToNode(new AiWithheldToolResult(LauncherOperationException.CreateResult(error).Error!,
                new AiWithheldToolData(intent is not null, invocation)), AiToolObservationJsonContext.Default.AiWithheldToolResult)!.AsObject();
        }

        if (mutation) await ObserveJobAsync(result, invocation, emit, ct).ConfigureAwait(false);
        return result;
    }

    private static bool NeedsReview(string name, JsonObject args)
        => name is "content_apply_review" or "app_control" or "execute"
            || (name == "game_settings_apply" && args["overwrite"]?.GetValue<bool>() == true)
            || (name == "resourcepack_select" && args["allowIncompatible"]?.GetValue<bool>() == true)
            || (name == "process_stop" && args["force"]?.GetValue<bool>() == true);

    private async Task<AiToolReview> CaptureReviewAsync(string name, JsonObject args, CancellationToken ct)
    {
        _policy.CheckReview(name, args, ct);
        if (name == "execute")
        {
            return new AiExecuteReview(paths.Root, args["arguments"]?.DeepClone() ?? throw new ArgumentException("Explicit CLI arguments are required."));
        }

        if (name == "app_control")
        {
            var actual = await _invoke("app_show", new JsonObject { ["appId"] = args["appId"]!.DeepClone() }, ct, null).ConfigureAwait(false);
            if (actual["status"]?.ToString() != "succeeded")
                throw new LauncherOperationException("AI_CONFIRMATION_REVIEW_UNAVAILABLE", "The selected App could not be observed.", actual);
            var state = actual["data"]!.AsObject();
            if (state["fingerprint"]?.ToString() != args["request"]?["expectedFingerprint"]?.ToString())
                throw new LauncherOperationException("APP_STATE_CHANGED", "The App changed before this operation review.", state);
            return new AiObservedToolReview(state.DeepClone().AsObject());
        }

        if (name == "content_apply_review")
        {
            var review = await new LauncherClient(paths).GetContentReviewAsync(args["instanceId"]!.ToString(), args["reviewId"]!.ToString(), ct).ConfigureAwait(false);
            if (review.Stale == true)
                throw new LauncherOperationException("CONTENT_REVIEW_STALE", "The saved content review no longer matches the actual content/runtime baseline.");
            if (review.Mode != "local") _policy.RequireNetwork();
            return new AiContentToolReview(review);
        }

        if (name == "process_stop")
        {
            var session = new LauncherClient(paths).Sessions.Get(args["sessionId"]!.ToString());
            return AiProcessStopReview.Capture(session);
        }

        var tool = name == "game_settings_apply" ? "game_settings_get" : "resourcepack_list";
        var observed = await _invoke(tool, new JsonObject { ["instanceId"] = args["instanceId"]!.DeepClone() }, ct, null).ConfigureAwait(false);
        if (observed["status"]?.ToString() != "succeeded")
            throw new LauncherOperationException("AI_CONFIRMATION_REVIEW_UNAVAILABLE", "Read the actual target before confirming this operation.", observed);
        return new AiObservedToolReview(observed);
    }

    private async Task RecheckAppScopeAsync(string name, JsonObject arguments, CancellationToken ct)
    {
        if (name != "app_control") return;
        using var scope = await AcquireAppScopeAsync(arguments, ct).ConfigureAwait(false);
    }

    private async Task<IDisposable?> AcquireAppScopeAsync(JsonObject arguments, CancellationToken ct)
    {
        var request = arguments["request"]!.AsObject();
        var directory = request["preferences"]?["dataDirectory"]?.GetValue<string>();
        var source = request["action"]?.ToString() == "WallpaperImport" ? request["source"]?.GetValue<string>() : null;
        if (directory is null && source is null) return null;
        var target = directory is not null ? directory.Length == 0 ? new LauncherPaths().Root : directory : source!;
        if (!Path.IsPathFullyQualified(target)) throw new ArgumentException("Use an absolute selected App file/directory target.");
        _policy.CheckManagedFile(directory is not null, ct);
        var roots = (await new AiExtensionStore(paths).ReadDocumentAsync(ct).ConfigureAwait(false))["fileRoots"]!.AsArray();
        foreach (var entry in roots)
        {
            var root = entry!["path"]!.GetValue<string>();
            if (directory is not null && entry["mode"]?.ToString() != "read_write") continue;
            var relative = Path.GetRelativePath(root, target);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            return directory is null ? AiScopedFiles.Open(root, relative) : AiScopedFiles.OpenDirectory(root, relative == "." ? string.Empty : relative);
        }

        throw new LauncherOperationException("AI_FILE_SCOPE_REQUIRED", "Authorize the selected image or next-start directory before applying this App operation.");
    }

    private async Task ObserveJobAsync(JsonObject result, string parent,
        Func<string, string?, JsonObject, Task> emit, CancellationToken ct)
    {
        if (result["data"] is not JsonObject data) return;
        var id = data["jobId"]?.GetValue<string>();
        if (id is null && data["request"]?["operation"]?.ToString() is "launcher-task" or "command"
            && data["id"]?.GetValue<string>() is { } candidate)
        {
            id = candidate;
        }

        if (id is null) return;
        var jobs = turn["jobIds"] as JsonArray ?? [];
        if (!jobs.Any(item => item?.ToString() == id)) jobs.Add((JsonNode?)JsonValue.Create(id));
        if (turn["jobIds"] is null) turn["jobIds"] = jobs;
        await _store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        string? previous = null;
        var client = new LauncherClient(paths);
        var waiting = false;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var job = client.Jobs.Get(id);
                var view = JobView(id, job);
                var active = job.Status is "queued" or "running" or "canceling" && job.ActivityUnknown != true;
                if (active && !waiting)
                {
                    waiting = true;
                    await emit("job_wait_started", parent, new JsonObject { ["jobId"] = id }).ConfigureAwait(false);
                }

                var serialized = JsonSerializer.Serialize(view, AiToolObservationJsonContext.Default.AiJobObservation);
                if (serialized != previous)
                {
                    await emit("job_progress", parent, new JsonObject { ["jobId"] = id, ["job"] = JsonSerializer.SerializeToNode(view, AiToolObservationJsonContext.Default.AiJobObservation) }).ConfigureAwait(false);
                    previous = serialized;
                }

                if (!active)
                {
                    result["jobObservation"] = JsonSerializer.SerializeToNode(view, AiToolObservationJsonContext.Default.AiJobObservation);
                    return;
                }

                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (waiting) await emit("job_wait_finished", parent, new JsonObject { ["jobId"] = id }).ConfigureAwait(false);
        }
    }

    private static AiJobObservation JobView(string id, JobStatus job)
    {
        JsonNode? Field(string name, JsonNode? value)
        {
            if (value is null && job.AdditionalData.TryGetValue(name, out var original))
                value = JsonNode.Parse(original.GetRawText());
            return value;
        }

        var unrecognizedProgress = job.Progress is null ? Field("progress", null) : null;
        var error = Field("error", JsonValue.Create(job.Error));
        var progressCharacters = job.Progress is { } progress
            ? JsonSerializer.Serialize(progress, ApplicationJsonContext.Progress).Length
            : unrecognizedProgress?.ToJsonString().Length ?? 0;
        var largeProgress = progressCharacters > 8000;
        var largeError = error?.ToJsonString().Length > 8000;
        return new()
        {
            Id = id,
            Status = job.Status,
            ActivityUnknown = job.ActivityUnknown,
            OwnerWarning = job.OwnerWarning,
            InstanceId = job.Request?.Parameters?["instanceId"]?.DeepClone(),
            ResultAvailable = job.Result is not null,
            Read = new(new(id)),
            Progress = largeProgress ? null : job.Progress,
            Error = largeError ? null : error?.DeepClone(),
            ProgressAvailableThroughJobShow = largeProgress ? true : null,
            ErrorAvailableThroughJobShow = largeError ? true : null,
            AdditionalData = !largeProgress && unrecognizedProgress is not null
                ? new Dictionary<string, JsonElement> { ["progress"] = job.AdditionalData["progress"].Clone() } : null
        };
    }
}