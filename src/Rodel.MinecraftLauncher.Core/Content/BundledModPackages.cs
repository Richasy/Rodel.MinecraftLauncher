// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Identifies one actually shipped runtime combination and its module filenames.</summary>
/// <param name="MinecraftVersion">The exact base game release.</param>
/// <param name="Loader">The actual loader, independent of compatible metadata formats.</param>
/// <param name="Modules">The shipped module stems, including required shared dependencies.</param>
internal sealed record BundledModTarget(string MinecraftVersion, string Loader, string[] Modules);

/// <summary>Describes build-produced runtime combinations; unsupported combinations are never inferred.</summary>
/// <param name="Targets">The complete shipped artifact matrix.</param>
internal sealed record BundledModManifest(BundledModTarget[] Targets);

/// <summary>Holds one verified shipped archive and its native metadata for dependency review and publication.</summary>
/// <param name="Source">The host-owned source archive.</param>
/// <param name="Id">The native mod identity.</param>
/// <param name="Name">The native display name.</param>
/// <param name="Version">The shipped mod release.</param>
/// <param name="MinecraftVersion">The exact game target.</param>
/// <param name="Loader">The loader-specific build target.</param>
/// <param name="Sha1">The actual archive digest.</param>
/// <param name="Size">The actual archive length.</param>
/// <param name="Metadata">The original extensible mod metadata.</param>
/// <param name="Dependencies">Required dependencies parsed from that same metadata.</param>
internal sealed record BundledModPackage(string Source, string Id, string Name, string Version, string MinecraftVersion,
    string Loader, string Sha1, long Size, JsonObject Metadata, JsonObject Dependencies)
{
    private static readonly Lazy<Task<BundledModTarget[]>> Targets = new(() => ReadTargetsAsync(CancellationToken.None));
    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, Lazy<Task<IReadOnlyList<BundledModPackage>>>> Packages = new(StringComparer.Ordinal);
    internal string FileName => Id + "-" + Version + "-" + MinecraftVersion + "-" + Loader + ".jar";
    internal static Task<BundledModTarget[]> ReadCachedTargetsAsync(CancellationToken ct) => Targets.Value.WaitAsync(ct);
    internal static async Task<IReadOnlyList<BundledModPackage>> ReadCachedAsync(BundledModTarget target, CancellationToken ct)
    {
        Lazy<Task<IReadOnlyList<BundledModPackage>>> pending;
        lock (CacheGate)
        {
            var key = target.MinecraftVersion + "/" + target.Loader;
            if (!Packages.TryGetValue(key, out pending!))
                Packages[key] = pending = new(() => ReadAsync(target, CancellationToken.None));
        }

        try { return await pending.Value.WaitAsync(ct).ConfigureAwait(false); }
        catch when (pending.IsValueCreated && pending.Value.IsFaulted)
        {
            lock (CacheGate)
            {
                var key = target.MinecraftVersion + "/" + target.Loader;
                if (Packages.GetValueOrDefault(key) == pending) Packages.Remove(key);
            }

            throw;
        }
    }
    internal static async Task<BundledModTarget[]> ReadTargetsAsync(CancellationToken ct)
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "BuiltInMods", "catalog.json"));
        var catalog = await JsonSerializer.DeserializeAsync(source, BundledModJsonContext.Default.BundledModManifest, ct).ConfigureAwait(false);
        var targets = catalog?.Targets ?? [];
        if (targets.Length == 0 || targets.Any(target => target is null || !System.Version.TryParse(target.MinecraftVersion, out _)
            || target.Loader is not ("fabric" or "quilt" or "forge" or "neoforge") || target.Modules is null || target.Modules.Length == 0
            || target.Modules.Distinct(StringComparer.Ordinal).Count() != target.Modules.Length
            || target.Modules.Any(module => module is not ("rodel-ai" or "world-reroll" or "bettertools" or "fabric-api")))
            || targets.Select(target => (target.MinecraftVersion, target.Loader)).Distinct().Count() != targets.Length)
        {
            throw new InvalidDataException("The bundled Minecraft target catalog is invalid.");
        }

        return targets;
    }
    internal static async Task<IReadOnlyList<BundledModPackage>> ReadAsync(BundledModTarget target, CancellationToken ct)
    {
        var result = new List<BundledModPackage>();
        foreach (var file in target.Modules)
        {
            var expectedId = file switch { "rodel-ai" => "rodel_ai", "world-reroll" => "worldreroll", "bettertools" => "bettertools", "fabric-api" => "fabric-api", _ => throw new InvalidDataException("Unknown bundled module.") };
            if (!System.Version.TryParse(target.MinecraftVersion, out _) || target.Loader is not ("fabric" or "quilt" or "forge" or "neoforge"))
                throw new InvalidDataException("Invalid bundled runtime target.");
            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "BuiltInMods", target.MinecraftVersion, target.Loader, file + ".jar");
            var entry = await ContentService.InspectPackageAsync(source, ct).ConfigureAwait(false);
            var metadata = entry.Metadata?.AsObject() ?? throw new InvalidDataException("Bundled metadata is missing.");
            var id = metadata["id"]?.GetValue<string>() ?? metadata["modId"]?.GetValue<string>();
            var version = metadata["version"]!.GetValue<string>();
            if (id != expectedId || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version is "" or "." or "..")
                throw new InvalidDataException("The bundled mod identity is invalid.");
            if (id != "fabric-api" && target.Loader is "fabric" or "quilt" && metadata["depends"]?["minecraft"]?.GetValue<string>() != target.MinecraftVersion)
                throw new InvalidDataException("A bundled mod targets a different Minecraft version.");
            var expectedFormat = target.Loader switch { "forge" => "META-INF/mods.toml", "neoforge" => "META-INF/neoforge.mods.toml", _ => "fabric.mod.json" };
            if (entry.MetadataFormat != expectedFormat)
                throw new InvalidDataException("The bundled mod has incompatible loader metadata.");
            var dependencies = await ContentService.ReadPackageDependenciesAsync(source, entry, id!, ct).ConfigureAwait(false);
            if (dependencies.ContainsKey("rodel_game_ui"))
                throw new InvalidDataException("Bundled mods must be independent of Rodel UI.");
            if (target.Loader is "forge" or "neoforge" && (dependencies["minecraft"]?.GetValue<string>() != "[" + target.MinecraftVersion + "]" || dependencies[target.Loader] is null))
                throw new InvalidDataException("The bundled mod does not declare its exact Minecraft/loader target.");
            result.Add(new(source, id!, metadata["name"]?.GetValue<string>() ?? metadata["displayName"]!.GetValue<string>(), version, target.MinecraftVersion,
                target.Loader, entry.Sha1!, entry.Size!.Value, metadata, dependencies));
        }

        return result;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BundledModManifest))]
internal partial class BundledModJsonContext : JsonSerializerContext;