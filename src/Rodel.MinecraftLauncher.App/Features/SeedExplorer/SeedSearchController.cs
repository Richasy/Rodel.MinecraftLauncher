// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using Sprout;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Coordinates editable positive conditions and retained Core search attempts without owning another generation algorithm.</summary>
internal sealed class SeedSearchController(SeedExplorerController owner) : IDisposable
{
    private readonly Dictionary<string, SeedCondition[]> _drafts = [];
    private readonly HashSet<string> _thumbnailPending = [];
    private CancellationTokenSource? _attempt, _previews;
    private Task _running = Task.CompletedTask, _thumbnailTask = Task.CompletedTask;
    private int _generation, _previewGeneration, _conditionKey;
    private string _draftKey = string.Empty;
    private SeedExplorerSnapshot Current { get => owner.State.Current; set { if (!owner.Disposed) owner.State.Current = value; } }

    internal void LoadDrafts(Dictionary<string, SeedCondition[]> drafts)
    {
        foreach (var pair in drafts) _drafts[pair.Key] = pair.Value;
    }
    internal Dictionary<string, SeedCondition[]> CaptureDrafts()
    {
        if (_draftKey.Length > 0) _drafts[_draftKey] = Current.Conditions;
        return [with(_drafts)];
    }

    internal async Task RefreshAsync()
    {
        try
        {
            var searches = await Task.Run(() => owner.Client.Seeds.Searches.List(), owner.Lifetime.Token);
            Current = Current with { Searches = searches };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { owner.Error(error); }
    }

    internal void RestoreConditions()
    {
        _previews?.Cancel(); _thumbnailPending.Clear(); _previewGeneration++;
        var snapshot = Current;
        if (snapshot.Profile is null) return;
        var next = SeedWorkspaceDraft.Key(snapshot.Profile, snapshot.World);
        if (next == _draftKey) return;
        if (_draftKey.Length > 0) _drafts[_draftKey] = snapshot.Conditions;
        _draftKey = next;
        if (_drafts.TryGetValue(next, out var conditions)) { Current = Current with { Conditions = conditions }; return; }

        Current = Current with { Conditions = [] };
    }

    internal async Task AddAsync(string kind)
    {
        if (Current.Searching || Current.Opening || Current.CatalogLoading || Current.Profile is null || Current.Conditions.Length >= 12) return;
        var profile = Current.Profile; var world = Current.World;
        await owner.Map.EnsureCatalogAsync();
        if (owner.Disposed || Current.Opening || Current.Searching || Current.Conditions.Length >= 12
            || Current.Profile != profile || Current.World != world || Current.Catalog is null)
        {
            return;
        }

        var target = SeedText.Targets(Current, kind).FirstOrDefault()?.Targets;
        if (target is null) return;
        Current = Current with
        {
            Conditions = [.. Current.Conditions, new() { Id = NextId(), Kind = kind, Targets = target,
            Reference = world.Dimension == "overworld" && Current.Catalog?.SeedBoundSpawn != false ? "spawn" : "origin", Y = kind == "biome" ? Current.SliceY : null }]
        };
    }
    private string NextId()
    {
        string id;
        do { id = "condition" + (++_conditionKey).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        while (Current.Conditions.Any(condition => condition.Id == id));
        return id;
    }
    internal void Change(SeedCondition condition)
    {
        if (Current.Searching) return;
        Current = Current with { Conditions = [.. Current.Conditions.Select(item => item.Id == condition.Id ? condition : item)] };
    }
    internal void ChangeBiomeStep(int value)
    {
        if (Current.Searching || value is < 4 or > 256 || Current.BiomeStep == value) return;
        Current = Current with { BiomeStep = value };
    }
    internal void Remove(string id)
    {
        if (Current.Searching) return;
        Current = Current with { Conditions = [.. Current.Conditions.Where(item => item.Id != id).Select(item => item.Reference == id ? item with { Reference = "origin" } : item)] };
    }

    internal Task StartAsync()
    {
        if (Current.Searching || Current.Opening || Current.Profile is null || Current.Conditions.Length == 0) return Task.CompletedTask;
        return _running = RunAsync(null);
    }
    internal Task ResumeAsync(SeedSearchState state)
    {
        if (Current.Searching) return Task.CompletedTask;
        return _running = RunAsync(state);
    }

    internal async Task ShowHistoryAsync(SeedSearchState state)
    {
        if (Current.Searching) return;
        try
        {
            if (Current.Profile != state.Query.Profile || Current.World != state.Query.World || Current.Catalog is null || Current.Fingerprint.Length == 0 || Current.Opening)
            {
                if (!await owner.Map.OpenAsync(state.Query.Profile, state.Query.World, state.Query.World.Dimension == "the_nether" ? 64 : null,
                    state.Query.StartSeed, keepSearch: true, loadMap: Current.Seed.Length > 0, expectedFingerprint: state.Fingerprint))
                {
                    return;
                }
            }

            if (state.Fingerprint.Length > 0 && state.Fingerprint != Current.Fingerprint) throw new InvalidOperationException(Resources.SeedRuntimeChanged);
            _previews?.Cancel(); _previewGeneration++; _thumbnailPending.Clear();
            Current = Current with
            {
                Search = state,
                Conditions = state.Query.Conditions,
                SearchTab = 1,
                Previews = [],
                SearchMode = true,
                SearchSeedBudget = state.Query.MaxSeeds,
                SearchResultBudget = state.Query.MaxResults,
                SearchSeconds = state.Query.TimeLimitSeconds,
                BiomeStep = state.Query.BiomeStep
            };
            Publish(state, ++_generation);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { owner.Error(error); }
    }

    private async Task RunAsync(SeedSearchState? retained)
    {
        if (retained is null || Current.Search?.Id != retained.Id)
        {
            _previews?.Cancel(); _previewGeneration++; _thumbnailPending.Clear();
            Current = Current with { Previews = [] };
        }

        var generation = ++_generation;
        _attempt?.Dispose(); _attempt = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var ct = _attempt.Token;
        Current = Current with { Searching = true, Search = retained, SearchTab = 1, Error = string.Empty };
        try
        {
            if (retained is not null && (Current.Profile != retained.Query.Profile || Current.World != retained.Query.World))
            {
                if (!await owner.Map.OpenAsync(retained.Query.Profile, retained.Query.World,
                    retained.Query.World.Dimension == "the_nether" ? 64 : null, retained.Query.StartSeed, keepSearch: true, expectedFingerprint: retained.Fingerprint))
                {
                    return;
                }

                ct.ThrowIfCancellationRequested();
            }

            var snapshot = Current;
            var state = retained ?? await Task.Run(() => owner.Client.Seeds.Searches.CreateAsync(new()
            {
                Profile = snapshot.Profile!,
                World = snapshot.World with { },
                StartSeed = snapshot.SeedInput,
                MaxSeeds = snapshot.SearchSeedBudget,
                MaxResults = snapshot.SearchResultBudget,
                TimeLimitSeconds = snapshot.SearchSeconds,
                BiomeStep = snapshot.BiomeStep,
                Conditions = [.. snapshot.Conditions.Select(condition => condition with { Targets = [.. condition.Targets] })],
            }, ct), ct);
            if (generation != _generation || owner.Disposed) return;
            Current = Current with { Search = state, Conditions = state.Query.Conditions };
            var progress = new SearchProgress(value => owner.Dispatcher.TryEnqueue(() => Publish(value, generation)));
            var result = await Task.Run(() => owner.Client.Seeds.Searches.RunAsync(state.Id, progress, ct), CancellationToken.None);
            Publish(result, generation);
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _generation) owner.Error(error); }
        finally { if (generation == _generation) Current = Current with { Searching = false }; }
    }

    private void Publish(SeedSearchState state, int generation)
    {
        if (owner.Disposed || generation != _generation) return;
        var current = Current.Search;
        if (current is not null && current.Id == state.Id
            && (state.ModifiedAt < current.ModifiedAt || (current.Result.Status != "running" && state.Result.Status == "running" && state.Result.NextOffset <= current.Result.NextOffset)))
        {
            return;
        }

        Current = Current with { Search = state };
        foreach (var hit in state.Result.Hits)
            if (!Current.Previews.Any(preview => preview.Seed == hit.Seed)) _thumbnailPending.Add(hit.Seed);
        EnsureThumbnails();
    }

    private void EnsureThumbnails()
    {
        if (!owner.Disposed && _thumbnailTask.IsCompleted && _thumbnailPending.Count > 0)
        {
            _previews?.Dispose(); _previews = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
            _thumbnailTask = LoadThumbnailsAsync(_previewGeneration, _previews.Token);
        }
    }

    private async Task LoadThumbnailsAsync(int generation, CancellationToken ct)
    {
        try
        {
            while (_thumbnailPending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (generation != _previewGeneration) return;
                if (Current.Opening || Current.MapBusy) { await Task.Delay(150, ct); continue; }

                var seed = _thumbnailPending.First(); _thumbnailPending.Remove(seed);
                var fingerprint = Current.Fingerprint; var world = Current.World with { };
                var hit = Current.Search?.Result.Hits.FirstOrDefault(value => value.Seed == seed);
                if (hit is null) continue;
                var tile = await owner.Map.ThumbnailAsync(hit, ct);
                if (tile is null || fingerprint != Current.Fingerprint || world != Current.World || generation != _previewGeneration) continue;
                var image = await Task.Run(() => SeedMapTiles.Image(tile, false), ct);
                Current = Current with { Previews = [.. Current.Previews.Where(preview => preview.Seed != seed), new(seed, image)] };
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (generation == _previewGeneration) owner.Error(error); }
        finally { if (!owner.Disposed && _thumbnailPending.Count > 0) owner.Dispatcher.TryEnqueue(EnsureThumbnails); }
    }

    internal async Task ResetContextAsync()
    {
        _attempt?.Cancel(); _previews?.Cancel();
        _generation++; _previewGeneration++; _thumbnailPending.Clear();
        Current = Current with { Search = null, Previews = [], Searching = false, SearchTab = 0 };
        await _running;
    }

    internal async Task StopAsync()
    {
        _attempt?.Cancel();
        if (owner.Disposed) _previews?.Cancel();
        await _running;
        if (owner.Disposed) await _thumbnailTask;
    }

    /// <summary>Releases search/preview cancellation sources after their owned tasks have stopped.</summary>
    public void Dispose() { _attempt?.Dispose(); _previews?.Dispose(); }

    private sealed class SearchProgress(Action<SeedSearchState> publish) : IProgress<SeedSearchState>
    {
        /// <inheritdoc/>
        public void Report(SeedSearchState value) => publish(value);
    }
}