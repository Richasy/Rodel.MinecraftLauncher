// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Downloads;

/// <summary>Resolves the selected public download source while retaining explicit user mirror overrides.</summary>
internal static class DownloadSources
{
    internal static JsonObject Mirrors(JsonObject settings)
    {
        var mirrors = new JsonObject();
        if (settings["source"]?.ToString() == "bmclapi")
        {
            Add("launchermeta.mojang.com", "/", "/");
            Add("launcher.mojang.com", "/", "/");
            Add("piston-meta.mojang.com", "/", "/");
            Add("piston-data.mojang.com", "/", "/");
            Add("resources.download.minecraft.net", "/", "/assets/");
            Add("libraries.minecraft.net", "/", "/maven/");
            Add("files.minecraftforge.net", "/maven/", "/maven/");
            Add("maven.minecraftforge.net", "/", "/maven/");
            Add("maven.neoforged.net", "/releases/", "/maven/");
            Add("maven.neoforged.net", "/repository/releases/", "/maven/");
            Add("meta.fabricmc.net", "/", "/fabric-meta/");
            Add("maven.fabricmc.net", "/", "/maven/");
            Add("meta.quiltmc.org", "/", "/quilt-meta/");
            Add("maven.quiltmc.org", "/repository/release/", "/maven/");
            Add("authlib-injector.yushi.moe", "/", "/mirrors/authlib-injector/");
        }

        if (settings["mirrors"] is JsonObject custom)
        {
            foreach (var pair in custom)
                mirrors[pair.Key] = pair.Value?.DeepClone();
        }

        return mirrors;

        void Add(string host, string path, string target)
        {
            foreach (var scheme in new[] { "https", "http" })
                mirrors[scheme + "://" + host + path] = "https://bmclapi2.bangbang93.com" + target;
        }
    }
}