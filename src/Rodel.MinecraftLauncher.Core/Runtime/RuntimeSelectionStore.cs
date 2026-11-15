// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Runtime;

/// <summary>Owns runtime selection snapshots, commit leases and recoverable history.</summary>
internal sealed class RuntimeSelectionStore(LauncherPaths paths)
{
    private readonly InstanceStore _instances = new(paths);
    private static readonly string[] RuntimeKeys = ["versionId", "loader", "migrationRuntime"];
    private static bool RuntimeEquals(JsonObject current, JsonObject expected) => RuntimeKeys.All(key => JsonNode.DeepEquals(current[key], expected[key]));
    internal static JsonObject Snapshot(string metadata) => JsonFiles.ReadObject(metadata)["settings"]?.DeepClone() as JsonObject ?? [];
    internal async Task<RuntimeCommitResult> CommitAsync(string instanceId, string desired, JsonObject expected, string source, bool preserveMigration, CancellationToken cancellationToken)
    {
        var instance = _instances.Get(instanceId);
        var target = await RuntimeProfile.DescribeAsync(instance.MinecraftDirectory, desired, cancellationToken).ConfigureAwait(false);
        var targetSummary = RuntimeTarget.From(target);
        var targetJson = JsonSerializer.SerializeToNode(targetSummary, RuntimeJsonContext.Default.RuntimeTarget)!.AsObject();
        var warnings = new List<string>();
        // Installer leases may use the same directory as the game. Acquire our commit lease only after they complete.
        using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (new SessionService(paths).HasActiveGame(instance.GameDirectory))
            throw new IOException("Game started during preparation; files are reusable and the runtime selection is unchanged.");
        using var metadataLease = await ResourceLease.AcquireAsync(resource: instance.MetadataPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var record = JsonFiles.ReadObject(instance.MetadataPath);
        var previous = record["settings"]?.DeepClone() as JsonObject ?? [];
        if (!RuntimeEquals(previous, expected))
            throw new IOException("Runtime selection changed during preparation; prepared files remain reusable. Review the current selection and retry.");
        var next = (JsonObject)previous.DeepClone();
        next["versionId"] = desired;
        next["loader"] = target.Loader;
        // Persist null as an instance override so a stale global loader cannot leak into vanilla content filtering.
        if (!preserveMigration)
            next["migrationRuntime"] = null;
        var operationId = Guid.NewGuid().ToString("N");
        var operationFile = RuntimeOperationPath(operationId);
        var operation = new JsonObject
        {
            ["id"] = operationId,
            ["instanceId"] = instance.Id,
            ["previousSettings"] = previous,
            ["targetSettings"] = next.DeepClone(),
            ["target"] = targetJson.DeepClone(),
            ["source"] = source,
            ["state"] = "prepared",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        await JsonFiles.WriteAsync(operationFile, operation, cancellationToken).ConfigureAwait(false);
        record["settings"] = next;
        await JsonFiles.WriteAsync(instance.MetadataPath, record, cancellationToken).ConfigureAwait(false);
        operation["state"] = "applied";
        try
        {
            await JsonFiles.WriteAsync(operationFile, operation, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            warnings.Add("Runtime committed; snapshot status write failed, rollback remains available: " + ex.Message);
        }

        return new(operationId, targetSummary, warnings);
    }

    internal RuntimeOperationSummary Get(string instanceId, string id)
    {
        var operation = JsonFiles.ReadObject(RuntimeOperationPath(id));
        if (operation["instanceId"]?.ToString() != instanceId)
            throw new ArgumentException("Runtime operation belongs to another instance.");
        if (operation["previousSettings"] is not JsonObject previous || operation["targetSettings"] is not JsonObject target)
            throw new InvalidDataException("Runtime operation has invalid settings snapshots.");
        var current = Snapshot(_instances.Get(instanceId).MetadataPath);
        var state = StoredReportFields.Text(operation, "state");
        var createdAt = StoredReportFields.Text(operation, "createdAt");
        var savedTarget = operation["target"] as JsonObject;
        if (savedTarget is not null)
        {
            savedTarget = (JsonObject)savedTarget.DeepClone();
            operation.Remove("target");
        }

        operation.Remove("id");
        if (state is not null) operation.Remove("state");
        operation.Remove("observedState");
        return new()
        {
            Id = id,
            State = state,
            CreatedAt = createdAt,
            Target = savedTarget,
            ObservedState = RuntimeEquals(current, previous) ? "previous-selected" : RuntimeEquals(current, target) ? "target-selected" : "selection-changed",
            AdditionalData = StoredReportFields.Remaining(operation)
        };
    }

    internal RuntimeOperationList List(string instanceId)
    {
        var operations = new List<RuntimeOperationSummary>();
        var folder = Path.Combine(paths.Root, "runtime-operations");
        foreach (var file in Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").OrderDescending(StringComparer.Ordinal) : Enumerable.Empty<string>())
        {
            var id = Path.GetFileNameWithoutExtension(file);
            try
            {
                var header = JsonFiles.ReadObject(file);
                if (header["instanceId"]?.ToString() == instanceId)
                    operations.Add(Get(instanceId, id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                operations.Add(new() { Id = id, State = "unreadable", Ownership = "unverified", Warning = ex.Message });
            }
        }

        return new(instanceId, operations);
    }

    private string RuntimeOperationPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid runtime operation id.");
        return Path.Combine(paths.Root, "runtime-operations", id + ".json");
    }

    internal async Task<RuntimeRollbackResult> RollbackAsync(string instanceId, string operationId, CancellationToken cancellationToken)
    {
        var instance = _instances.Get(instanceId);
        using var gameLease = await ResourceLease.AcquireAsync(resource: instance.GameDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (new SessionService(paths).HasActiveGame(instance.GameDirectory))
            throw new IOException("Retry runtime rollback after this game exits.");
        using var metadataLease = await ResourceLease.AcquireAsync(resource: instance.MetadataPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var operation = JsonFiles.ReadObject(RuntimeOperationPath(operationId));
        if (operation["instanceId"]?.ToString() != instanceId)
            throw new ArgumentException("Runtime operation belongs to another instance.");
        var record = JsonFiles.ReadObject(instance.MetadataPath);
        var current = record["settings"] as JsonObject ?? [];
        // Only change fields owned by this operation; retain subsequent unrelated settings edits.
        var target = operation["targetSettings"]!.AsObject();
        var previous = operation["previousSettings"]!.AsObject();
        if (RuntimeEquals(current, previous))
        {
            operation["state"] = "rolled-back";
            await JsonFiles.WriteAsync(RuntimeOperationPath(operationId), operation, cancellationToken).ConfigureAwait(false);
            return RuntimeRollbackResult.Read(operation);
        }

        foreach (var key in RuntimeKeys)
        {
            if (!JsonNode.DeepEquals(current[key], target[key]))
                throw new IOException("The selected runtime changed again; inspect it before rolling back this older operation.");
        }

        foreach (var key in RuntimeKeys)
        {
            if (previous.ContainsKey(key))
                current[key] = previous[key]?.DeepClone();
            else
                current.Remove(key);
        }

        record["settings"] = current.DeepClone();
        await JsonFiles.WriteAsync(instance.MetadataPath, record, cancellationToken).ConfigureAwait(false);
        operation["state"] = "rolled-back";
        await JsonFiles.WriteAsync(RuntimeOperationPath(operationId), operation, cancellationToken).ConfigureAwait(false);
        return RuntimeRollbackResult.Read(operation);
    }
}