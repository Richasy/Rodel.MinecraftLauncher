// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Servers;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Plans and prepares one instance, starts its monitored game, and optionally waits for actual exit; cancellation before startup cancels preparation.</summary>
    public async Task<ILaunchResult> LaunchAsync(string instanceId, LaunchOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.InheritServer)
            options = (await CreateLaunchAsync(instanceId, options, cancellationToken).ConfigureAwait(false)).Options;
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        var instance = Instances.Get(instanceId);
        var serverAddress = options.ServerAddress;
        if (serverAddress is null && options.Server is { } requestedServer)
        {
            var favorite = (await Servers.ListAsync(cancellationToken).ConfigureAwait(false)).Select(entry => entry.Favorite)
                .FirstOrDefault(entry => entry?.Id == requestedServer);
            serverAddress = favorite?.Address ?? requestedServer;
        }

        if (serverAddress is not null)
            ServerService.ParseAddress(serverAddress);
        var planner = new LaunchPlanner(paths);
        var effective = new SettingsService(paths).GetEffective(instance);
        var settings = effective.Values;
        if (options.VersionId is null && Packs.IsManaged(instanceId))
        {
            var pack = Packs.GetState(instanceId);
            if (NeedsPackRuntimeSelection(instance, pack) || pack.Missing.Count > 0)
            {
                if (options.PlanOnly || options.Offline)
                {
                    throw new LauncherOperationException("PACK_PREPARATION_REQUIRED", "This pack's declared runtime or content is not ready. Continue pack preparation before launching; the vanilla game is not a substitute.",
                        JsonSerializer.SerializeToNode(await InspectPackPreparationAsync(instanceId, cancellationToken).ConfigureAwait(false), PackJsonContext.Default.PackState));
                }

                await PreparePackAsync(instanceId, cancellationToken: cancellationToken).ConfigureAwait(false);
                instance = Instances.Get(instanceId);
                effective = Settings.GetEffective(instance);
                settings = effective.Values;
            }
        }

        var version = options.VersionId ?? settings["versionId"]?.GetValue<string>() ?? instance.VersionId ?? throw new ArgumentException("This imported directory has multiple or no versions. Supply VersionId or select an instance runtime.");
        var java = SelectJavaPath(effective, options.JavaPath, instance.JavaPath);
        // Persisted paths travel with the game directory. A one-shot application override is relative
        // to the caller's current directory, like other file arguments.
        if (java is not null)
            java = JavaDiscovery.ResolveExecutable(java, options.JavaPath is null ? instance.GameDirectory : Environment.CurrentDirectory);
        var identityId = options.AccountId ?? (options.InheritIdentity ? settings["accountId"]?.GetValue<string>() : null);
        var identity = identityId is null ? null : Identities.Get(identityId);
        if (identity?.Type is "microsoft" or "external" && options.PlayerName is { } customName && customName != identity.Name)
            throw new ArgumentException("An online player's name comes from their authenticated Minecraft profile.");
        var player = options.PlayerName ?? identity?.Name ?? settings["playerName"]?.GetValue<string>() ?? "Player";
        var memory = options.MaxMemoryMb ?? settings["maxMemoryMb"]?.GetValue<int>() ?? 2048;
        var minimumMemory = settings["minMemoryMb"]?.GetValue<int>();
        var jvm = (settings["jvmArguments"] as JsonArray ?? []).Select(x => x!.GetValue<string>()).Concat(options.JvmArguments).ToArray();
        var preparedJava = false;
        if (!options.PlanOnly && !options.Offline && !File.Exists(Path.Combine(instance.MinecraftDirectory, "versions", version, version + ".json")))
            await new InstallationService(paths).InstallAsync(instance.MinecraftDirectory, version, progress: InstallationEvents, cancellationToken: cancellationToken).ConfigureAwait(false);
        var plan = await planner.BuildAsync(minecraftDirectory: instance.MinecraftDirectory, gameDirectory: instance.GameDirectory, versionId: version, javaPath: java, playerName: player, maxMemoryMb: memory, extraJvmArguments: jvm, minMemoryMb: minimumMemory, launchOptions: ResolutionOptions(settings, options), identity: identity, ct: cancellationToken).ConfigureAwait(false);
        if (options.PlanOnly)
        {
            if (serverAddress is not null)
                plan = await WithServerAsync(plan, instance, version, serverAddress, cancellationToken).ConfigureAwait(false);
            return await ApplyLaunchOptionsAsync(plan, settings, options, instance, version, cancellationToken).ConfigureAwait(false);
        }

        if (java is null && !options.Offline)
        {
            var recommended = plan.JavaSelection?.PreferredMajor;
            var chosen = await new JavaDiscovery(paths).ProbeAsync(plan.JavaPath, cancellationToken).ConfigureAwait(false);
            if ((!chosen.IsUsable || (recommended is not null && chosen.MajorVersion != recommended)) && plan.JavaSelection?.RuntimeComponent is { } component)
            {
                try
                {
                    var runtime = await new JavaRuntimeService(paths).InstallAsync(component: component, progress: DownloadEvents, ct: cancellationToken).ConfigureAwait(false);
                    java = runtime.JavaPath;
                    preparedJava = true;
                    plan = await planner.BuildAsync(minecraftDirectory: instance.MinecraftDirectory, gameDirectory: instance.GameDirectory, versionId: version, javaPath: java, playerName: player, maxMemoryMb: memory, extraJvmArguments: jvm, minMemoryMb: minimumMemory, launchOptions: ResolutionOptions(settings, options), identity: identity, ct: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or InvalidDataException)
                {
                    plan = plan with
                    {
                        Warnings = [.. plan.Warnings, "Automatic Java preparation failed; trying the available Java: " + ex.Message]
                    };
                }
            }
        }

        if (!options.Offline && plan.RequiredFiles.Any(f => !f.Exists && f.Kind != "assetMapped"))
        {
            await new InstallationService(paths).InstallAsync(instance.MinecraftDirectory, version, progress: InstallationEvents, cancellationToken: cancellationToken).ConfigureAwait(false);
            plan = await planner.BuildAsync(minecraftDirectory: instance.MinecraftDirectory, gameDirectory: instance.GameDirectory, versionId: version, javaPath: java, playerName: player, maxMemoryMb: memory, extraJvmArguments: jvm, minMemoryMb: minimumMemory, launchOptions: ResolutionOptions(settings, options), identity: identity, ct: cancellationToken).ConfigureAwait(false);
        }

        if (serverAddress is not null)
            plan = await WithServerAsync(plan, instance, version, serverAddress, cancellationToken).ConfigureAwait(false);
        plan = await ApplyLaunchOptionsAsync(plan, settings, options, instance, version, cancellationToken).ConfigureAwait(false);
        if (preparedJava && plan.JavaSelection is { } selectedJava)
            plan = plan with { JavaSelection = selectedJava with { Source = JavaSelectionSource.AutomaticRuntime } };
        var sessions = new SessionService(paths);
        var sessionId = await sessions.CreateCoreAsync(plan, instance.Id, _activeTask.Value, options.RepairPlanId, cancellationToken).ConfigureAwait(false);
        if (options.SessionCreated is { } created) await created(sessionId).ConfigureAwait(false);
        // Once persisted, let the monitor record cancellation as a terminal session state.
        await _sessionHost.StartAsync(paths, sessionId, cancellationToken).ConfigureAwait(false);
        SessionStatus result;
        do
        {
            result = await sessions.WaitForStartAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        while (result.Status is "queued" or "preparing");
        if (result.Status == "canceled")
            throw new OperationCanceledException("Game preparation was canceled.", cancellationToken);
        if (result.Status == "failed")
        {
            throw new LauncherOperationException(result.ErrorCode ?? "GAME_PREPARATION_FAILED",
                result.Error ?? "Game preparation failed.", JsonSerializer.SerializeToNode(result, LaunchJsonContext.Default.SessionStatus));
        }

        if (options.WaitForExit)
            result = await sessions.WaitForExitAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (result.Status is "interrupted" or "failed")
        {
            throw new LauncherOperationException("GAME_INTERRUPTED", $"Game session {sessionId} did not finish launching: {result.Error ?? "its monitor was interrupted"}.",
                JsonSerializer.SerializeToNode(result, LaunchJsonContext.Default.SessionStatus));
        }

        if (result.Status == "exited" && result.ExitCode is { } exitCode && exitCode != 0)
        {
            throw new LauncherOperationException("GAME_EXITED", $"Game session {sessionId} exited with code {exitCode}. Use logs {sessionId} to inspect its output.",
                JsonSerializer.SerializeToNode(result, LaunchJsonContext.Default.SessionStatus));
        }

        result.AdditionalData.Remove("arguments");
        result.AdditionalData.Remove("nativeArchives");
        result.AdditionalData.Remove("assetMappings");
        if (result.Warnings is null && (!result.AdditionalData.TryGetValue("warnings", out var storedWarnings) || storedWarnings.ValueKind == JsonValueKind.Null))
        {
            result.AdditionalData.Remove("warnings");
            result = result with { Warnings = plan.Warnings };
        }

        return result;
    }

    // Reading settings remains possible for repair. Execution must not turn a broken explicit
    // managed choice into automatic discovery, while a one-shot path always takes precedence.
    private static string? SelectJavaPath(EffectiveSettings effective, string? explicitPath, string? fallbackPath)
    {
        if (explicitPath is not null)
            return explicitPath;
        if (effective.Warnings.OfType<JsonObject>().FirstOrDefault(value => value["code"]?.ToString() == "JAVA_RUNTIME_UNAVAILABLE") is { } issue)
            throw new LauncherOperationException("JAVA_RUNTIME_UNAVAILABLE", issue["message"] + " " + issue["recovery"], issue.DeepClone());
        return effective.Values["javaPath"]?.GetValue<string>() ?? fallbackPath;
    }

    private static async Task<LaunchPlan> WithServerAsync(LaunchPlan plan, InstanceRecord instance, string version, string address, CancellationToken cancellationToken)
    {
        var endpoint = ServerService.ParseAddress(address);
        var metadata = await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, version, cancellationToken).ConfigureAwait(false);
        var modern = metadata["arguments"]?["game"]?.ToJsonString().Contains("quickPlayMultiplayer", StringComparison.Ordinal) == true;
        return plan with
        {
            Server = address,
            ServerConnectionMode = modern ? "quick-play" : "legacy",
            Arguments = modern ? [.. plan.Arguments, "--quickPlayMultiplayer", address] : [.. plan.Arguments, "--server", endpoint.Host, "--port", endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)]
        };
    }
}