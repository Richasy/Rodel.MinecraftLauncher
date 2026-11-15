// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Owns incident evidence, frozen reversible changes and write receipts shared by App and CLI/MCP.</summary>
public sealed class AiRepairService(LauncherClient client)
{
    private string Root => Path.Combine(client.Paths.Root, "ai", "repairs");
    private string RecordPath(string id) => Path.Combine(Root, AiChatStore.CheckId(id) + ".json");
    private Task<ResourceLease> LockAsync(string id, CancellationToken ct) => ResourceLease.AcquireAsync(RecordPath(id), ct);

    /// <summary>Captures the original evidence once and reuses the same incident on repeated entry, even before a model is configured.</summary>
    public async Task<AiRepairReport> CreateAsync(AiRepairCreate request, CancellationToken ct = default)
    {
        if (request.SessionId is null && request.JobId is null) throw new ArgumentException("Select a recorded failing run or preparation task.");
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.InstanceId + "\0" + request.SessionId + "\0" + (request.SessionId is null ? request.JobId : null))))[..32];
        using var lease = await LockAsync(id, ct).ConfigureAwait(false);
        if (File.Exists(RecordPath(id))) return Read(id);
        var diagnosis = await client.Maintenance.DiagnoseLaunchAsync(request.InstanceId, request.SessionId, request.SessionId is null ? request.JobId : null, ct).ConfigureAwait(false);
        if (diagnosis.GameActive) throw new LauncherOperationException("REPAIR_GAME_ACTIVE", "Close the affected game before starting repair; no process was stopped.");
        if (request.SessionId is { } sessionId)
        {
            var session = client.Sessions.Get(sessionId);
            if (session.InstanceId != request.InstanceId || session.Status is not ("exited" or "failed" or "interrupted")
                || (session.Status == "exited" && session.ExitCode == 0))
            {
                throw new LauncherOperationException("REPAIR_NOT_A_FAILURE", "Repair needs an abnormal run belonging to this exact instance.");
            }
        }
        else if (client.Jobs.Get(request.JobId!).Status is not ("failed" or "interrupted"))
        {
            throw new LauncherOperationException("REPAIR_NOT_A_FAILURE", "The preparation task is not a recorded failure.");
        }

        var record = new JsonObject
        {
            ["id"] = id,
            ["instanceId"] = request.InstanceId,
            ["originSessionId"] = request.SessionId,
            ["originJobId"] = request.JobId,
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = "ready",
            ["authorization"] = "reversible_instance_repair",
            ["originalDiagnosis"] = JsonSerializer.SerializeToNode(diagnosis, DiagnosticJsonContext.Default.ILaunchDiagnosis),
            ["originalInventory"] = await InventoryAsync(request.InstanceId, ct).ConfigureAwait(false),
            ["originalSettings"] = client.Settings.GetOverrides(client.Instances.Get(request.InstanceId)),
            ["plans"] = new JsonArray(),
            ["scope"] = "Instance mod disable, existing Java, heap settings and add-only resolved dependencies. No worlds, account changes, arbitrary files, bulk updates or game-world automation."
        };
        await SaveAsync(record, ct).ConfigureAwait(false);
        return AiRepairReport.Read(record);
    }

    /// <summary>Reads durable evidence and trials, without executing or inferring the outcome of interrupted work.</summary>
    public AiRepairReport Read(string id) => AiRepairReport.Read(ReadDocument(id));

    internal JsonObject ReadDocument(string id) => JsonFiles.ReadObject(RecordPath(id));

    internal async Task RecordResultAsync(string repairId, JsonObject result, CancellationToken ct)
    {
        if (result["data"]?["continuation"]?["tool"]?.ToString() != "result_read"
            || result["data"]?["resultId"]?.ToString() is not { } resultId)
        {
            return;
        }

        using var lease = await LockAsync(repairId, ct).ConfigureAwait(false);
        var record = ReadDocument(repairId); var ids = record["resultIds"] as JsonArray ?? [];
        if (!ids.Any(id => id?.ToString() == resultId)) ids.Add((JsonNode?)JsonValue.Create(resultId));
        if (record["resultIds"] is null) record["resultIds"] = ids;
        await SaveAsync(record, ct).ConfigureAwait(false);
    }

    /// <summary>Reads original incident evidence alongside current instance settings, mod declarations and usable local Java candidates.</summary>
    public async Task<AiRepairReport> InspectAsync(string id, CancellationToken ct = default)
    {
        var report = Read(id); var instance = client.Instances.Get(report.InstanceId!);
        var inventory = await InventoryAsync(instance.Id, ct).ConfigureAwait(false);
        var effective = client.Settings.GetEffective(instance).Values;
        var analysis = await client.AnalyzeContentAsync(instance.Id, ct).ConfigureAwait(false);
        var java = await client.Java.DiscoverAsync(effective["javaPath"]?.ToString(), instance.MinecraftDirectory, ct).ConfigureAwait(false);
        return report with
        {
            CurrentInventory = [.. inventory.OfType<JsonObject>().Select(AiRepairContentFile.Read)],
            EffectiveSettings = effective,
            ModAnalysis = analysis,
            JavaCandidates = [.. java.Where(j => j.IsUsable).Select(j => new AiRepairJavaCandidate(j.Path, j.MajorVersion))],
            AdditionalData = report.AdditionalData?.Where(pair => pair.Key is not ("currentInventory" or "effectiveSettings" or "modAnalysis" or "javaCandidates"))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
    }

    /// <summary>Freezes a single minimal change against current settings and content; rejects stale or unresolved earlier effects.</summary>
    public async Task<AiRepairPlanReport> PlanAsync(AiRepairPlan request, CancellationToken ct = default)
    {
        if (!AiRepairPlan.Kinds.Contains(request.Kind, StringComparer.Ordinal))
            throw new ArgumentException("Repair kind must be one of: " + string.Join(", ", AiRepairPlan.Kinds) + ".");
        using var lease = await LockAsync(request.RepairId, ct).ConfigureAwait(false);
        var record = ReadDocument(request.RepairId);
        var instanceId = record["instanceId"]!.ToString();
        client.EnsureInstanceIdle(instanceId);
        var plans = record["plans"]!.AsArray();
        if (plans.OfType<JsonObject>().Any(p => p["state"]?.ToString() is "applying" or "applied" or "verifying" or "unknown_effect" or "restoring"))
            throw new LauncherOperationException("REPAIR_PREVIOUS_TRIAL_PENDING", "Inspect, verify or restore the previous trial before preparing another change.");
        if (plans.Count >= 6) throw new LauncherOperationException("REPAIR_TRIAL_LIMIT", "Stop changing this instance: the incident reached its bounded trial limit. Ask for missing evidence instead.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000) throw new ArgumentException("Provide a bounded evidence-based explanation.");
        var inventory = await InventoryAsync(instanceId, ct).ConfigureAwait(false);
        var settings = client.Settings.GetOverrides(client.Instances.Get(instanceId));
        var effective = client.Settings.GetEffective(client.Instances.Get(instanceId)).Values;
        var evidence = record["originalDiagnosis"]!.AsObject();
        var latest = new AiRepairEvidenceReader(plans.OfType<JsonObject>().LastOrDefault()?["diagnosis"] as JsonObject ?? evidence);
        var plan = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["kind"] = request.Kind,
            ["reason"] = request.Reason,
            ["state"] = "planned",
            ["beforeInventory"] = inventory,
            ["beforeSettings"] = settings,
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };
        plan["beforeEffectiveSettings"] = effective;
        plan["versionId"] = client.Instances.Get(instanceId).VersionId;
        switch (request.Kind)
        {
            case "disable_mod":
                var file = inventory.OfType<JsonObject>().SingleOrDefault(f => f["fileName"]?.ToString() == request.Target)
                    ?? throw new ArgumentException("Select one actual installed mod file name.");
                if (file["enabled"]?.GetValue<bool>() != true || file["sha1"] is null || file["directory"]?.GetValue<bool>() == true)
                    throw new LauncherOperationException("REPAIR_MOD_UNSUITABLE", "Only an enabled, readable mod archive can be tried.");
                var analysis = await client.AnalyzeContentAsync(instanceId, ct).ConfigureAwait(false);
                var names = analysis.Mods.Where(m => m.FileName == request.Target).Select(m => m.Id).Append(Path.GetFileNameWithoutExtension(request.Target!));
                if (!names.Any(n => n.Length >= 3 && latest.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    throw new LauncherOperationException("REPAIR_MOD_EVIDENCE_REQUIRED", "The observed loader/crash evidence must identify this mod; unrelated mods cannot be disabled by trial enumeration.");
                plan["target"] = file.DeepClone();
                break;
            case "java":
                if (!latest.Contains("JAVA_VERSION_MISMATCH")) throw new LauncherOperationException("REPAIR_JAVA_EVIDENCE_REQUIRED", "No Java mismatch was observed.");
                var javaInstance = client.Instances.Get(instanceId);
                var available = await client.Java.DiscoverAsync(client.Settings.GetEffective(javaInstance).Values["javaPath"]?.ToString(), javaInstance.MinecraftDirectory, ct).ConfigureAwait(false);
                var java = available.FirstOrDefault(j => string.Equals(j.Path, request.Target, StringComparison.OrdinalIgnoreCase))
                    ?? throw new LauncherOperationException("REPAIR_JAVA_NOT_DISCOVERED", "Select an executable returned by the host's Java discovery; model-supplied programs are not probed or executed.");
                var required = latest.RequiredJavaMajor;
                if (!java.IsUsable || required is null || java.MajorVersion < required) throw new LauncherOperationException("REPAIR_JAVA_UNSUITABLE", "Select usable Java satisfying the observed class-version requirement.");
                plan["changes"] = new JsonObject { ["javaPath"] = java.Path };
                break;
            case "memory":
                var code = latest.ProblemCode;
                var current = client.Settings.GetEffective(client.Instances.Get(instanceId)).Values["maxMemoryMb"]?.GetValue<int>() ?? 2048;
                if (code is not ("JAVA_OUT_OF_MEMORY" or "JAVA_MEMORY_RESERVATION") || request.MemoryMb is not { } mb
                    || mb < 512 || mb > 16384 || (code == "JAVA_OUT_OF_MEMORY" ? mb <= current || mb > current + 2048 : mb >= current))
                {
                    throw new LauncherOperationException("REPAIR_MEMORY_UNSUITABLE", "Use a bounded heap adjustment in the direction supported by the actual memory error.");
                }

                plan["changes"] = new JsonObject { ["maxMemoryMb"] = mb, ["memoryMode"] = "manual" };
                if (effective["jvmArguments"] is JsonArray jvm && jvm.Any(a => a?.ToString().StartsWith("-Xmx", StringComparison.Ordinal) == true))
                {
                    plan["changes"]!["jvmArguments"] = new JsonArray([.. jvm.Select(a => (JsonNode?)JsonValue.Create(
                        a!.ToString().StartsWith("-Xmx", StringComparison.Ordinal) ? "-Xmx" + mb + "M"
                        : a.ToString().StartsWith("-Xms", StringComparison.Ordinal) ? "-Xms" + Math.Min(effective["minMemoryMb"]?.GetValue<int>() ?? 512, mb) + "M" : a.ToString()))]);
                }

                if ((client.Settings.GetEffective(client.Instances.Get(instanceId)).Values["minMemoryMb"]?.GetValue<int>() ?? 512) > mb)
                    plan["changes"]!["minMemoryMb"] = mb;
                break;
            case "dependencies":
                var source = plans.OfType<JsonObject>().LastOrDefault()?["verificationSessionId"]?.ToString() ?? record["originSessionId"]?.ToString()
                    ?? throw new LauncherOperationException("REPAIR_SESSION_REQUIRED", "Dependency repair requires loader evidence from a game run.");
                var review = await client.PlanDependencyRepairAsync(instanceId, source, cancellationToken: ct).ConfigureAwait(false);
                if (!review.Plan.CanApply || review.Unresolved.Count > 0)
                {
                    throw new LauncherOperationException("REPAIR_DEPENDENCIES_UNRESOLVED", "The exact dependency group cannot be resolved safely; no files were installed. Inspect unresolved entries before proceeding. Changing target or reason cannot change this automatic group; do not repeat an unchanged failed plan.",
                        JsonSerializer.SerializeToNode(review, ContentJsonContext.DependencyReview));
                }
                // This incident grant permits additions, never replacement of existing player-selected content.
                EnsureAddOnlyDependencies(review.Plan.Nodes);
                plan["dependencyReview"] = JsonSerializer.SerializeToNode(review, ContentJsonContext.DependencyReview);
                break;
            default: throw new ArgumentException("Unknown repair kind.");
        }

        var signature = request.Kind + "\0" + plan["target"] + "\0" + plan["changes"] + "\0" + plan["dependencyReview"]?["inventoryFingerprint"];
        if (plans.OfType<JsonObject>().Any(p => p["signature"]?.ToString() == signature && p["state"]?.ToString() != "planned"))
            throw new LauncherOperationException("REPAIR_TRIAL_ALREADY_ATTEMPTED", "This exact candidate was already attempted; inspect its outcome instead of repeating it.");
        plan["signature"] = signature;
        plans.Add((JsonNode)plan); record["status"] = "planned";
        await SaveAsync(record, ct).ConfigureAwait(false);
        return AiRepairPlanReport.Read(plan.DeepClone().AsObject());
    }

    /// <summary>Consumes one frozen plan before dispatch and records actual write-after state; an uncertain receipt is never replayed.</summary>
    public async Task<AiRepairPlanReport> ApplyAsync(AiRepairAction request, CancellationToken ct = default)
    {
        using var lease = await LockAsync(request.RepairId, ct).ConfigureAwait(false);
        var record = ReadDocument(request.RepairId); var plan = Plan(record, request.PlanId); var id = record["instanceId"]!.ToString();
        if (plan["state"]?.ToString() != "planned") return AiRepairPlanReport.Read(plan.DeepClone().AsObject());
        client.EnsureInstanceIdle(id);
        if (!JsonNode.DeepEquals(plan["beforeInventory"], await InventoryAsync(id, ct).ConfigureAwait(false))
            || !JsonNode.DeepEquals(plan["beforeSettings"], client.Settings.GetOverrides(client.Instances.Get(id)))
            || !JsonNode.DeepEquals(plan["beforeEffectiveSettings"], client.Settings.GetEffective(client.Instances.Get(id)).Values)
            || plan["versionId"]?.ToString() != client.Instances.Get(id).VersionId)
        {
            throw new LauncherOperationException("REPAIR_PLAN_STALE", "The instance changed since planning. Preserve those edits and prepare a new plan.");
        }

        plan["state"] = "applying"; record["status"] = "applying";
        await SaveAsync(record, ct).ConfigureAwait(false);
        try
        {
            switch (plan["kind"]!.ToString())
            {
                case "disable_mod":
                    await client.Content.SetEnabledCheckedAsync(client.Instances.Get(id).GameDirectory, plan["target"]!["fileName"]!.ToString(), false, plan["target"]!["sha1"]!.ToString(), ct).ConfigureAwait(false);
                    break;
                case "java":
                case "memory":
                    await client.Settings.UpdateCheckedAsync(plan["changes"]!.AsObject(), [], client.Instances.Get(id), plan["beforeSettings"]!.AsObject(), ct).ConfigureAwait(false);
                    break;
                case "dependencies":
                    await client.ApplyDependencyRepairAsync(id, plan["dependencyReview"]!["id"]!.ToString(), ct).ConfigureAwait(false);
                    break;
            }

            plan["afterInventory"] = await InventoryAsync(id, CancellationToken.None).ConfigureAwait(false);
            plan["afterSettings"] = client.Settings.GetOverrides(client.Instances.Get(id));
            if (plan["changes"] is JsonObject expectedChanges && expectedChanges.Any(change => !JsonNode.DeepEquals(change.Value, plan["afterSettings"]![change.Key])))
                throw new LauncherOperationException("REPAIR_READBACK_MISMATCH", "Written settings did not match the frozen plan; inspect actual state before continuing.");
            if (plan["kind"]?.ToString() == "disable_mod" && !plan["afterInventory"]!.AsArray().OfType<JsonObject>().Any(f =>
                f["fileName"]?.ToString() == plan["target"]!["fileName"] + ".disabled" && f["sha1"]?.ToString() == plan["target"]!["sha1"]?.ToString() && f["enabled"]?.GetValue<bool>() == false))
            {
                throw new LauncherOperationException("REPAIR_READBACK_MISMATCH", "The disabled mod could not be read back with its original contents.");
            }

            plan["state"] = "applied"; record["status"] = "applied";
            await SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
            return AiRepairPlanReport.Read(plan.DeepClone().AsObject());
        }
        catch (Exception error)
        {
            var rejected = error is LauncherOperationException { Code: "REPAIR_MOD_CHANGED" or "REPAIR_PLAN_STALE" or "DEPENDENCY_REVIEW_STALE" };
            plan["state"] = rejected ? "stale" : "unknown_effect"; record["status"] = rejected ? "needs_information" : "unknown_effect";
            plan["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone();
            await SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Restores only unchanged trial-owned targets. User edits, changed archives and active games stop rollback.</summary>
    public async Task<AiRepairPlanReport> RestoreAsync(AiRepairAction request, CancellationToken ct = default)
    {
        using var lease = await LockAsync(request.RepairId, ct).ConfigureAwait(false);
        var record = ReadDocument(request.RepairId); var plan = Plan(record, request.PlanId);
        await RestoreCoreAsync(record, plan, ct).ConfigureAwait(false);
        return AiRepairPlanReport.Read(plan.DeepClone().AsObject());
    }

    internal async Task RestoreCoreAsync(JsonObject record, JsonObject plan, CancellationToken ct)
    {
        if (plan["state"]?.ToString() == "restored") return;
        if (plan["afterInventory"] is null || plan["afterSettings"] is null || plan["state"]?.ToString() is "applying" or "restoring" or "unknown_effect")
            throw new LauncherOperationException("REPAIR_EFFECT_UNKNOWN", "A committed write-after receipt is required before restoring; inspect actual state without replaying the mutation.");
        var id = record["instanceId"]!.ToString();
        if (plan["verificationSessionId"]?.ToString() is { } verificationId)
        {
            var verification = client.Sessions.Get(verificationId);
            if (verification.InstanceId == id && verification.Status == "running"
                && verification.AdditionalData.TryGetValue("repairPlanId", out var tag) && tag.GetString() == plan["id"]!.ToString())
            {
                client.Sessions.Stop(verificationId);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (client.Sessions.Get(verificationId).Status == "running" && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(200, ct).ConfigureAwait(false);
            }
        }

        client.EnsureInstanceIdle(id);
        var current = await InventoryAsync(id, ct).ConfigureAwait(false);
        var before = plan["beforeInventory"]!.AsArray(); var after = plan["afterInventory"]!.AsArray();
        if (plan["kind"]?.ToString() == "disable_mod")
        {
            var name = plan["target"]!["fileName"]!.ToString() + ".disabled";
            var expected = after.OfType<JsonObject>().Single(f => f["fileName"]?.ToString() == name);
            if (!JsonNode.DeepEquals(expected, current.OfType<JsonObject>().SingleOrDefault(f => f["fileName"]?.ToString() == name)))
                throw new LauncherOperationException("REPAIR_ROLLBACK_CONFLICT", "The trial mod changed after repair; preserve it and inspect the conflict.");
        }
        else if (plan["kind"]?.ToString() == "dependencies" && !JsonNode.DeepEquals(current, after))
        {
            throw new LauncherOperationException("REPAIR_ROLLBACK_CONFLICT", "Content changed after dependency repair; automatic rollback would risk player edits.");
        }

        var old = plan["beforeSettings"]!.AsObject(); var written = plan["afterSettings"]!.AsObject();
        var keys = old.Select(p => p.Key).Union(written.Select(p => p.Key)).Where(k => old.ContainsKey(k) != written.ContainsKey(k) || !JsonNode.DeepEquals(old[k], written[k])).ToArray();
        var actual = client.Settings.GetOverrides(client.Instances.Get(id));
        foreach (var key in keys)
        {
            if (actual.ContainsKey(key) != written.ContainsKey(key) || !JsonNode.DeepEquals(actual[key], written[key]))
                throw new LauncherOperationException("REPAIR_ROLLBACK_CONFLICT", "A trial-owned setting was subsequently edited; it was preserved.");
        }

        plan["state"] = "restoring"; record["status"] = "restoring"; await SaveAsync(record, ct).ConfigureAwait(false);
        try
        {
            if (plan["kind"]?.ToString() == "disable_mod")
            {
                await client.Content.SetEnabledCheckedAsync(client.Instances.Get(id).GameDirectory, plan["target"]!["fileName"] + ".disabled", true, plan["target"]!["sha1"]!.ToString(), ct).ConfigureAwait(false);
            }
            else if (plan["kind"]?.ToString() == "dependencies")
            {
                foreach (var added in after.OfType<JsonObject>().Where(f => !before.OfType<JsonObject>().Any(b => b["fileName"]?.ToString() == f["fileName"]?.ToString())))
                    await client.Content.RemoveCheckedAsync(client.Instances.Get(id).GameDirectory, added["fileName"]!.ToString(), added["sha1"]!.ToString(), ct).ConfigureAwait(false);
            }

            if (keys.Length > 0)
            {
                var changes = new JsonObject(); foreach (var key in keys.Where(old.ContainsKey)) changes[key] = old[key]?.DeepClone();
                await client.Settings.UpdateCheckedAsync(changes, [.. keys.Where(k => !old.ContainsKey(k))], client.Instances.Get(id), actual, ct).ConfigureAwait(false);
            }

            plan["state"] = "restored"; record["status"] = "restored";
            await SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            plan["state"] = "unknown_effect"; record["status"] = "unknown_effect";
            await SaveAsync(record, CancellationToken.None).ConfigureAwait(false); throw;
        }
    }

    /// <summary>Starts at most one verification run for a consumed plan and observes its own initialization/exit evidence.</summary>
    public Task<AiRepairPlanReport> VerifyAsync(AiRepairAction request, CancellationToken ct = default) => new AiRepairVerification(client, this).RunAsync(request, ct);

    internal static JsonObject Plan(JsonObject record, string planId) => record["plans"]!.AsArray().OfType<JsonObject>().SingleOrDefault(p => p["id"]?.ToString() == planId)
        ?? throw new ArgumentException("The plan does not belong to this repair incident.");
    /// <summary>Rejects replacement candidates using the content planner's canonical change field, never extension metadata.</summary>
    internal static void EnsureAddOnlyDependencies(IReadOnlyList<ContentPlanNode> nodes)
    {
        if (nodes.Any(node => node.Change is "replace" or "update"))
            throw new LauncherOperationException("REPAIR_DEPENDENCIES_REPLACE", "This dependency group requires replacing content outside the add-only repair scope.");
    }

    internal Task SaveAsync(JsonObject record, CancellationToken ct) => JsonFiles.WriteAsync(RecordPath(record["id"]!.ToString()), record, ct);
    internal Task<ResourceLease> AcquireAsync(string id, CancellationToken ct) => LockAsync(id, ct);
    private async Task<JsonArray> InventoryAsync(string id, CancellationToken ct)
    {
        var files = await client.ListContentAsync(id, cancellationToken: ct).ConfigureAwait(false);
        return new JsonArray([.. files.OrderBy(f => f.FileName, StringComparer.Ordinal).Select(f => (JsonNode)new JsonObject
        {
            ["fileName"] = f.FileName, ["sha1"] = f.Sha1, ["enabled"] = f.Enabled, ["directory"] = f.Directory, ["tracking"] = f.Tracking?.DeepClone()
        })]);
    }
}