// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Reactive;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

/// <summary>Keeps instance membership current while slower configuration and content reads complete.</summary>
[Store]
internal sealed partial class InstanceListStore
{
    private readonly LatestOperation<IReadOnlyList<ManagedInstance>> _read;
    private readonly CancellationToken _lifetime;
    private IReadOnlyList<InstanceSummary> _request = [];
    private IProgress<IReadOnlyList<ManagedInstance>>? _progress;
    private Task _pending = Task.CompletedTask;
    private long? _inventoryRevision;
    private long _revision;

    internal InstanceListStore(Func<IReadOnlyList<InstanceSummary>, IProgress<IReadOnlyList<ManagedInstance>>, CancellationToken, Task<IReadOnlyList<ManagedInstance>>> read)
    {
        _lifetime = Lifetime;
        _read = new(ct => read(_request, _progress!, ct), _lifetime);
        Rows = [];
        Error = string.Empty;
    }

    internal partial IReadOnlyList<ManagedInstance> Rows { get; private set; }
    internal partial string Error { get; private set; }
    internal partial bool Loading { get; private set; }

    internal Task EnsureAsync(long revision, IReadOnlyList<InstanceSummary> summaries)
    {
        if (_inventoryRevision == revision && Error.Length == 0 && Rows.All(row => row.Error is null))
            return _pending;
        _inventoryRevision = revision;
        return _pending = ReadRowsAsync(summaries);
    }

    internal Task RefreshAsync(IReadOnlyList<InstanceSummary> summaries)
    {
        _inventoryRevision = null;
        return _pending = ReadRowsAsync(summaries);
    }

    private async Task ReadRowsAsync(IReadOnlyList<InstanceSummary> summaries)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        var revision = ++_revision;
        _read.Cancel();
        // Membership is authoritative immediately; detail hydration must never keep a deleted row actionable.
        var ids = summaries.Select(summary => summary.Id).ToHashSet(StringComparer.Ordinal);
        if (Rows.Any(row => !ids.Contains(row.Id)))
            Rows = [.. Rows.Where(row => ids.Contains(row.Id))];
        Error = string.Empty;
        Loading = summaries.Count > 0;
        if (summaries.Count == 0)
            return;
        var completed = false;
        _progress = new Progress<IReadOnlyList<ManagedInstance>>(rows =>
        {
            if (!completed && !_lifetime.IsCancellationRequested && revision == _revision)
                Rows = rows;
        });
        _request = summaries;
        try
        {
            await _read.Run();
            if (!_lifetime.IsCancellationRequested && revision == _revision)
            {
                var result = _read.State;
                if (result.Error is { } error)
                    Error = error.Message;
                else if (result is { HasValue: true, Value: { } rows })
                    Rows = rows;
            }
        }
        finally
        {
            completed = true;
            if (!_lifetime.IsCancellationRequested && revision == _revision)
                Loading = false;
        }
    }

    internal static Task<IReadOnlyList<ManagedInstance>> ReadAsync(
        LauncherClient client,
        HomeController home,
        IReadOnlyList<InstanceSummary> summaries,
        IProgress<IReadOnlyList<ManagedInstance>> progress,
        CancellationToken cancellationToken) => Task.Run<IReadOnlyList<ManagedInstance>>(async () =>
    {
        var registrations = new List<ManagedInstance>();
        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var instance = client.Instances.Get(summary.Id);
                registrations.Add(new(instance, null, null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
            }
        }

        progress.Report(registrations);
        var result = registrations.ToArray();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, result.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                var row = result[index];
                try
                {
                    result[index] = row with { Details = await home.ReadInstanceDetailsAsync(row.Id, ct).ConfigureAwait(false) };
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result[index] = row with { Error = exception.Message };
                }
            }).ConfigureAwait(false);
        return result;
    }, cancellationToken);
}