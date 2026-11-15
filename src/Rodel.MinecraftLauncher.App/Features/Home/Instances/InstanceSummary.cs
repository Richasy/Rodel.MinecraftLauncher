// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Home;

internal sealed record InstanceSummary(
    string Id,
    string Name,
    string Version,
    bool Unreadable,
    string? IconData = null,
    string? ServerId = null,
    string? Loader = null,
    string? PackName = null)
{
    internal string DisplayName => string.IsNullOrEmpty(Name) ? Resources.UnreadableInstance : Name;
    internal string DisplayVersion => string.IsNullOrEmpty(Version) ? Resources.VersionNotConfigured : Version;

    internal string Subtitle(string versionFallback, string vanilla) => string.Join(" · ", new[] { string.IsNullOrWhiteSpace(Version) ? versionFallback : Version, Loader is "vanilla" or "none" ? vanilla : Loader, PackName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}