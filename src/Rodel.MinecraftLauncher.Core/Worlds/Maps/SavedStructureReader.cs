// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using System.Diagnostics;
using System.Globalization;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Searches actual region headers and bounded legacy chunk files, never enumerating unsaved coordinates as generation candidates.</summary>
internal static class SavedStructureReader
{
    internal static async Task<SeedStructureResult> ReadAsync(WorldMapSnapshot snapshot, string directory, Dictionary<string, WorldFileStamp> files,
        WorldStructureQuery query, CancellationToken ct)
    {
        if (query.Radius is < 0 or > 8192 || query.Limit is < 1 or > 1024 || query.Targets is null || query.Targets.Length > 1024
            || Math.Abs((long)query.CenterX) > 30_000_000 || Math.Abs((long)query.CenterZ) > 30_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Select a bounded saved-structure radius.");
        }

        foreach (var file in files.Values) file.EnsureUnchanged();
        var timer = Stopwatch.StartNew();
        var candidates = new HashSet<(int X, int Z)>(); var warnings = new HashSet<string>(StringComparer.Ordinal);
        var west = ((long)query.CenterX - query.Radius) >> 4; var east = ((long)query.CenterX + query.Radius) >> 4;
        var north = ((long)query.CenterZ - query.Radius) >> 4; var south = ((long)query.CenterZ + query.Radius) >> 4;
        var region = Path.Combine(directory, "region");
        foreach (var stamp in files.Values.Where(stamp => Path.GetDirectoryName(stamp.Path) == region && Path.GetExtension(stamp.Path) is ".mca" or ".mcr").ToArray())
        {
            ct.ThrowIfCancellationRequested();
            var parts = Path.GetFileName(stamp.Path).Split('.');
            if (parts.Length != 4 || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var rx) || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out var rz)) continue;
            if ((long)rx * 32 > east || ((long)rx * 32) + 31 < west || (long)rz * 32 > south || ((long)rz * 32) + 31 < north) continue;
            try
            {
                stamp.EnsureUnchanged();
                var input = new FileStream(stamp.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                await using var disposal = input.ConfigureAwait(false);
                var header = new byte[4096]; await input.ReadExactlyAsync(header, ct).ConfigureAwait(false);
                for (var slot = 0; slot < 1024; slot++)
                {
                    if ((header[slot * 4] | header[(slot * 4) + 1] | header[(slot * 4) + 2] | header[(slot * 4) + 3]) == 0) continue;
                    var x = ((long)rx * 32) + (slot % 32); var z = ((long)rz * 32) + (slot / 32);
                    if (x >= west && x <= east && z >= north && z <= south) candidates.Add(((int)x, (int)z));
                }

                stamp.EnsureUnchanged();
            }
            catch (WorldSnapshotChangedException) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { if (warnings.Count < 16) warnings.Add("WORLD_STRUCTURE_READ: " + error.Message); }
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "c.*.dat", options))
            {
                ct.ThrowIfCancellationRequested();
                var parts = Path.GetFileName(path).Split('.');
                if (parts.Length != 4 || Base36(parts[1]) is not { } x || Base36(parts[2]) is not { } z || x < west || x > east || z < north || z > south) continue;
                candidates.Add((x, z));
            }
        }

        var found = new Dictionary<(string Id, int X, int Z), SeedPosition>();
        foreach (var (x, z) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var chunk = await WorldRegionReader.ReadAsync(directory, x, z, files, ct).ConfigureAwait(false);
                foreach (var p in chunk?.Structures ?? [])
                {
                    if (query.Targets.Length != 0 && !query.Targets.Contains(p.Id, StringComparer.Ordinal)) continue;
                    if (Math.Pow((long)p.X - query.CenterX, 2) + Math.Pow((long)p.Z - query.CenterZ, 2) > (long)query.Radius * query.Radius) continue;
                    found[(p.Id, p.X, p.Z)] = p;
                }
            }
            catch (WorldSnapshotChangedException) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
            { if (warnings.Count < 16) warnings.Add("WORLD_STRUCTURE_READ: " + error.Message); }
        }

        foreach (var file in files.Values) file.EnsureUnchanged();
        return new()
        {
            Seed = snapshot.Seed ?? string.Empty,
            World = snapshot.Settings with { Dimension = query.Dimension },
            CenterX = query.CenterX,
            CenterZ = query.CenterZ,
            Radius = query.Radius,
            Targets = [.. query.Targets],
            Found = found.Count,
            Source = "saved",
            Warnings = [.. warnings],
            Fingerprint = snapshot.Revision,
            Positions = [.. found.Values.OrderBy(p => Math.Pow((long)p.X - query.CenterX, 2) + Math.Pow((long)p.Z - query.CenterZ, 2)).ThenBy(p => p.Id, StringComparer.Ordinal).ThenBy(p => p.X).ThenBy(p => p.Z).Take(query.Limit)],
            ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
        };
    }

    private static int? Base36(string text)
    {
        var negative = text.StartsWith('-'); long value = 0;
        foreach (var c in text.AsSpan(negative ? 1 : 0))
        {
            var digit = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'z' ? c - 'a' + 10 : -1;
            if (digit < 0 || value > int.MaxValue / 36L) return null;
            value = (value * 36) + digit;
        }

        return value > int.MaxValue ? null : negative ? -(int)value : (int)value;
    }
}