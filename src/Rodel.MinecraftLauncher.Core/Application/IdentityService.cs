// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Manages account registrations and protected authentication sessions for one launcher data root.</summary>
/// <param name="paths">Owned launcher storage.</param>
/// <param name="authenticationHttp">Optional caller-owned HTTP transport for external authentication and skin profile queries.</param>
public sealed partial class IdentityService(LauncherPaths paths, HttpClient? authenticationHttp = null)
{
    private string FilePath => Path.Combine(paths.Root, "identities.json");

    private JsonArray ReadRecords() => File.Exists(FilePath) ? JsonFiles.ReadObject(FilePath)["identities"]?.DeepClone().AsArray() ?? [] : [];
    /// <summary>Reads all identities, preserving unfamiliar persisted array entries.</summary>
    public IReadOnlyList<IdentityEntry> List() => [.. ReadRecords().Select(IdentityEntry.Read)];
    /// <summary>Registers or returns the deterministic offline identity, selecting a newly created first account as the default.</summary>
    public async Task<IdentityRecord> AddOfflineAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        var document = File.Exists(FilePath) ? JsonFiles.ReadObject(FilePath) : [];
        var entries = document["identities"] as JsonArray ?? [];
        var uuid = LaunchPlanner.OfflineUuid(name);
        var entry = entries.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.GetValue<string>() == uuid);
        if (entry is null)
        {
            var firstAccount = entries.Count == 0;
            entry = new JsonObject
            {
                ["id"] = uuid,
                ["name"] = name,
                ["type"] = "offline",
                ["uuid"] = uuid
            };
            entries.Add((JsonNode)entry);
            if (entries.Parent is null) document["identities"] = entries;
            await JsonFiles.WriteAsync(FilePath, document, ct).ConfigureAwait(false);
            await SelectFirstAccountAsync(firstAccount, uuid, ct).ConfigureAwait(false);
        }

        return IdentityRecord.Read(entry);
    }

    private Task SelectFirstAccountAsync(bool firstAccount, string id, CancellationToken ct) => firstAccount
        ? new Configuration.SettingsService(paths).SetAsync("accountId", JsonValue.Create(id), cancellationToken: ct)
        : Task.CompletedTask;

    /// <summary>Reads one stored identity without discarding its unknown metadata.</summary>
    public IdentityRecord Get(string id) => IdentityRecord.Read(ReadRecords().OfType<JsonObject>().FirstOrDefault(x => x["id"]?.GetValue<string>() == id) ?? throw new LauncherOperationException("ACCOUNT_NOT_FOUND", "The selected account was removed or is unavailable. Select an account again."));
    /// <summary>Removes an account and its protected credentials, clearing defaults while retaining game data.</summary>
    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        using var lease = await ResourceLease.AcquireAsync(resource: FilePath, cancellationToken: ct).ConfigureAwait(false);
        var settings = new Configuration.SettingsService(paths);
        if (settings.Get("accountId").Value?.GetValue<string>() == id)
            await settings.SetAsync("accountId", null, cancellationToken: ct).ConfigureAwait(false);
        foreach (var instance in new Instances.InstanceStore(paths).List())
        {
            if (JsonFiles.ReadObject(instance.MetadataPath)["settings"]?["accountId"]?.GetValue<string>() == id)
                await settings.SetAsync("accountId", null, instance, ct).ConfigureAwait(false);
        }

        var document = File.Exists(FilePath) ? JsonFiles.ReadObject(FilePath) : [];
        var entries = document["identities"] as JsonArray ?? [];
        var match = entries.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.GetValue<string>() == id);
        if (match is not null)
            entries.Remove(match);
        if (entries.Parent is null) document["identities"] = entries;
        await JsonFiles.WriteAsync(FilePath, document, ct).ConfigureAwait(false);
        if (File.Exists(CredentialPath(id))) File.Delete(CredentialPath(id));
    }
}