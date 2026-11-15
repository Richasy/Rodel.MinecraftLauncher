// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Keeps the active viewport's requested tiles resident while bounding inactive map history by recency.</summary>
internal sealed class SeedMapTileCache(int capacity = 192)
{
    private readonly Dictionary<string, (SeedRenderedTile Tile, long Used)> _entries = [with(StringComparer.Ordinal)];
    private HashSet<string> _active = [with(StringComparer.Ordinal)];
    private long _clock;

    internal IEnumerable<SeedRenderedTile> Tiles => _entries.Values.Select(entry => entry.Tile);

    internal void Protect(IEnumerable<string> keys)
    {
        _active = new(keys, StringComparer.Ordinal);
        Evict();
    }

    internal bool TryGet(string key, [NotNullWhen(true)] out SeedRenderedTile? tile)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            tile = entry.Tile;
            _entries[key] = (tile, ++_clock);
            return true;
        }

        tile = null;
        return false;
    }

    internal void Store(SeedRenderedTile tile)
    {
        _entries[tile.RequestKey] = (tile, ++_clock);
        Evict();
    }

    internal void Clear() { _entries.Clear(); _active.Clear(); }

    private void Evict()
    {
        foreach (var key in _entries.Where(entry => !_active.Contains(entry.Key)).OrderBy(entry => entry.Value.Used)
            .Take(Math.Max(0, _entries.Count - capacity)).Select(entry => entry.Key).ToArray())
        {
            _entries.Remove(key);
        }
    }
}