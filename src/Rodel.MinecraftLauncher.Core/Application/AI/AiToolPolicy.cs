// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiToolPolicy(LauncherPaths paths, JsonObject turn)
{
    internal static readonly string[] RepairTools = ["repair_show", "repair_plan", "repair_apply", "repair_verify", "repair_restore", "result_read"];
    private bool _safeOperationAuthorized;
    private bool OperationAllowed => turn["mode"]?.ToString() == "allow_operations"
        || (turn["mode"]?.ToString() == "safe" && _safeOperationAuthorized);
    private bool AuthorizationCurrent(JsonObject current) =>
        (current["operationRevision"]?.GetValue<long>() ?? 0) == (turn["operationRevision"]?.GetValue<long>() ?? 0)
        && (turn["turnAuthorization"]?.ToString() == "explicit_turn" || current["mode"]?.ToString() == "allow_operations"
            || (_safeOperationAuthorized && turn["mode"]?.ToString() == "safe" && current["mode"]?.ToString() == "safe"));

    internal async Task AuthorizeOperationAsync(string name, JsonObject arguments, string invocation,
        Func<string, string?, JsonObject, Task> emit, CancellationToken ct,
        AiToolReview? review = null, Func<CancellationToken, Task<AiToolReview>>? recheck = null)
    {
        _safeOperationAuthorized = false;
        if (turn["mode"]?.ToString() != "safe") return;
        JsonObject Current(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = new AiChatStore(paths).GetDocument(turn["conversationId"]!.ToString());
            if (current["mode"]?.ToString() != "safe"
                || (current["operationRevision"]?.GetValue<long>() ?? 0) != (turn["operationRevision"]?.GetValue<long>() ?? 0))
            {
                throw new LauncherOperationException("AI_OPERATION_AUTHORIZATION_REVOKED", "Operation permission changed before this call was approved.");
            }

            return current;
        }

        var current = Current(ct);
        if (SkinMutationTools.Contains(name, StringComparer.Ordinal)
            && turn["skinScopeId"]?.GetValue<string>() is { } skinScopeId
            && new Skins.SkinService(paths).Proposals.ReadScope(skinScopeId).Limits.DirectEditing)
        {
            // The editor's whole-document grant already authorizes these tools. Keep the normal live
            // binding/revocation checks; do not mint a general chat, file or launcher operation grant.
            _safeOperationAuthorized = true;
            Check(name, arguments, ct);
            return;
        }

        var revision = turn["operationRevision"]?.GetValue<long>() ?? 0;
        if (current["operationGrantRevision"]?.GetValue<long>() != revision
            && current["operationToolGrants"]?[name]?.GetValue<long>() != revision)
        {
            var frozen = review ?? new AiOperationPermissionReview(name, arguments.DeepClone().AsObject());
            await new AiToolApprovals(paths).WaitAsync(turn, invocation, name, arguments, frozen,
                recheck ?? (_ => Task.FromResult(frozen)), token => { _ = Current(token); return Task.CompletedTask; },
                emit, ct, operationPermission: true).ConfigureAwait(false);
        }

        _ = Current(ct);
        _safeOperationAuthorized = true;
    }
    public static readonly string[] ReadTools =
        ["capabilities", "context", "app_list", "app_show", "app_wallpapers", "app_cache", "instance_list", "instance_show", "config_get", "runtime_show",
        "runtime_operations", "runtime_operation_show", "game_settings_get", "resourcepack_list", "shaderpack_list",
        "content_search", "content_list", "content_plan", "content_review_updates", "content_review_install",
        "content_review_show", "job_list", "job_show", "diagnose", "launch_plan", "process_list", "read_logs", "result_read", "result_image_read",
        "game_bridge_show", "game_snapshot", "game_region", "game_query", "game_operation_show"];
    internal static readonly string[] SkinReadTools =
        ["skin_inspect", "skin_read_face", "skin_pick", "skin_select", "skin_capture", "skin_history", "skin_footprint", "skin_templates", "skin_proposal_show", "skin_receipt"];
    private static readonly string[] SkinMutationTools = ["skin_propose", "skin_proposal_reject", "skin_proposal_preview"];
    private static readonly string[] MutationTools =
        ["repair_apply", "repair_verify", "repair_restore", "execute", "app_control", "config_set", "config_patch", "content_apply_review", "runtime_change", "runtime_rollback",
        "game_settings_set", "game_settings_apply", "game_settings_discard_pending", "resourcepack_select", "shaderpack_select",
        "instance_create", "job_cancel", "launch", "process_stop", "game_edit", "game_operation_cancel"];
    private static readonly string[] PlanTools = ["runtime_change", "game_settings_set", "resourcepack_select", "shaderpack_select"];
    private static readonly string[] FileTools =
        ["game_settings_get", "resourcepack_list", "shaderpack_list", "shaderpack_select", "content_list", "content_plan", "content_review_updates",
        "content_review_install", "content_review_show", "content_apply_review", "diagnose", "launch_plan", "read_logs",
        "runtime_change", "runtime_rollback", "game_settings_set", "game_settings_apply", "game_settings_discard_pending",
        "resourcepack_select", "launch"];
    private static readonly string[] NetworkTools =
        ["content_search", "content_plan", "content_review_updates", "content_review_install"];

    /// <summary>Suppresses definitions whose required capability or broad resource scope is unavailable.</summary>
    internal bool CanDiscover(string name)
    {
        if (FileTools.Contains(name, StringComparer.Ordinal) && turn["fileAccessEnabled"]?.GetValue<bool>() != true) return false;
        if (NetworkTools.Contains(name, StringComparer.Ordinal) && turn["networkAccessEnabled"]?.GetValue<bool>() != true) return false;
        return !ToolCatalog.TaskNames.Contains(name, StringComparer.Ordinal) || (turn["instanceId"] is null
            && turn["fileAccessEnabled"]?.GetValue<bool>() == true && turn["networkAccessEnabled"]?.GetValue<bool>() == true);
    }

    public static bool IsMutation(string name, JsonObject? arguments = null)
        => (MutationTools.Contains(name, StringComparer.Ordinal) || SkinMutationTools.Contains(name, StringComparer.Ordinal) || ToolCatalog.TaskNames.Contains(name, StringComparer.Ordinal))
            && !(PlanTools.Contains(name, StringComparer.Ordinal) && arguments?["plan"]?.GetValue<bool>() == true);
    public static bool CanOffer(string name, bool operations)
        => name is "repair_show" or "repair_plan" || ReadTools.Contains(name, StringComparer.Ordinal) || SkinReadTools.Contains(name, StringComparer.Ordinal) || PlanTools.Contains(name, StringComparer.Ordinal)
            || (operations && IsMutation(name));

    public void Normalize(string name, JsonObject arguments)
    {
        if (name == "app_control")
        {
            var request = arguments["request"]?.Deserialize(AppControlJsonContext.Default.AppControlRequest)
                ?? throw new ArgumentException("A typed App request is required.");
            if (!Enum.IsDefined(request.Action) || request.Action is AppControlAction.State or AppControlAction.WallpaperList or AppControlAction.ImageCacheStatus)
                throw new ArgumentException("Use the read-only App tools for observations.");
            arguments["request"] = JsonSerializer.SerializeToNode(request, AppControlJsonContext.Default.AppControlRequest);
        }

        if (name == "diagnose" && arguments["reportPath"]?.GetValue<string>() is { } report && !Path.IsPathFullyQualified(report))
        {
            var id = arguments["instanceId"]?.GetValue<string>()
                ?? throw new LauncherOperationException("AI_FILE_SCOPE_REQUIRED", "A relative report path needs an explicit instance.");
            arguments["reportPath"] = Path.GetFullPath(report, new LauncherClient(paths).Instances.Get(id).GameDirectory);
        }
    }

    internal bool ObserveGameSnapshot(JsonObject arguments, JsonObject result)
    {
        if (result["status"]?.ToString() != "succeeded" || result["data"] is not JsonObject data) return false;
        var snapshot = data.Deserialize(GameBridgeJsonContext.Default.GameSnapshot);
        if (snapshot is not { Ready: true, WorldEpoch: not null, Dimension: not null } || snapshot.SessionId != arguments["sessionId"]?.ToString()) return false;
        var bindings = turn["gameWorldBindings"] as JsonObject ?? [];
        if (bindings[snapshot.SessionId] is JsonObject previous)
        {
            if (snapshot.Mode != "local_owned" || snapshot.LocalWorldEpoch is null || previous["localWorldEpoch"]?.ToString() != snapshot.LocalWorldEpoch
                || previous["gameIncarnation"]?.ToString() != snapshot.GameIncarnation)
            {
                return false;
            }

            if (previous["worldEpoch"]?.ToString() == snapshot.WorldEpoch && previous["dimension"]?.ToString() == snapshot.Dimension) return false;
        }

        bindings[snapshot.SessionId] = new JsonObject
        {
            ["gameIncarnation"] = snapshot.GameIncarnation,
            ["worldEpoch"] = snapshot.WorldEpoch,
            ["localWorldEpoch"] = snapshot.Mode == "local_owned" ? snapshot.LocalWorldEpoch : null,
            ["dimension"] = snapshot.Dimension
        };
        if (turn["gameWorldBindings"] is null) turn["gameWorldBindings"] = bindings;
        return true;
    }

    public void RequireNetwork()
    {
        var current = new AiChatStore(paths).GetDocument(turn["conversationId"]!.ToString());
        if (turn["networkAccessEnabled"]?.GetValue<bool>() != true || current["networkAccessEnabled"]?.GetValue<bool>() == false)
            throw new LauncherOperationException("AI_NETWORK_ACCESS_DISABLED", "Assistant network retrieval is disabled for this conversation; model API access is independent.");
    }

    public void CheckManagedFile(bool write, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = new AiChatStore(paths).GetDocument(turn["conversationId"]!.ToString());
        if (turn["fileAccessEnabled"]?.GetValue<bool>() != true || current["fileAccessEnabled"]?.GetValue<bool>() == false)
            throw new LauncherOperationException("AI_FILE_ACCESS_DISABLED", "File access is disabled for this conversation.");
        if (write)
        {
            if (!OperationAllowed)
                throw new LauncherOperationException("AI_TOOL_READ_ONLY", "File writes require operation permission as well as a read_write directory grant.");
            if (!AuthorizationCurrent(current))
            {
                throw new LauncherOperationException("AI_OPERATION_AUTHORIZATION_REVOKED", "Operation permission changed before this file write.");
            }
        }
    }
    public void CheckExternal(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = new AiChatStore(paths).GetDocument(turn["conversationId"]!.ToString());
        if (!OperationAllowed || !AuthorizationCurrent(current))
        {
            throw new LauncherOperationException("AI_EXTERNAL_OPERATION_PERMISSION_REQUIRED", "Unknown third-party effects require current operation permission. Annotations cannot grant it.");
        }

        if (turn["fileAccessEnabled"]?.GetValue<bool>() != true || current["fileAccessEnabled"]?.GetValue<bool>() == false
            || turn["networkAccessEnabled"]?.GetValue<bool>() != true || current["networkAccessEnabled"]?.GetValue<bool>() == false)
        {
            throw new LauncherOperationException("AI_EXTERNAL_SCOPE_UNCONSTRAINED",
                "This third-party program has unverified file/network effects and cannot be dispatched while those assistant capabilities are restricted. It is not an OS sandbox or a managed file/network replacement.");
        }
    }

    /// <summary>Validates current operation authorization and the exact instance, file and network scope before dispatch.</summary>
    public void Check(string name, JsonObject arguments, CancellationToken ct) => CheckCore(name, arguments, false, ct);

    /// <summary>Allows a current safe-mode request to read its review without granting permission to dispatch a mutation.</summary>
    internal void CheckReview(string name, JsonObject arguments, CancellationToken ct) => CheckCore(name, arguments, true, ct);

    private void CheckCore(string name, JsonObject arguments, bool reviewing, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (turn["gameSessionId"]?.GetValue<string>() is { } gameSession
            && arguments["sessionId"]?.GetValue<string>() is { } proposedSession && gameSession != proposedSession)
        {
            throw new LauncherOperationException("AI_GAME_SESSION_SCOPE", "This game-originated turn cannot operate on another running game.");
        }

        var current = new AiChatStore(paths).GetDocument(turn["conversationId"]!.ToString());
        if (turn["repairId"]?.ToString() is { } repairId)
        {
            var repair = new LauncherClient(paths).Repairs.ReadDocument(repairId);
            if (current["repairId"]?.ToString() != repairId || !RepairTools.Contains(name, StringComparer.Ordinal)
                || (name != "result_read" && arguments["repairId"]?.ToString() != repairId)
                || (name == "result_read" && !(repair["resultIds"] as JsonArray ?? []).Any(id => id?.ToString() == arguments["resultId"]?.ToString()))
                || repair["conversationId"]?.ToString() != turn["conversationId"]?.ToString()
                || repair["instanceId"]?.ToString() != turn["instanceId"]?.ToString())
            {
                throw new LauncherOperationException("AI_REPAIR_SCOPE", "Only tools bound to this exact incident are authorized; general launcher and file operations are unavailable.");
            }

            if (turn["fileAccessEnabled"]?.GetValue<bool>() != true || current["fileAccessEnabled"]?.GetValue<bool>() == false)
                throw new LauncherOperationException("AI_FILE_ACCESS_DISABLED", "Repair access was revoked.");
            if (name == "repair_plan" && arguments["kind"]?.ToString() == "dependencies") RequireNetwork();
            if (name == "repair_apply" && AiRepairService.Plan(repair, arguments["planId"]!.ToString())["kind"]?.ToString() == "dependencies") RequireNetwork();
            return;
        }

        if (name.StartsWith("repair_", StringComparison.Ordinal))
            throw new LauncherOperationException("AI_REPAIR_SCOPE", "Start an incident-scoped repair before using repair tools.");
        if (turn["skinScopeId"]?.GetValue<string>() is { } scopeId)
        {
            if (current["skinScopeId"]?.GetValue<string>() != scopeId
                || (!SkinReadTools.Contains(name, StringComparer.Ordinal) && !SkinMutationTools.Contains(name, StringComparer.Ordinal)))
            {
                throw new LauncherOperationException("AI_SKIN_SCOPE", "Only the bound skin candidate tools are available in this conversation.");
            }

            new Skins.SkinService(paths).Proposals.CheckBinding(scopeId,
                arguments["documentId"]?.GetValue<string>(), arguments["proposalId"]?.GetValue<string>());
            if (arguments["scopeId"]?.GetValue<string>() is { } supplied && supplied != scopeId)
                throw new LauncherOperationException("AI_SKIN_SCOPE", "This call names another skin grant.");
        }
        else if (name.StartsWith("skin_", StringComparison.Ordinal))
        {
            throw new LauncherOperationException("AI_SKIN_SCOPE_REQUIRED", "Create a conversation bound to an explicit skin grant.");
        }

        var mutation = IsMutation(name, arguments);
        var pendingSafeOperation = reviewing && turn["mode"]?.ToString() == "safe" && current["mode"]?.ToString() == "safe"
            && (current["operationRevision"]?.GetValue<long>() ?? 0) == (turn["operationRevision"]?.GetValue<long>() ?? 0);
        var operationAllowed = OperationAllowed || pendingSafeOperation;
        if (!CanOffer(name, operationAllowed) || (mutation && !operationAllowed))
            throw new LauncherOperationException("AI_TOOL_READ_ONLY", "This turn did not receive permission for this operation. Tool text cannot grant permission.");
        if (mutation && !AuthorizationCurrent(current) && !pendingSafeOperation)
        {
            throw new LauncherOperationException("AI_OPERATION_AUTHORIZATION_REVOKED", "The conversation's operation permission changed after this turn was authorized. The pending call was not dispatched.");
        }

        if (name == "execute" || ToolCatalog.TaskNames.Contains(name, StringComparer.Ordinal))
        {
            if (turn["instanceId"] is not null)
                throw new LauncherOperationException("AI_COMMAND_SCOPE_UNCONSTRAINED", "A generic launcher command cannot prove instance-only scope. Use the scoped tool or a separately authorized unbound conversation.");
            CheckManagedFile(false, ct);
            RequireNetwork();
        }

        if (name == "app_control")
        {
            var request = arguments["request"] as JsonObject ?? throw new ArgumentException("A typed App request is required.");
            if (turn["instanceId"] is { } selected && request["instanceId"] is { } proposedApp && selected.ToString() != proposedApp.ToString())
                throw new LauncherOperationException("AI_INSTANCE_SCOPE", "This App navigation targets another instance.");
            if (request["action"]?.ToString() == "WallpaperImport" || request["preferences"]?["dataDirectory"] is not null)
                CheckManagedFile(false, ct);
        }

        if (turn["instanceId"] is { } target && arguments["instanceId"]?.ToString() != target.ToString()
            && name is "config_set" or "config_patch")
        {
            throw new LauncherOperationException("AI_INSTANCE_SCOPE", "An instance-scoped conversation cannot write global settings or another instance.");
        }

        if (turn["instanceId"] is { } instance && arguments["instanceId"] is { } proposed && instance.ToString() != proposed.ToString())
            throw new LauncherOperationException("AI_INSTANCE_SCOPE", "This tool targets another instance from the conversation.");
        if (turn["instanceId"]?.GetValue<string>() is { } bound)
        {
            var client = new LauncherClient(paths);
            if (arguments["sessionId"]?.GetValue<string>() is { } session && client.Sessions.Get(session).InstanceId != bound)
                throw new LauncherOperationException("AI_INSTANCE_SCOPE", "This game session belongs to a different instance.");
            if (arguments["jobId"]?.GetValue<string>() is { } job && client.Jobs.Get(job).Request?.Parameters?["instanceId"]?.ToString() != bound)
                throw new LauncherOperationException("AI_INSTANCE_SCOPE", "This job does not belong to the selected instance.");
        }

        if (name == "game_edit")
        {
            var session = arguments["sessionId"]?.GetValue<string>() ?? throw new ArgumentException("A game session is required.");
            var edit = arguments["request"]?.Deserialize(GameBridgeJsonContext.Default.GameEditRequest) ?? throw new ArgumentException("A typed game edit is required.");
            var binding = turn["gameWorldBindings"]?[session] as JsonObject
                ?? throw new LauncherOperationException("AI_GAME_OBSERVATION_REQUIRED", "Read a fresh game_snapshot in this turn before editing its world.");
            if (binding["gameIncarnation"]?.ToString() != edit.GameIncarnation || binding["worldEpoch"]?.ToString() != edit.WorldEpoch || binding["dimension"]?.ToString() != edit.Dimension)
                throw new LauncherOperationException("AI_GAME_WORLD_CHANGED", "This turn's observed world changed. Start a newly authorized turn for the new world; do not retarget the old edit.");
        }

        if (FileTools.Contains(name, StringComparer.Ordinal)
            && (turn["fileAccessEnabled"]?.GetValue<bool>() != true || current["fileAccessEnabled"]?.GetValue<bool>() == false))
        {
            throw new LauncherOperationException("AI_FILE_ACCESS_DISABLED", "File access is disabled for this conversation. The model service itself remains available.");
        }

        if (NetworkTools.Contains(name, StringComparer.Ordinal)) RequireNetwork();
        if (name is "runtime_change" or "launch" && arguments["offline"]?.GetValue<bool>() != true) RequireNetwork();
        if (name == "diagnose" && arguments["reportPath"]?.GetValue<string>() is { } report)
        {
            var client = new LauncherClient(paths);
            var id = arguments["instanceId"]?.GetValue<string>()
                ?? throw new LauncherOperationException("AI_FILE_SCOPE_REQUIRED", "Select the instance containing this report or authorize its directory before reading it.");
            var game = client.Instances.Get(id).GameDirectory;
            using var scope = AiScopedFiles.Open(game, Path.GetRelativePath(game, Path.GetFullPath(report)));
        }
    }
}