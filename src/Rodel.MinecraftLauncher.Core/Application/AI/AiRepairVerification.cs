// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using System.Diagnostics;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Observes a tagged verification run; never equates a window, silence or model text with game readiness.</summary>
internal sealed class AiRepairVerification(LauncherClient client, AiRepairService repairs)
{
    internal async Task<AiRepairPlanReport> RunAsync(AiRepairAction request, CancellationToken ct)
    {
        using var lease = await repairs.AcquireAsync(request.RepairId, ct).ConfigureAwait(false);
        var record = repairs.ReadDocument(request.RepairId); var plan = AiRepairService.Plan(record, request.PlanId);
        var instanceId = record["instanceId"]!.ToString();
        var sessionId = plan["verificationSessionId"]?.ToString();
        // A session is tagged before its monitor starts, so lost launch receipts can be observed without launching twice.
        var tagged = client.Sessions.List().Where(s => s.InstanceId == instanceId
            && s.AdditionalData.TryGetValue("repairPlanId", out var tag) && tag.GetString() == request.PlanId).ToArray();
        if (tagged.Length > 1) throw new LauncherOperationException("REPAIR_MULTIPLE_VERIFICATION_RUNS", "Inspect the recorded runs before continuing; no new game was launched.");
        sessionId ??= tagged.SingleOrDefault()?.Id;
        if (plan["outcome"] is not null) return AiRepairPlanReport.Read(plan.DeepClone().AsObject());
        if (plan["state"]?.ToString() is not ("applied" or "verifying"))
            throw new LauncherOperationException("REPAIR_NOT_APPLIED", "Only an applied trial can be verified; unknown writes must be inspected first.");
        if (sessionId is null)
        {
            if (plan["state"]?.ToString() == "verifying")
                throw new LauncherOperationException("REPAIR_LAUNCH_UNCERTAIN", "The launch was dispatched without a known session; inspect process/session state instead of replaying it.");
            client.EnsureInstanceIdle(instanceId);
            plan["state"] = "verifying"; record["status"] = "verifying";
            await repairs.SaveAsync(record, ct).ConfigureAwait(false);
            try
            {
                var origin = record["originSessionId"]?.ToString() is { } original ? client.Sessions.Get(original) : null;
                var started = await client.LaunchAsync(instanceId, new LaunchOptions
                {
                    Offline = true,
                    InheritServer = false,
                    AccountId = origin?.AccountId,
                    RepairPlanId = request.PlanId
                }, ct).ConfigureAwait(false);
                sessionId = (started as SessionStatus)?.Id
                    ?? throw new InvalidDataException("Repair verification did not return an actual game session.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                sessionId = client.Sessions.List().SingleOrDefault(s => s.InstanceId == instanceId
                    && s.AdditionalData.TryGetValue("repairPlanId", out var tag) && tag.GetString() == request.PlanId)?.Id;
                if (sessionId is null)
                {
                    plan["launchError"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone();
                    record["status"] = "needs_information";
                    await repairs.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }

            plan["verificationSessionId"] = sessionId;
            await repairs.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
        }

        var clock = Stopwatch.StartNew(); DateTimeOffset? initializedSince = null;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var session = client.Sessions.Get(sessionId);
                if (session.InstanceId != instanceId || sessionId == record["originSessionId"]?.ToString())
                    throw new LauncherOperationException("REPAIR_SESSION_SCOPE", "Verification cannot reuse the original failure or another instance's run.");
                var diagnosis = await client.Maintenance.DiagnoseSessionAsync(sessionId, ct).ConfigureAwait(false);
                plan["diagnosis"] = JsonSerializer.SerializeToNode(diagnosis, ApplicationJsonContext.Default.SessionDiagnosis);
                var logs = diagnosis.LogExcerpt is { } excerpt ? string.Join("\n", excerpt.Stdout.Concat(excerpt.Stderr)) : string.Empty;
                var renderInitialized = logs.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase)
                    && logs.Contains("atlas", StringComparison.OrdinalIgnoreCase) && logs.Contains("Created:", StringComparison.OrdinalIgnoreCase);
                var fatal = diagnosis.ReportedErrors.Any(error => error.Level == "FATAL");
                if (session.Status is "exited" or "failed" or "interrupted")
                {
                    var failed = session.Status != "exited" || session.ExitCode != 0;
                    var original = new AiRepairEvidenceReader(record["originalDiagnosis"]!.AsObject());
                    var originalCode = original.ProblemCode;
                    var signature = original.FailureSignature;
                    var same = failed && originalCode is not (null or "UNCLASSIFIED") && diagnosis.Problem?.Code == originalCode
                        && signature.Length > 0 && string.Equals(signature, AiRepairEvidenceReader.Signature(diagnosis.Problem), StringComparison.OrdinalIgnoreCase);
                    plan["originalFaultObserved"] = same;
                    plan["outcome"] = failed ? same ? "original_failure" : "different_failure" : "inconclusive";
                    plan["state"] = "observed"; record["status"] = failed ? "failed" : "needs_information";
                    if (same) await repairs.RestoreCoreAsync(record, plan, ct).ConfigureAwait(false);
                    break;
                }

                if (session.Readiness == "window_visible" && renderInitialized && !fatal)
                {
                    initializedSince ??= DateTimeOffset.UtcNow;
                    if (DateTimeOffset.UtcNow - initializedSince >= TimeSpan.FromSeconds(10))
                    {
                        plan["outcome"] = "startup_checked"; plan["state"] = "observed"; record["status"] = "startup_checked";
                        plan["verificationScope"] = "Observed window plus sound/texture initialization and ten seconds of process survival. Menu, world entry and original in-world actions are not verified. Use ask_user only for missing reproduction conditions; no game-world automation is available.";
                        break;
                    }
                }
                else
                {
                    initializedSince = null;
                }

                if (clock.Elapsed >= TimeSpan.FromSeconds(90))
                {
                    plan["outcome"] = "inconclusive"; plan["state"] = "observed"; record["status"] = "needs_information";
                    plan["verificationScope"] = "Initialization could not be established within the observation budget. Process/window existence alone does not prove success.";
                    break;
                }

                await repairs.SaveAsync(record, ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }

            await repairs.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
            return AiRepairPlanReport.Read(plan.DeepClone().AsObject());
        }
        catch (OperationCanceledException)
        {
            // This is the exact game started for verification, never an unrelated player session.
            plan["state"] = "verifying"; record["status"] = "paused";
            await repairs.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

}