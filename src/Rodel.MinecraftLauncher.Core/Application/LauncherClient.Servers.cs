// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Servers;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Freezes an explicit or inherited server target before queuing a launch; ordinary local instances remain local.</summary>
    /// <param name="instanceId">The existing local or dedicated client.</param>
    /// <param name="options">Explicit overrides; disabling server inheritance provides ordinary launch for a dedicated client.</param>
    /// <param name="cancellationToken">Cancels target resolution before a task is persisted.</param>
    /// <returns>The existing typed launch request with its server inheritance resolved once.</returns>
    public async Task<LaunchTask> CreateLaunchAsync(string instanceId, LaunchOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var instance = Instances.Get(instanceId);
        if ((options.ServerAddress ?? options.Server) is { } explicitServer)
            return await CreateServerLaunchAsync(explicitServer, instanceId, options, cancellationToken).ConfigureAwait(false);
        if (options.InheritServer && options.World is null && instance.ServerId is { } owner)
        {
            var favorite = await Servers.GetAsync(owner, cancellationToken).ConfigureAwait(false);
            return FreezeLaunchIdentity(instanceId, options with { ServerAddress = favorite.Address ?? throw new InvalidDataException("The server favorite has no address.") });
        }

        return FreezeLaunchIdentity(instanceId, options with { InheritServer = false });
    }

    /// <summary>Creates the existing typed launch request using an explicit or associated instance. Does not query status or install content.</summary>
    public async Task<LaunchTask> CreateServerLaunchAsync(string addressOrFavoriteId, string? instanceId = null, LaunchOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options?.ServerAddress is { } concreteAddress)
        {
            ServerService.ParseAddress(concreteAddress);
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new LauncherOperationException("SERVER_INSTANCE_REQUIRED", "Choose an existing instance for this connection.");
            return FreezeLaunchIdentity(instanceId, options with { Server = null });
        }

        var favorite = (await Servers.ListAsync(cancellationToken).ConfigureAwait(false)).Select(entry => entry.Favorite)
            .FirstOrDefault(entry => entry?.Id == addressOrFavoriteId);
        var address = favorite?.Address ?? addressOrFavoriteId;
        ServerService.ParseAddress(address);
        if (instanceId is null)
        {
            instanceId = favorite?.PreferredInstanceId;
            if (instanceId is null && favorite?.InstanceIds is { Count: 1 } associated)
                instanceId = associated[0];
        }

        if (string.IsNullOrWhiteSpace(instanceId))
            throw new LauncherOperationException("SERVER_INSTANCE_REQUIRED", "Choose an existing instance for this connection, or associate a preferred instance with the favorite.");
        return FreezeLaunchIdentity(instanceId, (options ?? new()) with { Server = null, ServerAddress = address });
    }

    private LaunchTask FreezeLaunchIdentity(string instanceId, LaunchOptions options)
    {
        var instance = Instances.Get(instanceId);
        options = options with { InheritServer = false };
        if (!options.InheritIdentity)
            return new(instance.Id, options);
        var values = Settings.GetEffective(instance).Values;
        var accountId = options.AccountId ?? values["accountId"]?.GetValue<string>();
        if (accountId is not null)
            _ = Identities.Get(accountId);
        return new(instance.Id, options with
        {
            AccountId = accountId,
            PlayerName = options.PlayerName ?? (accountId is null ? values["playerName"]?.GetValue<string>() ?? "Player" : null),
            InheritIdentity = false,
        });
    }
}