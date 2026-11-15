// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Runs a retained seed search through the shared task progress channel, preserving its cursor and witnesses on cancellation.</summary>
    public Task<SeedSearchState> SearchSeedsAsync(string searchId, CancellationToken cancellationToken = default) =>
        Seeds.Searches.RunAsync(searchId, new InlineProgress<SeedSearchState>(state => Report(new()
        {
            Stage = "seed-search",
            Path = state.Id,
            DisplayName = state.Result.Status,
            Message = System.FormattableString.Invariant($"{state.Result.Checked} candidates; {state.Result.Hits.Length} matches; next offset {state.Result.NextOffset}"),
        })), cancellationToken);

    /// <summary>Explicitly prepares seed-generation game/Java dependencies using existing installation progress and recovery.</summary>
    public Task<SeedRuntimeProfile> PrepareSeedRuntimeAsync(string version = "26.3", CancellationToken cancellationToken = default) =>
        Seeds.PrepareAsync(version, InstallationEvents, cancellationToken);
}