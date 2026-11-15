// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Packs;

/// <summary>Limits a creation-form drop to one archive; Core inspects its actual manifest after selection.</summary>
internal static class PackDropSource
{
    internal static bool Accepts(IReadOnlyList<string> files) => files.Count == 1 && File.Exists(files[0])
        && Path.GetExtension(files[0]).ToLowerInvariant() is ".mrpack" or ".zip";
}