// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Schedules optional skin queries on the owning UI context, coalescing triggers and backing off failures for each source and role.</summary>
internal sealed class AccountSkinRefresh(Func<string, CancellationToken, Task<IdentityRecord>> query, TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<Binding, Entry> _entries = [];
    private readonly SemaphoreSlim _requests = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    internal event Action<IdentityRecord>? Updated;

    internal static bool SameRole(IdentityRecord? first, IdentityRecord? second) => first is not null && second is not null && Binding.Of(first) == Binding.Of(second);

    internal Task RefreshAsync(IdentityRecord? account)
    {
        if (_disposed || account?.Id is null || account.Type is not ("microsoft" or "external")) return Task.CompletedTask;
        var binding = Binding.Of(account);
        var now = _clock.GetUtcNow();
        if (!_entries.TryGetValue(binding, out var entry)) _entries[binding] = entry = new();
        if (!entry.Observed || entry.VerifiedAt != account.VerifiedAt)
        {
            entry.Observed = true;
            entry.VerifiedAt = account.VerifiedAt;
            entry.Failures = 0;
            entry.NextAttempt = DateTimeOffset.TryParse(account.VerifiedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var verified) && verified <= now
                ? verified.AddMinutes(5) : now;
        }

        if (entry.Pending is { } pending) return pending;
        if (now < entry.NextAttempt) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Pending = completion.Task;
        _ = RunAsync(account, entry, completion);
        return completion.Task;
    }

    private async Task RunAsync(IdentityRecord account, Entry entry, TaskCompletionSource completion)
    {
        var ct = _lifetime.Token;
        try
        {
            await _requests.WaitAsync(ct);
            IdentityRecord result;
            try { result = await Task.Run(() => query(account.Id!, ct), ct); }
            finally { _requests.Release(); }

            if (_disposed || entry.VerifiedAt != account.VerifiedAt) return;
            entry.NextAttempt = _clock.GetUtcNow().AddMinutes(5);
            entry.Failures = 0;
            if (SameRole(account, result))
                Updated?.Invoke(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_disposed && entry.VerifiedAt == account.VerifiedAt)
            {
                entry.Failures = Math.Min(4, entry.Failures + 1);
                entry.NextAttempt = _clock.GetUtcNow().AddMinutes(Math.Min(60, 5 * (1 << entry.Failures)));
            }
        }
        finally
        {
            entry.Pending = null;
            completion.TrySetResult();
        }
    }

    /// <summary>Cancels this window's optional work and prevents late completions from notifying views.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Updated = null;
        _ = ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        await Task.WhenAll(_entries.Values.Select(entry => entry.Pending ?? Task.CompletedTask));
        _requests.Dispose();
        _lifetime.Dispose();
    }

    private readonly record struct Binding(string? Id, string? Type, string? Source, string? Uuid)
    {
        internal static Binding Of(IdentityRecord account) => new(account.Id, account.Type, account.AuthServer, account.Uuid);
    }

    private sealed class Entry
    {
        internal bool Observed;
        internal string? VerifiedAt;
        internal DateTimeOffset NextAttempt;
        internal int Failures;
        internal Task? Pending;
    }
}