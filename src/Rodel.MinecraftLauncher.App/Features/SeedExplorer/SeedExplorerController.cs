// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Owns this window's coordinators; generation and durable business data remain in Core.</summary>
internal sealed class SeedExplorerController : IAsyncDisposable
{
    private readonly SeedWorkspacePreferences _preferences = new(AppStorage.LocalPath);
    private bool _draftLoaded;
    private bool _searchFromWorld;
    internal SeedWorkspaceDraft Draft { get; private set; } = new();
    internal LauncherClient Client { get; }
    internal Window Window { get; }
    internal SeedExplorerState State { get; }
    internal SeedMapController Map { get; }
    internal SeedSearchController Search { get; }
    internal SeedBookmarkController Bookmarks { get; }
    internal SeedStructureController Structures { get; }
    internal SeedRegionController Regions { get; }
    internal SeedWorldController Worlds { get; }
    internal CancellationTokenSource Lifetime { get; } = new();
    internal Action? DismissPopups { get; set; }
    internal bool Disposed { get; private set; }
    internal DispatcherQueue Dispatcher { get; } = DispatcherQueue.GetForCurrentThread();

    internal SeedExplorerController(LauncherClient client, Window window, SeedExplorerState state)
    {
        Client = client; Window = window; State = state;
        Map = new(this); Search = new(this); Bookmarks = new(this); Structures = new(this); Regions = new(this); Worlds = new(this);
    }

    internal async Task InitializeAsync()
    {
        var input = State.Current.SeedInput;
        var mode = State.Current.SearchMode;
        try
        {
            var draft = _preferences.LoadAsync(Lifetime.Token);
            await Task.WhenAll(draft, Search.RefreshAsync(), Bookmarks.RefreshAsync());
            Draft = await draft;
            if (Disposed) return;
            if (!_preferences.HasSavedDraft && State.Current.Searches.OrderByDescending(search => search.ModifiedAt).FirstOrDefault() is { } previous)
            {
                var query = previous.Query;
                Draft = new()
                {
                    Profile = query.Profile,
                    SeedInput = query.StartSeed,
                    World = query.World,
                    SearchMode = State.Current.SearchMode,
                    Conditions = new() { [SeedWorkspaceDraft.Key(query.Profile, query.World)] = query.Conditions },
                    SeedBudget = query.MaxSeeds,
                    ResultBudget = query.MaxResults,
                    Seconds = query.TimeLimitSeconds,
                    BiomeStep = query.BiomeStep,
                };
            }

            _draftLoaded = true;
            Search.LoadDrafts(Draft.Conditions);
            State.Current = State.Current with
            {
                SeedInput = State.Current.SeedInput == input ? Draft.SeedInput : State.Current.SeedInput,
                SearchMode = State.Current.SearchMode == mode ? Draft.SearchMode : State.Current.SearchMode,
                SearchSeedBudget = Draft.SeedBudget,
                SearchResultBudget = Draft.ResultBudget,
                SearchSeconds = Draft.Seconds,
                BiomeStep = Draft.BiomeStep,
                WorldInstanceId = Draft.WorldInstanceId,
                WorldFolder = Draft.WorldFolder,
                SavedMap = Draft.SavedMap,
                TrackPlayer = Draft.TrackPlayer,
            };
            await Map.InitializeAsync();
            await Worlds.InitializeAsync();
            if (Draft.WorldSource && !State.Current.SearchMode) await Worlds.SelectSourceAsync(true);
        }
        catch (OperationCanceledException) { }
    }
    internal void SelectMode(bool search)
    {
        if (search && State.Current.WorldSource) { _searchFromWorld = true; _ = ChangeSourceForModeAsync(false, true); return; }

        if (!search && _searchFromWorld) { _searchFromWorld = false; _ = ChangeSourceForModeAsync(true, false); return; }

        DismissPopups?.Invoke();
        if (State.Current.SceneOpen) Regions.CloseScene();
        State.Current = State.Current with { SearchMode = search };
        if (search && State.Current.Conditions.Length > 0) _ = Map.EnsureCatalogAsync();
    }
    private async Task ChangeSourceForModeAsync(bool world, bool search)
    {
        try { await Worlds.SelectSourceAsync(world); State.Current = State.Current with { SearchMode = search }; }
        catch (OperationCanceledException) { }
        catch (Exception error) { Error(error); }
    }
    internal void Error(Exception error)
    {
        if (Disposed || error is OperationCanceledException) return;
        System.Diagnostics.Trace.TraceError("Seed explorer operation failed: {0}", error);
        var failure = LauncherOperationException.CreateResult(error).Error!;
        NotifyError(string.Format(Resources.SeedOperationFailed, failure.Code, failure.Message));
    }
    internal void NotifyError(string message)
    {
        if (Disposed) return;
        State.Current = State.Current with { Error = message };
        LauncherToast.ShowError(Window, message);
    }
    internal void Copy(string text)
    {
        if (!Sprout.Host.Clipboard.SetText(text, Window.Handle)) State.Current = State.Current with { Phase = "copy_failed" };
        else State.Current = State.Current with { Phase = "copied" };
    }

    /// <summary>Cancels view work, preserves committed search data, and closes only this window's original JVM.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Disposed) return;
        Disposed = true;
        var current = State.Current;
        if (_draftLoaded)
        {
            _preferences.Save(new()
            {
                Profile = Worlds.ManualDraft?.Profile ?? current.Profile ?? Draft.Profile,
                SeedInput = Worlds.ManualDraft?.SeedInput ?? current.SeedInput,
                World = Worlds.ManualDraft?.World ?? (current.Profile is null ? Draft.World : current.World),
                SliceY = Worlds.ManualDraft is { } manual ? manual.SliceY : current.Profile is null ? Draft.SliceY : current.SliceY,
                Terrain = current.Profile is null ? Draft.Terrain : current.Terrain,
                SearchMode = current.SearchMode,
                Conditions = Search.CaptureDrafts(),
                SeedBudget = current.SearchSeedBudget,
                ResultBudget = current.SearchResultBudget,
                Seconds = current.SearchSeconds,
                BiomeStep = current.BiomeStep,
                WorldSource = current.WorldSource,
                WorldInstanceId = current.WorldInstanceId,
                WorldFolder = current.WorldFolder,
                SavedMap = current.SavedMap,
                TrackPlayer = current.TrackPlayer,
            });
        }

        Lifetime.Cancel(); DismissPopups?.Invoke();
        await Search.StopAsync();
        await Structures.StopAsync();
        await Regions.StopAsync();
        await Worlds.DisposeAsync();
        await Map.DisposeAsync();
        Search.Dispose(); Lifetime.Dispose();
    }
}