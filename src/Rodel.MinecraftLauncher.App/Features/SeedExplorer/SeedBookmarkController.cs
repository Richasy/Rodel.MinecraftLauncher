// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Connects the two bookmark segments to Core persistence and restores each record's own generation context.</summary>
internal sealed class SeedBookmarkController(SeedExplorerController owner)
{
    private readonly Dictionary<string, int> _keys = [];
    private int _nextKey;
    private long _refreshRevision;
    private SeedExplorerSnapshot Current { get => owner.State.Current; set { if (!owner.Disposed) owner.State.Current = value; } }
    internal int Key(string id) { if (!_keys.TryGetValue(id, out var key)) _keys[id] = key = ++_nextKey; return key; }
    internal async Task RefreshAsync()
    {
        var revision = ++_refreshRevision;
        try
        {
            var bookmarks = await Task.Run(() => owner.Client.Seeds.Bookmarks.List(), owner.Lifetime.Token);
            if (revision == _refreshRevision && !owner.Lifetime.IsCancellationRequested)
                Current = Current with { Bookmarks = bookmarks };
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (revision == _refreshRevision && !owner.Lifetime.IsCancellationRequested)
                owner.Error(error);
        }
    }

    internal bool Saved(SeedPosition? position = null) => Find(position) is not null;
    private SeedBookmark? Find(SeedPosition? position) => Current.Bookmarks.FirstOrDefault(bookmark => bookmark.Kind == (position is null ? "seed" : "place")
        && (Current.UseSavedMap ? bookmark.SavedWorld == Current.WorldSnapshot?.Selection : bookmark.SavedWorld is null && bookmark.Seed == Current.Seed && bookmark.Fingerprint == Current.Fingerprint)
        && bookmark.World == Current.World && bookmark.SliceY == Current.SliceY
        && (position is null || (bookmark.X == position.X && bookmark.Z == position.Z && bookmark.Position?.Id == position.Id)));

    internal async Task ToggleAsync(SeedPosition? position = null, int teleportY = 80)
    {
        var snapshot = Current;
        if (snapshot.Profile is null || (!snapshot.UseSavedMap && snapshot.Seed.Length == 0) || snapshot.Opening || snapshot.BookmarkBusy) return;
        Current = Current with { BookmarkBusy = true };
        try
        {
            if (Find(position) is { } previous)
            {
                await RemoveBookmarkAsync(previous.Id);
            }
            else
            {
                await Task.Run(() => owner.Client.Seeds.Bookmarks.SaveAsync(new()
                {
                    Kind = position is null ? "seed" : "place",
                    Name = position is null ? snapshot.UseSavedMap ? snapshot.WorldSnapshot!.Name : snapshot.Seed : position.Id,
                    SavedWorld = snapshot.UseSavedMap ? snapshot.WorldSnapshot!.Selection : null,
                    Profile = snapshot.Profile with { },
                    Fingerprint = snapshot.Fingerprint,
                    Seed = snapshot.Seed,
                    World = snapshot.World with { },
                    SliceY = snapshot.SliceY,
                    X = position?.X ?? (int)Math.Round(snapshot.CenterX),
                    Z = position?.Z ?? (int)Math.Round(snapshot.CenterZ),
                    TeleportY = teleportY,
                    BlocksPerPixel = snapshot.BlocksPerPixel,
                    Position = position,
                }, owner.Lifetime.Token));
            }

            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { owner.Error(error); }
        finally { Current = Current with { BookmarkBusy = false }; }
    }

    internal async Task RemoveAsync(string id)
    {
        try { await RemoveBookmarkAsync(id); await RefreshAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { owner.Error(error); }
    }
    private async Task RemoveBookmarkAsync(string id)
    {
        await Task.Run(() => owner.Client.Seeds.Bookmarks.RemoveAsync(id, owner.Lifetime.Token));
        // Invalidate reads started before the mutation and remove the entry before reloading its neighbours.
        _refreshRevision++;
        Current = Current with { Bookmarks = [.. Current.Bookmarks.Where(bookmark => bookmark.Id != id)] };
    }
    internal async Task OpenAsync(SeedBookmark bookmark)
    {
        owner.DismissPopups?.Invoke();
        if (bookmark.SavedWorld is not null) { await owner.Worlds.OpenBookmarkAsync(bookmark); return; }

        await owner.Worlds.SelectSourceAsync(false);
        await owner.Map.OpenAsync(bookmark.Profile, bookmark.World, bookmark.SliceY, bookmark.Seed, bookmark: bookmark);
    }
    internal void Copy(SeedBookmark bookmark) => owner.Copy(bookmark.Kind == "place" ? SeedCommands.Teleport(bookmark.World, bookmark.X, bookmark.TeleportY, bookmark.Z) : bookmark.Seed);
}