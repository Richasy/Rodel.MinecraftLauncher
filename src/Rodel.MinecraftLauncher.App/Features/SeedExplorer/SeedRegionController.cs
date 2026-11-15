// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.App.Features.SeedExplorer.Rendering;
using Sprout.Graphics;
using System.Numerics;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Coordinates frozen original region analysis and structure previews; UI choices never become generation algorithms.</summary>
internal sealed class SeedRegionController(SeedExplorerController owner)
{
    private CancellationTokenSource? _analysis, _scene, _mesh;
    private Task _analysisTask = Task.CompletedTask, _sceneTask = Task.CompletedTask, _meshTask = Task.CompletedTask;
    private long _analysisRevision, _sceneRevision, _meshRevision;
    private readonly HashSet<SeedSceneGpuRenderer> _renderers = [];
    private SeedBounds? _selectionBefore;
    private (int Minimum, int Maximum)? _containerSlice;
    private Vector3? _containerFocus;
    private SeedExplorerSnapshot Current { get => owner.State.Current; set { if (!owner.Disposed) owner.State.Current = value; } }

    internal SeedBounds DefaultBounds()
    {
        var dimension = Current.Catalog?.Dimensions.FirstOrDefault(item => item.Id == Current.World.Dimension);
        var x = (int)Math.Clamp(Current.CenterX, -29_999_872, 29_999_871);
        var z = (int)Math.Clamp(Current.CenterZ, -29_999_872, 29_999_871);
        return new() { MinX = x - 64, MaxX = x + 63, MinZ = z - 64, MaxZ = z + 63, MinY = dimension?.MinY ?? -64, MaxY = (dimension?.MinY ?? -64) + (dimension?.Height ?? 384) - 1 };
    }

    internal void SelectBounds(SeedBounds bounds) => Current = Current with { RegionBounds = bounds with { }, RegionError = string.Empty };
    internal void BeginSelection() { owner.DismissPopups?.Invoke(); _selectionBefore = Current.RegionBounds; Current = Current with { SelectingRegion = true, SearchMode = false }; }
    internal void CancelSelection() => Current = Current with { SelectingRegion = false, RegionBounds = _selectionBefore };
    internal void Select(int x1, int z1, int x2, int z2)
    {
        var current = Current.RegionBounds ?? DefaultBounds();
        SelectBounds(current with
        {
            MinX = Math.Clamp(Math.Min(x1, x2), -30_000_000, 29_999_999),
            MaxX = Math.Clamp(Math.Max(x1, x2), -30_000_000, 29_999_999),
            MinZ = Math.Clamp(Math.Min(z1, z2), -30_000_000, 29_999_999),
            MaxZ = Math.Clamp(Math.Max(z1, z2), -30_000_000, 29_999_999)
        });
    }

    internal Task AnalyzeAsync()
    {
        if (owner.Disposed || Current.Profile is null || (!Current.UseSavedMap && Current.Seed.Length == 0) || Current.Opening) return Task.CompletedTask;
        _analysis?.Cancel();
        var source = _analysis = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var revision = ++_analysisRevision;
        var previous = _analysisTask;
        var query = new SeedRegionQuery { Profile = Current.Profile, Seed = Current.Seed, World = Current.World with { }, Bounds = (Current.RegionBounds ?? DefaultBounds()) with { } };
        Current = Current with { RegionBounds = query.Bounds, RegionBusy = true, RegionError = string.Empty, RegionProgress = null };
        return _analysisTask = AnalyzeCoreAsync(previous, query, Current.Fingerprint, revision, source);
    }

    private async Task AnalyzeCoreAsync(Task previous, SeedRegionQuery query, string fingerprint, long revision, CancellationTokenSource source)
    {
        try
        {
            await previous;
            var progress = new Progress<SeedRegionProgress>(value => { if (Valid(revision, _analysisRevision, fingerprint, query)) Current = Current with { RegionProgress = value }; });
            var result = await owner.Map.RegionAsync(query, progress, source.Token);
            if (!Valid(revision, _analysisRevision, fingerprint, query) || source.IsCancellationRequested) return;
            Current = Current with { RegionData = result, RegionContainer = null, RegionItem = null };
            await LoadImagesAsync(result, source.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (revision == _analysisRevision) Current = Current with { RegionError = error.Message }; }
        finally
        {
            if (revision == _analysisRevision) Current = Current with { RegionBusy = false, RegionProgress = null };
            if (ReferenceEquals(_analysis, source)) _analysis = null;
            source.Dispose();
            owner.Map.ResumeViewport();
        }
    }

    private bool Valid(long revision, long currentRevision, string fingerprint, SeedRegionQuery query) => !owner.Disposed && revision == currentRevision
        && Current.Fingerprint == fingerprint && Current.Seed == query.Seed && Current.World == query.World && !Current.Opening;

    internal async Task OpenSceneAsync(SeedPosition position, SeedContainer? selected = null)
    {
        if (owner.Disposed || Current.Opening || Current.Profile is null || (!Current.UseSavedMap && Current.Seed.Length == 0)) return;
        var revision = ++_sceneRevision;
        _analysis?.Cancel(); _scene?.Cancel(); _mesh?.Cancel();
        var source = _scene = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        void ReleasePreflight()
        {
            if (ReferenceEquals(source, _scene)) _scene = null;
            source.Dispose();
        }

        var context = Current.Fingerprint;
        var snapshot = Current.WorldSnapshot;
        bool preview;
        try { preview = await owner.Worlds.IsPreviewAsync(position, source.Token); }
        catch (OperationCanceledException) { ReleasePreflight(); return; }
        catch (Exception error)
        {
            if (revision == _sceneRevision)
            {
                Current = Current with { SceneBusy = false, SceneProgress = null };
                owner.Error(error);
            }

            ReleasePreflight(); return;
        }

        if (owner.Disposed || revision != _sceneRevision || context != Current.Fingerprint || !ReferenceEquals(snapshot, Current.WorldSnapshot) || Current.Opening || source.IsCancellationRequested)
        {
            ReleasePreflight(); return;
        }

        owner.DismissPopups?.Invoke();
        _containerSlice = null;
        _containerFocus = null;
        var previous = _sceneTask;
        var bounds = PreviewBounds(position, preview, out var cropped);
        var query = new SeedRegionQuery { Profile = Current.Profile, Seed = Current.Seed, World = Current.World with { }, Bounds = bounds, IncludeBlocks = true };
        owner.State.Camera = new();
        Current = Current with
        {
            SceneOpen = true,
            ScenePreview = preview,
            SceneBusy = true,
            SceneError = string.Empty,
            SceneData = null,
            Scene = null,
            SceneMesh = null,
            ScenePosition = position,
            SceneProgress = null,
            SceneContainer = null,
            SceneItem = null,
            SceneTab = selected is null ? 0 : 1,
            SceneFilter = string.Empty,
            SceneCropped = cropped,
            SelectingRegion = false,
            SceneMinY = bounds.MinY,
            SceneMaxY = bounds.MaxY
        };
        await (_sceneTask = OpenCoreAsync(previous, query, Current.Fingerprint, revision, selected, source));
    }

    private SeedBounds PreviewBounds(SeedPosition p, bool preview, out bool cropped)
    {
        var dimension = Current.Catalog!.Dimensions.First(item => item.Id == Current.World.Dimension);
        var bounds = p.Bounds is { Length: 6 } b ? new SeedBounds { MinX = b[0], MinY = b[1], MinZ = b[2], MaxX = b[3], MaxY = b[4], MaxZ = b[5] }
            : new() { MinX = p.X - 8, MaxX = p.X + 8, MinY = p.Y - 2, MaxY = p.Y + 6, MinZ = p.Z - 8, MaxZ = p.Z + 8 };
        // The treasure start's Y=90 is an assembly placeholder. Vanilla chooses the chest's real height during placement.
        if (p is { Kind: "structure", Id: "buried_treasure" })
            bounds = new() { MinX = p.X - 2, MaxX = p.X + 2, MinY = dimension.MinY, MaxY = dimension.MinY + dimension.Height - 1, MinZ = p.Z - 2, MaxZ = p.Z + 2 };
        bounds = bounds with
        {
            MinY = Math.Max(dimension.MinY, bounds.MinY - (p.Kind == "structure" && (!Current.UseSavedMap || preview) ? 24 : 0)),
            MaxY = Math.Min(dimension.MinY + dimension.Height - 1, bounds.MaxY + 2),
            MinX = Math.Max(-30_000_000, bounds.MinX),
            MaxX = Math.Min(29_999_999, bounds.MaxX),
            MinZ = Math.Max(-30_000_000, bounds.MinZ),
            MaxZ = Math.Min(29_999_999, bounds.MaxZ)
        };
        var original = bounds;
        if (bounds.MaxX - bounds.MinX >= 256) { var left = Math.Clamp(p.X - 127, -30_000_000, 30_000_000 - 256); bounds = bounds with { MinX = left, MaxX = left + 255 }; }

        if (bounds.MaxZ - bounds.MinZ >= 256) { var top = Math.Clamp(p.Z - 127, -30_000_000, 30_000_000 - 256); bounds = bounds with { MinZ = top, MaxZ = top + 255 }; }

        var height = (int)(4_194_304 / (((long)bounds.MaxX - bounds.MinX + 1) * (bounds.MaxZ - bounds.MinZ + 1)));
        if (bounds.MaxY - bounds.MinY + 1 > height)
        {
            var minimum = Math.Clamp(p.Y - (height / 2), bounds.MinY, bounds.MaxY - height + 1);
            bounds = bounds with { MinY = minimum, MaxY = minimum + height - 1 };
        }

        cropped = original != bounds;
        return bounds;
    }

    private async Task OpenCoreAsync(Task previous, SeedRegionQuery query, string fingerprint, long revision, SeedContainer? selected, CancellationTokenSource source)
    {
        try
        {
            await previous;
            var progress = new Progress<SeedRegionProgress>(value => { if (Valid(revision, _sceneRevision, fingerprint, query)) Current = Current with { SceneProgress = value }; });
            var result = await owner.Map.RegionAsync(query, progress, source.Token);
            if (!Valid(revision, _sceneRevision, fingerprint, query) || source.IsCancellationRequested) return;
            Current = Current with { SceneProgress = new() { Phase = "models" } };
            var scene = await owner.Map.BuildSceneAsync(result, source.Token);
            if (!Valid(revision, _sceneRevision, fingerprint, query) || source.IsCancellationRequested) return;
            var minimum = Math.Clamp(Current.ScenePosition!.Bounds?.ElementAtOrDefault(1) ?? query.Bounds.MinY, query.Bounds.MinY, query.Bounds.MaxY);
            var maximum = query.Bounds.MaxY;
            var found = selected is not null ? result.Containers.FirstOrDefault(item => item.X == selected.X && item.Y == selected.Y && item.Z == selected.Z) : null;
            var position = Current.ScenePosition;
            if (position is { Kind: "structure", Id: "buried_treasure" })
            {
                found = result.Containers.FirstOrDefault(item => item.X == position.X && item.Z == position.Z && ((Current.UseSavedMap && !Current.ScenePreview) || item.LootTable == "minecraft:chests/buried_treasure"));
                minimum = found is null ? query.Bounds.MinY : Math.Max(query.Bounds.MinY, found.Y - 3);
                maximum = found is null ? query.Bounds.MaxY : Math.Min(query.Bounds.MaxY, found.Y + 2);
                if (found is not null) position = position with { Y = found.Y };
            }

            Current = Current with { SceneData = result, Scene = scene, ScenePosition = position, SceneMinY = minimum, SceneMaxY = maximum };
            if (found is not null) SelectContainer(found);
            else await BuildMeshAsync();
            await LoadImagesAsync(result, source.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (revision == _sceneRevision) Current = Current with { SceneError = error.Message }; }
        finally
        {
            if (revision == _sceneRevision) Current = Current with { SceneBusy = false, SceneProgress = null };
            if (ReferenceEquals(_scene, source)) _scene = null;
            source.Dispose();
        }
    }

    private async Task LoadImagesAsync(SeedRegionResult data, CancellationToken ct)
    {
        var fingerprint = Current.Fingerprint;
        var originals = await owner.Map.ItemImagesAsync(data.Containers.SelectMany(item => item.Items).Select(item => item.Id).Concat(data.Materials.Select(item => item.Id)), ct);
        ct.ThrowIfCancellationRequested();
        if (owner.Disposed || Current.Fingerprint != fingerprint) return;
        var images = new Dictionary<string, ImageSource>(Current.ItemImages, StringComparer.Ordinal);
        if (images.Count > 1024) images.Clear();
        foreach (var (id, png) in originals) if (!images.ContainsKey(id)) images[id] = ImageSource.FromBytes(png);
        Current = Current with { ItemImages = images };
    }

    internal void SelectContainer(SeedContainer container)
    {
        if (Current.SceneData is not { } data || !data.Containers.Contains(container)) return;
        if (Current.SceneContainer is null)
        {
            _containerSlice = (Current.SceneMinY, Current.SceneMaxY);
            _containerFocus = owner.State.Camera.Focus;
        }

        Current = Current with { SceneTab = 1, SceneContainer = container, SceneItem = null, SceneMinY = Math.Max(data.Bounds.MinY, container.Y - 1), SceneMaxY = container.Y };
        _ = BuildMeshAsync();
    }
    internal void ReturnToContainerList()
    {
        if (Current.SceneContainer is null) return;
        (var Minimum, var Maximum) = _containerSlice ?? (Current.SceneMinY, Current.SceneMaxY);
        owner.Window.ClearFocus();
        _containerSlice = null;
        owner.State.Camera = owner.State.Camera with { Focus = _containerFocus };
        _containerFocus = null;
        Current = Current with { SceneContainer = null, SceneItem = null, SceneMinY = Minimum, SceneMaxY = Maximum };
        _ = BuildMeshAsync();
    }
    internal Task BuildMeshAsync()
    {
        if (Current.Scene is not { } scene) return Task.CompletedTask;
        _mesh?.Cancel();
        var source = _mesh = CancellationTokenSource.CreateLinkedTokenSource(owner.Lifetime.Token);
        var revision = ++_meshRevision;
        var previous = _meshTask;
        var minimum = Current.SceneMinY; var maximum = Current.SceneMaxY;
        Current = Current with { SceneMeshing = true, SceneError = string.Empty };
        return _meshTask = MeshCoreAsync(previous, scene, minimum, maximum, revision, source);
    }
    private async Task MeshCoreAsync(Task previous, SeedBlockScene scene, int minimum, int maximum, long revision, CancellationTokenSource source)
    {
        try
        {
            await previous;
            source.Token.ThrowIfCancellationRequested();
            var mesh = await Task.Run(() => scene.CreateMesh(minimum, maximum, source.Token), source.Token);
            if (!owner.Disposed && revision == _meshRevision && ReferenceEquals(scene, Current.Scene) && Current.SceneOpen)
                Current = Current with { SceneMesh = mesh };
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (revision == _meshRevision) Current = Current with { SceneError = error.Message }; }
        finally
        {
            if (revision == _meshRevision) Current = Current with { SceneMeshing = false };
            if (ReferenceEquals(_mesh, source)) _mesh = null;
            source.Dispose();
        }
    }
    internal void Slice(int minimum, int maximum) { Current = Current with { SceneMinY = minimum, SceneMaxY = maximum }; _ = BuildMeshAsync(); }
    internal void CloseScene()
    {
        _containerSlice = null;
        _containerFocus = null;
        _scene?.Cancel(); _mesh?.Cancel(); _sceneRevision++; _meshRevision++;
        // Release the departing inspector's focus before its hidden conditional content is reconciled.
        if (Current.SceneOpen) owner.Window.ClearFocus();
        Current = Current with { SceneOpen = false, SceneBusy = false, SceneMeshing = false, SceneData = null, Scene = null, SceneMesh = null, SceneContainer = null, SceneItem = null };
        owner.Map.ResumeViewport();
    }
    internal void LocateContainer(SeedContainer container)
    {
        if (Current.SceneOpen && Current.SceneData is { } data && data.Containers.Contains(container))
        {
            var b = data.Bounds;
            owner.State.Camera = owner.State.Camera with { Focus = new(container.X - b.MinX + .5f, container.Y - b.MinY + .5f, container.Z - b.MinZ + .5f) };
            return;
        }

        owner.DismissPopups?.Invoke(); CloseScene(); owner.Map.Pan(container.X, container.Z);
        Current = Current with { PendingPlace = new() { Id = container.Id, Kind = "container", X = container.X, Y = container.Y, Z = container.Z }, PendingTeleportY = container.Y };
    }
    internal void OpenRegionContainer(SeedContainer container)
    {
        if (Current.RegionData is not { } data || !data.Containers.Contains(container) || data.Seed != Current.Seed || data.World != Current.World || data.Fingerprint != Current.Fingerprint) return;
        Current = Current with { RegionContainer = container, RegionItem = null };
    }
    internal void CopyContainer(SeedContainer container) => owner.Copy(SeedCommands.Teleport(Current.World, container.X, container.Y, container.Z));
    internal void Register(SeedSceneGpuRenderer renderer) { _renderers.RemoveWhere(item => item.Completion.IsCompleted); _renderers.Add(renderer); }
    internal void CancelAnalysis() => _analysis?.Cancel();
    internal async Task StopAsync()
    {
        _containerSlice = null;
        _containerFocus = null;
        _analysisRevision++; _sceneRevision++; _meshRevision++;
        _analysis?.Cancel(); _scene?.Cancel(); _mesh?.Cancel();
        foreach (var renderer in _renderers) renderer.Stop();
        await Task.WhenAll(_analysisTask, _sceneTask, _meshTask, Task.WhenAll(_renderers.Select(item => item.Completion)));
        _renderers.Clear();
        Current = Current with
        {
            RegionBusy = false,
            SelectingRegion = false,
            RegionEditBounds = false,
            RegionBounds = null,
            RegionData = null,
            RegionContainer = null,
            RegionItem = null,
            SceneOpen = false,
            SceneBusy = false,
            SceneMeshing = false,
            Scene = null,
            SceneData = null,
            SceneMesh = null,
            SceneContainer = null,
            SceneItem = null,
            ItemImages = new Dictionary<string, ImageSource>()
        };
    }
}