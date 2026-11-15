// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Binds the version selector to the exact profile returned by a successfully opened Core runtime.</summary>
internal static class SeedVersionSelection
{
    internal static (SeedVersionOption[] Versions, int Index) Opened(SeedVersionOption[] versions, SeedRuntimeProfile profile, string minecraftVersion)
    {
        var index = Array.FindIndex(versions, item => item.MinecraftVersion == minecraftVersion);
        if (index < 0) return ([.. versions, new(profile, minecraftVersion, true)], versions.Length);

        SeedVersionOption[] updated = [.. versions];
        updated[index] = versions[index] with { Profile = profile, Installed = true, Error = null };
        return (updated, index);
    }
}