// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed class JobService(LauncherPaths paths)
{
    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid job identifier.");
        return Path.Combine(paths.JobsDirectory, id + ".json");
    }

    /// <summary>Reads one task, observing owner liveness and independently reporting sidecar failures.</summary>
    public JobStatus Get(string id) => Get(id, refreshOnExit: true);
    private JobStatus Get(string id, bool refreshOnExit)
    {
        var record = JsonFiles.ReadObject(FilePath(id));
        var state = JobStatus.FromStored(record);
        if (File.Exists(Path.ChangeExtension(FilePath(id), ".progress")))
        {
            try
            {
                var progress = JsonFiles.ReadObject(Path.ChangeExtension(FilePath(id), ".progress"));
                state.AdditionalData.Remove("progress");
                state = state with { Progress = LauncherProgress.Read(progress) };
            }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                state.AdditionalData.Remove("progressWarning");
                state = state with { ProgressWarning = ex.Message };
            }
        }

        if (state.Status == "running")
        {
            if (!HasOwnerIdentity(record))
            {
                state.AdditionalData.Remove("activityUnknown");
                state.AdditionalData.Remove("ownerWarning");
                state = state with { ActivityUnknown = true, OwnerWarning = "The running job has an incomplete process identity." };
            }
            else if (!OwnerAlive(record))
            {
                // The owner may have atomically saved its final result after our first read.
                if (refreshOnExit)
                    return Get(id, refreshOnExit: false);
                state = state with { Status = "interrupted" };
            }
        }

        if (state.Status == "queued")
        {
            var ownerFile = FilePath(id) + ".owner";
            JsonObject? owner;
            try
            {
                owner = File.Exists(ownerFile) ? JsonFiles.ReadObject(ownerFile) : null;
            }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                state.AdditionalData.Remove("activityUnknown");
                state.AdditionalData.Remove("ownerWarning");
                state = state with { ActivityUnknown = true, OwnerWarning = ex.Message };
                return state;
            }

            if (owner is not null && !HasOwnerIdentity(owner))
            {
                state.AdditionalData.Remove("activityUnknown");
                state.AdditionalData.Remove("ownerWarning");
                state = state with { ActivityUnknown = true, OwnerWarning = "The dispatched job has an incomplete process identity." };
                return state;
            }

            if ((owner is null || !OwnerAlive(owner)) && DateTimeOffset.TryParse(state.CreatedAt, out var created) && DateTimeOffset.UtcNow - created > TimeSpan.FromSeconds(30))
            {
                if (refreshOnExit)
                    return Get(id, refreshOnExit: false);
                state.AdditionalData.Remove("canResume");
                state.AdditionalData.Remove("recoveryStrategy");
                state = state with { Status = "interrupted", CanResume = true, RecoveryStrategy = "restart-not-started" };
            }
        }

        return state;
    }

    private static bool IsUnreadable(Exception ex) => ex is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or FormatException;
    /// <summary>Lists task states and reports each unreadable task without discarding other entries.</summary>
    public IReadOnlyList<JobStatus> List()
    {
        var result = new List<JobStatus>();
        if (!Directory.Exists(paths.JobsDirectory))
            return result;
        foreach (var file in Directory.EnumerateFiles(paths.JobsDirectory, "*.json"))
        {
            try
            {
                result.Add(Get(Path.GetFileNameWithoutExtension(file)));
            }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                result.Add(new() { Id = Path.GetFileNameWithoutExtension(file), Status = "unreadable", Path = file, Error = ex.Message, ActivityUnknown = true });
            }
        }

        return result;
    }

    private JsonObject GetRecord(string id) => JsonSerializer.SerializeToNode(Get(id), ApplicationJsonContext.Job)!.AsObject();

    /// <summary>Persists a queued operation request and returns its initial lifecycle snapshot.</summary>
    public async Task<JobStatus> CreateAsync(JobDefinition definition, string? resumedFrom = null, string? clientRequestId = null, CancellationToken ct = default)
    {
        if (clientRequestId is not null && !Guid.TryParseExact(clientRequestId, "N", out _))
            throw new ArgumentException("A client request identity must be a 32-digit GUID.", nameof(clientRequestId));
        var id = clientRequestId ?? Guid.NewGuid().ToString("N");
        using var admission = clientRequestId is null ? null : await ResourceLease.AcquireAsync(FilePath(id) + ".admission", ct).ConfigureAwait(false);
        if (clientRequestId is not null && File.Exists(FilePath(id)))
        {
            var existing = Get(id);
            if (existing.ClientRequestId != clientRequestId
                || existing.Request?.Operation != definition.Operation
                || !JsonNode.DeepEquals(existing.Request?.Parameters, definition.Parameters))
            {
                throw new LauncherOperationException("JOB_REQUEST_IDENTITY_CONFLICT", "This request identity already belongs to a different durable job. Nothing was resubmitted.");
            }

            existing.AdditionalData.Remove("existingRequest");
            return existing with { ExistingRequest = true };
        }

        var resumable = definition.CanResume;
        var state = new JobStatus
        {
            Id = id,
            Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Request = new JobRequest
            {
                Operation = definition.Operation,
                Parameters = (JsonObject)definition.Parameters.DeepClone()
            },
            ResumedFrom = resumedFrom,
            CanResume = resumable,
            ClientRequestId = clientRequestId,
            RecoveryStrategy = resumable ? "replay-verified-files" : "inspect-effects-before-repeating"
        };
        if (resumedFrom is null) state.AdditionalData["resumedFrom"] = JsonSerializer.SerializeToElement((string?)null, ApplicationJsonContext.Default.String);
        if (clientRequestId is null) state.AdditionalData["clientRequestId"] = JsonSerializer.SerializeToElement((string?)null, ApplicationJsonContext.Default.String);
        await JsonFiles.WriteAsync(FilePath(id), JsonSerializer.SerializeToNode(state, ApplicationJsonContext.Job)!, ct).ConfigureAwait(false);
        await new LauncherActivityLog(paths).WriteAsync(resumedFrom is null ? "task.queued" : "task.resumed", definition.Parameters["instanceId"]?.ToString(), id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        return state;
    }

    public Task MarkDispatchedAsync(string id, int processId, string startUtc) => JsonFiles.WriteAsync(FilePath(id) + ".owner", new JsonObject { ["processId"] = processId, ["processStartUtc"] = startUtc });

    /// <summary>Creates a fresh attempt for a resumable request; the host executes it through RunAsync.</summary>
    public async Task<JobStatus> ResumeAsync(string id, CancellationToken cancellationToken = default)
    {
        var state = Get(id);
        if (state.Status is "running" or "queued" or "succeeded")
            return state;
        if (state.CanResume != true)
            throw new LauncherOperationException("JOB_NOT_RESUMABLE", "Inspect the previous task effects before explicitly starting another operation.", JsonSerializer.SerializeToNode(state, ApplicationJsonContext.Job));
        return await CreateAsync(ReadDefinition(GetRecord(id)), id, ct: cancellationToken).ConfigureAwait(false);
    }

    private static JobDefinition ReadDefinition(JsonObject state)
    {
        if (state["request"] is not JsonObject request
            || request["operation"] is not JsonValue operation || !operation.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name)
            || request["parameters"] is not JsonObject parameters)
        {
            throw new InvalidDataException("The job must contain a request with an operation and parameters object.");
        }

        return new JobDefinition(name, (JsonObject)parameters.DeepClone(), state["canResume"]?.GetValue<bool>() == true);
    }

    /// <summary>Requests cooperative cancellation without changing a terminal task or claiming it has stopped.</summary>
    public async Task<JobStatus> CancelAsync(string id, CancellationToken ct = default)
    {
        var state = Get(id);
        if (state.Status is "succeeded" or "failed" or "canceled" or "interrupted")
            return state;
        await File.WriteAllTextAsync(FilePath(id) + ".cancel", DateTimeOffset.UtcNow.ToString("O"), ct).ConfigureAwait(false);
        state.AdditionalData.Remove("cancelRequested");
        await new LauncherActivityLog(paths).WriteAsync("task.cancel-requested", state.Request?.Parameters?["instanceId"]?.ToString(), id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        return state with { CancelRequested = true };
    }

    public async Task<JsonNode?> RunAsync(string id, Func<JobDefinition, CancellationToken, Task<JsonNode?>> execute, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath(id), cancellationToken: ct).ConfigureAwait(false);
        var state = GetRecord(id);
        if (state["status"]?.GetValue<string>() == "succeeded")
            return state["result"]?.DeepClone();
        var instanceId = (state["request"] as JsonObject)?["parameters"] is JsonObject parameters
            ? parameters["instanceId"]?.ToString() : null;
        state["status"] = "running";
        state["processId"] = Environment.ProcessId;
        using var owner = Process.GetCurrentProcess();
        state["processStartUtc"] = owner.StartTime.ToUniversalTime().ToString("O");
        await JsonFiles.WriteAsync(FilePath(id), state, ct).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watching = new CancellationTokenSource();
        var watcher = WatchCancellationAsync(FilePath(id) + ".cancel", linked, watching.Token);
        try
        {
            if (File.Exists(FilePath(id) + ".cancel"))
                linked.Cancel();
            linked.Token.ThrowIfCancellationRequested();
            var result = await execute(ReadDefinition(state), linked.Token).ConfigureAwait(false);
            state["status"] = "succeeded";
            state["result"] = result?.DeepClone();
            state["finishedAt"] = DateTimeOffset.UtcNow.ToString("O");
            if (!await new LauncherActivityLog(paths).WriteAsync("task.succeeded", instanceId, id, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false))
                state["activityLogWarning"] = "ACTIVITY_LOG_UNAVAILABLE";
            await JsonFiles.WriteAsync(FilePath(id), state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            state["status"] = ex is OperationCanceledException ? "canceled" : "failed";
            state["error"] = ex.Message;
            state["failure"] = LauncherOperationException.ToResult(ex);
            state["finishedAt"] = DateTimeOffset.UtcNow.ToString("O");
            if (!await new LauncherActivityLog(paths).WriteAsync(ex is OperationCanceledException ? "task.canceled" : "task.failed", instanceId, id,
                code: state["failure"]?["error"]?["code"]?.ToString(), nativeError: ex.HResult, httpStatus: state["failure"]?["error"]?["httpStatus"]?.GetValue<int>(), cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false))
            {
                state["activityLogWarning"] = "ACTIVITY_LOG_UNAVAILABLE";
            }

            await JsonFiles.WriteAsync(FilePath(id), state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            watching.Cancel();
            await watcher.ConfigureAwait(false);
        }
    }

    private static async Task WatchCancellationAsync(string marker, CancellationTokenSource operation, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (File.Exists(marker))
                {
                    operation.Cancel();
                    return;
                }

                await Task.Delay(100, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private static bool HasOwnerIdentity(JsonObject state) => state["processId"] is JsonValue pid && pid.TryGetValue<int>(out var value) && value > 0 && state["processStartUtc"] is JsonValue start && start.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, out _);
    private static bool OwnerAlive(JsonObject state)
    {
        try
        {
            if (state["processId"]?.GetValue<int>() is not { } pid)
                return false;
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().ToString("O") == state["processStartUtc"]?.GetValue<string>();
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}