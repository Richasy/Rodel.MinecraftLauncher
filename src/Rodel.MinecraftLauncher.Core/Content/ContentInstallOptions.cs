// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Explicit content choices. They do not restrict local jar loading.</summary>
public sealed record ContentInstallOptions
{
    public IReadOnlyDictionary<string, string>? VersionSelections { get; init; }
    /// <summary>Gets explicitly selected Modrinth project IDs keyed by untracked local file name; candidate archives must still match the local mod declarations.</summary>
    public IReadOnlyDictionary<string, string>? SourceSelections { get; init; }
    /// <summary>Exact selections freeze candidates without overriding constraints of retained installed consumers.</summary>
    public bool PreserveRetainedConsumers { get; init; }
    public string? Channel { get; init; }
    public bool IncludeDependencies { get; init; } = true;

    /// <summary>Optional baseline from a previously reviewed plan. Omit for ordinary fresh planning.</summary>
    public string? ExpectedLockFingerprint { get; init; }
    public bool UseCurrentContext { get; init; }
    public string? MinecraftVersion { get; init; }
    public string? Loader { get; init; }
    public string? ContextWarning { get; init; }
}