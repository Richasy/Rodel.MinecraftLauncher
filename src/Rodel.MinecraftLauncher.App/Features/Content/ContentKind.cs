// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal enum ContentKind
{
    Mods,
    ResourcePacks,
    Shaders
}

internal enum ContentViewMode
{
    Installed,
    Worlds,
    Screenshots
}

internal static class ContentKinds
{
    internal static string Directory(this ContentKind kind) => kind switch
    {
        ContentKind.Mods => "mods",
        ContentKind.ResourcePacks => "resourcepacks",
        ContentKind.Shaders => "shaderpacks",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string ProjectType(this ContentKind kind) => kind switch
    {
        ContentKind.Mods => "mod",
        ContentKind.ResourcePacks => "resourcepack",
        ContentKind.Shaders => "shader",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string Title(this ContentKind kind) => kind switch
    {
        ContentKind.Mods => Resources.ContentMods,
        ContentKind.ResourcePacks => Resources.ContentResourcePacks,
        ContentKind.Shaders => Resources.ContentShaderPacks,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}