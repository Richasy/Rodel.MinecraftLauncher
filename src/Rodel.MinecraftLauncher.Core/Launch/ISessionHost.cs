// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Starts monitoring a prepared session. Hosts may choose an in-process monitor or their own independent helper.</summary>
public interface ISessionHost
{
    public Task StartAsync(LauncherPaths paths, string sessionId, CancellationToken cancellationToken = default);
}