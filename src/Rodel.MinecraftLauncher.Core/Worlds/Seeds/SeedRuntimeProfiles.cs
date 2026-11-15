// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Worlds.Seeds;

/// <summary>Maps current launcher settings to the original-generation profile shared by manual seeds and saved-world previews.</summary>
internal static class SeedRuntimeProfiles
{
    internal static SeedRuntimeProfile FromInstance(LauncherPaths paths, InstanceRecord instance)
    {
        var java = new SettingsService(paths).GetEffective(instance).Values["javaPath"]?.GetValue<string>() ?? instance.JavaPath;
        return new()
        {
            MinecraftDirectory = instance.MinecraftDirectory,
            VersionId = instance.VersionId ?? string.Empty,
            JavaPath = java is null ? null : JavaDiscovery.ResolveExecutable(java, instance.GameDirectory)
        };
    }
}