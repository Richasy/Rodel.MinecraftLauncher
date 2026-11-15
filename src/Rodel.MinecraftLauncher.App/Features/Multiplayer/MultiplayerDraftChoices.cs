// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Worlds;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Loads real save and running-session choices outside rendering, rejecting late results after an instance change.</summary>
internal sealed class MultiplayerDraftChoices(LauncherClient client, Action changed, CancellationToken lifetime)
{
    private int _generation;
    private string _instance = string.Empty;
    private readonly Dictionary<string, string> _gameWorlds = [];
    internal IReadOnlyList<WorldSummary> Worlds { get; private set; } = [];
    internal IReadOnlyList<SessionStatus> Games { get; private set; } = [];
    internal string? WorldFolder { get; private set; }
    internal string? SessionId { get; private set; }
    internal string? IconData { get; private set; }
    internal bool Loading { get; private set; }
    internal string Error { get; private set; } = string.Empty;
    internal WorldSummary? World => Worlds.FirstOrDefault(item => item.Folder == WorldFolder);
    internal bool HasGame => SessionId is not null;
    internal string? CurrentWorldName => SessionId is { } id ? _gameWorlds.GetValueOrDefault(id) : null;
    internal IEnumerable<string> GameLabels => Games.Select(game => string.Join(" · ", new[] {
        game.Id is { } id ? _gameWorlds.GetValueOrDefault(id) : null, game.VersionId, game.Server,
        DateTimeOffset.TryParse(game.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created)
            ? created.ToLocalTime().ToString("T", CultureInfo.CurrentCulture) : null }.Where(value => !string.IsNullOrEmpty(value))));
    internal bool CanLaunch => !Loading && (Games.Count == 0 || Games.Any(item => item.Id == SessionId));

    internal async Task LoadAsync(string instance)
    {
        var generation = ++_generation;
        var same = _instance == instance;
        _instance = instance;
        if (!same) { WorldFolder = null; SessionId = null; IconData = null; }

        Worlds = []; Games = []; _gameWorlds.Clear(); Error = string.Empty; Loading = true; changed();
        try
        {
            if (instance.Length == 0) return;
            var games = await Task.Run(() => client.Sessions.List().Where(item => item.InstanceId == instance && item.Status == "running" && item.ActivityUnknown != true).ToArray(), lifetime);
            IReadOnlyList<WorldSummary> worlds = [];
            try { worlds = await Task.Run(() => client.Worlds.ListAsync(instance, lifetime), lifetime); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }

            if (generation != _generation || lifetime.IsCancellationRequested) return;
            Games = games;
            Worlds = [.. worlds.OrderByDescending(item => item.LastPlayed)];
            SessionId = games.Any(item => item.Id == SessionId) ? SessionId : games.Length == 1 ? games[0].Id : null;
            WorldFolder = same && (WorldFolder is null || Worlds.Any(item => item.Folder == WorldFolder)) ? WorldFolder : Worlds.Count > 0 ? Worlds[0].Folder : null;
            foreach (var game in games)
            {
                if (game.Id is not { } id) continue;
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var snapshot = await client.GameBridge.SnapshotAsync(id, deadline.Token);
                    if (snapshot is not { Ready: true, Mode: "local_owned", WorldEpoch: { } epoch, Dimension: { } dimension }) continue;
                    var result = await client.GameBridge.QueryAsync(id, new GameQueryRequest { Kind = "world", GameIncarnation = snapshot.GameIncarnation, WorldEpoch = epoch, Dimension = dimension }, deadline.Token);
                    if (generation != _generation || lifetime.IsCancellationRequested) return;
                    if (result.Data.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String)
                        _gameWorlds[id] = name.GetString() ?? string.Empty;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or LauncherOperationException) { }
            }

            await ReadIconAsync(generation);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (generation == _generation) Error = error.Message; }
        finally { if (generation == _generation) { Loading = false; changed(); } }
    }

    internal void SelectWorld(int index)
    {
        WorldFolder = index > 0 && index <= Worlds.Count ? Worlds[index - 1].Folder : null;
        IconData = null; changed();
        _ = ReadIconAsync(_generation);
    }

    internal void SelectGame(int index)
    {
        SessionId = index >= 0 && index < Games.Count ? Games[index].Id : null;
        changed();
    }

    private async Task ReadIconAsync(int generation)
    {
        var folder = WorldFolder;
        if (World?.Icon is not { } icon) return;
        try
        {
            var bytes = await Features.Content.WorldIconReader.ReadAsync(icon, lifetime);
            if (generation == _generation && folder == WorldFolder && bytes is not null && !lifetime.IsCancellationRequested)
            { IconData = Convert.ToBase64String(bytes); changed(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
    }
}