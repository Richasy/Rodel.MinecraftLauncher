// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentInstallTarget(string Id, string Name);

internal sealed record ContentVersionCatalog(IReadOnlyList<ContentVersion> Versions, string? MinecraftVersion, string? Loader);

internal sealed record ContentReviewSelection(
    string InstanceId,
    string ProjectId,
    ContentKind Kind,
    string VersionId);

internal sealed record ContentInstallBinding(
    string InstanceId,
    string ReviewId,
    ContentKind Kind,
    string VersionId);

/// <summary>Coordinates candidate selection and frozen review without owning the content application task.</summary>
[Store]
internal sealed partial class ContentInstallStore
{
    private readonly string _projectId;
    private readonly ContentKind _kind;
    private readonly Func<ContentInstallTarget, CancellationToken, Task<ContentVersionCatalog>> _load;
    private readonly Func<ContentReviewSelection, CancellationToken, Task<ContentReview>> _review;
    private readonly CancellationToken _lifetime;

    internal ContentInstallStore(
        string projectId,
        ContentKind kind,
        ContentInstallTarget target,
        Func<ContentInstallTarget, CancellationToken, Task<ContentVersionCatalog>> load,
        Func<ContentReviewSelection, CancellationToken, Task<ContentReview>> review)
    {
        _projectId = projectId;
        _kind = kind;
        _load = load;
        _review = review;
        _lifetime = Lifetime;
        Selection = new(
            new(target, [], load, _lifetime),
            null,
            null,
            null,
            false,
            false);
    }

    private partial InstallSelection Selection { get; set; }
    internal ContentInstallTarget Target => Selection.Catalog.Target;
    internal IReadOnlyList<ContentVersion> Versions => Selection.CatalogApplied ? Selection.Catalog.Operation.State.Value?.Versions ?? [] : Selection.Catalog.Previous;
    internal int Selected => Versions.ToList().FindIndex(version => version.Id == Selection.VersionId);
    internal string VersionText => Selection.Input ?? (Selected >= 0 ? Versions[Selected].Label : string.Empty);
    internal bool Loading => !_lifetime.IsCancellationRequested && !Selection.CatalogApplied && !Selection.Catalog.Operation.State.IsError;
    internal string LoadError => Selection.Catalog.Operation.State.Error?.Message ?? string.Empty;
    internal bool Planning => Selection.Review is { } read && (read.Operation.State.IsIdle || read.Operation.State.IsLoading);
    internal ContentReview? Review => Selection.Review?.Operation.State.Value;
    internal string PlanError => Selection.Review?.Operation.State.Error?.Message ?? string.Empty;
    internal bool Ready => !_lifetime.IsCancellationRequested && !Selection.Consumed && !Loading && !Planning && Review?.Plan?.CanApply == true;
    internal bool NeedsReview => Selection.Consumed || PlanError.Length > 0 || (Review is not null && Review.Plan?.CanApply != true);

    internal async Task LoadAsync(ContentInstallTarget? target = null)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        target ??= Target;
        var previous = target.Id == Target.Id ? Versions : [];
        Selection.Catalog.Operation.Cancel();
        Selection.Review?.Operation.Cancel();
        var read = new CatalogRead(target, previous, _load, _lifetime);
        Selection = new(
            read,
            null,
            null,
            null,
            false,
            false);
        await read.Operation.Run();
        if (_lifetime.IsCancellationRequested || !ReferenceEquals(Selection.Catalog, read) || read.Operation.State is not { IsSuccess: true, Value: { } catalog })
        {
            return;
        }

        var selected = catalog.Versions.FirstOrDefault(version => catalog.MinecraftVersion is { Length: > 0 } minecraft && version.GameVersions.Contains(minecraft) && (_kind != ContentKind.Mods || (catalog.Loader is { } loader && version.Loaders.Contains(loader))));
        selected ??= target.Id.Length == 0 && catalog.Versions.Count > 0 ? catalog.Versions[0] : null;
        Selection = Selection with
        {
            VersionId = selected?.Id,
            CatalogApplied = true
        };
        await ReviewAsync();
    }

    internal Task ChooseAsync(int index)
    {
        if (_lifetime.IsCancellationRequested || Loading || index < 0 || index >= Versions.Count)
            return Task.CompletedTask;
        if (Selection.VersionId == Versions[index].Id && Selection.Input is null)
            return Task.CompletedTask;
        Selection.Review?.Operation.Cancel();
        Selection = Selection with
        {
            VersionId = Versions[index].Id,
            Input = null,
            Review = null,
            Consumed = false
        };
        return ReviewAsync();
    }

    internal Task ChangeTextAsync(string text)
    {
        if (_lifetime.IsCancellationRequested || Loading || text == VersionText)
            return Task.CompletedTask;
        var matches = Versions.Where(version => version.Label == text || version.Number == text || version.Id == text).Take(2).ToArray();
        Selection.Review?.Operation.Cancel();
        Selection = Selection with
        {
            VersionId = matches.Length == 1 ? matches[0].Id : null,
            Input = text,
            Review = null,
            Consumed = false
        };
        return ReviewAsync();
    }

    internal Task ReviewAsync()
    {
        if (_lifetime.IsCancellationRequested || Loading || Target.Id.Length == 0 || Selection.VersionId is not { } version)
            return Task.CompletedTask;
        Selection.Review?.Operation.Cancel();
        var read = new ReviewRead(new(Target.Id, _projectId, _kind, version), _review, _lifetime);
        Selection = Selection with
        {
            Review = read,
            Consumed = false
        };
        return read.Operation.Run();
    }

    internal ContentInstallBinding? TakeReview()
    {
        if (!Ready || Selection.Review is not { } read || Review is not { } review)
            return null;
        Selection = Selection with
        {
            Consumed = true
        };
        return new(read.Request.InstanceId, review.Id, read.Request.Kind, read.Request.VersionId);
    }

    private sealed record InstallSelection(
        CatalogRead Catalog,
        string? VersionId,
        string? Input,
        ReviewRead? Review,
        bool Consumed,
        bool CatalogApplied);

    private sealed class CatalogRead
    {
        internal CatalogRead(
            ContentInstallTarget target,
            IReadOnlyList<ContentVersion> previous,
            Func<ContentInstallTarget, CancellationToken, Task<ContentVersionCatalog>> load,
            CancellationToken lifetime)
        {
            Target = target;
            Previous = previous;
            Operation = new(ct => load(target, ct), lifetime);
        }

        internal ContentInstallTarget Target { get; }
        internal IReadOnlyList<ContentVersion> Previous { get; }
        internal LatestOperation<ContentVersionCatalog> Operation { get; }
    }

    private sealed class ReviewRead
    {
        internal ReviewRead(ContentReviewSelection request, Func<ContentReviewSelection, CancellationToken, Task<ContentReview>> review, CancellationToken lifetime)
        {
            Request = request;
            Operation = new(async ct =>
                {
                    var result = await review(request, ct);
                    if (result.InstanceId != request.InstanceId || result.Plan is null || result.Plan.PrimaryProject != request.ProjectId)
                        throw new InvalidDataException(Resources.ContentUpdatesSelectionChanged);
                    return result;
                }, lifetime);
        }

        internal ContentReviewSelection Request { get; }
        internal LatestOperation<ContentReview> Operation { get; }
    }
}