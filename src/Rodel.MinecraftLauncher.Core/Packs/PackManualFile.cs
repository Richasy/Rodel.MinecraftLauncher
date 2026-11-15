// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>A locally selected missing file, staged for the ordinary pack review rather than installed or trusted by name.</summary>
/// <param name="ManualDirectory">The isolated directory to pass to the next completion/review workflow.</param>
/// <param name="Key">The exact missing declaration path or publisher project:file key.</param>
/// <param name="FileName">The player's selected source name, for display without exposing its parent path.</param>
public sealed record PackManualFile(string ManualDirectory, string Key, string FileName);

public sealed partial class PackService
{
    /// <summary>Copies one selected file to a missing declaration's manual staging slot. Completion still verifies bytes and uses the existing review/apply transaction.</summary>
    /// <param name="instanceId">The incomplete managed instance.</param>
    /// <param name="key">An exact current missing path or publisher project:file key.</param>
    /// <param name="source">The readable file selected by the player, never a folder.</param>
    /// <param name="cancellationToken">Cancels copying without changing the current instance or a previously staged file.</param>
    /// <returns>The directory to use as manualDirectory when the player continues preparation; this is not a verification receipt.</returns>
    public async Task<PackManualFile> StageManualFileAsync(string instanceId, string key, string source, CancellationToken cancellationToken = default)
    {
        var instance = new InstanceStore(_paths).Get(instanceId);
        var missing = GetState(instanceId).Missing.SingleOrDefault(file => (file.ManualKey ?? file.Path) == key)
            ?? throw new ArgumentException("Select an exact current missing pack declaration.", nameof(key));
        var root = PackFiles.Safe(Path.Combine(_paths.Root, "packs", "manual"), instance.Id);
        var relative = (missing.ManualKey is null ? missing.Path : missing.ManualFileName) ?? throw new InvalidDataException("The missing declaration has no local staging slot.");
        var target = PackFiles.Safe(root, relative);
        using var lease = await ResourceLease.AcquireAsync(root, cancellationToken).ConfigureAwait(false);
        var input = File.OpenRead(source);
        await using (input.ConfigureAwait(false))
            await AtomicFile.CopyAsync(input, target, 65536, FileOptions.Asynchronous, cancellationToken).ConfigureAwait(false);
        return new(root, key, Path.GetFileName(source));
    }
}