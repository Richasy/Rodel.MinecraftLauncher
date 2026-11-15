// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Captures only save metadata and chunk file stamps, including unqueried legacy chunks and new dimension folders.</summary>
internal static class WorldMapFileInventory
{
    internal static readonly string[] sourceArray = ["region", "data", "entities", "poi"];

    internal static Dictionary<string, WorldFileStamp> Capture(string directory, IEnumerable<string> dimensions, bool finite, CancellationToken ct)
    {
        var result = new Dictionary<string, WorldFileStamp>(StringComparer.OrdinalIgnoreCase);
        if (finite) return result;
        Add(Path.Combine(directory, "level.dat"));
        Add(Path.Combine(directory, "level.dat_old"));
        Add(Path.Combine(directory, "data", "minecraft", "world_gen_settings.dat"));
        foreach (var name in new[] { "playerdata", "players", Path.Combine("players", "data") })
        {
            var players = Path.Combine(directory, name);
            if (Exists(players)) foreach (var file in Directory.EnumerateFiles(players, "*.dat")) Add(file);
        }

        var roots = new HashSet<string>(dimensions, StringComparer.OrdinalIgnoreCase) { directory };
        if (Exists(directory))
            foreach (var old in Directory.EnumerateDirectories(directory, "DIM*")) roots.Add(old);
        var namespaced = Path.Combine(directory, "dimensions");
        if (Exists(namespaced))
        {
            foreach (var space in Directory.EnumerateDirectories(namespaced))
            {
                if (!Exists(space)) continue;
                var pending = new Stack<(string Path, int Depth)>();
                foreach (var child in Directory.EnumerateDirectories(space)) pending.Push((child, 1));
                while (pending.TryPop(out var item))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Exists(item.Path)) continue;
                    if (sourceArray.Any(name => Directory.Exists(Path.Combine(item.Path, name)))) roots.Add(item.Path);
                    else if (item.Depth < 16)
                        foreach (var child in Directory.EnumerateDirectories(item.Path)) pending.Push((child, item.Depth + 1));
                }
            }
        }

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Exists(root)) continue;
            var region = Path.Combine(root, "region");
            if (Exists(region))
            {
                foreach (var file in Directory.EnumerateFiles(region))
                    if (Path.GetExtension(file) is ".mca" or ".mcr" or ".mcc") Add(file);
            }

            foreach (var x in Directory.EnumerateDirectories(root).Where(IsLegacyCoordinate))
            {
                if (!Exists(x)) continue;
                foreach (var z in Directory.EnumerateDirectories(x).Where(IsLegacyCoordinate))
                    if (Exists(z)) foreach (var file in Directory.EnumerateFiles(z, "c.*.*.dat")) Add(file);
            }
        }

        return result;

        bool Exists(string path)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(path)) return false;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }

        void Add(string path)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            result[path] = WorldFileStamp.Read(path);
        }
    }

    private static bool IsLegacyCoordinate(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length is >= 1 and <= 2 && name.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'z');
    }
}