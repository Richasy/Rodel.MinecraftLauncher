// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.Core.Application;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed record CacheCleanupEntry(
    int Key,
    string Path,
    string Name,
    string Reason,
    string Detail,
    bool Warning,
    bool Last);

internal sealed record CacheCleanupState
{
    internal bool Busy { get; init; }
    internal bool Executing { get; init; }
    internal bool Completed { get; init; }
    internal double Days { get; init; } = 30;
    internal CachePruneResult? Plan { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string Outcome { get; init; } = string.Empty;
    internal long CandidateBytes => Plan?.CandidateBytes ?? 0;
    internal int CandidateCount => Plan?.Details.Count ?? 0;
    internal bool ValidAge => double.IsFinite(Days) && Days >= 0 && Days <= int.MaxValue && Days == Math.Truncate(Days);
    internal bool CanApply => !Busy && !Completed && ValidAge && CandidateCount > 0;
}

/// <summary>Previews Core's cache policy and re-evaluates the same age rule on execution.</summary>
internal sealed class CacheCleanupController(LauncherClient client, HomeController home) : IDisposable
{
    private CancellationTokenSource? _operation;
    private bool _disposed;
    internal CacheCleanupState State { get; private set; } = new();
    internal event Action<CacheCleanupState>? Changed;

    private void Publish(CacheCleanupState state)
    {
        if (_disposed)
            return;
        State = state;
        Changed?.Invoke(state);
    }

    internal void SetDays(double days)
    {
        if (State.Busy || State.Completed || days.Equals(State.Days))
            return;
        Publish(State with
        {
            Days = days,
            Plan = null,
            Error = string.Empty,
            Outcome = string.Empty
        });
    }

    internal async Task RunAsync(bool apply)
    {
        if (_disposed || State.Busy || !State.ValidAge || (apply && !State.CanApply))
            return;
        var days = (int)State.Days;
        _operation?.Dispose();
        _operation = new();
        var ct = _operation.Token;
        Publish(State with
        {
            Busy = true,
            Executing = apply,
            Error = string.Empty,
            Outcome = string.Empty
        });
        using var ownedWork = home.TrackManagementWork();
        try
        {
            var result = await Task.Run(() => client.Cache.PruneAsync(days, plan: !apply, ct: ct), ct);
            Publish(State with
            {
                Busy = false,
                Executing = false,
                Completed = apply,
                Plan = result,
                Outcome = apply
                        ? result.CacheBusy ? Resources.CacheCleanupBusy : string.Format(Resources.CacheCleanupResult, AppImageCache.FormatSize(result.ReclaimedBytes), result.Removed.Count)
                        : string.Empty
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Publish(State with
            {
                Busy = false,
                Executing = false,
                Plan = null,
                Outcome = apply ? Resources.CacheCleanupStopped : string.Empty
            });
        }
        catch (Exception exception)
        {
            Publish(State with
            {
                Busy = false,
                Executing = false,
                Plan = null,
                Error = exception.Message
            });
        }
    }

    internal void Cancel() => _operation?.Cancel();

    internal CacheCleanupEntry[] Entries(CacheCleanupState state)
    {
        if (state.Plan is null)
            return [];
        var rows = new List<CacheCleanupEntry>();
        var removed = state.Plan.Removed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = state.Plan.Warnings.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var category in state.Completed ? new[]
        {
            "warnings",
            "details",
            "retained"
        }
            : ["details", "warnings", "retained"])
        {
            IEnumerable<(string Path, string? Reason, long Bytes)> items = category switch
            {
                "details" => state.Plan.Details.Select(item => (item.Path, (string?)item.Reason, item.Bytes)),
                "retained" => state.Plan.Retained.Select(item => (item.Path, (string?)item.Reason, 0L)),
                _ => state.Plan.Warnings.Select(item => (item.Path, (string?)null, 0L))
            };
            foreach (var item in items)
            {
                var path = item.Path;
                if (state.Completed && category == "details" && removed.Contains(path))
                    continue;
                if (!seen.Add(path))
                    continue;
                var issues = warnings.GetValueOrDefault(path) ?? [];
                var reason = issues.Length > 0
                    ? removed.Contains(path)
                    ? Resources.CacheCleanupAfterRemovalIssue
                    : issues.Any(issue => issue.NativeError is 32 or 33) ? Resources.CacheCleanupFileInUse : Resources.CacheCleanupItemFailed
                    : Reason(item.Reason);
                if (category == "details" && issues.Length == 0)
                {
                    reason = string.Format(state.Completed ? Resources.CacheCleanupNotRemoved : Resources.CacheCleanupCandidate, AppImageCache.FormatSize(item.Bytes), reason);
                }

                rows.Add(new(
                        rows.Count,
                        path,
                        System.IO.Path.GetFileName(path),
                        reason,
                        string.Join(Environment.NewLine, issues.Select(issue => issue.Warning)),
                        issues.Length > 0,
                        false));
            }
        }

        var duplicateNames = rows.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any()).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            if (duplicateNames.Contains(rows[index].Name))
            {
                rows[index] = rows[index] with
                {
                    Name = rows[index].Name + " · " + System.IO.Path.GetRelativePath(client.Paths.Root, System.IO.Path.GetDirectoryName(rows[index].Path) ?? client.Paths.Root)
                };
            }
        }

        if (rows.Count > 0)
        {
            rows[^1] = rows[^1] with
            {
                Last = true
            };
        }

        return [.. rows];
    }

    private static string Reason(string? reason) => reason switch
    {
        "expired-metadata" => Resources.CacheCleanupMetadata,
        "expired-download-cache" => Resources.CacheCleanupDownload,
        "expired-partial" => Resources.CacheCleanupPartial,
        "unreferenced-installed-cache" => Resources.CacheCleanupUnreferenced,
        "referenced" or "tracked-content" => Resources.CacheCleanupReferenced,
        "active-game" => Resources.CacheCleanupActiveGame,
        "reference-data-unreadable" => Resources.CacheCleanupUnreadable,
        "external-root" => Resources.CacheCleanupExternal,
        "modified-after-download" => Resources.CacheCleanupModified,
        _ => Resources.CacheCleanupUnknown,
    };

    public void Dispose()
    {
        _disposed = true;
        _operation?.Cancel();
        _operation?.Dispose();
    }
}