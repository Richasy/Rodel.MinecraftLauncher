// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentUpdateCandidate(
    string FileName,
    string Name,
    string Current,
    string Target,
    string? ProjectId,
    string? VersionId,
    string Status,
    string Reason,
    bool Enabled,
    bool Selected)
{
    internal ContentItem? Local { get; init; }
    internal string? Channel { get; init; }
    internal IReadOnlyList<ContentSourceCandidate> SourceCandidates { get; init; } = [];
    internal string? SelectedSource { get; init; }
}

internal sealed record ContentUpdatesState
{
    internal bool Reading { get; init; }
    internal bool Reviewed { get; init; }
    internal bool Ready { get; init; }
    internal bool BaselineChanged { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal ContentReview? Evidence { get; init; }
    internal JsonObject? Failure { get; init; }
    internal string RawReport => Failure?.ToJsonString(new() { WriteIndented = true }) ?? (Evidence is null ? string.Empty : JsonSerializer.SerializeToNode(Evidence, ContentJsonContext.Review)!.ToJsonString(new() { WriteIndented = true }));
    internal IReadOnlyList<ContentUpdateCandidate> Items { get; init; } = [];
    internal IReadOnlyList<ContentPlanRow> Plan { get; init; } = [];
    internal IReadOnlyList<ContentFinding> Findings { get; init; } = [];
    internal string Context { get; init; } = string.Empty;
    internal int SelectedCount => Items.Count(item => item.Selected);
}

/// <summary>Owns the read-only selection and exact review; execution remains in the shared typed task controller.</summary>
internal sealed class ContentUpdatesController(
    LauncherClient client,
    string instanceId,
    ContentKind kind,
    IReadOnlyList<ContentItem> items) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IReadOnlyList<ContentItem> _items = [.. items];
    private ContentReview? _check;
    private ContentReview? _review;
    private readonly Dictionary<string, string> _sourceSelections = [with(StringComparer.OrdinalIgnoreCase)];
    internal ContentUpdatesState State { get; private set; } = new();
    internal string? ReviewId => State.Ready ? _review?.Id : null;
    internal event Action<ContentUpdatesState>? Changed;

    private void Publish(ContentUpdatesState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    internal async Task CheckAsync(bool failuresOnly = false, string? fileName = null)
    {
        if (State.Reading || _lifetime.IsCancellationRequested)
            return;
        var files = fileName is not null ? [fileName] : failuresOnly ? State.Items.Where(item => item.Status == "failed").Select(item => item.FileName).ToArray() : [.. _items.Select(item => item.FileName)];
        if (files.Length == 0)
            return;
        _review = null;
        Publish(State with
        {
            Reading = true,
            Reviewed = false,
            Ready = false,
            Error = string.Empty,
            Plan = [],
            Findings = []
        });
        var ct = _lifetime.Token;
        try
        {
            var sources = new Dictionary<string, string>(_sourceSelections, StringComparer.OrdinalIgnoreCase);
            var check = await Task.Run(() => client.ReviewContentUpdatesAsync(instanceId, files, new()
            {
                Kind = kind.Directory(),
                InstallOptions = new() { SourceSelections = sources }
            }, ct), ct);
            if (ct.IsCancellationRequested)
                return;
            if ((failuresOnly || fileName is not null) && _check is not null && check.LockFingerprint != _check.LockFingerprint)
            {
                // Policies changed while retrying: do not merge results from different inventories.
                Publish(State with { Reading = false });
                await CheckAsync();
                return;
            }

            var candidates = check.Items!.Select(Read).ToArray();
            if (failuresOnly || fileName is not null)
            {
                var replacements = candidates.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
                candidates = [.. State.Items.Select(item => replacements.GetValueOrDefault(item.FileName) ?? item)];
            }

            _check = check;
            Publish(State with
            {
                Reading = false,
                BaselineChanged = false,
                Items = candidates,
                Evidence = check,
                Failure = null,
                Context = ReadContext(check)
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!ct.IsCancellationRequested)
            {
                Publish(State with
                {
                    Reading = false,
                    Error = Resources.ContentUpdatesCheckFailed,
                    Evidence = null,
                    Failure = LauncherOperationException.ToResult(error)
                });
            }
        }
    }

    private ContentUpdateCandidate Read(ContentReviewItem value)
    {
        var file = value.FileName;
        var local = _items.FirstOrDefault(item => item.FileName == file);
        return new(
            file,
            local?.Name ?? file,
            value.CurrentVersion?.ToString() ?? local?.Version ?? string.Empty,
            value.TargetVersion?.ToString() ?? value.TargetVersionId ?? string.Empty,
            value.ProjectId,
            value.TargetVersionId,
            value.Reason == "upstream-unavailable" ? "failed" : value.Status!,
            value.Reason ?? string.Empty,
            value.Enabled != false,
            value.Status == "available")
        {
            Local = local,
            Channel = value.Channel,
            SourceCandidates = value.SourceCandidates,
            SelectedSource = _sourceSelections.GetValueOrDefault(file)
        };
    }

    internal async Task SelectSourceAsync(string fileName, int index)
    {
        if (State.Reading || State.Reviewed || _lifetime.IsCancellationRequested) return;
        var item = State.Items.FirstOrDefault(value => value.FileName == fileName);
        if (item is null || index < 0 || index > item.SourceCandidates.Count) return;
        var selected = index == 0 ? null : item.SourceCandidates[index - 1].ProjectId;
        if (selected == item.SelectedSource) return;
        if (index == 0) _sourceSelections.Remove(fileName);
        else _sourceSelections[fileName] = selected!;
        await CheckAsync(fileName: fileName);
    }

    internal void Toggle(string fileName)
    {
        if (State.Reading || State.Reviewed)
            return;
        _review = null;
        Publish(State with
        {
            Ready = false,
            Items = [.. State.Items.Select(item => item.FileName == fileName && item.Status == "available" ? item with { Selected = !item.Selected } : item)]
        });
    }

    internal void ToggleAll()
    {
        if (State.Reading || State.Reviewed)
            return;
        var select = State.Items.Any(item => item.Status == "available" && !item.Selected);
        _review = null;
        Publish(State with
        {
            Ready = false,
            Items = [.. State.Items.Select(item => item.Status == "available" ? item with { Selected = select } : item)]
        });
    }

    internal void Back()
    {
        if (State.Reading)
            return;
        _review = null;
        Publish(State with
        {
            Reviewed = false,
            Ready = false,
            Plan = [],
            Findings = [],
            Error = State.BaselineChanged ? Resources.ContentUpdatesBaselineChanged : string.Empty
        });
    }

    internal async Task ReviewAsync()
    {
        if (State.Reading || State.SelectedCount == 0 || _check is null || _lifetime.IsCancellationRequested)
            return;
        _review = null;
        Publish(State with
        {
            Reading = true,
            Reviewed = true,
            Ready = false,
            Error = string.Empty,
            Plan = [],
            Findings = []
        });
        var ct = _lifetime.Token;
        try
        {
            var selected = State.Items.Where(item => item.Selected).ToArray();
            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in selected)
            {
                if (item.ProjectId is null || item.VersionId is null)
                    throw new InvalidDataException(Resources.ContentUpdatesSelectionChanged);
                if (versions.TryGetValue(item.ProjectId, out var version) && version != item.VersionId)
                    throw new InvalidDataException(Resources.ContentUpdatesSelectionConflict);
                versions[item.ProjectId] = item.VersionId;
            }

            var review = await Task.Run(() => client.ReviewContentUpdatesAsync(instanceId, [.. selected.Select(item => item.FileName)], new()
            {
                Kind = kind.Directory(),
                InstallOptions = new()
                {
                    VersionSelections = versions,
                    SourceSelections = new Dictionary<string, string>(_sourceSelections, StringComparer.OrdinalIgnoreCase),
                    PreserveRetainedConsumers = true,
                    ExpectedLockFingerprint = _check.LockFingerprint!
                },
            }, ct), ct);
            if (ct.IsCancellationRequested)
                return;
            _review = review;
            var results = review.Items!;
            var unchanged = selected.All(item => results.Any(result => result.FileName == item.FileName && result.Status == "available" && result.TargetVersionId == item.VersionId));
            var plan = review.Plan;
            var ready = unchanged && review.CanApply == true;
            Publish(State with
            {
                Reading = false,
                Ready = ready,
                Evidence = review,
                Failure = null,
                Context = ReadContext(review),
                Plan = plan is null ? [] : [.. ContentPlanRow.ReadAll(plan, selected.GroupBy(item => item.ProjectId!).ToDictionary(group => group.Key, group => string.Join(", ", group.Select(item => item.Current).Distinct())))
                    .OrderBy(row => row.RequiredBy.Length > 0)
                    .Select((row, index) => row with { Last = index == plan.Nodes.Count - 1 })],
                Findings = plan is null ? [] : ContentFinding.ReadAll(plan),
                Error = ready
                        ? string.Empty
                        : unchanged ? Resources.ContentUpdatesConflict : Resources.ContentUpdatesSelectionChanged,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!ct.IsCancellationRequested)
            {
                var baselineChanged = error is LauncherOperationException { Code: "CONTENT_BASELINE_CHANGED" };
                Publish(State with
                {
                    Reading = false,
                    BaselineChanged = baselineChanged,
                    Error = baselineChanged ? Resources.ContentUpdatesBaselineChanged : Resources.ContentUpdatesConflict,
                    Evidence = null,
                    Failure = LauncherOperationException.ToResult(error)
                });
            }
        }
    }

    internal void Invalidate()
    {
        _review = null;
        Publish(State with
        {
            Reviewed = false,
            Ready = false,
            Plan = [],
            Findings = [],
            Error = Resources.ContentUpdatesRecheckRequired
        });
    }

    private static string ReadContext(ContentReview value) => string.Join(" · ", new[] { value.Context.MinecraftVersion, value.Context.Loader }.Where(text => !string.IsNullOrEmpty(text)));

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}