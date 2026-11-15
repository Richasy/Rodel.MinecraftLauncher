// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>Only optional client entries are selected; required and unsupported entries retain their manifest semantics.</summary>
public sealed record PackOptionalSelection(string? Policy = null, IReadOnlyList<string>? IncludePaths = null, IReadOnlyList<string>? ExcludePaths = null)
{
    internal PackOptionalState Resolve(PackOptionalState? previous = null)
    {
        var policy = Policy ?? previous?.Policy ?? "include-all";
        if (policy is not ("include-all" or "exclude-all"))
            throw new ArgumentException("Optional policy must be include-all or exclude-all.");
        // An explicit all-policy replaces previous choices; path-only edits keep them.
        var selections = Policy is null && previous is not null ? new Dictionary<string, bool>(previous.Paths, StringComparer.Ordinal) : [];
        foreach (var path in IncludePaths ?? [])
        {
            _ = PackFiles.SafeRead(Path.GetTempPath(), path);
            selections[path] = true;
        }

        foreach (var path in ExcludePaths ?? [])
        {
            _ = PackFiles.SafeRead(Path.GetTempPath(), path);
            selections[path] = false;
        }

        return new() { Policy = policy, Paths = selections };
    }
}