// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Plans and applies explicit external-client integration while preserving manual content and durable ownership evidence.</summary>
public sealed class AiClientIntegrationService(LauncherPaths paths)
{
    private sealed record ProductFile(string Path, string Hash, string? Source, string? Text, bool Bridge);
    private sealed record Preparation(AiIntegrationPlan Plan, JsonObject Record, AiExternalClient Client, string RecordPath, List<ProductFile> Files, string Launcher);
    private static string Hash(JsonNode? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(value).ToJsonString())));
    private static JsonNode Canonical(JsonNode? value)
    {
        if (value is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var pair in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)) result[pair.Key] = pair.Value is null ? null : Canonical(pair.Value);
            return result;
        }

        if (value is JsonArray array) return new JsonArray([.. array.Select(item => item is null ? null : Canonical(item))]);
        return value?.DeepClone() ?? JsonValue.Create("absent")!;
    }

    private static string? FileHash(string file)
    {
        if (!File.Exists(file)) return null;
        using var opened = AiScopedFiles.Open(Path.GetDirectoryName(file)!, Path.GetFileName(file));
        return Convert.ToHexString(SHA256.HashData(opened.Stream));
    }

    /// <summary>Observes client bindings and proposes ownership-safe changes without applying them.</summary>
    public async Task<AiIntegrationPlan> PlanAsync(AiClientIntegrationRequest request, CancellationToken ct = default)
        => (await PrepareAsync(request with { }, ct).ConfigureAwait(false)).Plan;

    /// <summary>Reads the same frozen integration observation used to authorize an apply.</summary>
    public Task<AiIntegrationPlan> StatusAsync(AiClientIntegrationRequest request, CancellationToken ct = default)
        => PlanAsync(request, ct);

    private async Task<Preparation> PrepareAsync(AiClientIntegrationRequest request, CancellationToken ct)
    {
        if (request.Action is not ("install" or "remove")) throw new ArgumentException("Select install or remove.");
        var client = new AiExternalClient(request);
        var installation = request.InstallationRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rodel.MinecraftLauncher", "ExternalClients");
        if (!Path.IsPathFullyQualified(installation) || Path.GetPathRoot(installation) == Path.TrimEndingDirectorySeparator(installation))
            throw new ArgumentException("Select a dedicated absolute integration installation directory.");
        installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        var recordPath = Path.Combine(installation, client.Client + ".json");
        var record = File.Exists(recordPath) ? JsonFiles.ReadObject(recordPath) : new JsonObject
        {
            ["schemaVersion"] = 1,
            ["client"] = client.Client,
            ["files"] = new JsonArray()
        };
        if (record["schemaVersion"]?.GetValue<int>() != 1 || record["client"]?.ToString() != client.Client)
            throw new LauncherOperationException("AI_INTEGRATION_RECORD_UNSUPPORTED", "The integration ownership record needs repair or a newer launcher; no client files were overwritten.");
        var launcher = request.LauncherExecutable ?? record["launcherExecutable"]?.GetValue<string>() ?? string.Empty;
        if (launcher.Length > 0)
        {
            if (!Path.IsPathFullyQualified(launcher)) throw new ArgumentException("Select an absolute independently installed CLI executable.");
            launcher = Path.GetFullPath(launcher);
        }

        var files = new List<ProductFile>();
        if (request.Action == "install")
        {
            var skillRoot = Path.Combine(client.SkillsRoot, "rodel-minecraft");
            var index = """
                ---
                name: rodel-minecraft
                description: Manage Minecraft Java launcher instances, diagnose settings, review content, observe jobs and prepare game bridge access for in-game data or local player actions through the Rodel Minecraft launcher MCP.
                ---

                # Rodel Minecraft Launcher

                Use the `rodel-minecraft` MCP server. It initially exposes only five discovery/execution/result tools. Read `capabilities` for relevant names and one `toolName` schema, then use `tool_call` with that exact name and arguments. Use `execute` only for CLI-only workflows after reading topic help. Do not read every schema or state for ordinary conversation.
                Read real state first; preserve custom settings, unknown fields and instance overrides.
                A queued job is not completion. Inspect the recorded job until terminal status.
                Freeze content reviews and obtain the user's exact approval before applying them.
                Do not replay an uncertain mutation to recover its output. Observe state and preserve recovery references.
                This external client owns authorization. The built-in chat's read-only mode is NOT an OS sandbox for this MCP server.
                The installation is bound to one explicit launcher data root. Reconfigure through the installer before changing it.
                For in-game data or player control, read references/settings-explained.md, inspect the target instance's gameBridgeEnabled and actual session status. Offer enablement, or patch only that instance when authorized; the setting takes effect on the next launch, so do not restart a running game without task authorization. Remote servers remain read-only.
                Load only the relevant supporting reference under `references/`.
                """;
            files.Add(TextFile(Path.Combine(skillRoot, "SKILL.md"), index + "\n"));
            foreach (var item in await AiSkills.BuiltinsAsync(ct).ConfigureAwait(false))
            {
                var name = item.Key["builtin:".Length..];
                files.Add(TextFile(Path.Combine(skillRoot, "references", name + ".md"), item.Value.Body!));
            }

        }
        else
        {
            var productSkills = (await AiSkills.BuiltinsAsync(ct).ConfigureAwait(false)).Keys
                .Select(id => Path.Combine("references", id["builtin:".Length..] + ".md"))
                .Append("SKILL.md").ToHashSet(StringComparer.Ordinal);
            foreach (var owned in record["files"]!.AsArray().OfType<JsonObject>().Where(item => item["bridge"]?.GetValue<bool>() != true))
            {
                var path = owned["path"]!.ToString();
                var skillRoot = owned["skillsRoot"]?.ToString() ?? record["skillsRoot"]?.ToString();
                if (skillRoot is null || !path.StartsWith(Path.Combine(skillRoot, "rodel-minecraft") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An ownership record points outside its product skill directory.");
                var relative = Path.GetRelativePath(Path.Combine(skillRoot, "rodel-minecraft"), path);
                if (!productSkills.Contains(relative))
                {
                    throw new InvalidDataException("An ownership record names a file outside the known product skill payload.");
                }

                files.Add(new(path, owned["sha256"]!.ToString(), null, null, false));
            }
        }

        var actions = new List<AiIntegrationFile>();
        foreach (var file in files)
        {
            var current = FileHash(file.Path);
            var owned = record["files"]!.AsArray().OfType<JsonObject>().SingleOrDefault(item =>
                string.Equals(item["path"]?.ToString(), file.Path, StringComparison.OrdinalIgnoreCase));
            var action = request.Action == "remove" ? current is null ? "absent" : current == file.Hash ? "remove" : "preserve_modified"
                : current == file.Hash ? "unchanged" : current is null ? "create" : owned?["sha256"]?.ToString() == current ? "update_owned" : "conflict";
            actions.Add(new()
            {
                Path = file.Path,
                CurrentSha256 = current,
                ProductSha256 = file.Hash,
                Action = action,
                Bridge = file.Bridge
            });
        }

        JsonObject? entry = null;
        JsonObject? inspectionError = null;
        try { if (!request.SkillsOnly && (request.Action == "remove" || File.Exists(launcher))) entry = await client.ReadEntryAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or JsonException or LauncherOperationException)
        {
            inspectionError = LauncherOperationException.ToResult(error)["error"]!.DeepClone().AsObject();
        }

        var pendingMatch = entry is not null && record["mcpDispatch"]?["state"]?.ToString() is "registering" or "registering_after_remove"
            && record["mcpDispatch"]?["desiredFingerprint"]?.ToString() == Hash(entry);
        var ownedEntry = entry is not null && (record["mcpFingerprint"]?.ToString() == Hash(entry) || pendingMatch);
        var launcherAvailable = File.Exists(launcher) || files.Any(file => file.Path == launcher);
        var scopeChanged = record["mcpFingerprint"] is not null && record["configRoot"] is { } oldConfigRoot
            && !oldConfigRoot.ToString().Equals(client.ConfigRoot, StringComparison.OrdinalIgnoreCase);
        var mcpAction = request.SkillsOnly ? "skills_only" : !launcherAvailable && request.Action != "remove" ? "cli_required" : !client.CanRegister ? "export_only"
            : inspectionError is not null ? "inspection_failed"
            : scopeChanged ? "scope_change_requires_remove"
            : request.Action == "remove" ? entry is null ? "absent" : ownedEntry ? "remove_owned" : "preserve_modified"
            : entry is null ? "register" : pendingMatch && client.Matches(entry, launcher, paths.Root) ? "recover_observed_binding"
            : ownedEntry ? "update_owned" : "conflict";
        var plan = new AiIntegrationPlan
        {
            Client = client.Client,
            ClientExecutable = client.Executable,
            ClientVersion = client.Version,
            Action = request.Action,
            ConfigRoot = client.ConfigRoot,
            SkillsRoot = client.SkillsRoot,
            InstallationRoot = installation,
            LauncherExecutable = launcher,
            LauncherSha256 = FileHash(launcher),
            DataRoot = paths.Root,
            PreviousDataRoot = record["dataRoot"]?.DeepClone(),
            BindingChanged = record["dataRoot"] is { } priorRoot && priorRoot.ToString() != paths.Root,
            PreviousConfigRoot = record["configRoot"]?.DeepClone(),
            ClientScopeChanged = scopeChanged,
            Files = [.. actions],
            McpAction = mcpAction,
            McpCurrentFingerprint = entry is null ? null : Hash(entry),
            InspectionError = inspectionError,
            ReplaceConflicts = request.ReplaceConflicts,
            McpOwnershipEvidence = pendingMatch ? "observed_exact_pending_intent" : ownedEntry ? "recorded_fingerprint" : "not_owned",
            LastAttemptState = record["state"]?.DeepClone(),
            LastOutcomes = record["outcomes"]?.DeepClone(),
            LastMcpResult = record["mcpResult"]?.DeepClone(),
            PendingMcpDispatch = record["mcpDispatch"]?.DeepClone(),
            ConfigurationExport = new()
            {
                Command = launcher,
                Args = AiExternalClient.McpArguments(paths.Root)
            },
            McpRegistered = ownedEntry && client.Matches(entry, launcher, paths.Root)
        };
        plan = plan with { Fingerprint = Hash(JsonSerializer.SerializeToNode(plan, AiJsonContext.Default.AiIntegrationPlan)) };
        return new(plan, record, client, recordPath, files, launcher);
    }

    /// <summary>Applies the exact current fingerprint, recording recoverable file and client dispatch outcomes.</summary>
    public async Task<AiIntegrationReceipt> ApplyAsync(AiClientIntegrationRequest request, CancellationToken ct = default)
    {
        request = request with { };
        var preparation = await PrepareAsync(request, ct).ConfigureAwait(false);
        EnsureDirectory(Path.GetDirectoryName(preparation.RecordPath)!);
        using var lease = await ResourceLease.AcquireAsync(preparation.RecordPath, ct).ConfigureAwait(false);
        preparation = await PrepareAsync(request, ct).ConfigureAwait(false);
        var (plan, record, client, recordPath, files, launcher) = preparation;
        if (plan.McpAction == "cli_required") throw new LauncherOperationException("AI_INTEGRATION_CLI_REQUIRED", "Install the standalone CLI and select its executable, or install skills only.");
        if (string.IsNullOrWhiteSpace(request.PlanFingerprint) || request.PlanFingerprint != plan.Fingerprint)
            throw new LauncherOperationException("AI_INTEGRATION_PLAN_CHANGED", "The client, installation, source or current files changed. Inspect a fresh plan before applying.");
        if (plan.McpAction == "scope_change_requires_remove")
            throw new LauncherOperationException("AI_INTEGRATION_SCOPE_CHANGED", "Remove the owned registration using its recorded client configuration root before selecting another client root. The old binding was not silently abandoned.");
        if (request.Action == "install" && !request.ReplaceConflicts
            && (plan.McpAction == "conflict" || plan.Files.Any(item => item.Action == "conflict")))
        {
            throw new LauncherOperationException("AI_INTEGRATION_CONFLICT", "Existing manual content conflicts with this exact plan. It was preserved; choose replacement explicitly or install skills only.");
        }

        record["state"] = "applying";
        record["pendingPlan"] = JsonSerializer.SerializeToNode(plan, AiJsonContext.Default.AiIntegrationPlan);
        if (record["mcpFingerprint"] is null) record["configRoot"] = client.ConfigRoot;
        record["skillsRoot"] = client.SkillsRoot;
        await JsonFiles.WriteAsync(recordPath, record, ct).ConfigureAwait(false);
        var outcomes = new List<AiIntegrationFileOutcome>();
        foreach (var file in files)
        {
            var action = plan.Files.Single(item => item.Path == file.Path);
            try
            {
                if (request.Action == "remove")
                {
                    if (action.Action == "remove")
                    {
                        if (FileHash(file.Path) != file.Hash) throw new LauncherOperationException("AI_INTEGRATION_FILE_CHANGED", "A skill changed after the removal plan. It was not removed.");
                        using var parent = AiScopedFiles.OpenDirectory(Path.GetDirectoryName(file.Path)!);
                        var removed = file.Path + ".rodel-removed-" + Guid.NewGuid().ToString("N");
                        File.Move(file.Path, removed, overwrite: false);
                        if (FileHash(removed) != file.Hash)
                        {
                            if (!File.Exists(file.Path)) File.Move(removed, file.Path);
                            throw new LauncherOperationException("AI_INTEGRATION_FILE_CHANGED", "A concurrent file revision was preserved; removal did not delete its bytes.");
                        }

                        File.Delete(removed);
                    }

                    if (action.Action is "remove" or "absent")
                    {
                        var old = record["files"]!.AsArray().FirstOrDefault(item => item?["path"]?.ToString() == file.Path);
                        if (old is not null) record["files"]!.AsArray().Remove(old);
                    }
                }
                else
                {
                    await WriteProductFileAsync(file, action.CurrentSha256, ct).ConfigureAwait(false);
                    var old = record["files"]!.AsArray().FirstOrDefault(item => item?["path"]?.ToString() == file.Path);
                    if (old is not null) record["files"]!.AsArray().Remove(old);
                    record["files"]!.AsArray().Add((JsonNode)new JsonObject
                    {
                        ["path"] = file.Path,
                        ["sha256"] = file.Hash,
                        ["bridge"] = file.Bridge,
                        ["skillsRoot"] = file.Bridge ? null : client.SkillsRoot
                    });
                }

                outcomes.Add(new AiIntegrationFileOutcome
                {
                    Path = file.Path,
                    Status = request.Action == "install" && action.Action == "conflict"
                        ? "replaced_with_backup" : action.Action
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException)
            {
                outcomes.Add(new AiIntegrationFileOutcome { Path = file.Path, Status = "failed", Error = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
            }

            record["outcomes"] = JsonSerializer.SerializeToNode([.. outcomes], AiJsonContext.Default.AiIntegrationFileOutcomeArray);
            await JsonFiles.WriteAsync(recordPath, record, CancellationToken.None).ConfigureAwait(false);
        }

        var mcp = new AiIntegrationMcpOutcome { Status = plan.McpAction, Registered = false };
        try
        {
            var action = plan.McpAction;
            if (action is "register" or "update_owned" or "conflict" or "remove_owned" or "recover_observed_binding")
            {
                var current = await client.ReadEntryAsync(ct).ConfigureAwait(false);
                if ((current is null ? null : Hash(current)) != plan.McpCurrentFingerprint)
                    throw new LauncherOperationException("AI_INTEGRATION_CLIENT_CHANGED", "The product MCP entry changed after the plan. It was not overwritten.");
                if (request.Action == "remove")
                {
                    await client.RemoveAsync(current, ct).ConfigureAwait(false);
                    if (await client.ReadEntryAsync(ct).ConfigureAwait(false) is not null) throw new IOException("The client still reports the removed MCP entry.");
                    record.Remove("mcpFingerprint");
                    mcp = mcp with { Status = "removed" };
                }
                else
                {
                    if (FileHash(launcher) != plan.LauncherSha256)
                        throw new LauncherOperationException("AI_INTEGRATION_CLI_CHANGED", "The selected standalone CLI changed; inspect a fresh plan before registration.");
                    await VerifyBridgeAsync(launcher, ct).ConfigureAwait(false);
                    if (FileHash(launcher) != plan.LauncherSha256)
                        throw new LauncherOperationException("AI_INTEGRATION_CLI_CHANGED", "The selected standalone CLI changed during verification; inspect a fresh plan before registration.");
                    if (action != "recover_observed_binding")
                    {
                        record["mcpDispatch"] = new JsonObject
                        {
                            ["state"] = "registering",
                            ["client"] = client.Client,
                            ["previousFingerprint"] = plan.McpCurrentFingerprint,
                            ["desiredFingerprint"] = Hash(client.ExpectedEntry(launcher, paths.Root)),
                            ["stableLauncher"] = launcher,
                            ["dataRoot"] = paths.Root
                        };
                        await JsonFiles.WriteAsync(recordPath, record, ct).ConfigureAwait(false);
                        await client.RegisterAsync(current, launcher, paths.Root, ct).ConfigureAwait(false);
                    }

                    var observed = await client.ReadEntryAsync(ct).ConfigureAwait(false);
                    if (!client.Matches(observed, launcher, paths.Root)) throw new IOException("The client did not retain the planned stable command/data-root binding.");
                    record["mcpFingerprint"] = Hash(observed);
                    record["configRoot"] = client.ConfigRoot;
                    record["dataRoot"] = paths.Root;
                    mcp = mcp with { Status = action == "recover_observed_binding" ? "observed_registered" : "registered", Registered = true };
                    record["mcpDispatch"]!["state"] = "verified";
                }
            }
            else if (request.Action == "remove" && action == "absent")
            {
                record.Remove("mcpFingerprint");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException or JsonException)
        {
            var failure = LauncherOperationException.ToResult(error);
            using var details = JsonDocument.Parse(failure["data"]?.ToJsonString() ?? "null");
            mcp = mcp with { Status = "failed", Error = failure["error"]!.DeepClone(), Details = details.RootElement.Clone() };
        }

        record["mcpResult"] = JsonSerializer.SerializeToNode(mcp, AiJsonContext.Default.AiIntegrationMcpOutcome);
        var status = outcomes.Any(item => item.Status is "failed" or "preserve_modified")
            || mcp.Status is "failed" or "preserve_modified" or "inspection_failed" ? "partial" : "completed";
        record["launcherExecutable"] = launcher;
        record["state"] = status;
        record.Remove("pendingPlan");
        await JsonFiles.WriteAsync(recordPath, record, CancellationToken.None).ConfigureAwait(false);
        return new AiIntegrationReceipt
        {
            Status = status,
            Files = [.. outcomes],
            Mcp = mcp,
            ConfigurationExport = plan.ConfigurationExport,
            BridgeRetained = request.Action == "remove"
        };
    }

    private async Task VerifyBridgeAsync(string launcher, CancellationToken ct)
    {
        var session = await AiMcpSession.ConnectAsync(new AiMcpRegistration
        {
            Id = "rodel_install_check",
            DisplayName = "Installed launcher verification",
            ExecutablePath = launcher,
            WorkingDirectory = Path.GetDirectoryName(launcher),
            Arguments = AiExternalClient.McpArguments(paths.Root)
        }, ct).ConfigureAwait(false);
        await using var lifetime = session.ConfigureAwait(false);
        if (!session.Tools.Any(tool => session.ActualName(tool.Name) == "capabilities")
            || !session.Tools.Any(tool => session.ActualName(tool.Name) == "tool_call"))
        {
            throw new LauncherOperationException("AI_INTEGRATION_BRIDGE_INVALID", "The stable executable did not expose the expected real launcher MCP catalog.");
        }

        var call = session.Tools.Single(tool => session.ActualName(tool.Name) == "tool_call");
        var observed = await session.CallAsync(call.Name, new JsonObject
        {
            ["name"] = "config_get",
            ["arguments"] = new JsonObject { ["key"] = "language" }
        }, ct).ConfigureAwait(false);
        if (observed.Status != "succeeded")
            throw new LauncherOperationException("AI_INTEGRATION_BRIDGE_INVALID", "The selected standalone CLI could not read Core state through its compact MCP surface.");
    }

    private static ProductFile TextFile(string path, string text)
        => new(path, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), null, text, false);

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))
                ?? throw new ArgumentException("An installation directory needs a parent.");
            EnsureDirectory(parent);
            using var pinned = AiScopedFiles.OpenDirectory(parent);
            Directory.CreateDirectory(path);
        }

        using var check = AiScopedFiles.OpenDirectory(path);
    }

    private static async Task WriteProductFileAsync(ProductFile file, string? expected, CancellationToken ct)
    {
        if (FileHash(file.Path) != expected) throw new LauncherOperationException("AI_INTEGRATION_FILE_CHANGED", "The destination changed after the plan.");
        if (expected == file.Hash) return;
        EnsureDirectory(Path.GetDirectoryName(file.Path)!);
        using var pinned = AiScopedFiles.OpenDirectory(Path.GetDirectoryName(file.Path)!);
        var temporary = file.Path + ".rodel-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (file.Text is not null)
            {
                await File.WriteAllTextAsync(temporary, file.Text, new UTF8Encoding(false), ct).ConfigureAwait(false);
            }
            else
            {
                using var source = AiScopedFiles.Open(Path.GetDirectoryName(file.Source!)!, Path.GetFileName(file.Source!));
                var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using (output.ConfigureAwait(false))
                {
                    await source.Stream.CopyToAsync(output, ct).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
            }

            if (FileHash(temporary) != file.Hash || FileHash(file.Path) != expected)
                throw new LauncherOperationException("AI_INTEGRATION_FILE_CHANGED", "The source or destination changed during preparation.");
            if (expected is null)
            {
                File.Move(temporary, file.Path, overwrite: false);
            }
            else
            {
                var backup = file.Path + ".rodel-backup-" + Guid.NewGuid().ToString("N");
                File.Replace(temporary, file.Path, backup);
                if (FileHash(backup) != expected)
                {
                    throw new LauncherOperationException("AI_INTEGRATION_COMMIT_RACE", "A concurrent destination revision was preserved in the adjacent recovery backup. Inspect it before retrying.",
                        new JsonObject { ["backupPath"] = backup });
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}