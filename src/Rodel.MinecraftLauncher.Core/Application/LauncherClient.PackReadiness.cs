// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Runtime;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Reads current pack content, committed runtime selection, required game files and usable Java without downloading or starting a game.</summary>
    /// <param name="instanceId">The retained pack instance to inspect.</param>
    /// <param name="cancellationToken">Cancels metadata and Java inspection.</param>
    /// <returns>Current readiness rather than the outcome of an older preparation task.</returns>
    public async Task<PackState> InspectPackPreparationAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var state = Packs.GetState(instanceId);
        var instance = Instances.Get(instanceId);
        var effective = Settings.GetEffective(instance);
        var files = new PackPreparationStep { Id = "files", Status = state.Missing.Count == 0 ? "completed" : "blocked", Acquisition = state.Pack?.Acquisition, MissingCount = state.Missing.Count };
        var runtime = new PackPreparationStep { Id = "runtime" };
        var java = new PackPreparationStep { Id = "java" };
        if (!NeedsPackRuntimeSelection(instance, state))
        {
            try
            {
                var version = effective.Values["versionId"]?.GetValue<string>() ?? instance.VersionId!;
                var javaPath = SelectJavaPath(effective, null, instance.JavaPath);
                if (javaPath is not null)
                    javaPath = JavaDiscovery.ResolveExecutable(javaPath, instance.GameDirectory);
                var plan = await new LaunchPlanner(paths).BuildAsync(instance.MinecraftDirectory, instance.GameDirectory, version,
                    javaPath: javaPath, ct: cancellationToken).ConfigureAwait(false);
                state.RuntimePrepared = plan.RequiredFiles.All(file => file.Kind == "assetMapped" || file.Exists);
                state.Java = plan.JavaSelection;
                runtime.Status = state.RuntimePrepared ? "completed" : "pending";
                if (state.RuntimePrepared)
                {
                    var profile = await RuntimeProfile.DescribeAsync(instance.MinecraftDirectory, version, cancellationToken).ConfigureAwait(false);
                    state.Runtime = new(true, version, plan.Warnings, string.Empty, RuntimeTarget.From(profile)) { SelectionSource = "current-selection" };
                    runtime.Runtime = state.Runtime;
                }

                java.Status = plan.JavaSelection?.Candidates.Any(candidate => candidate.IsUsable && string.Equals(candidate.Path, plan.JavaSelection.SelectedPath, StringComparison.OrdinalIgnoreCase)) == true
                    ? "completed" : "pending";
                if (java.Status == "completed")
                    java.Java = new() { Java = plan.JavaSelection };
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                runtime.Error = exception.Message;
            }
        }

        state.Preparation = new()
        {
            InstanceId = instanceId,
            Steps = [files, runtime, java],
            Status = files.Status == "completed" && runtime.Status == "completed" && java.Status == "completed" ? "completed" : "pending"
        };
        return state;
    }

    private static bool NeedsPackRuntimeSelection(InstanceRecord instance, PackState state)
    {
        if (!state.Dependencies.Keys.Any(key => key is "fabric-loader" or "quilt-loader" or "forge" or "neoforge")) return false;
        var selection = RuntimeSelectionStore.Snapshot(instance.MetadataPath);
        var version = selection["versionId"]?.GetValue<string>();
        // A runtime commit records loader, including an explicit null for a deliberate vanilla
        // selection. Merely saving the initial base version in a settings form is not that commit.
        return version is null || (!selection.ContainsKey("loader") && version == state.Pack?.MinecraftVersion);
    }
}