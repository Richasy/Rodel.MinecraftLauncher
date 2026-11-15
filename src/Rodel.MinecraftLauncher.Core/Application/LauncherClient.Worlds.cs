// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Worlds;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    public WorldService Worlds => new(paths, new InlineProgress<LauncherProgress>(value => Report(value)));
    public async Task<WorldOperationResult> ManageWorldAsync(string instanceId, WorldAction action, string? world = null,
        string? source = null, string? destination = null, CancellationToken cancellationToken = default)
    {
        EnsureInstanceIdle(instanceId);
        var result = await Worlds.ExecuteAsync(instanceId, action, world, source, destination, cancellationToken).ConfigureAwait(false);
        await Activity.WriteAsync("world." + action.ToString().ToLowerInvariant(), instanceId, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        return result;
    }
}